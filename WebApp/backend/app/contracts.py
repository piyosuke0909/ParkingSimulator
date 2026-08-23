from __future__ import annotations

import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from jsonschema import Draft202012Validator, FormatChecker

BASE_DIR = Path(__file__).resolve().parent.parent
CONTRACT_DIR = BASE_DIR / "contracts"


def _load(name: str) -> dict[str, Any]:
    return json.loads((CONTRACT_DIR / name).read_text(encoding="utf-8-sig"))


SNAPSHOT_SCHEMA = _load("P2_Snapshot_Contract_v1_1.schema.json")
EVENT_SCHEMA = _load("P2_Event_Contract_v1.schema.json")
COMMAND_SCHEMA = _load("P3_Command_Contract_v1.schema.json")

FORMAT_CHECKER = FormatChecker()
SNAPSHOT_VALIDATOR = Draft202012Validator(SNAPSHOT_SCHEMA, format_checker=FORMAT_CHECKER)
EVENT_VALIDATOR = Draft202012Validator(EVENT_SCHEMA, format_checker=FORMAT_CHECKER)
COMMAND_BATCH_VALIDATOR = Draft202012Validator(COMMAND_SCHEMA, format_checker=FORMAT_CHECKER)


def schema_errors(validator: Draft202012Validator, obj: Any, limit: int = 12) -> list[str]:
    errors = sorted(validator.iter_errors(obj), key=lambda e: list(e.absolute_path))
    out: list[str] = []
    for error in errors[:limit]:
        path = ".".join(str(p) for p in error.absolute_path) or "$"
        out.append(f"{path}: {error.message}")
    return out


def canonical_hash(obj: Any) -> str:
    raw = json.dumps(obj, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")
    return hashlib.sha256(raw).hexdigest()


def parse_utc(value: str) -> datetime:
    text = value.strip()
    if text.endswith("Z"):
        text = text[:-1] + "+00:00"
    parsed = datetime.fromisoformat(text)
    if parsed.tzinfo is None:
        parsed = parsed.replace(tzinfo=timezone.utc)
    return parsed.astimezone(timezone.utc)


def utc_now() -> datetime:
    return datetime.now(timezone.utc)


def ensure_utc(value: datetime) -> datetime:
    if value.tzinfo is None:
        return value.replace(tzinfo=timezone.utc)
    return value.astimezone(timezone.utc)


def iso_z(value: datetime | None = None) -> str:
    value = ensure_utc(value or utc_now())
    return value.isoformat(timespec="milliseconds").replace("+00:00", "Z")
