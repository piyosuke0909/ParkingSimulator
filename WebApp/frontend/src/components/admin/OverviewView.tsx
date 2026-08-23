import { useEffect, useState } from "react";
import type { AdminState, AiRecommendation, AiStatus, AreaPolicyValue } from "../types";
import { guardNames, riskLabel } from "./constants";
import type { AdminPolicy } from "./types";
import { formatTime, percent, riskClass } from "./utils";
import { unityBuildUrl } from "./unityBuildUrl";

type Props = {
  state: AdminState | null;
  ai: AiRecommendation | null;
  aiStatus: AiStatus | null;
  instruction: string;
  generating: boolean;
  unityBuildAvailable: boolean | null;
  policy: AdminPolicy;
  commandBusyAreas: Set<string>;
  onInstructionChange: (value: string) => void;
  onGenerateAi: () => void;
  onSetAreaPolicy: (areaId: string, policy: AreaPolicyValue) => Promise<void>;
  onOpenArea: (areaId: string) => void;
};

function policyLabel(policy: AdminPolicy, areaId: string) {
  if (policy.closedAreaIds.includes(areaId)) {
    return "案内停止";
  }
  if (policy.priorityAreaIds.includes(areaId)) {
    return "優先案内";
  }
  if (policy.restrictedAreaIds.includes(areaId)) {
    return "誘導制限";
  }
  return "通常";
}

function policyTone(policy: AdminPolicy, areaId: string) {
  if (policy.closedAreaIds.includes(areaId)) {
    return "closed";
  }
  if (policy.priorityAreaIds.includes(areaId)) {
    return "priority";
  }
  if (policy.restrictedAreaIds.includes(areaId)) {
    return "restricted";
  }
  return "normal";
}

