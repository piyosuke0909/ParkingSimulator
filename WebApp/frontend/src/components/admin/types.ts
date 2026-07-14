export type AdminView = "overview" | "parking" | "policy" | "guards" | "ai" | "logs";

export type MapMode = "normal" | "heatmap";

export type AdminPolicy = {
  priorityAreaIds: string[];
  closedAreaIds: string[];
  restrictedAreaIds: string[];
};
