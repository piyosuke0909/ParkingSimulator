from __future__ import annotations

from fastapi import APIRouter, Depends, Header, Query
from fastapi.responses import JSONResponse
from sqlalchemy import select
from sqlalchemy.exc import SQLAlchemyError
from sqlalchemy.orm import Session

from app.api_models import CreateCommandRequest
from app.auth import require_api_key
from app.contracts import iso_z
from app.db import get_db
from app.errors import error_response
from app.models import CommandRecord, RuntimeTarget, SnapshotRecord
from app.services.commands import CommandError, command_view, create_command, refresh_terminal_states

router = APIRouter(prefix="/api/v1/admin", tags=["admin-p3"], dependencies=[Depends(require_api_key)])


@router.get("/runtime-targets")
def runtime_targets(db: Session = Depends(get_db)):
    rows = db.scalars(
        select(RuntimeTarget).order_by(RuntimeTarget.last_poll_at_utc.desc(), RuntimeTarget.source_id.asc())
    ).all()
    return {
        "targets": [
            {
                "sourceId": r.source_id,
                "sessionId": r.session_id,
                "runId": r.run_id,
                "lastPollAtUtc": iso_z(r.last_poll_at_utc) if r.last_poll_at_utc else None,
                "lastSnapshotAtUtc": iso_z(r.last_snapshot_at_utc) if r.last_snapshot_at_utc else None,
                "lastEventAtUtc": iso_z(r.last_event_at_utc) if r.last_event_at_utc else None,
            }
            for r in rows
        ]
    }


@router.post("/commands")
def post_command(
    body: CreateCommandRequest,
    idempotency_key: str | None = Header(default=None, alias="Idempotency-Key"),
    db: Session = Depends(get_db),
):
    if not idempotency_key or not idempotency_key.strip():
        return error_response(400, "IDEMPOTENCY_KEY_REQUIRED", "Idempotency-Key header is required.")
    try:
        command, created = create_command(db, body, idempotency_key.strip())
        db.commit()
        payload = command_view(command)
        return JSONResponse(status_code=201 if created else 200, content=payload)
    except CommandError as exc:
        db.rollback()
        return error_response(exc.status_code, exc.code, exc.message)
    except SQLAlchemyError:
        db.rollback()
        return error_response(503, "COMMAND_STORE_UNAVAILABLE", "Command store is temporarily unavailable.")
    except Exception:
        db.rollback()
        raise


@router.get("/commands")
def list_commands(
    sourceId: str | None = None,
    runId: str | None = None,
    limit: int = Query(50, ge=1, le=200),
    db: Session = Depends(get_db),
):
    refresh_terminal_states(db)
    query = select(CommandRecord)
    if sourceId:
        query = query.where(CommandRecord.target_source_id == sourceId)
    if runId:
        query = query.where(CommandRecord.target_run_id == runId)
    query = query.order_by(CommandRecord.created_at_utc.desc()).limit(limit)
    rows = db.scalars(query).all()
    db.commit()
    return {"commands": [command_view(r) for r in rows]}


@router.get("/commands/{command_id}")
def get_command(command_id: str, db: Session = Depends(get_db)):
    refresh_terminal_states(db)
    row = db.scalar(select(CommandRecord).where(CommandRecord.command_id == command_id))
    db.commit()
    if not row:
        return error_response(404, "COMMAND_NOT_FOUND", "Command was not found.")
    return command_view(row)


@router.get("/snapshots/latest")
def latest_snapshot(
    runId: str | None = None,
    db: Session = Depends(get_db),
):
    query = select(SnapshotRecord)
    if runId:
        query = query.where(SnapshotRecord.run_id == runId)
    row = db.scalar(query.order_by(SnapshotRecord.received_at_utc.desc()).limit(1))
    if not row:
        return error_response(404, "SNAPSHOT_NOT_FOUND", "No Snapshot has been stored.")
    return row.payload
