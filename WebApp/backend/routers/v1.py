from __future__ import annotations

import hashlib
import json
from collections import OrderedDict
from datetime import datetime
from threading import RLock
from typing import Any

from fastapi import APIRouter, Depends, HTTPException, Query, Request, Response, status
from pydantic import ValidationError

from models.v1 import P2CommandEvent, P2SimulationSnapshot
from services.api_auth import require_local_api_key
from services.command_store import (
    CommandConflictError,
    CommandNotFoundError,
    TargetConflictError,
    command_store,
)
from services.contract_validation import (
    COMMAND_BATCH_VALIDATOR,
    EVENT_VALIDATOR,
    SNAPSHOT_VALIDATOR,
    schema_errors,
)
from services.state_store import store


router = APIRouter(
    prefix="/api/v1",
    tags=["v1"],
    dependencies=[Depends(require_local_api_key)],
)
MAX_EVENT_BATCH_COUNT = 50
MAX_EVENT_REQUEST_BYTES = 1024 * 1024
MAX_SNAPSHOT_REQUEST_BYTES = 5 * 1024 * 1024
MAX_EVENT_DEDUPLICATION_ENTRIES = 50_000
MAX_SNAPSHOT_DEDUPLICATION_ENTRIES = 10_000
_event_hashes: OrderedDict[str, str] = OrderedDict()
_event_lock = RLock()
_snapshot_hashes: OrderedDict[str, str] = OrderedDict()
_snapshot_lock = RLock()


def _remember_hash(
    cache: OrderedDict[str, str],
    item_id: str,
    digest: str,
    max_entries: int,
) -> None:
    cache[item_id] = digest
    cache.move_to_end(item_id)
    while len(cache) > max_entries:
        cache.popitem(last=False)


