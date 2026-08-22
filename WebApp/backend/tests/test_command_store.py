from __future__ import annotations

import unittest
from datetime import UTC, datetime, timedelta

from models.v1 import CreateAreaPolicyCommandRequest, P2CommandEvent, SetAreaPolicyPayload
from services.command_store import CommandConflictError, CommandStore


class CommandStoreTests(unittest.TestCase):
    def setUp(self) -> None:
        self.store = CommandStore()
        self.target = self.store.register_target("unity-webgl-admin-01", "session-1", "run-1")
        self.request = CreateAreaPolicyCommandRequest(
            payload=SetAreaPolicyPayload(areaId="A", policy="PRIORITY"),
            idempotencyKey="test-area-a-priority",
        )

    def command_event(self, command: dict, event_id: str, event_type: str) -> P2CommandEvent:
        status = event_type.split(".", 1)[1].upper()
        return P2CommandEvent.model_validate(
            {
                "contractName": "smart-parking.simulation-event",
                "schemaVersion": "1.0",
                "eventId": event_id,
                "eventType": event_type,
                "sourceSystem": "unity",
                "generatedAtUtc": datetime.now(UTC).isoformat(),
                "sceneName": "SampleScene",
                "sessionId": command["targetSessionId"],
                "runId": command["targetRunId"],
                "sequenceNumber": 1,
                "scenarioId": "scenario-test",
                "simulationTimeSeconds": 1,
                "entityType": "command",
                "entityId": command["commandId"],
                "commandId": command["commandId"],
                "payload": {
                    "command": {
                        "idempotencyKey": command["idempotencyKey"],
                        "commandType": "SET_AREA_POLICY",
                        "status": status,
                        "reasonCode": "POLICY_APPLIED" if event_type == "command.succeeded" else "",
                        "message": "",
                        "retryable": False,
                        "result": {
                            "areaId": "A",
                            "previousPolicy": "NORMAL",
                            "appliedPolicy": "PRIORITY",
                        },
                    }
                },
            }
        )

    def test_create_poll_and_complete_area_policy(self) -> None:
        created = self.store.create(self.request, self.target)
        self.assertEqual(created["status"], "pending")

        batch = self.store.poll(self.target, 10)
        self.assertEqual(batch.contractName, "smart-parking.command-batch")
        self.assertEqual(batch.leaseSeconds, 10)
        self.assertEqual(batch.commands[0].targetSourceId, "unity-webgl-admin-01")

        completed = self.store.record_event(
            self.command_event(created, "event-1", "command.succeeded")
        )
        self.assertEqual(completed["status"], "succeeded")
        self.assertEqual(self.store.admin_summary()["areaPolicies"]["A"], "PRIORITY")
        self.assertEqual(self.store.poll(self.target, 10).commands, [])

    def test_priority_controls_delivery_order(self) -> None:
        low = self.store.create(self.request, self.target)
        high_request = CreateAreaPolicyCommandRequest(
            payload=SetAreaPolicyPayload(areaId="B", policy="CLOSED"),
            idempotencyKey="test-area-b-closed",
            priority=100,
        )
        high = self.store.create(high_request, self.target)
        command_ids = [item.commandId for item in self.store.poll(self.target, 10).commands]
        self.assertEqual(command_ids, [high["commandId"], low["commandId"]])

    def test_same_idempotency_key_returns_same_command(self) -> None:
        first = self.store.create(self.request, self.target)
        second = self.store.create(self.request, self.target)
        self.assertEqual(first["commandId"], second["commandId"])

    def test_conflicting_idempotency_key_is_rejected(self) -> None:
        self.store.create(self.request, self.target)
        changed = CreateAreaPolicyCommandRequest(
            payload=SetAreaPolicyPayload(areaId="A", policy="CLOSED"),
            idempotencyKey=self.request.idempotencyKey,
        )
        with self.assertRaises(CommandConflictError):
            self.store.create(changed, self.target)

    def test_idempotency_compares_all_request_fields(self) -> None:
        self.store.create(self.request, self.target)
        changed = CreateAreaPolicyCommandRequest(
            payload=self.request.payload,
            idempotencyKey=self.request.idempotencyKey,
            expiresInSeconds=self.request.expiresInSeconds + 10,
        )
        with self.assertRaises(CommandConflictError):
            self.store.create(changed, self.target)

    def test_result_target_must_match(self) -> None:
        created = self.store.create(self.request, self.target)
        event = self.command_event(created, "event-wrong-target", "command.rejected")
        event.sessionId = "session-other"
        with self.assertRaises(CommandConflictError):
            self.store.record_event(event)

    def test_event_status_and_success_result_must_match_command(self) -> None:
        created = self.store.create(self.request, self.target)
        wrong_status = self.command_event(created, "event-wrong-status", "command.succeeded")
        wrong_status.payload["command"]["status"] = "FAILED"
        with self.assertRaises(CommandConflictError):
            self.store.record_event(wrong_status)

        wrong_result = self.command_event(created, "event-wrong-result", "command.succeeded")
        wrong_result.payload["command"]["result"]["appliedPolicy"] = "CLOSED"
        with self.assertRaises(CommandConflictError):
            self.store.record_event(wrong_result)

    def test_late_progress_event_does_not_overwrite_terminal_result(self) -> None:
        created = self.store.create(self.request, self.target)
        succeeded = self.command_event(created, "event-succeeded", "command.succeeded")
        completed = self.store.record_event(succeeded)
        self.assertEqual(completed["status"], "succeeded")

        late_started = self.command_event(created, "event-late-started", "command.started")
        unchanged = self.store.record_event(late_started)
        self.assertEqual(unchanged["status"], "succeeded")
        self.assertEqual(unchanged["lastResult"]["eventId"], "event-succeeded")

    def test_conflicting_terminal_event_is_rejected(self) -> None:
        created = self.store.create(self.request, self.target)
        self.store.record_event(self.command_event(created, "event-succeeded", "command.succeeded"))
        with self.assertRaises(CommandConflictError):
            self.store.record_event(
                self.command_event(created, "event-conflicting-terminal", "command.failed")
            )

    def test_area_policy_returns_to_normal_after_effective_until(self) -> None:
        current = datetime.now(UTC)
        self.store.now = lambda: current
        request = CreateAreaPolicyCommandRequest(
            payload=SetAreaPolicyPayload(
                areaId="A",
                policy="PRIORITY",
                effectiveUntilUtc=current + timedelta(seconds=20),
            ),
            idempotencyKey="test-expiring-policy",
        )
        created = self.store.create(request, self.target)
        self.store.record_event(
            self.command_event(created, "event-expiring-policy", "command.succeeded")
        )
        self.assertEqual(self.store.admin_summary()["areaPolicies"]["A"], "PRIORITY")

        current += timedelta(seconds=20)
        self.assertEqual(self.store.admin_summary()["areaPolicies"]["A"], "NORMAL")

    def test_delivery_lease_and_retry_limit(self) -> None:
        current = datetime(2026, 8, 18, tzinfo=UTC)
        self.store.now = lambda: current
        created = self.store.create(self.request, self.target)

        self.assertEqual(len(self.store.poll(self.target, 10).commands), 1)
        current += timedelta(seconds=9)
        self.assertEqual(self.store.poll(self.target, 10).commands, [])

        current += timedelta(seconds=1)
        self.assertEqual(len(self.store.poll(self.target, 10).commands), 1)
        current += timedelta(seconds=10)
        self.assertEqual(len(self.store.poll(self.target, 10).commands), 1)
        current += timedelta(seconds=10)
        self.assertEqual(self.store.poll(self.target, 10).commands, [])

        command = next(
            item
            for item in self.store.admin_summary()["commands"]
            if item["commandId"] == created["commandId"]
        )
        self.assertEqual(command["deliveryAttempts"], 3)
        self.assertEqual(command["status"], "timed_out")


if __name__ == "__main__":
    unittest.main()
