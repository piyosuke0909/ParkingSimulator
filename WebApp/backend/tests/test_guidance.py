from __future__ import annotations

import unittest

from services.state_store import store


class GuidanceTests(unittest.TestCase):
    def setUp(self) -> None:
        store.reservations.clear()
        store.logs.clear()

    def test_repeated_start_reuses_active_reservation(self) -> None:
        first = store.start_guidance("user-guidance-test", "A")
        second = store.start_guidance("user-guidance-test", "B")

        self.assertEqual(first["reservation"]["reservationId"], second["reservation"]["reservationId"])
        self.assertEqual(first["reservation"]["targetAreaId"], second["reservation"]["targetAreaId"])
        self.assertEqual(len(store.reservations), 1)


if __name__ == "__main__":
    unittest.main()
