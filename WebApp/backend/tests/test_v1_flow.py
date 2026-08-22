from __future__ import annotations

import asyncio
import json
import unittest
from datetime import UTC, datetime

from fastapi import HTTPException

from models.schemas import UnitySnapshot
from models.v1 import CreateAreaPolicyCommandRequest, SetAreaPolicyPayload
from routers.admin import create_command, get_admin_state
from routers.v1 import get_commands, receive_events, receive_snapshot
from services.command_store import command_store
from services.state_store import store


class FakeRequest:
    def __init__(self, body: bytes, content_type: str = "application/x-ndjson") -> None:
        self._body = body
        self.headers = {"content-type": content_type}

    async def body(self) -> bytes:
        return self._body


class V1FlowTests(unittest.TestCase):
    def setUp(self) -> None:
        command_store.reset()
        store.latest_snapshot = None
        store.latest_sequence_by_source_scene.clear()
        store.snapshot_version = 0
        store.snapshot_received_at = None
        store.logs.clear()

    def test_snapshot_admin_command_unity_poll_and_event_result(self) -> None:
        empty_batch = get_commands(
            "unity-webgl-admin-01",
            "unity-session-test",
            "unity-session-test-run-0001",
            10,
        )
        self.assertEqual(empty_batch["commands"], [])

        snapshot = {
            "contractName": "smart-parking.simulation-snapshot",
            "schemaVersion": "1.1",
            "snapshotType": "simulation_snapshot",
            "snapshotId": "snapshot-1",
            "sourceSystem": "unity",
            "generatedAtUtc": datetime.now(UTC).isoformat(),
            "sceneName": "SampleScene",
            "sessionId": "unity-session-test",
            "runId": "unity-session-test-run-0001",
            "sequenceNumber": 1,
            "scenario": {
                "scenarioId": "scenario-test",
                "scenarioVersion": "1.0",
                "scenarioName": "Test",
                "facilityId": "facility-test",
                "mapVersion": "1.0",
                "timeOfDay": "day",
                "randomSeed": 1,
                "randomSeedPolicy": "fixed",
                "simulationTimeSeconds": 0,
                "durationSeconds": 60,
                "isCompleted": False,
            },
            "runtime": {
                "arrivalRateSourceId": "test-source",
                "arrivalDistribution": "fixed",
                "baseVehiclesPerMinute": 1,
                "arrivalRateMultiplier": 1,
                "effectiveVehiclesPerMinute": 1,
                "vehicleSpeedMultiplier": 1,
                "activeVehicleCount": 0,
                "discoveredVehicleCount": 0,
                "maxConcurrentVehicles": 10,
                "totalSpawnedCount": 0,
            },
            "summary": {
                "totalSlotCount": 0,
                "availableSlotCount": 0,
                "emptySlotCount": 0,
                "reservedSlotCount": 0,
                "occupiedSlotCount": 0,
                "leavingSlotCount": 0,
                "disabledSlotCount": 0,
                "areaCount": 0,
                "accessPointCount": 0,
                "vehicleCount": 0,
                "activeScenarioFactorCount": 0,
            },
            "activeScenarioFactors": [],
            "accessPoints": [],
            "areas": [],
            "parkingSlots": [],
            "vehicles": [],
        }
        response = asyncio.run(
            receive_snapshot(
                FakeRequest(json.dumps(snapshot).encode(), "application/json")
            )
        )
        self.assertEqual(response.status_code, 204)
        duplicate = asyncio.run(
            receive_snapshot(
                FakeRequest(json.dumps(snapshot).encode(), "application/json")
            )
        )
        self.assertEqual(duplicate.status_code, 204)
        changed_snapshot = {**snapshot, "sceneName": "DifferentScene"}
        with self.assertRaises(HTTPException) as conflict:
            asyncio.run(
                receive_snapshot(
                    FakeRequest(json.dumps(changed_snapshot).encode(), "application/json")
                )
            )
        self.assertEqual(conflict.exception.status_code, 409)

        created = create_command(
            CreateAreaPolicyCommandRequest(
                payload=SetAreaPolicyPayload(areaId="A", policy="CLOSED"),
                idempotencyKey="flow-a-closed",
            )
        )["command"]
        self.assertTrue(get_admin_state()["commandTargetMatchesSnapshot"])
        batch = get_commands(
            "unity-webgl-admin-01",
            "unity-session-test",
            "unity-session-test-run-0001",
            10,
        )
        self.assertEqual(batch["contractName"], "smart-parking.command-batch")
        self.assertEqual([created["commandId"]], [item["commandId"] for item in batch["commands"]])

        event = {
            "contractName": "smart-parking.simulation-event",
            "schemaVersion": "1.0",
            "eventId": "flow-result-1",
            "eventType": "command.succeeded",
            "sourceSystem": "unity",
            "generatedAtUtc": datetime.now(UTC).isoformat(),
            "sceneName": "SampleScene",
            "sessionId": "unity-session-test",
            "runId": "unity-session-test-run-0001",
            "sequenceNumber": 2,
            "scenarioId": "scenario-test",
            "simulationTimeSeconds": 2,
            "correlationId": created["idempotencyKey"],
            "entityType": "command",
            "entityId": created["commandId"],
            "commandId": created["commandId"],
            "payload": {
                "command": {
                    "idempotencyKey": created["idempotencyKey"],
                    "commandType": "SET_AREA_POLICY",
                    "status": "SUCCEEDED",
                    "reasonCode": "POLICY_APPLIED",
                    "message": "Policy applied.",
                    "retryable": False,
                    "result": {"areaId": "A", "previousPolicy": "NORMAL", "appliedPolicy": "CLOSED"},
                }
            },
        }
        result = asyncio.run(receive_events(FakeRequest((json.dumps(event) + "\n").encode())))
        self.assertEqual(result.status_code, 204)
        admin_state = get_admin_state()
        self.assertEqual(admin_state["areaPolicies"]["A"], "CLOSED")
        self.assertEqual(admin_state["commands"][0]["status"], "succeeded")

    def test_admin_command_rejects_different_snapshot_run(self) -> None:
        get_commands(
            "unity-webgl-admin-01",
            "command-session",
            "command-run",
            10,
        )
        store.ingest_snapshot(
            UnitySnapshot(
                sourceId="unity",
                scene="SampleScene",
                sessionId="different-session",
                runId="different-run",
                sequenceNumber=1,
                timestamp=datetime.now(UTC),
            )
        )

        self.assertFalse(get_admin_state()["commandTargetMatchesSnapshot"])
        with self.assertRaises(HTTPException) as mismatch:
            create_command(
                CreateAreaPolicyCommandRequest(
                    payload=SetAreaPolicyPayload(areaId="A", policy="PRIORITY"),
                    idempotencyKey="mismatched-run-command",
                )
            )
        self.assertEqual(mismatch.exception.status_code, 409)


if __name__ == "__main__":
    unittest.main()
