import type { RiskLevel } from "../types";
import type { AdminView } from "./types";

export const defaultInstruction =
  "現在の駐車場状況を見て、混雑を避ける案内方針と警備員の配置案を提案してください。";

export const riskLabel: Record<RiskLevel, string> = {
  low: "余裕あり",
  medium: "注意",
  high: "混雑"
};

export const guardNames: Record<string, string> = {
  G01: "警備員 1",
  G02: "警備員 2",
  G03: "警備員 3",
  G04: "警備員 4",
  G05: "警備員 5"
};

export const navItems: { id: AdminView; label: string }[] = [
  { id: "overview", label: "全体状況" },
  { id: "parking", label: "駐車場マップ" },
  { id: "guards", label: "警備員配置" },
  { id: "ai", label: "AI提案" }
];
