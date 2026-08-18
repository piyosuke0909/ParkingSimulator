from __future__ import annotations

import copy
import json
import uuid
from datetime import datetime, timezone
from pathlib import Path

from fastapi.testclient import TestClient

from app.main import app

ROOT = Path(__file__).resolve().parent.parent
SAMPLE_SNAPSHOT = ROOT / 'tests' / 'fixtures' / 'snapshot_v1_1.json'
SAMPLE_EVENTS = ROOT / 'tests' / 'fixtures' / 'events_v1_0.jsonl'
HEADERS = {"X-API-Key": "test-key"}


def _snapshot() -> dict:
    return json.loads(SAMPLE_SNAPSHOT.read_text(encoding="utf-8-sig"))


def _events(n: int = 5) -> list[dict]:
    lines = [x for x in SAMPLE_EVENTS.read_text(encoding="utf-8-sig").splitlines() if x.strip()]
    return [json.loads(x) for x in lines[:n]]


def _ndjson(items: list[dict]) -> str:
    return "\n".join(json.dumps(x, ensure_ascii=False) for x in items) + "\n"


def test_health_auth() -> None:
    with TestClient(app) as client:
        assert client.get("/api/health").status_code == 401
        r = client.get("/api/health", headers=HEADERS)
        assert r.status_code == 200
        assert r.json()["status"] == "ok"


def test_snapshot_duplicate_and_conflict() -> None:
    payload = _snapshot()
    with TestClient(app) as client:
        r = client.post("/api/v1/snapshots", headers={**HEADERS, "Content-Type": "application/json"}, json=payload)
        assert r.status_code == 204
        r = client.post("/api/v1/snapshots", headers={**HEADERS, "Content-Type": "application/json"}, json=payload)
        assert r.status_code == 204
        changed = copy.deepcopy(payload)
        changed["summary"]["vehicleCount"] = changed["summary"]["vehicleCount"] + 1
        r = client.post("/api/v1/snapshots", headers={**HEADERS, "Content-Type": "application/json"}, json=changed)
        assert r.status_code == 409


def test_event_ndjson_duplicate() -> None:
    items = _events(5)
    body = _ndjson(items)
    with TestClient(app) as client:
        r = client.post("/api/v1/events", headers={**HEADERS, "Content-Type": "application/x-ndjson"}, content=body.encode("utf-8"))
        assert r.status_code == 204
        r = client.post("/api/v1/events", headers={**HEADERS, "Content-Type": "application/x-ndjson"}, content=body.encode("utf-8"))
        assert r.status_code == 204


def test_command_round_trip() -> None:
    snap = _snapshot()
    source_id = "unity-webgl-admin-01"
    session_id = snap["sessionId"]
    run_id = snap["runId"]
    with TestClient(app) as client:
        # Observe the Unity runtime target first.
        r = client.get(
            "/api/v1/commands",
            headers=HEADERS,
            params={"sourceId": source_id, "sessionId": session_id, "runId": run_id, "limit": 10},
        )
        assert r.status_code == 200
        assert r.json()["commands"] == []

        # Fresh Snapshot makes the target eligible for an Admin command.
        snap2 = copy.deepcopy(snap)
        snap2["snapshotId"] = snap2["snapshotId"] + "-command-test"
        snap2["sequenceNumber"] = int(snap2["sequenceNumber"]) + 1000
        snap2["generatedAtUtc"] = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
        r = client.post("/api/v1/snapshots", headers={**HEADERS, "Content-Type": "application/json"}, json=snap2)
        assert r.status_code == 204

        idem = str(uuid.uuid4())
        admin_body = {
            "targetSourceId": source_id,
            "targetSessionId": session_id,
            "targetRunId": run_id,
            "commandType": "SET_AREA_POLICY",
            "priority": 0,
            "issuedBy": "pytest",
            "expiresInSeconds": 30,
            "payload": {"areaId": "A", "policy": "CLOSED", "reason": "integration test"},
        }
        r = client.post(
            "/api/v1/admin/commands",
            headers={**HEADERS, "Idempotency-Key": idem},
            json=admin_body,
        )
        assert r.status_code == 201, r.text
        command_id = r.json()["commandId"]

        # Same Idempotency-Key + same request is idempotent.
        r2 = client.post(
            "/api/v1/admin/commands",
            headers={**HEADERS, "Idempotency-Key": idem},
            json=admin_body,
        )
        assert r2.status_code == 200
        assert r2.json()["commandId"] == command_id

        # Unity receives the command.
        r = client.get(
            "/api/v1/commands",
            headers=HEADERS,
            params={"sourceId": source_id, "sessionId": session_id, "runId": run_id, "limit": 10},
        )
        assert r.status_code == 200, r.text
        batch = r.json()
        assert len(batch["commands"]) == 1
        command = batch["commands"][0]
        assert command["commandId"] == command_id
        assert command["payload"]["policy"] == "CLOSED"

        # Simulate Unity terminal Event.
        terminal_event = {
            "contractName": "smart-parking.simulation-event",
            "schemaVersion": "1.0",
            "eventId": f"event-{uuid.uuid4()}",
            "eventType": "command.succeeded",
            "sourceSystem": "unity",
            "generatedAtUtc": "2026-08-18T07:00:00Z",
            "sceneName": "SampleScene",
            "sessionId": session_id,
            "runId": run_id,
            "sequenceNumber": 999999,
            "scenarioId": snap["scenario"]["scenarioId"],
            "scenarioVersion": snap["scenario"].get("scenarioVersion"),
            "facilityId": snap["scenario"].get("facilityId"),
            "simulationTimeSeconds": 123.0,
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
                    "message": "Policy applied.",
                    "retryable": False,
                    "result": {"areaId": "A", "previousPolicy": "NORMAL", "appliedPolicy": "CLOSED"},
                }
            },
        }
        r = client.post(
            "/api/v1/events",
            headers={**HEADERS, "Content-Type": "application/x-ndjson"},
            content=_ndjson([terminal_event]).encode("utf-8"),
        )
        assert r.status_code == 204, r.text

        r = client.get(f"/api/v1/admin/commands/{command_id}", headers=HEADERS)
        assert r.status_code == 200
        assert r.json()["status"] == "SUCCEEDED"
        assert r.json()["resultPayload"]["reasonCode"] == "POLICY_APPLIED"
