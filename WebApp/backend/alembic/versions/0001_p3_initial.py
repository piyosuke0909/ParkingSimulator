"""P3 initial persistent model.

Revision ID: 0001_p3_initial
Revises:
"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa
from sqlalchemy.dialects import postgresql

revision: str = "0001_p3_initial"
down_revision: Union[str, None] = None
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.create_table(
        "simulation_snapshots",
        sa.Column("id", sa.Integer(), primary_key=True, autoincrement=True),
        sa.Column("snapshot_id", sa.String(length=220), nullable=False, unique=True),
        sa.Column("source_system", sa.String(length=80), nullable=False),
        sa.Column("session_id", sa.String(length=220), nullable=False),
        sa.Column("run_id", sa.String(length=220), nullable=False),
        sa.Column("sequence_number", sa.Integer(), nullable=False),
        sa.Column("generated_at_utc", sa.DateTime(timezone=True), nullable=False),
        sa.Column("received_at_utc", sa.DateTime(timezone=True), server_default=sa.func.now(), nullable=False),
        sa.Column("payload_hash", sa.String(length=64), nullable=False),
        sa.Column("payload", postgresql.JSONB(astext_type=sa.Text()), nullable=False),
    )
    op.create_index("ix_simulation_snapshots_session_id", "simulation_snapshots", ["session_id"])
    op.create_index("ix_simulation_snapshots_run_id", "simulation_snapshots", ["run_id"])
    op.create_index("ix_snapshots_run_sequence", "simulation_snapshots", ["run_id", "sequence_number"])

    op.create_table(
        "simulation_events",
        sa.Column("id", sa.Integer(), primary_key=True, autoincrement=True),
        sa.Column("event_id", sa.String(length=220), nullable=False, unique=True),
        sa.Column("event_type", sa.String(length=100), nullable=False),
        sa.Column("source_system", sa.String(length=80), nullable=False),
        sa.Column("session_id", sa.String(length=220), nullable=False),
        sa.Column("run_id", sa.String(length=220), nullable=False),
        sa.Column("sequence_number", sa.Integer(), nullable=False),
        sa.Column("command_id", sa.String(length=220), nullable=True),
        sa.Column("generated_at_utc", sa.DateTime(timezone=True), nullable=False),
        sa.Column("received_at_utc", sa.DateTime(timezone=True), server_default=sa.func.now(), nullable=False),
        sa.Column("payload_hash", sa.String(length=64), nullable=False),
        sa.Column("payload", postgresql.JSONB(astext_type=sa.Text()), nullable=False),
    )
    op.create_index("ix_simulation_events_event_type", "simulation_events", ["event_type"])
    op.create_index("ix_simulation_events_session_id", "simulation_events", ["session_id"])
    op.create_index("ix_simulation_events_run_id", "simulation_events", ["run_id"])
    op.create_index("ix_simulation_events_command_id", "simulation_events", ["command_id"])
    op.create_index("ix_events_run_sequence", "simulation_events", ["run_id", "sequence_number"])

    op.create_table(
        "runtime_targets",
        sa.Column("source_id", sa.String(length=220), primary_key=True),
        sa.Column("session_id", sa.String(length=220), nullable=False),
        sa.Column("run_id", sa.String(length=220), nullable=False),
        sa.Column("first_seen_at_utc", sa.DateTime(timezone=True), server_default=sa.func.now(), nullable=False),
        sa.Column("last_poll_at_utc", sa.DateTime(timezone=True), nullable=True),
        sa.Column("last_snapshot_at_utc", sa.DateTime(timezone=True), nullable=True),
        sa.Column("last_event_at_utc", sa.DateTime(timezone=True), nullable=True),
        sa.Column("updated_at_utc", sa.DateTime(timezone=True), server_default=sa.func.now(), nullable=False),
    )

    op.create_table(
        "commands",
        sa.Column("id", sa.Integer(), primary_key=True, autoincrement=True),
        sa.Column("command_id", sa.String(length=220), nullable=False, unique=True),
        sa.Column("idempotency_key", sa.String(length=220), nullable=False),
        sa.Column("command_type", sa.String(length=100), nullable=False),
        sa.Column("target_source_id", sa.String(length=220), nullable=False),
        sa.Column("target_session_id", sa.String(length=220), nullable=False),
        sa.Column("target_run_id", sa.String(length=220), nullable=False),
        sa.Column("created_at_utc", sa.DateTime(timezone=True), nullable=False),
        sa.Column("expires_at_utc", sa.DateTime(timezone=True), nullable=False),
        sa.Column("priority", sa.Integer(), nullable=False, server_default="0"),
        sa.Column("correlation_id", sa.String(length=220), nullable=True),
        sa.Column("issued_by", sa.String(length=220), nullable=True),
        sa.Column("reason", sa.Text(), nullable=True),
        sa.Column("payload", postgresql.JSONB(astext_type=sa.Text()), nullable=False),
        sa.Column("request_hash", sa.String(length=64), nullable=False),
        sa.Column("status", sa.String(length=40), nullable=False, server_default="CREATED"),
        sa.Column("delivery_count", sa.Integer(), nullable=False, server_default="0"),
        sa.Column("last_delivered_at_utc", sa.DateTime(timezone=True), nullable=True),
        sa.Column("terminal_at_utc", sa.DateTime(timezone=True), nullable=True),
        sa.Column("terminal_event_id", sa.String(length=220), nullable=True),
        sa.Column("result_payload", postgresql.JSONB(astext_type=sa.Text()), nullable=True),
        sa.UniqueConstraint("idempotency_key", name="uq_commands_idempotency_key"),
    )
    op.create_index("ix_commands_command_type", "commands", ["command_type"])
    op.create_index("ix_commands_target_source_id", "commands", ["target_source_id"])
    op.create_index("ix_commands_target_run_id", "commands", ["target_run_id"])
    op.create_index("ix_commands_status", "commands", ["status"])
    op.create_index("ix_commands_target_order", "commands", ["target_source_id", "target_run_id", "priority", "created_at_utc"])


def downgrade() -> None:
    op.drop_table("commands")
    op.drop_table("runtime_targets")
    op.drop_table("simulation_events")
    op.drop_table("simulation_snapshots")
