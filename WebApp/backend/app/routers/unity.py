from __future__ import annotations

import json
from typing import Optional

from fastapi import APIRouter, Depends, Header, Query, Request
from fastapi.responses import Response
from sqlalchemy.exc import SQLAlchemyError
from sqlalchemy.orm import Session

from app.auth import require_api_key
from app.config import settings
from app.contracts import EVENT_VALIDATOR, SNAPSHOT_VALIDATOR, schema_errors
from app.db import get_db
from app.errors import error_response
from app.services.commands import build_batch, eligible_commands, refresh_terminal_states
from app.services.ingestion import DuplicateConflict, ingest_events, ingest_snapshot
from app.services.runtime_targets import PollTooFast, TargetConflict, observe_command_poll

router = APIRouter(prefix="/api/v1", tags=["unity-p3"], dependencies=[Depends(require_api_key)])


@router.post("/snapshots")
async def post_snapshot(request: Request, db: Session = Depends(get_db)):
    content_type = request.headers.get("content-type", "").lower()
    if not content_type.startswith("application/json"):
        return error_response(415, "UNSUPPORTED_MEDIA_TYPE", "Snapshot Content-Type must be application/json.")
    raw = await request.body()
    if len(raw) > settings.max_snapshot_bytes:
        return error_response(413, "REQUEST_TOO_LARGE", "Snapshot request exceeds the configured size limit.")
    try:
        payload = json.loads(raw.decode("utf-8"))
    except Exception:
        return error_response(400, "INVALID_JSON", "Snapshot body is not valid UTF-8 JSON.")
    errors = schema_errors(SNAPSHOT_VALIDATOR, payload)
    if errors:
        return error_response(422, "SNAPSHOT_SCHEMA_MISMATCH", "Snapshot does not satisfy contract v1.1.", errors)
    try:
        ingest_snapshot(db, payload)
        db.commit()
    except DuplicateConflict as exc:
        db.rollback()
        return error_response(409, "ID_PAYLOAD_CONFLICT", "The same snapshotId was already stored with a different payload.", extra={"snapshotId": exc.entity_id})
    except SQLAlchemyError:
        db.rollback()
        return error_response(503, "SNAPSHOT_STORE_UNAVAILABLE", "Snapshot store is temporarily unavailable.")
    except Exception:
        db.rollback()
        raise
    return Response(status_code=204)


@router.post("/events")
async def post_events(request: Request, db: Session = Depends(get_db)):
    content_type = request.headers.get("content-type", "").lower()
    if not content_type.startswith("application/x-ndjson"):
        return error_response(415, "UNSUPPORTED_MEDIA_TYPE", "Event Content-Type must be application/x-ndjson.")
    raw = await request.body()
    if len(raw) > settings.max_event_bytes:
        return error_response(413, "REQUEST_TOO_LARGE", "Event request exceeds the configured size limit.")
    try:
        text = raw.decode("utf-8")
    except UnicodeDecodeError:
        return error_response(400, "INVALID_NDJSON", "Event request is not valid UTF-8 NDJSON.")
    lines = [line for line in text.splitlines() if line.strip()]
    if not lines:
        return error_response(400, "INVALID_NDJSON", "Event request contains no JSON objects.")
    if len(lines) > settings.max_event_count:
        return error_response(413, "TOO_MANY_EVENTS", f"Event batch may contain at most {settings.max_event_count} events.")

    events: list[dict] = []
    all_errors: list[str] = []
    for index, line in enumerate(lines, start=1):
        try:
            event = json.loads(line)
        except Exception:
            return error_response(400, "INVALID_NDJSON", f"Line {index} is not valid JSON.")
        errors = schema_errors(EVENT_VALIDATOR, event)
        if errors:
            all_errors.extend([f"line {index}: {x}" for x in errors])
            if len(all_errors) >= 12:
                break
        events.append(event)
    if all_errors:
        return error_response(422, "EVENT_SCHEMA_MISMATCH", "One or more Events do not satisfy contract v1.0.", all_errors[:12])

    try:
        ingest_events(db, events)
        db.commit()
    except DuplicateConflict as exc:
        db.rollback()
        return error_response(409, "ID_PAYLOAD_CONFLICT", "The same eventId was already stored with a different payload.", extra={"eventId": exc.entity_id})
    except SQLAlchemyError:
        db.rollback()
        return error_response(503, "EVENT_STORE_UNAVAILABLE", "Event store is temporarily unavailable.")
    except Exception:
        db.rollback()
        raise
    return Response(status_code=204)


@router.get("/commands")
def get_commands(
    sourceId: str = Query(..., min_length=1),
    sessionId: str = Query(..., min_length=1),
    runId: str = Query(..., min_length=1),
    limit: int = Query(10, ge=1, le=10),
    db: Session = Depends(get_db),
):
    try:
        refresh_terminal_states(db)
        observe_command_poll(db, sourceId, sessionId, runId)
        records = eligible_commands(db, sourceId, sessionId, runId, min(limit, settings.max_command_count))
        batch = build_batch(records)
        db.commit()
        return batch
    except PollTooFast:
        db.rollback()
        return error_response(429, "POLLING_TOO_FAST", "Command polling interval is too short.")
    except TargetConflict:
        db.rollback()
        return error_response(409, "SESSION_NOT_CURRENT", "sessionId/runId is not the current execution for this sourceId.")
    except SQLAlchemyError:
        db.rollback()
        return error_response(503, "COMMAND_STORE_UNAVAILABLE", "Command store is temporarily unavailable.")
    except Exception:
        db.rollback()
        raise
