from __future__ import annotations

import uuid
from datetime import timedelta
from typing import Any

from sqlalchemy import select
from sqlalchemy.orm import Session

from app.api_models import CreateCommandRequest
from app.config import settings
from app.contracts import COMMAND_BATCH_VALIDATOR, canonical_hash, ensure_utc, iso_z, schema_errors, utc_now
from app.models import CommandRecord, RuntimeTarget

TERMINAL_STATUSES = {"SUCCEEDED", "FAILED", "REJECTED", "EXPIRED", "TIMED_OUT"}
EVENT_TO_STATUS = {
    "command.accepted": "ACCEPTED",
    "command.started": "STARTED",
    "command.succeeded": "SUCCEEDED",
    "command.failed": "FAILED",
    "command.rejected": "REJECTED",
    "command.expired": "EXPIRED",
}


class CommandError(Exception):
    def __init__(self, code: str, message: str, status_code: int = 409):
        super().__init__(message)
        self.code = code
        self.message = message
        self.status_code = status_code


def public_command(record: CommandRecord) -> dict[str, Any]:
    data: dict[str, Any] = {
        "contractName": "smart-parking.command",
        "schemaVersion": "1.0",
        "commandId": record.command_id,
        "idempotencyKey": record.idempotency_key,
        "commandType": record.command_type,
        "targetSourceId": record.target_source_id,
        "targetSessionId": record.target_session_id,
        "targetRunId": record.target_run_id,
        "createdAtUtc": iso_z(record.created_at_utc),
        "expiresAtUtc": iso_z(record.expires_at_utc),
        "priority": record.priority,
        "payload": record.payload,
    }
    if record.correlation_id:
        data["correlationId"] = record.correlation_id
    if record.issued_by:
        data["issuedBy"] = record.issued_by
    if record.reason:
        data["reason"] = record.reason
    return data


def command_view(record: CommandRecord) -> dict[str, Any]:
    return {
        "commandId": record.command_id,
        "idempotencyKey": record.idempotency_key,
        "commandType": record.command_type,
        "targetSourceId": record.target_source_id,
        "targetSessionId": record.target_session_id,
        "targetRunId": record.target_run_id,
        "createdAtUtc": iso_z(record.created_at_utc),
        "expiresAtUtc": iso_z(record.expires_at_utc),
        "priority": record.priority,
        "status": record.status,
        "deliveryCount": record.delivery_count,
        "lastDeliveredAtUtc": iso_z(record.last_delivered_at_utc) if record.last_delivered_at_utc else None,
        "terminalAtUtc": iso_z(record.terminal_at_utc) if record.terminal_at_utc else None,
        "payload": record.payload,
        "resultPayload": record.result_payload,
    }


def refresh_terminal_states(db: Session) -> None:
    now = utc_now()
    candidates = db.scalars(select(CommandRecord).where(CommandRecord.status.not_in(TERMINAL_STATUSES))).all()
    for command in candidates:
        if now >= ensure_utc(command.expires_at_utc):
            command.status = "EXPIRED"
            command.terminal_at_utc = now
            continue
        if (
            command.delivery_count >= settings.command_max_deliveries
            and command.last_delivered_at_utc
            and now >= ensure_utc(command.last_delivered_at_utc) + timedelta(seconds=settings.command_lease_seconds)
        ):
            command.status = "TIMED_OUT"
            command.terminal_at_utc = now
    db.flush()


def eligible_commands(db: Session, source_id: str, session_id: str, run_id: str, limit: int) -> list[CommandRecord]:
    refresh_terminal_states(db)
    now = utc_now()
    rows = db.scalars(
        select(CommandRecord)
        .where(
            CommandRecord.target_source_id == source_id,
            CommandRecord.target_session_id == session_id,
            CommandRecord.target_run_id == run_id,
            CommandRecord.status.not_in(TERMINAL_STATUSES),
        )
        .order_by(CommandRecord.priority.desc(), CommandRecord.created_at_utc.asc(), CommandRecord.command_id.asc())
    ).all()

    eligible: list[CommandRecord] = []
    for command in rows:
        if now >= ensure_utc(command.expires_at_utc):
            continue
        if command.delivery_count >= settings.command_max_deliveries:
            continue
        if command.delivery_count > 0 and command.last_delivered_at_utc:
            if now < ensure_utc(command.last_delivered_at_utc) + timedelta(seconds=settings.command_lease_seconds):
                continue
        eligible.append(command)
        if len(eligible) >= limit:
            break

    for command in eligible:
        command.delivery_count += 1
        command.last_delivered_at_utc = now
        if command.status == "CREATED":
            command.status = "DELIVERED"
    db.flush()
    return eligible