@router.post("/snapshots", status_code=status.HTTP_204_NO_CONTENT)
async def receive_snapshot(request: Request) -> Response:
    content_type = request.headers.get("content-type", "").split(";", 1)[0].strip().lower()
    if content_type != "application/json":
        raise HTTPException(status_code=415, detail="Snapshot Content-Type must be application/json")
    body = await request.body()
    if len(body) > MAX_SNAPSHOT_REQUEST_BYTES:
        raise HTTPException(status_code=413, detail="Snapshot request exceeds 5 MiB")
    try:
        value = json.loads(body.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise HTTPException(status_code=400, detail="Snapshot JSON is malformed") from exc

    errors = schema_errors(SNAPSHOT_VALIDATOR, value)
    if errors:
        raise HTTPException(status_code=422, detail={"message": "Snapshot v1.1 validation failed", "errors": errors})
    try:
        snapshot = P2SimulationSnapshot.model_validate(value)
    except ValidationError as exc:
        raise HTTPException(status_code=422, detail="Snapshot v1.1 validation failed") from exc

    digest = hashlib.sha256(
        json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")
    ).hexdigest()
    with _snapshot_lock:
        previous = _snapshot_hashes.get(snapshot.snapshotId)
        if previous and previous != digest:
            raise HTTPException(status_code=409, detail="snapshotId is already used for different content")
        if previous:
            _snapshot_hashes.move_to_end(snapshot.snapshotId)
        else:
            _remember_hash(
                _snapshot_hashes,
                snapshot.snapshotId,
                digest,
                MAX_SNAPSHOT_DEDUPLICATION_ENTRIES,
            )
            store.ingest_snapshot(snapshot.to_mvp_snapshot())
    return Response(status_code=status.HTTP_204_NO_CONTENT)


@router.post("/events", status_code=status.HTTP_204_NO_CONTENT)
async def receive_events(request: Request) -> Response:
    content_type = request.headers.get("content-type", "").split(";", 1)[0].strip().lower()
    if content_type != "application/x-ndjson":
        raise HTTPException(status_code=415, detail="Event Content-Type must be application/x-ndjson")

    body = await request.body()
    if len(body) > MAX_EVENT_REQUEST_BYTES:
        raise HTTPException(status_code=413, detail="Event request exceeds 1 MiB")
    try:
        text = body.decode("utf-8")
    except UnicodeDecodeError as exc:
        raise HTTPException(status_code=400, detail="Event request must be UTF-8") from exc

    lines = [line.strip() for line in text.splitlines() if line.strip()]
    if not lines:
        raise HTTPException(status_code=400, detail="Event batch is empty")
    if len(lines) > MAX_EVENT_BATCH_COUNT:
        raise HTTPException(status_code=422, detail="Event batch exceeds 50 events")

    parsed: list[tuple[str, str, dict[str, Any], P2CommandEvent | None]] = []
    for index, line in enumerate(lines, start=1):
        try:
            event = json.loads(line)
        except json.JSONDecodeError as exc:
            raise HTTPException(status_code=400, detail=f"Invalid JSON at NDJSON line {index}") from exc
        if not isinstance(event, dict):
            raise HTTPException(status_code=422, detail=f"Event at line {index} must be an object")

        event_id = event.get("eventId")
        required = (
            "contractName",
            "schemaVersion",
            "eventType",
            "sourceSystem",
            "generatedAtUtc",
            "sceneName",
            "sessionId",
            "runId",
            "sequenceNumber",
            "scenarioId",
            "simulationTimeSeconds",
            "entityType",
            "entityId",
            "payload",
        )
        if not isinstance(event_id, str) or not event_id.strip():
            raise HTTPException(status_code=422, detail=f"eventId is required at line {index}")
        if any(key not in event for key in required):
            raise HTTPException(status_code=422, detail=f"Event header is incomplete at line {index}")
        if event.get("contractName") != "smart-parking.simulation-event" or event.get("schemaVersion") != "1.0":
            raise HTTPException(status_code=422, detail=f"Event contract is invalid at line {index}")
        errors = schema_errors(EVENT_VALIDATOR, event)
        if errors:
            raise HTTPException(
                status_code=422,
                detail={"message": f"Event line {index} failed Event v1.0 validation", "errors": errors},
            )
        try:
            datetime.fromisoformat(str(event["generatedAtUtc"]).replace("Z", "+00:00"))
        except ValueError as exc:
            raise HTTPException(status_code=422, detail=f"generatedAtUtc is invalid at line {index}") from exc

        command_event = None
        if str(event.get("eventType", "")).startswith("command."):
            try:
                command_event = P2CommandEvent.model_validate(event)
                command_event.command_payload()
            except ValidationError as exc:
                raise HTTPException(
                    status_code=422,
                    detail=f"Command Event is invalid at line {index}: {exc.errors()[0]['msg']}",
                ) from exc

        digest = hashlib.sha256(
            json.dumps(event, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")
        ).hexdigest()
        parsed.append((event_id, digest, event, command_event))

    with _event_lock:
        for event_id, digest, _, _ in parsed:
            previous = _event_hashes.get(event_id)
            if previous and previous != digest:
                raise HTTPException(status_code=409, detail=f"eventId conflict: {event_id}")

        for event_id, digest, event, command_event in parsed:
            if event_id in _event_hashes:
                _event_hashes.move_to_end(event_id)
                continue
            if command_event:
                try:
                    command_store.record_event(command_event)
                except CommandNotFoundError as exc:
                    raise HTTPException(status_code=404, detail="commandId was not found") from exc
                except CommandConflictError as exc:
                    raise HTTPException(status_code=409, detail=str(exc)) from exc
            _remember_hash(_event_hashes, event_id, digest, MAX_EVENT_DEDUPLICATION_ENTRIES)
            store.add_log(
                "unity_event_received",
                f"Unity Event {event.get('eventType')} を受信しました。",
                {"eventId": event_id, "runId": event.get("runId")},
            )

    return Response(status_code=status.HTTP_204_NO_CONTENT)


@router.get("/commands")
def get_commands(
    source_id: str = Query(alias="sourceId", min_length=1),
    session_id: str = Query(alias="sessionId", min_length=1),
    run_id: str = Query(alias="runId", min_length=1),
    limit: int = Query(default=10, ge=1, le=10),
) -> dict[str, Any]:
    try:
        target = command_store.register_target(source_id, session_id, run_id)
    except TargetConflictError as exc:
        raise HTTPException(status_code=409, detail=str(exc)) from exc
    batch = command_store.poll(target, limit).model_dump(mode="json", exclude_none=True)
    errors = schema_errors(COMMAND_BATCH_VALIDATOR, batch)
    if errors:
        raise HTTPException(
            status_code=500,
            detail={"message": "Backend generated an invalid Command batch", "errors": errors},
        )
    return batch
