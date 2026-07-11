import type { AdminState, AiRecommendation, AiStatus } from "../types";
import { AiPanel } from "./AiPanel";
import { UnityView } from "./UnityView";
import { riskLabel } from "./constants";
import { percent, riskClass } from "./utils";

type Props = {
  state: AdminState | null;
  ai: AiRecommendation | null;
  aiStatus: AiStatus | null;
  instruction: string;
  generating: boolean;
  unityBuildAvailable: boolean | null;
  onInstructionChange: (value: string) => void;
  onGenerateAi: () => void;
  onOpenArea: (areaId: string) => void;
};

export function OverviewView({
  state,
  ai,
  aiStatus,
  instruction,
  generating,
  unityBuildAvailable,
  onInstructionChange,
  onGenerateAi,
  onOpenArea
}: Props) {
  return (
    <>
      <section className="adminStats" aria-label="管理指標">
        <article className="adminStat">
          <span>総駐車台数</span>
          <strong>{state?.summary.capacity ?? 0}</strong>
        </article>
        <article className="adminStat">
          <span>空き台数</span>
          <strong>{state?.summary.emptyCount ?? 0}</strong>
        </article>
        <article className="adminStat">
          <span>利用中</span>
          <strong>{state?.summary.occupiedCount ?? 0}</strong>
        </article>
        <article className="adminStat">
          <span>全体混雑率</span>
          <strong>{percent(state?.summary.occupancyRate)}</strong>
        </article>
      </section>

      <section className="adminOverviewGrid">
        <UnityView unityBuildAvailable={unityBuildAvailable} />

        <article className="adminPanel">
          <div className="adminPanelHeader">
            <div>
              <h2>区画別状況</h2>
              <p>混雑度の高い区画を優先表示します。</p>
            </div>
          </div>
          <div className="areaOverview">
            {(state?.areas ?? []).map((area) => (
              <button key={area.areaId} type="button" className={`areaCard ${riskClass(area.riskLevel)}`} onClick={() => onOpenArea(area.areaId)}>
                <strong>{area.label}</strong>
                <span>空き {area.effectiveAvailable} / {area.capacity}</span>
                <b>{percent(area.occupancyRate)}</b>
                <small>{riskLabel[area.riskLevel]} / risk {Math.round(area.riskScore)}</small>
              </button>
            ))}
          </div>
        </article>
      </section>

      <AiPanel
        state={state}
        ai={ai}
        aiStatus={aiStatus}
        instruction={instruction}
        generating={generating}
        onInstructionChange={onInstructionChange}
        onGenerate={onGenerateAi}
      />
    </>
  );
}
