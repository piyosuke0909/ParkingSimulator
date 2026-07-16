import type { AdminState, AiRecommendation, AreaStatus } from "../types";
import { AreaMap } from "./AreaMap";
import { guardNames } from "./constants";
import { ShiftTable } from "./ShiftTable";

type Props = {
  state: AdminState | null;
  ai: AiRecommendation | null;
  busiestArea: AreaStatus | null;
  priorityAreas: Set<string>;
};

export function GuardsView({ state, ai, busiestArea, priorityAreas }: Props) {
  return (
    <section className="guardsLayout">
      <article className="adminPanel guardLocationPanel">
        <div className="adminPanelHeader">
          <div>
            <h2>警備員位置マップ</h2>
            <p>現在の担当区画と AI 提案先をマップ上で確認します。</p>
          </div>
          <span className="statusPill">本日</span>
        </div>
        <div className="guardLocationBody">
          <AreaMap state={state} mode="normal" priorityAreas={priorityAreas} showGuards />
          <div className="guardLocationList">
            {(state?.guards ?? []).map((guard, index) => {
              const assignment = ai?.guardAssignments.find((item) => item.guardId === guard.guardId);
              return (
                <article className="guardLocationItem" key={guard.guardId}>
                  <strong>{index + 1}. {guardNames[guard.guardId] ?? guard.guardId}</strong>
                  <span>{guard.guardId} / 現在 {guard.currentArea}</span>
                  <p>{assignment ? `AI提案: ${assignment.targetArea} - ${assignment.task}` : "AI提案未生成"}</p>
                </article>
              );
            })}
          </div>
        </div>
      </article>

      <article className="adminPanel">
        <div className="adminPanelHeader">
          <div>
            <h2>警備員管理</h2>
            <p>配置エリア、シフト、移動可否を確認します。</p>
          </div>
        </div>
        <div className="guardCards">
          {(state?.guards ?? []).map((guard) => (
            <article className="guardCard" key={guard.guardId}>
              <div className="guardMain">
                <div>
                  <strong>{guardNames[guard.guardId] ?? guard.guardId}</strong>
                  <span>{guard.guardId}</span>
                </div>
                <span className={`badge ${guard.status === "active" ? "ok" : "warn"}`}>{guard.status}</span>
              </div>
              <p>担当: {guard.currentArea} / {guard.shift}</p>
              <p>休憩: {guard.break} / 移動 {guard.canMove ? "可" : "不可"}</p>
            </article>
          ))}
        </div>
      </article>

      <article className="adminPanel">
        <div className="adminPanelHeader">
          <h2>巡回タスク</h2>
          <span className="badge warn">{(state?.alerts ?? []).length}件</span>
        </div>
        <ul className="taskList">
          {(state?.alerts ?? []).length ? (
            state?.alerts.map((alert) => <li key={`${alert.type}-${alert.areaId}-${alert.message}`}>{alert.message}</li>)
          ) : (
            <li>{busiestArea ? `${busiestArea.label}を重点巡回。risk ${Math.round(busiestArea.riskScore)}` : "Unity受信後に巡回タスクを表示します。"}</li>
          )}
        </ul>
      </article>

      <article className="adminPanel shiftPanel">
        <div className="adminPanelHeader">
          <div>
            <h2>シフト</h2>
            <p>30分単位の簡易シフト表示です。</p>
          </div>
        </div>
        <ShiftTable guards={state?.guards ?? []} />
      </article>
    </section>
  );
}
