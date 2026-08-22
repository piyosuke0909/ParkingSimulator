from __future__ import annotations

import hashlib
import json
from dataclasses import dataclass
from datetime import UTC, datetime, timedelta
from threading import RLock
from typing import Any
from uuid import uuid4

from models.v1 import CommandBatch, CommandEnvelope, CreateAreaPolicyCommandRequest, P2CommandEvent


DELIVERY_LEASE = timedelta(seconds=10)
MAX_DELIVERY_ATTEMPTS = 3
TARGET_STALE_AFTER = timedelta(seconds=5)
TERMINAL_EVENTS = {
    "command.succeeded": "succeeded",
    "command.failed": "failed",
    "command.rejected": "rejected",
    "command.expired": "expired",
}
NON_TERMINAL_EVENTS = {
    "command.accepted": "accepted",
    "command.started": "started",
}
EVENT_STATUSES = {
    "command.accepted": "ACCEPTED",
    "command.started": "STARTED",
    "command.succeeded": "SUCCEEDED",
    "command.failed": "FAILED",
    "command.rejected": "REJECTED",
    "command.expired": "EXPIRED",
}
EVENT_TERMINAL_STATUSES = set(TERMINAL_EVENTS.values())
PROGRESS_STATUS_RANK = {
    "pending": 0,
    "delivered": 1,
    "accepted": 2,
    "started": 3,
}


class CommandConflictError(ValueError):
    pass


class CommandNotFoundError(LookupError):
    pass


class TargetConflictError(ValueError):
    pass


@dataclass(frozen=True)
class CommandTarget:
    source_id: str
    session_id: str
    run_id: str


@dataclass
class CommandRecord:
    command: CommandEnvelope
    status: str = "pending"
    delivery_attempts: int = 0
    last_delivered_at: datetime | None = None
    completed_at: datetime | None = None
    last_result: P2CommandEvent | None = None

    def response(self) -> dict[str, Any]:
        return {
            **self.command.model_dump(mode="json", exclude_none=True),
            "status": self.status,
            "deliveryAttempts": self.delivery_attempts,
            "lastDeliveredAtUtc": self.last_delivered_at.isoformat() if self.last_delivered_at else None,
            "completedAtUtc": self.completed_at.isoformat() if self.completed_at else None,
            "lastResult": self.last_result.model_dump(mode="json") if self.last_result else None,
        }


