export type RiskLevel = "low" | "medium" | "high";
export type AreaPolicyValue = "PRIORITY" | "CLOSED" | "RESTRICTED" | "NORMAL";

export type AdminCommand = {
  commandId: string;
  idempotencyKey: string;
  commandType: "SET_AREA_POLICY";
  targetSourceId: string;
  targetSessionId: string;
  targetRunId: string;
  createdAtUtc: string;
  expiresAtUtc: string;
  priority: number;
  payload: { areaId: string; policy: AreaPolicyValue; effectiveUntilUtc?: string; reason?: string };
  status: "pending" | "delivered" | "accepted" | "started" | "succeeded" | "failed" | "rejected" | "expired" | "timed_out";
  deliveryAttempts: number;
  completedAtUtc?: string | null;
  lastResult?: {
    eventType: string;
    payload?: {
      command?: {
        status: string;
        reasonCode?: string;
        message?: string;
        retryable?: boolean;
        result?: { areaId?: string; previousPolicy?: string; appliedPolicy?: string };
      };
    };
  } | null;
};

export type AreaStatus = {
  areaId: string;
  label: string;
  displayOrder: number;
  capacity: number;
  emptyCount: number;
  occupiedCount: number;
  activeAreaReservations: number;
  effectiveAvailable: number;
  occupancyRate: number;
  waitingCars: number;
  leavingCars: number;
  riskScore: number;
  riskLevel: RiskLevel;
  layout: {
    center: { x: number; y: number };
    polygon: [number, number][];
    slotCount?: number;
  };
};

export type ParkingStatus = {
  snapshotVersion: number;
  updatedAt: string;
  stale: boolean;
  source: string | null;
  summary: {
    capacity: number;
    emptyCount: number;
    occupiedCount: number;
    occupancyRate: number;
  };
  areas: AreaStatus[];
  slots?: ParkingMapSlot[];
  areaLayout: {
    generatedFrom: string;
    image: { width: number; height: number };
  };
};

export type AdminState = ParkingStatus & {
  alerts: { type: string; areaId: string | null; severity: RiskLevel; message: string }[];
  guards: { guardId: string; status: string; currentArea: string; shift: string; break: string; canMove: boolean }[];
  logs: { id: string; timestamp: string; type: string; message: string }[];
  areaPolicies: Record<string, AreaPolicyValue>;
  commands: AdminCommand[];
  unityConnected: boolean;
  runtimeOnlineSeconds?: number;
  commandTarget?: { sourceId: string; sessionId: string; runId: string } | null;
  snapshotIdentity?: { sourceId: string; scene: string; sessionId?: string | null; runId?: string | null } | null;
  commandTargetMatchesSnapshot: boolean;
  lastAiRecommendation?: AiRecommendation | null;
};

export type ParkingMapSlot = {
  slotId: string;
  areaId: string;
  state: string;
  sensorOccupied?: boolean;
  isLeaving?: boolean;
  mapPosition?: { x: number; y: number } | null;
  accessWaypointId?: string | null;
};

export type MvpGuidanceResponse = {
  guidanceLevel: "area" | "slot";
  status: "active" | "not_started" | "unavailable";
  updatedAt?: string;
  targetArea?: { areaId: string; label: string; reason: string };
  recommendedSlotId?: string | null;
  recommendedSlot?: {
    slotId: string;
    areaId: string;
    label: string;
    state: string;
    position?: { x: number; y: number; z: number } | null;
    mapPosition?: { x: number; y: number } | null;
    accessWaypointId?: string | null;
  } | null;
  message?: string;
  replanReason?: string | null;
  reservation?: { reservationId: string; status: string; targetAreaId: string; targetSlotId?: string | null; assignedCarId?: string | null; expiresAt: string } | null;
  assignedCar?: {
    carId: string;
    state: string;
    position?: { x: number; y: number; z: number } | null;
    mapPosition?: { x: number; y: number } | null;
    targetSlotId?: string | null;
    targetAreaId?: string | null;
    isStoppedByFrontCar?: boolean;
  } | null;
  route?: { svgPath: string; steps: string[]; source?: "unity-waypoints" | "area-fallback"; waypointIds?: string[] };
  summary?: { emptyCount: number; effectiveAvailable: number; congestionLevel: RiskLevel };
};

export type AiRecommendation = {
  generatedAt: string;
  source: "gemini" | "fallback";
  title: string;
  summary: string;
  riskLevel: RiskLevel;
  actions: { priority?: number; areaId: string; action: string; reason: string }[];
  guardAssignments: { guardId: string; targetArea: string; task: string; durationMinutes?: number; reason: string }[];
  operatorNotes: string[];
  cached?: boolean;
};

export type AiStatus = {
  provider: "gemini";
  model: string;
  configured: boolean;
  mode: "gemini" | "fallback";
};
