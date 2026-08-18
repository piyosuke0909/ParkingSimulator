#!/usr/bin/env python3
"""End-to-end smoke test for the confirmed SmartParking Command v1 contract."""

from __future__ import annotations

import argparse
import json
import os
import sys
from datetime import UTC, datetime
from typing import Any
from urllib.error import HTTPError, URLError
from urllib.parse import urlencode
from urllib.request import Request, urlopen
from uuid import uuid4


class SmokeTestError(RuntimeError):
    pass


def utc_now() -> str:
    return datetime.now(UTC).isoformat().replace("+00:00", "Z")


def request(
    base_url: str,
    path: str,
    *,
    api_key: str | None,
    method: str = "GET",
    body: dict[str, Any] | str | None = None,
    content_type: str = "application/json",
    expected_status: int,
) -> Any:
    headers = {"Accept": "application/json"}
    if api_key:
        headers["X-API-Key"] = api_key
    data = None
    if body is not None:
        headers["Content-Type"] = content_type
        data = body.encode("utf-8") if isinstance(body, str) else json.dumps(body).encode("utf-8")

    target = f"{base_url.rstrip('/')}{path}"
    try:
        with urlopen(Request(target, data=data, headers=headers, method=method), timeout=10) as response:
            status = response.status
            response_body = response.read().decode("utf-8")
    except HTTPError as error:
        status = error.code
        response_body = error.read().decode("utf-8", errors="replace")
    except URLError as error:
        raise SmokeTestError(f"Could not connect to {target}: {error.reason}") from error

    if status != expected_status:
        detail = response_body[:500] if response_body else "<empty body>"
        raise SmokeTestError(f"{method} {path}: expected HTTP {expected_status}, got {status}: {detail}")
    if not response_body:
        return None
    try:
        return json.loads(response_body)
    except json.JSONDecodeError as error:
        raise SmokeTestError(f"{method} {path}: response was not JSON") from error


