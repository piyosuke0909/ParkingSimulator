"use client";

import { useEffect, useMemo, useState } from "react";
import { fetchJson } from "./api";
import { ParkingMap } from "./ParkingMap";
import type { AdminState, AiRecommendation, AiStatus } from "./types";

export function AdminDashboard() {
  const [state, setState] = useState<AdminState | null>(null);
  const [mapMode, setMapMode] = useState<"normal" | "heatmap">("normal");
  const [instruction, setInstruction] = useState("混雑エリアを避けて入場車を誘導し、警備員配置を提案してください。");
  const [ai, setAi] = useState<AiRecommendation | null>(null);
  const [generating, setGenerating] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [unityBuildAvailable, setUnityBuildAvailable] = useState<boolean | null>(null);
  const [aiStatus, setAiStatus] = useState<AiStatus | null>(null);

  const connection = useMemo(() => {
    if (!state) {
      return {
        label: "接続確認中",
        className: "statusBadge",
        detail: "backend 状態を取得しています。"
      };
    }

    if (state.stale) {
      return {
        label: "Unity未受信",
        className: "statusBadge stale",
        detail: "最終 snapshot から5秒以上更新がありません。Unity WebGL の再読み込みを確認してください。"
      };
    }

    return {
      label: "Unity接続中",
      className: "statusBadge live",
      detail: "Unity WebGL から backend に snapshot が届いています。"
    };
  }, [state]);

  async function refresh() {
    try {
      const next = await fetchJson<AdminState>("/api/backend/admin/state");
      setState(next);
      if (!ai && next.lastAiRecommendation) {
        setAi(next.lastAiRecommendation);
      }
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "管理者状態の取得に失敗しました。");
    }
  }

  async function generateAi() {
    setGenerating(true);
    try {
      const response = await fetchJson<AiRecommendation>("/api/backend/admin/ai/recommendations", {
        method: "POST",
        body: JSON.stringify({ instruction })
      });
      setAi(response);
      await refresh();
    } catch (err) {
      setError(err instanceof Error ? err.message : "AI提案の生成に失敗しました。");
    } finally {
      setGenerating(false);
    }
  }

  useEffect(() => {
    refresh();
    const timer = window.setInterval(refresh, 3000);
    return () => window.clearInterval(timer);
  }, []);

  useEffect(() => {
    fetchJson<AiStatus>("/api/backend/admin/ai/status")
      .then(setAiStatus)
      .catch(() => setAiStatus(null));
  }, []);

  useEffect(() => {
    fetch("/unity-build/index.html", { method: "HEAD", cache: "no-store" })
      .then((response) => setUnityBuildAvailable(response.ok))
      .catch(() => setUnityBuildAvailable(false));
  }, []);

  return (
    <main className="shell adminShell">
      <section className="topbar">
        <div>
          <p className="eyebrow">SmartParking Admin</p>
          <h1>駐車場オペレーション</h1>
        </div>
        <a className="textLink" href="/">ユーザー画面</a>
      </section>

      <section className="adminGrid">
        <div className="unityPanel">
          <div className="sectionHeader">
            <h2>Unity Play</h2>
            <span className={connection.className}>{connection.label}</span>
          </div>

          <div className="connectionGrid">
            <div>
              <span>snapshot</span>
              <strong>v{state?.snapshotVersion ?? 0}</strong>
            </div>
            <div>
              <span>最終受信</span>
              <strong>{state?.updatedAt ? new Date(state.updatedAt).toLocaleTimeString("ja-JP") : "-"}</strong>
            </div>
            <div>
              <span>source</span>
              <strong>{state?.source ?? "未受信"}</strong>
            </div>
            <div>
              <span>総台数</span>
              <strong>{state?.summary.capacity ?? 0}台</strong>
            </div>
            <div>
              <span>空き合計</span>
              <strong>{state?.summary.emptyCount ?? 0}台</strong>
            </div>
            <div>
              <span>全体混雑率</span>
              <strong>{state ? Math.round(state.summary.occupancyRate * 100) : 0}%</strong>
            </div>
            <p>{connection.detail}</p>
          </div>

          {unityBuildAvailable ? (
            <iframe src="/unity-build/index.html" title="Unity WebGL" className="unityFrame" />
          ) : (
            <div className="unityMissing">
              <strong>Unity WebGL build が未配置です。</strong>
              <span>Unity で WebGL build を作成し、出力一式を WebApp/frontend/public/unity-build/ に置くと表示されます。</span>
            </div>
          )}
        </div>

        <div className="mapPanel adminMap">
          <div className="mapPanelHeader">
            <h2>駐車場マップ</h2>
            <div className="mapTabs" role="tablist" aria-label="マップ表示切り替え">
              <button
                role="tab"
                aria-selected={mapMode === "normal"}
                className={mapMode === "normal" ? "active" : ""}
                onClick={() => setMapMode("normal")}
              >
                通常
              </button>
              <button
                role="tab"
                aria-selected={mapMode === "heatmap"}
                className={mapMode === "heatmap" ? "active" : ""}
                onClick={() => setMapMode("heatmap")}
              >
                ヒートマップ
              </button>
            </div>
          </div>
          <ParkingMap areas={state?.areas ?? []} mode={mapMode} />
        </div>

        <div className="areaTablePanel">
          <h2>エリア状況</h2>
          <div className="areaTable">
            {(state?.areas ?? []).map((area) => (
              <div className="areaRow" key={area.areaId}>
                <strong>{area.label}</strong>
                <span>{Math.round(area.occupancyRate * 100)}%</span>
                <span>空き {area.emptyCount}</span>
                <span className={`riskText ${area.riskLevel}`}>{area.riskLevel}</span>
              </div>
            ))}
          </div>
          <div className="alerts">
            {(state?.alerts ?? []).map((alert) => (
              <p key={`${alert.type}-${alert.areaId}-${alert.message}`} className={`notice ${alert.severity}`}>{alert.message}</p>
            ))}
          </div>
        </div>

        <div className="aiPanel">
          <div className="sectionHeader">
            <h2>AI運営アシスタント</h2>
            <span className={`statusBadge ${aiStatus?.configured ? "live" : "stale"}`}>
              {aiStatus?.configured ? aiStatus.model : "fallback"}
            </span>
          </div>
          <p className="caption">提案・参考・管理者判断。API key 未設定時は fallback 提案を表示します。</p>
          <textarea value={instruction} onChange={(event) => setInstruction(event.target.value)} />
          <button className="primaryButton" onClick={generateAi} disabled={generating}>
            {generating ? "AI提案を生成中..." : "AIで生成"}
          </button>
          {ai ? (
            <div className="aiResult">
              <p className={`statusBadge ${ai.source === "fallback" ? "stale" : "live"}`}>{ai.source}{ai.cached ? " cached" : ""}</p>
              <h3>{ai.title}</h3>
              <p>{ai.summary}</p>
              <ul>
                {ai.actions.map((action) => (
                  <li key={`${action.areaId}-${action.action}`}>{action.action}</li>
                ))}
              </ul>
              <ul>
                {ai.guardAssignments.map((assignment) => (
                  <li key={`${assignment.guardId}-${assignment.targetArea}`}>{assignment.guardId}: {assignment.targetArea} / {assignment.task}</li>
                ))}
              </ul>
            </div>
          ) : null}
          {error ? <p className="errorText">{error}</p> : null}
        </div>

        <div className="logPanel">
          <h2>管理者ログ</h2>
          <div className="logList">
            {(state?.logs ?? []).slice().reverse().map((log) => (
              <p key={log.id}><span>{new Date(log.timestamp).toLocaleTimeString("ja-JP")}</span>{log.type} / {log.message}</p>
            ))}
          </div>
        </div>
      </section>
    </main>
  );
}
