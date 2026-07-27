import assert from "node:assert/strict";
import test from "node:test";

import { adaptMvpGuidance, deriveUserScreenState } from "./userGuidanceModel.ts";
import { emptyGuidanceResponse, readyGuidanceResponse, readyParkingStatus, staleParkingStatus } from "./userGuidanceFixtures.ts";

test("MVP response is adapted to canonical frontend names", () => {
  const model = adaptMvpGuidance({
    ...readyGuidanceResponse,
    reservation: {
      reservationId: "res-001",
      status: "active",
      targetAreaId: "area-a",
      targetSlotId: "slot-a-001",
      assignedCarId: "vehicle-001",
      expiresAt: "2026-07-17T03:05:00Z"
    },
    assignedCar: {
      carId: "vehicle-001",
      state: "driving_to_slot",
      targetAreaId: "area-a",
      targetSlotId: "slot-a-001"
    }
  });

  assert.equal(model.targetAreaId, "area-a");
  assert.equal(model.guidanceSession?.mvpReservationId, "res-001");
  assert.equal(model.guidanceSession?.vehicleId, "vehicle-001");
  assert.equal(model.assignedVehicle?.vehicleId, "vehicle-001");
  assert.equal(model.assignedVehicle?.vehicleState, "driving_to_slot");
});

test("screen state covers loading, ready, empty, error and stale", () => {
  const readyGuidance = adaptMvpGuidance(readyGuidanceResponse);
  const emptyGuidance = adaptMvpGuidance(emptyGuidanceResponse);

  assert.equal(deriveUserScreenState({ hasLoaded: false, parkingStatus: null, guidance: null, dataError: null }).phase, "loading");
  assert.equal(deriveUserScreenState({ hasLoaded: true, parkingStatus: readyParkingStatus, guidance: readyGuidance, dataError: null }).phase, "ready");
  assert.equal(deriveUserScreenState({ hasLoaded: true, parkingStatus: readyParkingStatus, guidance: emptyGuidance, dataError: null }).phase, "empty");
  assert.equal(deriveUserScreenState({ hasLoaded: true, parkingStatus: null, guidance: null, dataError: "接続できません" }).phase, "error");
  assert.equal(deriveUserScreenState({ hasLoaded: true, parkingStatus: staleParkingStatus, guidance: readyGuidance, dataError: null }).phase, "stale");
});

test("new guidance cannot start from error, empty or stale data", () => {
  const readyGuidance = adaptMvpGuidance(readyGuidanceResponse);

  assert.equal(deriveUserScreenState({ hasLoaded: true, parkingStatus: readyParkingStatus, guidance: readyGuidance, dataError: null }).canStartGuidance, true);
  assert.equal(deriveUserScreenState({ hasLoaded: true, parkingStatus: staleParkingStatus, guidance: readyGuidance, dataError: null }).canStartGuidance, false);
  assert.equal(deriveUserScreenState({ hasLoaded: true, parkingStatus: null, guidance: null, dataError: "error" }).canStartGuidance, false);
});

test("stale takes precedence over empty without inventing destination data", () => {
  const emptyGuidance = adaptMvpGuidance(emptyGuidanceResponse);
  const state = deriveUserScreenState({
    hasLoaded: true,
    parkingStatus: staleParkingStatus,
    guidance: emptyGuidance,
    dataError: null
  });

  assert.equal(state.phase, "stale");
  assert.equal(state.hasDestination, false);
  assert.equal(state.showRoute, false);
  assert.equal(state.canStartGuidance, false);
});
