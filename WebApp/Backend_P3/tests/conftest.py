from __future__ import annotations

import os
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT))

TEST_DB = Path(__file__).resolve().parent / "test.db"
if TEST_DB.exists():
    TEST_DB.unlink()

os.environ["DATABASE_URL"] = f"sqlite+pysqlite:///{TEST_DB.as_posix()}"
os.environ["SMARTPARKING_LOCAL_API_KEY"] = "test-key"
os.environ["REQUIRE_API_KEY"] = "true"
os.environ["AUTO_CREATE_DB"] = "true"
os.environ["COMMAND_MIN_POLL_INTERVAL_SECONDS"] = "0"
os.environ["TARGET_STALE_SECONDS"] = "120"
os.environ["COMMAND_LEASE_SECONDS"] = "10"
os.environ["COMMAND_MAX_DELIVERIES"] = "3"
