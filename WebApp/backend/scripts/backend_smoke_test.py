from __future__ import annotations

import json
import sys
import time
import uuid
from datetime import datetime, timezone
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.parse import urlencode
from urllib.request import Request, urlopen

ROOT = Path(__file__).resolve().parent.parent
BASE_URL = sys.argv[1].rstrip("/") if len(sys.argv) > 1 else "http://127.0.0.1:8000"
API_KEY = sys.argv[2] if len(sys.argv) > 2 else "local-dev-key"
HEADERS = {"X-API-Key": API_KEY}


def now_z() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="milliseconds").replace("+00:00", "Z")


def call(method: str, path: str, body=None, content_type: str | None = None, extra_headers: dict | None = None):
    data = None
    headers = dict(HEADERS)
    if body is not None:
        if isinstance(body, bytes):
            data = body
        else:
            data = json.dumps(body, ensure_ascii=False).encode("utf-8")
        headers["Content-Type"] = content_type or "application/json"
    if extra_headers:
        headers.update(extra_headers)
    req = Request(BASE_URL + path, data=data, headers=headers, method=method)
    try:
        with urlopen(req, timeout=10) as resp:
            raw = resp.read()
            return resp.status, json.loads(raw) if raw else None
    except HTTPError as exc:
        raw = exc.read()
        detail = raw.decode("utf-8", errors="replace") if raw else ""
        raise RuntimeError(f"{method} {path} -> HTTP {exc.code}: {detail}") from exc
    except URLError as exc:
        raise RuntimeError(f"Backendへ接続できません: {exc}") from exc


def main() -> None:
    suffix = uuid.uuid4().hex[:8]
    source_id = f"backend-smoke-unity-{suffix}"
    session_id = f"smoke-session-{suffix}"
    run_id = f"{session_id}-run-0001"

    print("[1/6] Health")
    status, _ = call("GET", "/api/health")
    assert status == 200

    print("[2/6] Command polling target registration")
    query = urlencode({"sourceId": source_id, "sessionId": session_id, "runId": run_id, "limit": 10})
    status, batch = call("GET", "/api/v1/commands?" + query)
    assert status == 200 and batch["commands"] == []

    print("[3/6] Snapshot v1.1 persistence")
    snapshot = json.loads((ROOT / "tests" / "fixtures" / "snapshot_v1_1.json").read_text(encoding="utf-8-sig"))
    snapshot["snapshotId"] = f"smoke-snapshot-{uuid.uuid4()}"
    snapshot["sessionId"] = session_id
    snapshot["runId"] = run_id
    snapshot["generatedAtUtc"] = now_z()
    snapshot["sequenceNumber"] = 1
    status, _ = call("POST", "/api/v1/snapshots", snapshot)
    assert status == 204

    print("[4/6] Admin Command creation")
    idem = str(uuid.uuid4())
    command_request = {
        "targetSourceId": source_id,
        "targetSessionId": session_id,
        "targetRunId": run_id,
        "commandType": "SET_AREA_POLICY",
        "priority": 0,
        "issuedBy": "backend-smoke-test",
        "expiresInSeconds": 30,
        "payload": {"areaId": "A", "policy": "CLOSED", "reason": "backend smoke test"},
    }
    status, command_view = call("POST", "/api/v1/admin/commands", command_request, extra_headers={"Idempotency-Key": idem})
    assert status in (200, 201)
    command_id = command_view["commandId"]

    print("[5/6] Command delivery")
    # Unity Command contract polls every 1 second. Respect the backend's
    # over-polling guard instead of weakening COMMAND_MIN_POLL_INTERVAL_SECONDS.
    time.sleep(1.0)
    status, batch = call("GET", "/api/v1/commands?" + query)
    assert status == 200 and len(batch["commands"]) == 1
    assert batch["commands"][0]["commandId"] == command_id

    print("[6/6] Terminal Event -> SUCCEEDED")
    event = {
        "contractName": "smart-parking.simulation-event",
        "schemaVersion": "1.0",
        "eventId": f"smoke-event-{uuid.uuid4()}",
        "eventType": "command.succeeded",
        "sourceSystem": "unity",
        "generatedAtUtc": now_z(),
        "sceneName": "SmokeScene",
        "sessionId": session_id,
        "runId": run_id,
        "sequenceNumber": 1,
        "scenarioId": "smoke-scenario",
        "scenarioVersion": "1",
        "facilityId": "smoke-facility",
        "simulationTimeSeconds": 1.0,
        "correlationId": idem,
        "entityType": "command",
        "entityId": command_id,
        "commandId": command_id,
        "payload": {
            "command": {
                "idempotencyKey": idem,
                "commandType": "SET_AREA_POLICY",
                "status": "SUCCEEDED",
                "reasonCode": "POLICY_APPLIED",
                "message": "Smoke test succeeded.",
                "retryable": False,
                "result": {"areaId": "A", "previousPolicy": "NORMAL", "appliedPolicy": "CLOSED"},
            }
        },
    }
    ndjson = (json.dumps(event, ensure_ascii=False) + "\n").encode("utf-8")
    status, _ = call("POST", "/api/v1/events", ndjson, "application/x-ndjson")
    assert status == 204
    status, result = call("GET", f"/api/v1/admin/commands/{command_id}")
    assert status == 200 and result["status"] == "SUCCEEDED"
    print("OK: Backend P3 smoke test passed.")


if __name__ == "__main__":
    main()
