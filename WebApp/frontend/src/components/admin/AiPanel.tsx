import type { AdminState, AiRecommendation, AiStatus } from "../types";
import { AiResult, EmptyAiResult } from "./AiResult";

type Props = {
  state: AdminState | null;
  ai: AiRecommendation | null;
  aiStatus: AiStatus | null;
  instruction: string;
  generating: boolean;
  onInstructionChange: (value: string) => void;
  onGenerate: () => void;
};

export function AiPanel({ state, ai, aiStatus, instruction, generating, onInstructionChange, onGenerate }: Props) {
  return (
    <section className="adminPanel aiGeneratorPanel">
      <div className="adminPanelHeader">
        <div className="aiTitle">
          <span>AI</span>
          <div>
            <h2>AI運営アシスタント</h2>
            <p>Unity 由来の混雑データと警備員状態から配置案を生成します。</p>
          </div>
        </div>
        <span className={`statusPill ${aiStatus?.configured ? "live" : "stale"}`}>
          {aiStatus?.configured ? aiStatus.model : "fallback"}
        </span>
      </div>
      <div className="aiInlineForm">
        <textarea value={instruction} onChange={(event) => onInstructionChange(event.target.value)} />
        <button className="adminButton primary" type="button" onClick={onGenerate} disabled={generating}>
          {generating ? "生成中..." : "AIで生成"}
        </button>
      </div>
      {ai ? <AiResult ai={ai} state={state} /> : <EmptyAiResult />}
    </section>
  );
}
