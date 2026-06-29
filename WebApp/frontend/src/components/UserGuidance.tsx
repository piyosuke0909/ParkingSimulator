"use client";

import { useEffect, useMemo, useState } from "react";
import { fetchJson } from "./api";
import { ParkingMap } from "./ParkingMap";
import type { AdminState, GuidanceResponse } from "./types";

export function UserGuidance() {
  const [state, setState] = useState<AdminState | null>(null);
  const [guidance, setGuidance] = useState<GuidanceResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const userSessionId = useMemo(() => {
    if (typeof window === "undefined") {
      return "demo-user";
    }
    const key = "smartparking-user-session";
    const existing = window.localStorage.getItem(key);
    if (existing) {
      return existing;
    }
    const value = `user_${crypto.randomUUID()}`;
    window.localStorage.setItem(key, value);
    return value;
  }, []);

  async function refresh() {
    try {
      setError(null);
      const [nextState, nextGuidance] = await Promise.all([
        fetchJson<AdminState>("/api/backend/admin/state"),
        fetchJson<GuidanceResponse>(`/api/backend/parking/recommendation?userSessionId=${encodeURIComponent(userSessionId)}`)
      ]);
      setState(nextState);
      setGuidance(nextGuidance);
    } catch (err) {
      setError(err instanceof Error ? err.message : "データ取得に失敗しました。");
    }
  }

  async function startGuidance() {
    if (!guidance?.targetArea) {
      return;
    }
    setLoading(true);
    try {
      const response = await fetchJson<GuidanceResponse>("/api/backend/guidance/start", {
        method: "POST",
        body: JSON.stringify({ userSessionId, targetAreaId: guidance.targetArea.areaId })
      });
      setGuidance(response);
      await refresh();
    } catch (err) {
      setError(err instanceof Error ? err.message : "案内開始に失敗しました。");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    refresh();
    const timer = window.setInterval(refresh, 3000);
    return () => window.clearInterval(timer);
  }, [userSessionId]);

  return (
    <main className="shell userShell">
      <section className="topbar">
        <div>
          <p className="eyebrow">SmartParking User</p>
          <h1>駐車エリア案内</h1>
        </div>
        <a className="textLink" href="/admin">管理者画面</a>
      </section>

      <section className="userGrid">
        <div className="guidancePanel">
          <p className="panelLabel">現在の案内</p>
          <h2>{guidance?.targetArea?.label ?? "案内情報を取得中"}</h2>
          <p className="mainMessage">{guidance?.message ?? "駐車場の状態を確認しています。"}</p>
          {guidance?.replanReason ? <p className="notice">{guidance.replanReason}</p> : null}
          {guidance?.summary ? (
            <div className="metricRow">
              <span>空き {guidance.summary.emptyCount}台</span>
              <span>有効空き {guidance.summary.effectiveAvailable}台</span>
              <span className={`riskText ${guidance.summary.congestionLevel}`}>{guidance.summary.congestionLevel}</span>
            </div>
          ) : null}
          <button className="primaryButton" onClick={startGuidance} disabled={loading || !guidance?.targetArea}>
            {guidance?.reservation ? "案内中" : loading ? "予約中..." : "案内開始"}
          </button>
          {guidance?.reservation ? <p className="caption">予約期限: {new Date(guidance.reservation.expiresAt).toLocaleTimeString("ja-JP")}</p> : null}
          {state ? (
            <p className="caption">
              Unity更新 v{state.snapshotVersion} / 空き合計 {state.summary.emptyCount}台 / 最終受信{" "}
              {new Date(state.updatedAt).toLocaleTimeString("ja-JP")}
            </p>
          ) : null}
          {error ? <p className="errorText">{error}</p> : null}
        </div>

        <div className="mapPanel">
          <ParkingMap areas={state?.areas ?? []} mode="normal" routePath={guidance?.route?.svgPath} />
          <ol className="steps">
            {(guidance?.route?.steps ?? []).map((step) => (
              <li key={step}>{step}</li>
            ))}
          </ol>
        </div>
      </section>
    </main>
  );
}
