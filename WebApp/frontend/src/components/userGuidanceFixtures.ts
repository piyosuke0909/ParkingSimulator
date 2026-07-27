import type { MvpGuidanceResponse, ParkingStatus } from "./types";

export const readyParkingStatus: ParkingStatus = {
  snapshotVersion: 12,
  updatedAt: "2026-07-17T03:00:00Z",
  stale: false,
  source: "unity-webgl-user-01",
  summary: { capacity: 120, emptyCount: 24, occupiedCount: 96, occupancyRate: 0.8 },
  areas: [
    {
      areaId: "area-a",
      label: "Aエリア",
      displayOrder: 1,
      capacity: 30,
      emptyCount: 8,
      occupiedCount: 22,
      activeAreaReservations: 1,
      effectiveAvailable: 7,
      occupancyRate: 0.7333,
      waitingCars: 0,
      leavingCars: 1,
      riskScore: 78,
      riskLevel: "medium",
      layout: { center: { x: 210, y: 160 }, polygon: [[120, 100], [300, 100], [300, 220], [120, 220]] }
    }
  ],
  slots: [],
  areaLayout: { generatedFrom: "test-fixture", image: { width: 637, height: 492 } }
};

export const readyGuidanceResponse: MvpGuidanceResponse = {
  guidanceLevel: "area",
  status: "not_started",
  updatedAt: "2026-07-17T03:00:00Z",
  targetArea: { areaId: "area-a", label: "Aエリア", reason: "空きが多く、入口から近いためおすすめです。" },
  recommendedSlotId: null,
  recommendedSlot: null,
  message: "Aエリアへ進んでください。",
  replanReason: null,
  reservation: null,
  assignedCar: null,
  route: { svgPath: "M28 246 H160 V164 H204", steps: ["中央通路へ進む", "Aエリアへ左折する"], source: "area-fallback" },
  summary: { emptyCount: 8, effectiveAvailable: 7, congestionLevel: "medium" }
};

export const emptyGuidanceResponse: MvpGuidanceResponse = {
  guidanceLevel: "area",
  status: "unavailable",
  message: "案内可能なエリアがありません。"
};

export const staleParkingStatus: ParkingStatus = { ...readyParkingStatus, stale: true };
