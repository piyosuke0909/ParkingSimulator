from __future__ import annotations

import json
import os
from pathlib import Path
from typing import Any

from jsonschema import Draft202012Validator, FormatChecker


def _schema_directory() -> Path:
    configured = os.getenv("SMARTPARKING_SCHEMA_DIR", "").strip()
    if configured:
        return Path(configured).expanduser().resolve()
    return Path(__file__).resolve().parents[2] / "MockBackend_P3" / "schemas"


def _load_schema(name: str) -> dict[str, Any]:
    path = _schema_directory() / name
    if not path.is_file():
        raise RuntimeError(f"SmartParking contract schema was not found: {path}")
    return json.loads(path.read_text(encoding="utf-8"))


def _validator(name: str) -> Draft202012Validator:
    return Draft202012Validator(_load_schema(name), format_checker=FormatChecker())


EVENT_VALIDATOR = _validator("P2_Event_Contract_v1.schema.json")
SNAPSHOT_VALIDATOR = _validator("P2_Snapshot_Contract_v1_1.schema.json")
COMMAND_BATCH_VALIDATOR = _validator("P3_Command_Contract_v1.schema.json")


def schema_errors(validator: Draft202012Validator, value: Any) -> list[str]:
    errors = sorted(validator.iter_errors(value), key=lambda item: list(item.absolute_path))
    formatted: list[str] = []
    for error in errors[:12]:
        path = ".".join(str(part) for part in error.absolute_path) or "$"
        formatted.append(f"{path}: {error.message}")
    return formatted