def snapshot(session_id: str, run_id: str, unique: str) -> dict[str, Any]:
    return {
        "contractName": "smart-parking.simulation-snapshot",
        "schemaVersion": "1.1",
        "snapshotType": "simulation_snapshot",
        "snapshotId": f"smoke-snapshot-{unique}",
        "sourceSystem": "unity",
        "generatedAtUtc": utc_now(),
        "sceneName": "SmokeTestScene",
        "sessionId": session_id,
        "runId": run_id,
        "sequenceNumber": 1,
        "scenario": {
            "scenarioId": "smoke-scenario",
            "scenarioVersion": "1.0",
            "scenarioName": "Command v1 smoke test",
            "facilityId": "smoke-facility",
            "mapVersion": "1.0",
            "timeOfDay": "day",
            "randomSeed": 1,
            "randomSeedPolicy": "fixed",
            "simulationTimeSeconds": 0,
            "durationSeconds": 60,
            "isCompleted": False,
        },
        "runtime": {
            "arrivalRateSourceId": "smoke",
            "arrivalDistribution": "fixed",
            "baseVehiclesPerMinute": 0,
            "arrivalRateMultiplier": 1,
            "effectiveVehiclesPerMinute": 0,
            "vehicleSpeedMultiplier": 1,
            "activeVehicleCount": 0,
            "discoveredVehicleCount": 0,
            "maxConcurrentVehicles": 1,
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


def command_result_event(
    command: dict[str, Any], session_id: str, run_id: str, unique: str
) -> dict[str, Any]:
    return {
        "contractName": "smart-parking.simulation-event",
        "schemaVersion": "1.0",
        "eventId": f"smoke-event-{unique}",
        "eventType": "command.succeeded",
        "sourceSystem": "unity",
        "generatedAtUtc": utc_now(),
        "sceneName": "SmokeTestScene",
        "sessionId": session_id,
        "runId": run_id,
        "sequenceNumber": 2,
        "scenarioId": "smoke-scenario",
        "simulationTimeSeconds": 1,
        "correlationId": command["idempotencyKey"],
        "entityType": "command",
        "entityId": command["commandId"],
        "commandId": command["commandId"],
        "payload": {
            "command": {
                "idempotencyKey": command["idempotencyKey"],
                "commandType": command["commandType"],
                "status": "SUCCEEDED",
                "reasonCode": "POLICY_APPLIED",
                "message": "Command v1 smoke test completed.",
                "retryable": False,
                "result": {
                    "areaId": "A",
                    "previousPolicy": "NORMAL",
                    "appliedPolicy": "NORMAL",
                },
            }
        },
    }


def assert_equal(actual: Any, expected: Any, label: str) -> None:
    if actual != expected:
        raise SmokeTestError(f"{label}: expected {expected!r}, got {actual!r}")


def run(base_url: str, api_key: str) -> None:
    unique = uuid4().hex
    source_id = f"smoke-unity-{unique[:8]}"
    session_id = f"smoke-session-{unique}"
    run_id = f"smoke-run-{unique}"
    headers_query = urlencode(
        {"sourceId": source_id, "sessionId": session_id, "runId": run_id, "limit": 10}
    )

    request(base_url, "/api/health", api_key=None, expected_status=401)
    request(base_url, "/api/health", api_key=api_key, expected_status=200)
    request(
        base_url,
        "/api/v1/snapshots",
        api_key=api_key,
        method="POST",
        body=snapshot(session_id, run_id, unique),
        expected_status=204,
    )

    empty_batch = request(
        base_url,
        f"/api/v1/commands?{headers_query}",
        api_key=api_key,
        expected_status=200,
    )
    assert_equal(empty_batch["contractName"], "smart-parking.command-batch", "batch contract")
    assert_equal(empty_batch["schemaVersion"], "1.0", "batch schema version")
    assert_equal(empty_batch["leaseSeconds"], 10, "batch lease")
    assert_equal(empty_batch["commands"], [], "initial command list")

    created_response = request(
        base_url,
        "/api/admin/commands",
        api_key=api_key,
        method="POST",
        body={
            "commandType": "SET_AREA_POLICY",
            "idempotencyKey": f"smoke-idempotency-{unique}",
            "expiresInSeconds": 300,
            "priority": 100,
            "issuedBy": "command-v1-smoke-test",
            "reason": "integration acceptance check",
            "payload": {"areaId": "A", "policy": "NORMAL", "reason": "smoke test"},
        },
        expected_status=201,
    )
    created = created_response["command"]

    batch = request(
        base_url,
        f"/api/v1/commands?{headers_query}",
        api_key=api_key,
        expected_status=200,
    )
    assert_equal(len(batch["commands"]), 1, "delivered command count")
    delivered = batch["commands"][0]
    for field, expected in (
        ("commandId", created["commandId"]),
        ("idempotencyKey", created["idempotencyKey"]),
        ("targetSourceId", source_id),
        ("targetSessionId", session_id),
        ("targetRunId", run_id),
        ("commandType", "SET_AREA_POLICY"),
        ("priority", 100),
    ):
        assert_equal(delivered[field], expected, f"command {field}")

    event = command_result_event(delivered, session_id, run_id, unique)
    request(
        base_url,
        "/api/v1/events",
        api_key=api_key,
        method="POST",
        body=json.dumps(event, ensure_ascii=False) + "\n",
        content_type="application/x-ndjson",
        expected_status=204,
    )

    admin_state = request(base_url, "/api/admin/state", api_key=api_key, expected_status=200)
    result = next(
        (item for item in admin_state["commands"] if item["commandId"] == created["commandId"]),
        None,
    )
    if result is None:
        raise SmokeTestError("completed command was not returned by /api/admin/state")
    assert_equal(result["status"], "succeeded", "terminal command status")
    assert_equal(admin_state["areaPolicies"]["A"], "NORMAL", "applied area policy")


def main() -> int:
    parser = argparse.ArgumentParser(description="Smoke-test SmartParking Command v1 end to end")
    parser.add_argument(
        "--base-url",
        default=os.getenv("SMARTPARKING_BASE_URL", "http://127.0.0.1:8000"),
        help="Backend base URL (default: SMARTPARKING_BASE_URL or local port 8000)",
    )
    parser.add_argument(
        "--api-key",
        default=os.getenv("SMARTPARKING_LOCAL_API_KEY", "local-dev-key"),
        help="Local API key (default: SMARTPARKING_LOCAL_API_KEY or local-dev-key)",
    )
    args = parser.parse_args()

    try:
        run(args.base_url, args.api_key)
    except SmokeTestError as error:
        print(f"Command v1 smoke test: FAILED\n{error}", file=sys.stderr)
        return 1
    print("Command v1 smoke test: PASSED")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
