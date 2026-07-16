import type { AdminState, AiRecommendation } from "../types";
import { guardNames, riskLabel } from "./constants";

export function EmptyAiResult() {
  return (
    <div className="aiResultBox empty">
      <strong>AI提案はまだ生成されていません。</strong>
      <p>現在の Unity 状態を見て、混雑回避と警備員配置を提案します。</p>
    </div>
  );
}

export function AiResult({ ai, state }: { ai: AiRecommendation; state: AdminState | null }) {
  return (
    <div className="aiResultBox">
      <div className="aiResultTopline">
        <div>
          <span className="aiResultLabel">
            {ai.source}
            {ai.cached ? " cached" : ""}
          </span>
          <h3>{ai.title}</h3>
        </div>
        <span className={`badge ${ai.riskLevel}`}>{riskLabel[ai.riskLevel]}</span>
      </div>
      <p className="aiSummary">{ai.summary}</p>
      <div className="aiMetrics">
        <div>
          <span>snapshot</span>
          <strong>v{state?.snapshotVersion ?? 0}</strong>
        </div>
        <div>
          <span>空き</span>
          <strong>{state?.summary.emptyCount ?? 0}</strong>
        </div>
        <div>
          <span>利用中</span>
          <strong>{state?.summary.occupiedCount ?? 0}</strong>
        </div>
        <div>
          <span>警備員</span>
          <strong>{state?.guards.length ?? 0}</strong>
        </div>
      </div>
      <ol className="aiActions">
        {ai.actions.map((action) => (
          <li key={`${action.areaId}-${action.action}`}>
            <strong>{action.areaId}</strong>
            <span>{action.action}</span>
            {action.reason ? <small>{action.reason}</small> : null}
          </li>
        ))}
      </ol>
      <section className="aiGuardPlan">
        <h4>警備員配置案</h4>
        <div className="aiGuardPlanList">
          {ai.guardAssignments.map((assignment) => (
            <article key={`${assignment.guardId}-${assignment.targetArea}`}>
              <strong>{guardNames[assignment.guardId] ?? assignment.guardId}</strong>
              <span>{assignment.targetArea}</span>
              <p>{assignment.task}</p>
            </article>
          ))}
        </div>
      </section>
    </div>
  );
}
