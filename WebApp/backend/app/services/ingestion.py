from __future__ import annotations

from typing import Any

from sqlalchemy import select
from sqlalchemy.orm import Session

from app.contracts import canonical_hash, parse_utc
from app.models import EventRecord, SnapshotRecord
from app.services.commands import apply_command_event
from app.services.runtime_targets import mark_event_received, mark_snapshot_received


class DuplicateConflict(Exception):
    def __init__(self, entity: str, entity_id: str):
        super().__init__(f"{entity} id conflict: {entity_id}")
        self.entity = entity
        self.entity_id = entity_id


def ingest_snapshot(db: Session, payload: dict[str, Any]) -> bool:
    snapshot_id = payload["snapshotId"]
    payload_hash = canonical_hash(payload)
    existing = db.scalar(select(SnapshotRecord).where(SnapshotRecord.snapshot_id == snapshot_id))
    if existing:
        if existing.payload_hash != payload_hash:
            raise DuplicateConflict("snapshot", snapshot_id)
        mark_snapshot_received(db, payload["sessionId"], payload["runId"], parse_utc(payload["generatedAtUtc"]))
        return False

    row = SnapshotRecord(
        snapshot_id=snapshot_id,
        source_system=payload["sourceSystem"],
        session_id=payload["sessionId"],
        run_id=payload["runId"],
        sequence_number=int(payload["sequenceNumber"]),
        generated_at_utc=parse_utc(payload["generatedAtUtc"]),
        payload_hash=payload_hash,
        payload=payload,
    )
    db.add(row)
    db.flush()
    mark_snapshot_received(db, payload["sessionId"], payload["runId"], parse_utc(payload["generatedAtUtc"]))
    return True


def ingest_events(db: Session, events: list[dict[str, Any]]) -> int:
    inserted = 0
    for payload in events:
        event_id = payload["eventId"]
        payload_hash = canonical_hash(payload)
        existing = db.scalar(select(EventRecord).where(EventRecord.event_id == event_id))
        if existing:
            if existing.payload_hash != payload_hash:
                raise DuplicateConflict("event", event_id)
            apply_command_event(db, payload)
            mark_event_received(db, payload["sessionId"], payload["runId"])
            continue

        row = EventRecord(
            event_id=event_id,
            event_type=payload["eventType"],
            source_system=payload["sourceSystem"],
            session_id=payload["sessionId"],
            run_id=payload["runId"],
            sequence_number=int(payload["sequenceNumber"]),
            command_id=payload.get("commandId"),
            generated_at_utc=parse_utc(payload["generatedAtUtc"]),
            payload_hash=payload_hash,
            payload=payload,
        )
        db.add(row)
        db.flush()
        inserted += 1
        apply_command_event(db, payload)
        mark_event_received(db, payload["sessionId"], payload["runId"])
    return inserted