class CommandStore:
    def __init__(self) -> None:
        self._commands: dict[str, CommandRecord] = {}
        self._idempotency_index: dict[str, str] = {}
        self._result_event_hashes: dict[str, str] = {}
        self._area_policies: dict[str, str] = {area: "NORMAL" for area in "ABCD"}
        self._area_policy_expirations: dict[str, datetime] = {}
        self._active_target: CommandTarget | None = None
        self._target_last_seen_at: datetime | None = None
        self._lock = RLock()

    @staticmethod
    def now() -> datetime:
        return datetime.now(UTC)

    def register_target(self, source_id: str, session_id: str, run_id: str) -> CommandTarget:
        target = CommandTarget(source_id, session_id, run_id)
        now = self.now()
        with self._lock:
            self._refresh_timeouts(now)
            if self._active_target and self._active_target != target:
                outstanding = any(
                    record.status not in {"succeeded", "failed", "rejected", "expired", "timed_out"}
                    for record in self._commands.values()
                )
                if outstanding:
                    raise TargetConflictError(
                        "別のUnity実行向けに未完了Commandがあるため、現在の実行へ切り替えられません。"
                    )
            self._active_target = target
            self._target_last_seen_at = now
            return target

    def current_target(self) -> CommandTarget | None:
        with self._lock:
            if not self._active_target or not self._target_last_seen_at:
                return None
            if self.now() - self._target_last_seen_at >= TARGET_STALE_AFTER:
                return None
            return self._active_target

    def create(self, request: CreateAreaPolicyCommandRequest, target: CommandTarget) -> dict[str, Any]:
        now = self.now()
        idempotency_key = request.idempotencyKey or f"admin-{uuid4().hex}"
        with self._lock:
            existing_id = self._idempotency_index.get(idempotency_key)
            if existing_id:
                existing = self._commands[existing_id]
                if self._same_request(existing.command, request, target):
                    return existing.response()
                raise CommandConflictError("idempotencyKey is already used for a different command")

            command = CommandEnvelope(
                commandId=f"cmd_{uuid4().hex}",
                idempotencyKey=idempotency_key,
                targetSourceId=target.source_id,
                targetSessionId=target.session_id,
                targetRunId=target.run_id,
                createdAtUtc=now,
                expiresAtUtc=now + timedelta(seconds=request.expiresInSeconds),
                priority=request.priority,
                correlationId=request.correlationId,
                issuedBy=request.issuedBy,
                reason=request.reason,
                payload=request.payload,
            )
            record = CommandRecord(command=command)
            self._commands[command.commandId] = record
            self._idempotency_index[idempotency_key] = command.commandId
            return record.response()

    def poll(self, target: CommandTarget, limit: int) -> CommandBatch:
        now = self.now()
        with self._lock:
            self._refresh_timeouts(now)
            candidates: list[CommandRecord] = []
            for record in self._commands.values():
                command = record.command
                if (
                    command.targetSourceId != target.source_id
                    or command.targetSessionId != target.session_id
                    or command.targetRunId != target.run_id
                ):
                    continue
                if record.status in {"succeeded", "failed", "rejected", "expired", "timed_out"}:
                    continue
                if record.delivery_attempts >= MAX_DELIVERY_ATTEMPTS:
                    continue
                if record.last_delivered_at and now - record.last_delivered_at < DELIVERY_LEASE:
                    continue
                candidates.append(record)

            candidates.sort(
                key=lambda item: (-item.command.priority, item.command.createdAtUtc, item.command.commandId)
            )
            selected = candidates[:limit]
            for record in selected:
                record.delivery_attempts += 1
                record.last_delivered_at = now
                if record.status == "pending":
                    record.status = "delivered"
            return CommandBatch(
                serverTimeUtc=now,
                leaseSeconds=int(DELIVERY_LEASE.total_seconds()),
                commands=[record.command for record in selected],
            )

    def record_event(self, event: P2CommandEvent) -> dict[str, Any]:
        event_json = json.dumps(event.model_dump(mode="json"), ensure_ascii=False, sort_keys=True)
        event_hash = hashlib.sha256(event_json.encode("utf-8")).hexdigest()
        payload = event.command_payload()
        with self._lock:
            previous_hash = self._result_event_hashes.get(event.eventId)
            if previous_hash:
                if previous_hash != event_hash:
                    raise CommandConflictError("eventId is already used for a different result")
                record = self._commands.get(event.commandId)
                if not record:
                    raise CommandNotFoundError(event.commandId)
                return record.response()

            record = self._commands.get(event.commandId)
            if not record:
                raise CommandNotFoundError(event.commandId)
            if (
                event.sessionId != record.command.targetSessionId
                or event.runId != record.command.targetRunId
                or payload.idempotencyKey != record.command.idempotencyKey
                or payload.commandType != record.command.commandType
            ):
                raise CommandConflictError("Command結果Eventが発行済みCommandと一致しません")

            if event.entityId != event.commandId:
                raise CommandConflictError("Command結果EventのentityIdとcommandIdが一致しません")

            expected_status = EVENT_STATUSES[event.eventType]
            if payload.status != expected_status:
                raise CommandConflictError(
                    f"{event.eventType} のstatusは {expected_status} である必要があります"
                )

            if event.eventType == "command.succeeded":
                if (
                    payload.result.areaId != record.command.payload.areaId
                    or payload.result.appliedPolicy != record.command.payload.policy
                ):
                    raise CommandConflictError("Command成功Eventのresultが発行済みCommandと一致しません")

            previous_terminal = (
                record.last_result
                if record.last_result and record.last_result.eventType in TERMINAL_EVENTS
                else None
            )
            if previous_terminal:
                if event.eventType in TERMINAL_EVENTS:
                    if (
                        event.eventType != previous_terminal.eventType
                        or payload != previous_terminal.command_payload()
                    ):
                        raise CommandConflictError("Commandに異なるTerminal Eventが既に記録されています")
                self._result_event_hashes[event.eventId] = event_hash
                return record.response()

            self._result_event_hashes[event.eventId] = event_hash
            if event.eventType in TERMINAL_EVENTS:
                record.last_result = event
                record.status = TERMINAL_EVENTS[event.eventType]
                record.completed_at = event.generatedAtUtc
                if event.eventType == "command.succeeded":
                    area_id = record.command.payload.areaId
                    policy = record.command.payload.policy
                    self._area_policies[area_id] = policy
                    effective_until = record.command.payload.effectiveUntilUtc
                    if policy != "NORMAL" and effective_until:
                        self._area_policy_expirations[area_id] = effective_until
                    else:
                        self._area_policy_expirations.pop(area_id, None)
            elif event.eventType in NON_TERMINAL_EVENTS:
                next_status = NON_TERMINAL_EVENTS[event.eventType]
                current_rank = PROGRESS_STATUS_RANK.get(record.status)
                next_rank = PROGRESS_STATUS_RANK[next_status]
                if current_rank is not None and next_rank >= current_rank:
                    record.last_result = event
                    record.status = next_status
            return record.response()

    def admin_summary(self) -> dict[str, Any]:
        with self._lock:
            now = self.now()
            self._refresh_timeouts(now)
            self._refresh_area_policies(now)
            records = sorted(self._commands.values(), key=lambda item: item.command.createdAtUtc, reverse=True)
            target = self.current_target()
            return {
                "areaPolicies": dict(self._area_policies),
                "commands": [record.response() for record in records[:30]],
                "commandTarget": None
                if not target
                else {
                    "sourceId": target.source_id,
                    "sessionId": target.session_id,
                    "runId": target.run_id,
                },
            }

    def reset(self) -> None:
        with self._lock:
            self._commands.clear()
            self._idempotency_index.clear()
            self._result_event_hashes.clear()
            self._area_policies = {area: "NORMAL" for area in "ABCD"}
            self._area_policy_expirations.clear()
            self._active_target = None
            self._target_last_seen_at = None

    def _refresh_timeouts(self, now: datetime) -> None:
        for record in self._commands.values():
            if record.status in {"succeeded", "failed", "rejected", "expired", "timed_out"}:
                continue
            if record.command.expiresAtUtc <= now:
                record.status = "expired"
                record.completed_at = now
                continue
            if (
                record.delivery_attempts >= MAX_DELIVERY_ATTEMPTS
                and record.last_delivered_at
                and now - record.last_delivered_at >= DELIVERY_LEASE
            ):
                record.status = "timed_out"
                record.completed_at = now

    def _refresh_area_policies(self, now: datetime) -> None:
        expired_areas = [
            area_id
            for area_id, effective_until in self._area_policy_expirations.items()
            if effective_until <= now
        ]
        for area_id in expired_areas:
            self._area_policies[area_id] = "NORMAL"
            self._area_policy_expirations.pop(area_id, None)

    @staticmethod
    def _same_request(
        command: CommandEnvelope,
        request: CreateAreaPolicyCommandRequest,
        target: CommandTarget,
    ) -> bool:
        return (
            command.commandType == request.commandType
            and command.payload == request.payload
            and command.targetSourceId == target.source_id
            and command.targetSessionId == target.session_id
            and command.targetRunId == target.run_id
            and command.expiresAtUtc - command.createdAtUtc
            == timedelta(seconds=request.expiresInSeconds)
            and command.priority == request.priority
            and command.correlationId == request.correlationId
            and command.issuedBy == request.issuedBy
            and command.reason == request.reason
        )


command_store = CommandStore()