export function OverviewView({
  state,
  ai,
  aiStatus,
  instruction,
  generating,
  unityBuildAvailable,
  policy,
  commandBusyAreas,
  onInstructionChange,
  onGenerateAi,
  onSetAreaPolicy,
  onOpenArea
}: Props) {
  const [selectedPolicyAreaId, setSelectedPolicyAreaId] = useState("A");
  const [leftPanelOpen, setLeftPanelOpen] = useState(true);
  const [rightPanelOpen, setRightPanelOpen] = useState(true);
  const [unityVisible, setUnityVisible] = useState(false);
  const areas = state?.areas ?? [];
  const alerts = state?.alerts ?? [];
  const selectedPolicyArea = areas.find((area) => area.areaId === selectedPolicyAreaId) ?? areas[0] ?? null;
  const commandBusy = selectedPolicyArea ? commandBusyAreas.has(selectedPolicyArea.areaId) : false;
  const commandDisabled =
    !selectedPolicyArea ||
    state?.unityConnected !== true ||
    Boolean(state?.stale) ||
    !state?.commandTarget ||
    state?.commandTargetMatchesSnapshot !== true ||
    commandBusy;

  useEffect(() => {
    if (areas.length && !areas.some((area) => area.areaId === selectedPolicyAreaId)) {
      setSelectedPolicyAreaId(areas[0].areaId);
    }
  }, [areas, selectedPolicyAreaId]);

  return (
    <section className={`opsDashboard ${leftPanelOpen ? "" : "leftCollapsed"} ${rightPanelOpen ? "" : "rightCollapsed"}`}>
      <section className="opsWorkspace">
        <aside className="opsPanel opsLeftRail" aria-label="状況パネル">
          <button className="opsRailHandle left" type="button" onClick={() => setLeftPanelOpen((current) => !current)}>
            {leftPanelOpen ? "‹" : "›"}
          </button>
          <section className="opsBlock">
            <header>
              <span>STATUS</span>
              <h2>エリア状況</h2>
            </header>
            <div className="opsScrollList">
              {areas.map((area) => (
                <button key={area.areaId} type="button" className={`opsAreaRow ${riskClass(area.riskLevel)}`} onClick={() => onOpenArea(area.areaId)}>
                  <div>
                    <strong>{area.label}</strong>
                    <span>{riskLabel[area.riskLevel]} / {policyLabel(policy, area.areaId)}</span>
                  </div>
                  <b>{area.effectiveAvailable}/{area.capacity}</b>
                </button>
              ))}
            </div>
          </section>

        </aside>

        <main className="opsMapPanel" aria-label="駐車場状態">
          <section className="opsKpis" aria-label="運用指標">
            <article>
              <span>空き枠</span>
              <strong>{state?.summary.emptyCount ?? 0}/{state?.summary.capacity ?? 0}</strong>
            </article>
            <article>
              <span>使用中</span>
              <strong>{state?.summary.occupiedCount ?? 0}</strong>
            </article>
            <article>
              <span>混雑率</span>
              <strong>{percent(state?.summary.occupancyRate)}</strong>
            </article>
            <article>
              <span>警告</span>
              <strong>{alerts.length}</strong>
            </article>
          </section>

          <section className="opsPanel opsBottomLog" aria-label="最近ログ">
            <header>
              <span>RECENT LOG</span>
              <h2>最近の運用ログ</h2>
            </header>
            <div className="opsLogScroller">
              {(state?.logs ?? []).slice(-8).reverse().map((log) => (
                <p key={log.id}>
                  <span>{formatTime(log.timestamp)}</span>
                  <b>{log.type}</b>
                  {log.message}
                </p>
              ))}
            </div>
          </section>

          <div className="opsUnityViewport">
            {unityBuildAvailable && unityVisible ? (
              <>
                <iframe src={unityBuildUrl({ view: "admin-fit-16x10", embed: "admin" })} title="Unity WebGL 駐車場状態" className="opsUnityFrame" />
                <button
                  className="adminButton unityVisibilityButton"
                  type="button"
                  aria-label="Unity画面を閉じる"
                  title="×"
                  onClick={() => setUnityVisible(false)}
                >
                  ×
                </button>
              </>
            ) : unityBuildAvailable ? (
              <div className="adminEmpty">
                <p>管理画面を軽く保つため、Unity映像は必要なときだけ読み込みます。</p>
                <button className="adminButton primary" type="button" onClick={() => setUnityVisible(true)}>
                  Unity画面を表示
                </button>
              </div>
            ) : (
              <div className="adminEmpty">Unity WebGL build がありません。</div>
            )}
          </div>
        </main>

        <aside className="opsPanel opsRightRail" aria-label="操作パネル">
          <button className="opsRailHandle right" type="button" onClick={() => setRightPanelOpen((current) => !current)}>
            {rightPanelOpen ? "›" : "‹"}
          </button>
          <section className="opsBlock">
            <header>
              <span>GUIDANCE CONTROL</span>
              <h2>案内方針操作</h2>
            </header>
            <div className="opsGuidanceControl">
              <div className="opsAreaSelector" aria-label="操作するエリアを選択">
                {areas.map((area) => (
                  <button
                    key={area.areaId}
                    className={`${selectedPolicyArea?.areaId === area.areaId ? "selected" : ""} ${policyTone(policy, area.areaId)}`}
                    type="button"
                    onClick={() => setSelectedPolicyAreaId(area.areaId)}
                  >
                    <strong>{area.areaId}</strong>
                    <span>{policyLabel(policy, area.areaId)}</span>
                  </button>
                ))}
              </div>

              {selectedPolicyArea ? (
                <article className={`opsSelectedArea ${policyTone(policy, selectedPolicyArea.areaId)}`}>
                  <div className="opsSelectedAreaHeader">
                    <div>
                      <span>選択中</span>
                      <strong>{selectedPolicyArea.label}</strong>
                    </div>
                    <b>{policyLabel(policy, selectedPolicyArea.areaId)}</b>
                  </div>
                  <div className="opsSelectedMetrics">
                    <div>
                      <span>有効空き</span>
                      <strong>{selectedPolicyArea.effectiveAvailable}/{selectedPolicyArea.capacity}</strong>
                    </div>
                    <div>
                      <span>混雑率</span>
                      <strong>{percent(selectedPolicyArea.occupancyRate)}</strong>
                    </div>
                    <div>
                      <span>待機</span>
                      <strong>{selectedPolicyArea.waitingCars}</strong>
                    </div>
                  </div>
                  <div className="opsCommandStack">
                    <button type="button" className="primary" disabled={commandDisabled} onClick={() => onSetAreaPolicy(selectedPolicyArea.areaId, "PRIORITY")}>
                      優先案内
                    </button>
                    <button type="button" className="danger" disabled={commandDisabled} onClick={() => onSetAreaPolicy(selectedPolicyArea.areaId, "CLOSED")}>
                      一時閉鎖
                    </button>
                    <button type="button" className="warn" disabled={commandDisabled} onClick={() => onSetAreaPolicy(selectedPolicyArea.areaId, "RESTRICTED")}>
                      誘導制限
                    </button>
                    <button
                      type="button"
                      disabled={commandDisabled || policyLabel(policy, selectedPolicyArea.areaId) === "通常"}
                      onClick={() => onSetAreaPolicy(selectedPolicyArea.areaId, "NORMAL")}
                    >
                      方針解除
                    </button>
                  </div>
                </article>
              ) : null}
            </div>
          </section>

          <section className="opsBlock">
            <header>
              <span>AI</span>
              <h2>提案</h2>
            </header>
            <div className="opsAiBox">
              <span className={`statusPill ${aiStatus?.configured ? "live" : "stale"}`}>{aiStatus?.configured ? aiStatus.model : "fallback"}</span>
              <textarea value={instruction} onChange={(event) => onInstructionChange(event.target.value)} />
              <button className="adminButton primary" type="button" onClick={onGenerateAi} disabled={generating}>
                {generating ? "生成中..." : "AIで提案"}
              </button>
              {ai ? (
                <div className="opsAiResult">
                  <strong>{ai.title}</strong>
                  <p>{ai.summary}</p>
                </div>
              ) : (
                <p className="opsEmpty">AI提案はまだ生成されていません。</p>
              )}
            </div>
          </section>

          <section className="opsBlock">
            <header>
              <span>GUARD</span>
              <h2>警備員</h2>
            </header>
            <div className="opsScrollList compact">
              {(state?.guards ?? []).map((guard) => (
                <p className="opsGuardRow" key={guard.guardId}>
                  <strong>{guardNames[guard.guardId] ?? guard.guardId}</strong>
                  <span>{guard.currentArea} / {guard.status}</span>
                </p>
              ))}
            </div>
          </section>
        </aside>
      </section>

    </section>
  );
}
