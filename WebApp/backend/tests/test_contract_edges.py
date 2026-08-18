from __future__ import annotations

import asyncio
import json
import unittest
from datetime import UTC, datetime

from fastapi import HTTPException

from routers.v1 import (
    MAX_EVENT_BATCH_COUNT,
    MAX_EVENT_REQUEST_BYTES,
    MAX_SNAPSHOT_REQUEST_BYTES,
    receive_events,
    receive_snapshot,
)
from services.api_auth import require_local_api_key


class FakeRequest:
    def __init__(self, body: bytes, content_type: str) -> None:
        self._body = body
        self.headers = {"content-type": content_type}

    async def body(self) -> bytes:
        return self._body


class ContractEdgeTests(unittest.TestCase):
    def test_local_api_key_is_required(self) -> None:
        with self.assertRaises(HTTPException) as missing:
            require_local_api_key(None)
        self.assertEqual(missing.exception.status_code, 401)

        with self.assertRaises(HTTPException) as invalid:
            require_local_api_key("wrong-key")
        self.assertEqual(invalid.exception.status_code, 401)
        self.assertIsNone(require_local_api_key("local-dev-key"))

    def test_snapshot_content_type_and_size_limits(self) -> None:
        with self.assertRaises(HTTPException) as content_type:
            asyncio.run(receive_snapshot(FakeRequest(b"{}", "text/plain")))
        self.assertEqual(content_type.exception.status_code, 415)

        oversized = b"x" * (MAX_SNAPSHOT_REQUEST_BYTES + 1)
        with self.assertRaises(HTTPException) as payload_size:
            asyncio.run(receive_snapshot(FakeRequest(oversized, "application/json")))
        self.assertEqual(payload_size.exception.status_code, 413)

    def test_snapshot_must_match_official_schema(self) -> None:
        invalid = {
            "contractName": "smart-parking.simulation-snapshot",
            "schemaVersion": "1.1",
            "snapshotType": "simulation_snapshot",
            "snapshotId": "missing-required-fields",
        }
        with self.assertRaises(HTTPException) as schema_error:
            asyncio.run(
                receive_snapshot(
                    FakeRequest(json.dumps(invalid).encode(), "application/json")
                )
            )
        self.assertEqual(schema_error.exception.status_code, 422)

    def test_event_must_match_official_schema(self) -> None:
        invalid = {
            "contractName": "smart-parking.simulation-event",
            "schemaVersion": "1.0",
            "eventId": "invalid-schema-event",
            "eventType": "scenario.started",
            "sourceSystem": "unity",
            "generatedAtUtc": datetime.now(UTC).isoformat(),
            "sceneName": "SampleScene",
            "sessionId": "session",
            "runId": "run",
            "sequenceNumber": 1,
            "scenarioId": "scenario",
            "simulationTimeSeconds": 0,
            "entityType": "scenario",
            "entityId": "scenario",
            "payload": {},
        }
        body = (json.dumps(invalid) + "\n").encode()
        with self.assertRaises(HTTPException) as schema_error:
            asyncio.run(
                receive_events(FakeRequest(body, "application/x-ndjson"))
            )
        self.assertEqual(schema_error.exception.status_code, 422)

    def test_event_batch_count_and_size_limits(self) -> None:
        too_many = ("{}\n" * (MAX_EVENT_BATCH_COUNT + 1)).encode()
        with self.assertRaises(HTTPException) as batch_count:
            asyncio.run(
                receive_events(FakeRequest(too_many, "application/x-ndjson"))
            )
        self.assertEqual(batch_count.exception.status_code, 422)

        oversized = b"x" * (MAX_EVENT_REQUEST_BYTES + 1)
        with self.assertRaises(HTTPException) as payload_size:
            asyncio.run(
                receive_events(FakeRequest(oversized, "application/x-ndjson"))
            )
        self.assertEqual(payload_size.exception.status_code, 413)


if __name__ == "__main__":
    unittest.main()
