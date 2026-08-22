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


def test_frontend_compat_admin_state_and_parking_status() -> None:
    snap = _snapshot()
    snap = copy.deepcopy(snap)
    snap["snapshotId"] = snap["snapshotId"] + "-frontend-compat"
    snap["sequenceNumber"] = int(snap["sequenceNumber"]) + 2000
    snap["generatedAtUtc"] = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
    with TestClient(app) as client:
        r = client.post("/api/v1/snapshots", headers={**HEADERS, "Content-Type": "application/json"}, json=snap)
        assert r.status_code == 204, r.text

        r = client.get("/api/admin/state", headers=HEADERS)
        assert r.status_code == 200, r.text
        admin = r.json()
        assert [area["areaId"] for area in admin["areas"]] == ["A", "B", "C", "D"]
        assert admin["summary"]["capacity"] == 240
        assert len(admin["slots"]) == 240
        assert "alerts" in admin and "guards" in admin and "logs" in admin

        r = client.get("/api/parking/status", headers=HEADERS)
        assert r.status_code == 200
        parking = r.json()
        assert parking["summary"]["capacity"] == 240
        assert len(parking["areas"]) == 4

        r = client.get("/api/parking/recommendation", headers=HEADERS, params={"userSessionId": "test-user"})
        assert r.status_code == 200
        assert r.json()["status"] in {"not_started", "unavailable"}


def test_latest_frontend_admin_command_compatibility() -> None:
    snap = copy.deepcopy(_snapshot())
    token = uuid.uuid4().hex[:8]
    source_id = f"unity-webgl-admin-latest-{token}"
    session_id = f"frontend-session-{token}"
    run_id = f"{session_id}-run-0001"
    snap["snapshotId"] = f"frontend-latest-snapshot-{token}"
    snap["sessionId"] = session_id
    snap["runId"] = run_id
    snap["sequenceNumber"] = 1
    snap["generatedAtUtc"] = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")

    with TestClient(app) as client:
        # Current Unity execution must be observed by Command polling.
        r = client.get(
            "/api/v1/commands",
            headers=HEADERS,
            params={"sourceId": source_id, "sessionId": session_id, "runId": run_id, "limit": 10},
        )
        assert r.status_code == 200, r.text

        # Latest Snapshot must match that session/run.
        r = client.post("/api/v1/snapshots", headers={**HEADERS, "Content-Type": "application/json"}, json=snap)
        assert r.status_code == 204, r.text

        # Latest Frontend expects these P3 compatibility fields from /api/admin/state.
        r = client.get("/api/admin/state", headers=HEADERS)
        assert r.status_code == 200, r.text
        state = r.json()
        assert state["commandTarget"] == {"sourceId": source_id, "sessionId": session_id, "runId": run_id}
        assert state["snapshotIdentity"]["sessionId"] == session_id
        assert state["snapshotIdentity"]["runId"] == run_id
        assert state["commandTargetMatchesSnapshot"] is True
        assert state["areaPolicies"]["A"] == "NORMAL"

        # Frontend does not supply target IDs; Backend resolves the active target from the latest Snapshot.
        idem = f"frontend-{token}"
        r = client.post(
            "/api/admin/commands",
            headers=HEADERS,
            json={
                "commandType": "SET_AREA_POLICY",
                "idempotencyKey": idem,
                "payload": {"areaId": "A", "policy": "CLOSED"},
            },
        )
        assert r.status_code == 201, r.text
        command = r.json()["command"]
        command_id = command["commandId"]
        assert command["targetSourceId"] == source_id
        assert command["status"] == "pending"
        assert command["deliveryAttempts"] == 0

        # Unity receives the generated command.
        r = client.get(
            "/api/v1/commands",
            headers=HEADERS,
            params={"sourceId": source_id, "sessionId": session_id, "runId": run_id, "limit": 10},
        )
        assert r.status_code == 200, r.text
        assert r.json()["commands"][0]["commandId"] == command_id

        terminal_event = {
            "contractName": "smart-parking.simulation-event",
            "schemaVersion": "1.0",
            "eventId": f"frontend-event-{token}",
            "eventType": "command.succeeded",
            "sourceSystem": "unity",
            "generatedAtUtc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
            "sceneName": "SampleScene",
            "sessionId": session_id,
            "runId": run_id,
            "sequenceNumber": 999001,
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

        r = client.get("/api/admin/state", headers=HEADERS)
        assert r.status_code == 200, r.text
        state = r.json()
        assert state["areaPolicies"]["A"] == "CLOSED"
        latest = next(item for item in state["commands"] if item["commandId"] == command_id)
        assert latest["status"] == "succeeded"
        assert latest["deliveryAttempts"] == 1
        assert latest["lastResult"]["eventType"] == "command.succeeded"
        assert latest["lastResult"]["payload"]["command"]["reasonCode"] == "POLICY_APPLIED"
