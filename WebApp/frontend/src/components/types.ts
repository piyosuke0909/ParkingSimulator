export type RiskLevel = "low" | "medium" | "high";

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

export type AdminState = {
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
  areaLayout: {
    generatedFrom: string;
    image: { width: number; height: number };
  };
  alerts: { type: string; areaId: string | null; severity: RiskLevel; message: string }[];
  guards: { guardId: string; status: string; currentArea: string; shift: string; break: string; canMove: boolean }[];
  logs: { id: string; timestamp: string; type: string; message: string }[];
  lastAiRecommendation?: AiRecommendation | null;
};

export type GuidanceResponse = {
  guidanceLevel: "area";
  status: "active" | "not_started" | "unavailable";
  updatedAt?: string;
  targetArea?: { areaId: string; label: string; reason: string };
  message?: string;
  replanReason?: string | null;
  reservation?: { reservationId: string; status: string; targetAreaId: string; expiresAt: string } | null;
  route?: { svgPath: string; steps: string[] };
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
