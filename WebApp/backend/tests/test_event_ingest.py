from __future__ import annotations

import asyncio
import json
import unittest
from datetime import UTC, datetime

from fastapi import HTTPException

from routers.v1 import receive_events


class FakeRequest:
    def __init__(self, body: bytes, content_type: str = "application/x-ndjson") -> None:
        self._body = body
        self.headers = {"content-type": content_type}

    async def body(self) -> bytes:
        return self._body


class EventIngestTests(unittest.TestCase):
    def event(self, event_id: str, event_type: str = "scenario.started") -> dict:
        return {
            "contractName": "smart-parking.simulation-event",
            "schemaVersion": "1.0",
            "eventId": event_id,
            "eventType": event_type,
            "sourceSystem": "unity",
            "generatedAtUtc": datetime.now(UTC).isoformat(),
            "sceneName": "SampleScene",
            "sessionId": "session-event-test",
            "runId": "run-event-test",
            "sequenceNumber": 1,
            "scenarioId": "scenario-test",
            "simulationTimeSeconds": 1,
            "entityType": "scenario",
            "entityId": "scenario-test",
            "payload": {"scenario": {}},
        }

    def test_ndjson_accepts_and_deduplicates_same_event(self) -> None:
        body = (json.dumps(self.event("event-ingest-1")) + "\n").encode()
        first = asyncio.run(receive_events(FakeRequest(body)))
        second = asyncio.run(receive_events(FakeRequest(body)))
        self.assertEqual(first.status_code, 204)
        self.assertEqual(second.status_code, 204)

    def test_event_id_payload_conflict_returns_409(self) -> None:
        first = (json.dumps(self.event("event-ingest-conflict")) + "\n").encode()
        changed = (json.dumps(self.event("event-ingest-conflict", "scenario.completed")) + "\n").encode()
        asyncio.run(receive_events(FakeRequest(first)))
        with self.assertRaises(HTTPException) as raised:
            asyncio.run(receive_events(FakeRequest(changed)))
        self.assertEqual(raised.exception.status_code, 409)

    def test_content_type_must_be_ndjson(self) -> None:
        body = (json.dumps(self.event("event-wrong-content-type")) + "\n").encode()
        with self.assertRaises(HTTPException) as raised:
            asyncio.run(receive_events(FakeRequest(body, "application/json")))
        self.assertEqual(raised.exception.status_code, 415)


if __name__ == "__main__":
    unittest.main()
