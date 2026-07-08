import type { AdminView } from "./types";
import type { RiskLevel } from "../types";

export const defaultInstruction =
  "Unityから取得した駐車場の混雑状況を見て、混雑エリアを避ける誘導方針と警備員の配置を提案してください。";

export const riskLabel: Record<RiskLevel, string> = {
  low: "余裕あり",
  medium: "注意",
  high: "混雑"
};

export const guardNames: Record<string, string> = {
  G01: "佐藤 健",
  G02: "田中 美咲",
  G03: "高橋 翔",
  G04: "鈴木 葵",
  G05: "伊藤 蓮"
};

export const navItems: { id: AdminView; label: string }[] = [
  { id: "overview", label: "全体状況" },
  { id: "parking", label: "駐車場" },
  { id: "guards", label: "警備員配置" },
  { id: "ai", label: "AI提案" },
  { id: "unity", label: "Unity" },
  { id: "logs", label: "ログ" }
];
