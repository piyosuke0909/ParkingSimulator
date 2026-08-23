from __future__ import annotations

from datetime import datetime
from typing import Any

from sqlalchemy import DateTime, Index, Integer, JSON, String, Text, UniqueConstraint, func
from sqlalchemy.dialects.postgresql import JSONB
from sqlalchemy.orm import Mapped, mapped_column

from app.db import Base

JSON_PAYLOAD = JSON().with_variant(JSONB(), "postgresql")


class SnapshotRecord(Base):
    __tablename__ = "simulation_snapshots"

    id: Mapped[int] = mapped_column(primary_key=True, autoincrement=True)
    snapshot_id: Mapped[str] = mapped_column(String(220), unique=True, nullable=False)
    source_system: Mapped[str] = mapped_column(String(80), nullable=False)
    session_id: Mapped[str] = mapped_column(String(220), nullable=False, index=True)
    run_id: Mapped[str] = mapped_column(String(220), nullable=False, index=True)
    sequence_number: Mapped[int] = mapped_column(Integer, nullable=False)
    generated_at_utc: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
    received_at_utc: Mapped[datetime] = mapped_column(DateTime(timezone=True), server_default=func.now(), nullable=False)
    payload_hash: Mapped[str] = mapped_column(String(64), nullable=False)
    payload: Mapped[dict[str, Any]] = mapped_column(JSON_PAYLOAD, nullable=False)

    __table_args__ = (Index("ix_snapshots_run_sequence", "run_id", "sequence_number"),)


class EventRecord(Base):
    __tablename__ = "simulation_events"

    id: Mapped[int] = mapped_column(primary_key=True, autoincrement=True)
    event_id: Mapped[str] = mapped_column(String(220), unique=True, nullable=False)
    event_type: Mapped[str] = mapped_column(String(100), nullable=False, index=True)
    source_system: Mapped[str] = mapped_column(String(80), nullable=False)
    session_id: Mapped[str] = mapped_column(String(220), nullable=False, index=True)
    run_id: Mapped[str] = mapped_column(String(220), nullable=False, index=True)
    sequence_number: Mapped[int] = mapped_column(Integer, nullable=False)
    command_id: Mapped[str | None] = mapped_column(String(220), nullable=True, index=True)
    generated_at_utc: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
    received_at_utc: Mapped[datetime] = mapped_column(DateTime(timezone=True), server_default=func.now(), nullable=False)
    payload_hash: Mapped[str] = mapped_column(String(64), nullable=False)
    payload: Mapped[dict[str, Any]] = mapped_column(JSON_PAYLOAD, nullable=False)

    __table_args__ = (Index("ix_events_run_sequence", "run_id", "sequence_number"),)


class RuntimeTarget(Base):
    __tablename__ = "runtime_targets"

    source_id: Mapped[str] = mapped_column(String(220), primary_key=True)
    session_id: Mapped[str] = mapped_column(String(220), nullable=False)
    run_id: Mapped[str] = mapped_column(String(220), nullable=False)
    first_seen_at_utc: Mapped[datetime] = mapped_column(DateTime(timezone=True), server_default=func.now(), nullable=False)
    last_poll_at_utc: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)
    last_snapshot_at_utc: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)
    last_event_at_utc: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)
    updated_at_utc: Mapped[datetime] = mapped_column(DateTime(timezone=True), server_default=func.now(), onupdate=func.now(), nullable=False)


class CommandRecord(Base):
    __tablename__ = "commands"

    id: Mapped[int] = mapped_column(primary_key=True, autoincrement=True)
    command_id: Mapped[str] = mapped_column(String(220), unique=True, nullable=False)
    idempotency_key: Mapped[str] = mapped_column(String(220), nullable=False)
    command_type: Mapped[str] = mapped_column(String(100), nullable=False, index=True)
    target_source_id: Mapped[str] = mapped_column(String(220), nullable=False, index=True)
    target_session_id: Mapped[str] = mapped_column(String(220), nullable=False)
    target_run_id: Mapped[str] = mapped_column(String(220), nullable=False, index=True)
    created_at_utc: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
    expires_at_utc: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
    priority: Mapped[int] = mapped_column(Integer, nullable=False, default=0)
    correlation_id: Mapped[str | None] = mapped_column(String(220), nullable=True)
    issued_by: Mapped[str | None] = mapped_column(String(220), nullable=True)
    reason: Mapped[str | None] = mapped_column(Text, nullable=True)
    payload: Mapped[dict[str, Any]] = mapped_column(JSON_PAYLOAD, nullable=False)
    request_hash: Mapped[str] = mapped_column(String(64), nullable=False)
    status: Mapped[str] = mapped_column(String(40), nullable=False, default="CREATED", index=True)
    delivery_count: Mapped[int] = mapped_column(Integer, nullable=False, default=0)
    last_delivered_at_utc: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)
    terminal_at_utc: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)
    terminal_event_id: Mapped[str | None] = mapped_column(String(220), nullable=True)
    result_payload: Mapped[dict[str, Any] | None] = mapped_column(JSON_PAYLOAD, nullable=True)

    __table_args__ = (
        Index("ix_commands_target_order", "target_source_id", "target_run_id", "priority", "created_at_utc"),
        UniqueConstraint("idempotency_key", name="uq_commands_idempotency_key"),
    )
