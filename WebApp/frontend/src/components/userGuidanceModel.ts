import type { MvpGuidanceResponse, ParkingStatus, RiskLevel } from "./types";

export type GuidanceSessionView = {
  mvpReservationId: string;
  status: string;
  targetAreaId: string;
  targetSlotId: string | null;
  vehicleId: string | null;
  expiresAt: string;
};

export type AssignedVehicleView = {
  vehicleId: string;
  vehicleState: string;
  position?: { x: number; y: number; z: number } | null;
  mapPosition?: { x: number; y: number } | null;
  targetSlotId?: string | null;
  targetAreaId?: string | null;
  isStoppedByFrontCar?: boolean;
};

export type UserGuidanceModel = {
  guidanceLevel: "area" | "slot";
  status: "active" | "not_started" | "unavailable";
  updatedAt?: string;
  targetAreaId: string | null;
  targetAreaLabel: string | null;
  targetAreaReason: string | null;
  targetSlotId: string | null;
  targetSlot: MvpGuidanceResponse["recommendedSlot"];
  message: string | null;
  replanReason: string | null;
  guidanceSession: GuidanceSessionView | null;
  assignedVehicle: AssignedVehicleView | null;
  route: MvpGuidanceResponse["route"];
  summary: {
    emptyCount: number;
    effectiveAvailable: number;
    congestionLevel: RiskLevel;
  } | null;
};

export type UserScreenPhase = "loading" | "ready" | "empty" | "error" | "stale";

export type UserScreenState = {
  phase: UserScreenPhase;
  canStartGuidance: boolean;
  hasDestination: boolean;
  showRoute: boolean;
  retryable: boolean;
};

export function adaptMvpGuidance(response: MvpGuidanceResponse): UserGuidanceModel {
  const mvpReservation = response.reservation;
  const mvpAssignedCar = response.assignedCar;

  return {
    guidanceLevel: response.guidanceLevel,
    status: response.status,
    updatedAt: response.updatedAt,
    targetAreaId: response.targetArea?.areaId ?? null,
    targetAreaLabel: response.targetArea?.label ?? null,
    targetAreaReason: response.targetArea?.reason ?? null,
    targetSlotId: response.recommendedSlotId ?? response.recommendedSlot?.slotId ?? null,
    targetSlot: response.recommendedSlot ?? null,
    message: response.message ?? null,
    replanReason: response.replanReason ?? null,
    guidanceSession: mvpReservation
      ? {
          mvpReservationId: mvpReservation.reservationId,
          status: mvpReservation.status,
          targetAreaId: mvpReservation.targetAreaId,
          targetSlotId: mvpReservation.targetSlotId ?? null,
          vehicleId: mvpReservation.assignedCarId ?? null,
          expiresAt: mvpReservation.expiresAt
        }
      : null,
    assignedVehicle: mvpAssignedCar
      ? {
          vehicleId: mvpAssignedCar.carId,
          vehicleState: mvpAssignedCar.state,
          position: mvpAssignedCar.position,
          mapPosition: mvpAssignedCar.mapPosition,
          targetSlotId: mvpAssignedCar.targetSlotId,
          targetAreaId: mvpAssignedCar.targetAreaId,
          isStoppedByFrontCar: mvpAssignedCar.isStoppedByFrontCar
        }
      : null,
    route: response.route,
    summary: response.summary ?? null
  };
}

export function guidanceTargetKey(guidance: UserGuidanceModel | null): string {
  if (!guidance) {
    return "";
  }

  return [guidance.status, guidance.targetAreaId ?? "", guidance.targetSlotId ?? "", guidance.route?.svgPath ?? ""].join("|");
}

export function guidanceLabel(guidance: UserGuidanceModel | null): string | null {
  return guidance?.targetSlot?.label ?? guidance?.targetAreaLabel ?? null;
}

export function deriveUserScreenState(input: {
  hasLoaded: boolean;
  parkingStatus: ParkingStatus | null;
  guidance: UserGuidanceModel | null;
  dataError: string | null;
}): UserScreenState {
  const hasDestination = Boolean(input.guidance?.targetAreaId);
  const hasUsableData = Boolean(input.parkingStatus && input.guidance && hasDestination);

  if (!input.hasLoaded && !input.dataError) {
    return { phase: "loading", canStartGuidance: false, hasDestination: false, showRoute: false, retryable: false };
  }

  if (input.dataError) {
    return {
      phase: "error",
      canStartGuidance: false,
      hasDestination,
      showRoute: hasUsableData,
      retryable: true
    };
  }

  if (!hasDestination || input.guidance?.status === "unavailable") {
    return { phase: "empty", canStartGuidance: false, hasDestination: false, showRoute: false, retryable: true };
  }

  if (input.parkingStatus?.stale) {
    return { phase: "stale", canStartGuidance: false, hasDestination: true, showRoute: true, retryable: true };
  }

  return { phase: "ready", canStartGuidance: true, hasDestination: true, showRoute: true, retryable: false };
}
