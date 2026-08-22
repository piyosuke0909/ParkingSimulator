from __future__ import annotations

import os
from dataclasses import dataclass
from pathlib import Path

from dotenv import load_dotenv

BASE_DIR = Path(__file__).resolve().parent.parent
load_dotenv(BASE_DIR / ".env", override=False)


def _bool(name: str, default: bool = False) -> bool:
    raw = os.getenv(name)
    if raw is None:
        return default
    return raw.strip().lower() in {"1", "true", "yes", "on"}


def _int(name: str, default: int) -> int:
    raw = os.getenv(name)
    if not raw:
        return default
    return int(raw)


def _float(name: str, default: float) -> float:
    raw = os.getenv(name)
    if not raw:
        return default
    return float(raw)


def _cors_origins() -> list[str]:
    raw = os.getenv("CORS_ORIGINS", "")
    if raw.strip():
        return [x.strip() for x in raw.split(",") if x.strip()]
    return [
        "http://localhost:3000",
        "http://127.0.0.1:3000",
        "http://localhost:5173",
        "http://127.0.0.1:5173",
        "http://localhost:8000",
        "http://127.0.0.1:8000",
    ]


@dataclass(frozen=True)
class Settings:
    app_name: str = os.getenv("APP_NAME", "SmartParking P3 Backend")
    app_env: str = os.getenv("APP_ENV", "local")
    database_url: str = os.getenv(
        "DATABASE_URL",
        "postgresql+psycopg://smartparking:smartparking@localhost:5432/smartparking",
    )
    api_key: str = os.getenv("SMARTPARKING_LOCAL_API_KEY", "")
    require_api_key: bool = _bool("REQUIRE_API_KEY", True)
    cors_origins: list[str] = None  # type: ignore[assignment]
    target_stale_seconds: int = _int("TARGET_STALE_SECONDS", 120)
    runtime_online_seconds: int = _int("RUNTIME_ONLINE_SECONDS", 5)
    command_lease_seconds: int = _int("COMMAND_LEASE_SECONDS", 10)
    command_max_deliveries: int = _int("COMMAND_MAX_DELIVERIES", 3)
    command_default_lifetime_seconds: int = _int("COMMAND_DEFAULT_LIFETIME_SECONDS", 30)
    command_min_poll_interval_seconds: float = _float("COMMAND_MIN_POLL_INTERVAL_SECONDS", 0.5)
    max_event_bytes: int = _int("MAX_EVENT_BYTES", 1024 * 1024)
    max_snapshot_bytes: int = _int("MAX_SNAPSHOT_BYTES", 5 * 1024 * 1024)
    max_event_count: int = _int("MAX_EVENT_COUNT", 50)
    max_command_count: int = _int("MAX_COMMAND_COUNT", 10)
    auto_create_db: bool = _bool("AUTO_CREATE_DB", False)

    def __post_init__(self) -> None:
        object.__setattr__(self, "cors_origins", _cors_origins())


settings = Settings()