def build_batch(records: list[CommandRecord]) -> dict[str, Any]:
    batch = {
        "contractName": "smart-parking.command-batch",
        "schemaVersion": "1.0",
        "serverTimeUtc": iso_z(),
        "leaseSeconds": settings.command_lease_seconds,
        "commands": [public_command(r) for r in records],
    }
    errors = schema_errors(COMMAND_BATCH_VALIDATOR, batch)
    if errors:
        raise RuntimeError("Generated Command batch does not satisfy P3 Command v1.0: " + "; ".join(errors))
    return batch


def _request_fingerprint(request: CreateCommandRequest) -> str:
    return canonical_hash(request.model_dump(mode="json", exclude_none=True))


def create_command(db: Session, request: CreateCommandRequest, idempotency_key: str) -> tuple[CommandRecord, bool]:
    existing = db.scalar(select(CommandRecord).where(CommandRecord.idempotency_key == idempotency_key))
    request_hash = _request_fingerprint(request)
    if existing:
        if existing.request_hash != request_hash:
            raise CommandError("IDEMPOTENCY_KEY_REUSE", "The same Idempotency-Key was already used for a different operation.")
        return existing, False

    target = db.get(RuntimeTarget, request.targetSourceId)
    if not target:
        raise CommandError("TARGET_NOT_OBSERVED", "The target Unity source has not polled the Command API yet.")
    if target.session_id != request.targetSessionId or target.run_id != request.targetRunId:
        raise CommandError("TARGET_MISMATCH", "The requested sessionId/runId is not the current execution for this sourceId.")
    if not target.last_snapshot_at_utc:
        raise CommandError("SNAPSHOT_NOT_AVAILABLE", "A current Snapshot has not been received for the target run.")
    age = (utc_now() - ensure_utc(target.last_snapshot_at_utc)).total_seconds()
    if age > settings.target_stale_seconds:
        raise CommandError("SNAPSHOT_STALE", "The latest Snapshot for the target run is stale.")

    created = utc_now()
    expires = created + timedelta(seconds=request.expiresInSeconds or settings.command_default_lifetime_seconds)
    payload = request.payload.model_dump(mode="json", exclude_none=True)
    command = CommandRecord(
        command_id=f"cmd_{uuid.uuid4()}",
        idempotency_key=idempotency_key,
        command_type=request.commandType,
        target_source_id=request.targetSourceId,
        target_session_id=request.targetSessionId,
        target_run_id=request.targetRunId,
        created_at_utc=created,
        expires_at_utc=expires,
        priority=request.priority,
        correlation_id=request.correlationId,
        issued_by=request.issuedBy,
        reason=request.reason,
        payload=payload,
        request_hash=request_hash,
        status="CREATED",
    )
    # Validate exactly what Unity will later receive before persisting it.
    probe_batch = {
        "contractName": "smart-parking.command-batch",
        "schemaVersion": "1.0",
        "serverTimeUtc": iso_z(created),
        "leaseSeconds": settings.command_lease_seconds,
        "commands": [public_command(command)],
    }
    errors = schema_errors(COMMAND_BATCH_VALIDATOR, probe_batch)
    if errors:
        raise CommandError("INVALID_COMMAND", "Generated Command does not satisfy Command v1.0.", 422)
    db.add(command)
    db.flush()
    return command, True


def apply_command_event(db: Session, event: dict[str, Any]) -> None:
    event_type = event.get("eventType")
    new_status = EVENT_TO_STATUS.get(event_type)
    if not new_status:
        return
    command_id = event.get("commandId")
    if not command_id:
        return
    command = db.scalar(select(CommandRecord).where(CommandRecord.command_id == command_id))
    if not command:
        return
    if command.status in TERMINAL_STATUSES:
        return
    command.status = new_status
    if new_status in TERMINAL_STATUSES:
        command.terminal_at_utc = utc_now()
        command.terminal_event_id = event.get("eventId")
        command.result_payload = (event.get("payload") or {}).get("command")
    db.flush()
