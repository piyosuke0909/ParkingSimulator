import type { AiRecommendation, AreaStatus, ParkingMapSlot, RiskLevel } from "../types";

export function percent(value?: number) {
  return `${Math.round((value ?? 0) * 100)}%`;
}

export function formatTime(value?: string) {
  if (!value) {
    return "-";
  }
  return new Date(value).toLocaleTimeString("ja-JP", {
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit"
  });
}

export function riskClass(risk: RiskLevel) {
  return `usage-${risk === "low" ? "low" : risk === "medium" ? "medium" : "high"}`;
}

export function slotClass(slot: ParkingMapSlot) {
  const state = slot.state.toLowerCase();
  if (slot.isLeaving || state.includes("leaving")) {
    return "alert";
  }
  if (state.includes("reserved")) {
    return "reserved";
  }
  if (slot.sensorOccupied || state.includes("occupied") || state.includes("parking")) {
    return "used";
  }
  return "empty";
}

export function slotLabel(slot: ParkingMapSlot) {
  const state = slot.state.toLowerCase();
  if (slot.isLeaving || state.includes("leaving")) {
    return "退出中";
  }
  if (state.includes("reserved")) {
    return "予約";
  }
  if (slot.sensorOccupied || state.includes("occupied") || state.includes("parking")) {
    return "利用中";
  }
  return "空き";
}

export function guardAreaPoint(area?: AreaStatus, index = 0) {
  const center = area?.layout.center ?? { x: 318, y: 246 };
  const offsets = [
    { x: -18, y: -14 },
    { x: 18, y: 14 },
    { x: 0, y: 22 },
    { x: 22, y: -20 }
  ];
  const offset = offsets[index % offsets.length];
  return { x: center.x + offset.x, y: center.y + offset.y };
}

export function selectedPriorityAreas(ai: AiRecommendation | null, areas: AreaStatus[]) {
  if (ai?.actions?.length) {
    return new Set(ai.actions.map((action) => action.areaId));
  }
  return new Set(
    areas
      .filter((area) => area.riskLevel !== "low")
      .sort((a, b) => b.riskScore - a.riskScore)
      .slice(0, 2)
      .map((area) => area.areaId)
  );
}
