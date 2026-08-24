from __future__ import annotations

from datetime import timedelta

from sqlalchemy import select
from sqlalchemy.orm import Session

from app.config import settings
from app.contracts import ensure_utc, utc_now
from app.models import CommandRecord, RuntimeTarget, SnapshotRecord

TERMINAL_STATUSES = {"SUCCEEDED", "FAILED", "REJECTED", "EXPIRED", "TIMED_OUT"}


class TargetConflict(Exception):
    pass


class PollTooFast(Exception):
    pass


def _has_outstanding_commands(db: Session, target: RuntimeTarget) -> bool:
    rows = db.scalars(
        select(CommandRecord).where(
            CommandRecord.target_source_id == target.source_id,
            CommandRecord.target_session_id == target.session_id,
            CommandRecord.target_run_id == target.run_id,
            CommandRecord.status.not_in(TERMINAL_STATUSES),
        ).limit(1)
    ).all()
    return bool(rows)


def observe_command_poll(db: Session, source_id: str, session_id: str, run_id: str) -> RuntimeTarget:
    now = utc_now()
    target = db.get(RuntimeTarget, source_id)
    if target is None:
        target = RuntimeTarget(source_id=source_id, session_id=session_id, run_id=run_id, last_poll_at_utc=now)
        latest_snapshot = db.scalar(
            select(SnapshotRecord)
            .where(SnapshotRecord.session_id == session_id, SnapshotRecord.run_id == run_id)
            .order_by(SnapshotRecord.received_at_utc.desc())
            .limit(1)
        )
        if latest_snapshot:
            target.last_snapshot_at_utc = latest_snapshot.generated_at_utc
        db.add(target)
        db.flush()
        return target

    same = target.session_id == session_id and target.run_id == run_id
    if not same:
        if _has_outstanding_commands(db, target):
            raise TargetConflict("The sourceId currently has unfinished Commands for another session/run.")
        target.session_id = session_id
        target.run_id = run_id
        target.first_seen_at_utc = now
        target.last_snapshot_at_utc = None
        target.last_event_at_utc = None
        latest_snapshot = db.scalar(
            select(SnapshotRecord)
            .where(SnapshotRecord.session_id == session_id, SnapshotRecord.run_id == run_id)
            .order_by(SnapshotRecord.received_at_utc.desc())
            .limit(1)
        )
        if latest_snapshot:
            target.last_snapshot_at_utc = latest_snapshot.generated_at_utc
    else:
        if target.last_poll_at_utc and settings.command_min_poll_interval_seconds > 0:
            if now - ensure_utc(target.last_poll_at_utc) < timedelta(seconds=settings.command_min_poll_interval_seconds):
                raise PollTooFast("Command polling is faster than the configured minimum interval.")

    target.last_poll_at_utc = now
    target.updated_at_utc = now
    db.flush()
    return target


def mark_snapshot_received(db: Session, session_id: str, run_id: str, generated_at_utc) -> None:
    now = utc_now()
    targets = db.scalars(select(RuntimeTarget).where(RuntimeTarget.session_id == session_id, RuntimeTarget.run_id == run_id)).all()
    for target in targets:
        target.last_snapshot_at_utc = generated_at_utc
        target.updated_at_utc = now


def mark_event_received(db: Session, session_id: str, run_id: str) -> None:
    now = utc_now()
    targets = db.scalars(select(RuntimeTarget).where(RuntimeTarget.session_id == session_id, RuntimeTarget.run_id == run_id)).all()
    for target in targets:
        target.last_event_at_utc = now
        target.updated_at_utc = now
