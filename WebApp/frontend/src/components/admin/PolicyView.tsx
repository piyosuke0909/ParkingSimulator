import type { AdminState, AiRecommendation, AreaStatus } from "../types";
import { riskLabel } from "./constants";
import type { AdminPolicy } from "./types";
import { percent, riskClass } from "./utils";

type Props = {
  state: AdminState | null;
  ai: AiRecommendation | null;
  policy: AdminPolicy;
  onPolicyChange: (policy: AdminPolicy) => void;
  onOpenArea: (areaId: string) => void;
};

type PolicyBucket = "priorityAreaIds" | "closedAreaIds" | "restrictedAreaIds";

function hasArea(policy: AdminPolicy, bucket: PolicyBucket, areaId: string) {
  return policy[bucket].includes(areaId);
}

function withoutArea(values: string[], areaId: string) {
  return values.filter((value) => value !== areaId);
}

function unique(values: string[]) {
  return Array.from(new Set(values));
}

function togglePolicyArea(policy: AdminPolicy, bucket: PolicyBucket, areaId: string): AdminPolicy {
  const exists = hasArea(policy, bucket, areaId);
  const next: AdminPolicy = {
    priorityAreaIds: withoutArea(policy.priorityAreaIds, areaId),
    closedAreaIds: withoutArea(policy.closedAreaIds, areaId),
    restrictedAreaIds: withoutArea(policy.restrictedAreaIds, areaId)
  };

  if (!exists) {
    next[bucket] = unique([...next[bucket], areaId]);
  }

  return next;
}

function policyStatus(policy: AdminPolicy, areaId: string) {
  if (hasArea(policy, "closedAreaIds", areaId)) {
    return "案内停止";
  }
  if (hasArea(policy, "priorityAreaIds", areaId)) {
    return "優先";
  }
  if (hasArea(policy, "restrictedAreaIds", areaId)) {
    return "制限";
  }
  return "通常";
}

function policyScore(area: AreaStatus, policy: AdminPolicy) {
  if (hasArea(policy, "closedAreaIds", area.areaId)) {
    return -10000;
  }

  let score = area.effectiveAvailable * 3 - Math.round(area.occupancyRate * 100) - area.waitingCars * 8 - area.leavingCars * 4 - area.riskScore;
  if (hasArea(policy, "priorityAreaIds", area.areaId)) {
    score += 120;
  }
  if (hasArea(policy, "restrictedAreaIds", area.areaId)) {
    score -= 90;
  }
  return score;
}

function policyReason(area: AreaStatus, policy: AdminPolicy) {
  if (hasArea(policy, "closedAreaIds", area.areaId)) {
    return "管理者が案内停止に設定";
  }
  if (hasArea(policy, "priorityAreaIds", area.areaId)) {
    return "管理者の優先案内エリア";
  }
  if (hasArea(policy, "restrictedAreaIds", area.areaId)) {
    return "管理者が誘導制限に設定";
  }
  if (area.effectiveAvailable <= 0) {
    return "有効な空き枠なし";
  }
  if (area.riskLevel === "high") {
    return "混雑リスクが高い";
  }
  return "空き状況と混雑度から判定";
}

export function PolicyView({ state, ai, policy, onPolicyChange, onOpenArea }: Props) {
  const areas = state?.areas ?? [];
  const rankedAreas = [...areas].sort((a, b) => policyScore(b, policy) - policyScore(a, policy));
  const topCandidate = rankedAreas.find((area) => !hasArea(policy, "closedAreaIds", area.areaId)) ?? null;
  const aiAreaIds = unique((ai?.actions ?? []).map((action) => action.areaId).filter((areaId) => areas.some((area) => area.areaId === areaId)));

  function toggle(bucket: PolicyBucket, areaId: string) {
    onPolicyChange(togglePolicyArea(policy, bucket, areaId));
  }

  function applyAiPriority() {
    onPolicyChange({
      ...policy,
      priorityAreaIds: aiAreaIds.filter((areaId) => !policy.closedAreaIds.includes(areaId)),
      restrictedAreaIds: policy.restrictedAreaIds.filter((areaId) => !aiAreaIds.includes(areaId))
    });
  }

  function clearPolicy() {
    onPolicyChange({ priorityAreaIds: [], closedAreaIds: [], restrictedAreaIds: [] });
  }

  return (
    <section className="policyLayout">
      <article className="adminPanel policySummaryPanel">
        <div className="adminPanelHeader">
          <div>
            <h2>現在の案内方針</h2>
            <p>現場判断として、優先案内・停止・制限をエリア単位で調整します。</p>
          </div>
          <div className="policyHeaderActions">
            <button className="adminLinkButton" type="button" onClick={clearPolicy}>
              解除
            </button>
            <button className="adminButton" type="button" onClick={applyAiPriority} disabled={!aiAreaIds.length}>
              AI提案を反映
            </button>
          </div>
        </div>

        <div className="policyDecision">
          <div>
            <span>案内候補</span>
            <strong>{topCandidate?.label ?? "-"}</strong>
            <p>{topCandidate ? policyReason(topCandidate, policy) : "Unity snapshot の受信待ちです。"}</p>
          </div>
          <div>
            <span>画面内方針</span>
            <strong>{policy.priorityAreaIds.length + policy.closedAreaIds.length + policy.restrictedAreaIds.length}</strong>
            <p>優先 {policy.priorityAreaIds.length} / 停止 {policy.closedAreaIds.length} / 制限 {policy.restrictedAreaIds.length}</p>
          </div>
          <div>
            <span>案内可能な空き</span>
            <strong>{areas.filter((area) => !policy.closedAreaIds.includes(area.areaId)).reduce((total, area) => total + area.effectiveAvailable, 0)}</strong>
            <p>一時閉鎖エリアを除いた有効空き枠です。</p>
          </div>
        </div>
      </article>

      <article className="adminPanel">
        <div className="adminPanelHeader">
          <div>
            <h2>エリア別方針</h2>
            <p>実際の誘導に近い判断単位で、各エリアの扱いを切り替えます。</p>
          </div>
        </div>
        <div className="policyAreaGrid">
          {areas.map((area) => (
            <section className={`policyAreaCard ${riskClass(area.riskLevel)}`} key={area.areaId}>
              <div className="policyAreaTop">
                <button type="button" onClick={() => onOpenArea(area.areaId)}>
                  <strong>{area.label}</strong>
                  <span>{policyStatus(policy, area.areaId)}</span>
                </button>
                <span className={`badge ${area.riskLevel}`}>{riskLabel[area.riskLevel]}</span>
              </div>
              <div className="policyMetrics">
                <div>
                  <span>有効空き</span>
                  <strong>{area.effectiveAvailable}/{area.capacity}</strong>
                </div>
                <div>
                  <span>混雑率</span>
                  <strong>{percent(area.occupancyRate)}</strong>
                </div>
                <div>
                  <span>待機</span>
                  <strong>{area.waitingCars}</strong>
                </div>
              </div>
              <div className="policyControls" aria-label={`${area.label} の案内方針`}>
                <button
                  className={hasArea(policy, "priorityAreaIds", area.areaId) ? "active" : ""}
                  type="button"
                  onClick={() => toggle("priorityAreaIds", area.areaId)}
                >
                  優先案内
                </button>
                <button
                  className={hasArea(policy, "closedAreaIds", area.areaId) ? "active danger" : ""}
                  type="button"
                  onClick={() => toggle("closedAreaIds", area.areaId)}
                >
                  一時閉鎖
                </button>
                <button
                  className={hasArea(policy, "restrictedAreaIds", area.areaId) ? "active warn" : ""}
                  type="button"
                  onClick={() => toggle("restrictedAreaIds", area.areaId)}
                >
                  誘導制限
                </button>
              </div>
            </section>
          ))}
        </div>
      </article>

      <article className="adminPanel">
        <div className="adminPanelHeader">
          <div>
            <h2>案内候補の並び</h2>
            <p>現在の空き状況と管理者方針から、案内先の優先度を確認します。</p>
          </div>
        </div>
        <div className="policyRankList">
          {rankedAreas.map((area, index) => (
            <button key={area.areaId} type="button" className={policy.closedAreaIds.includes(area.areaId) ? "closed" : ""} onClick={() => onOpenArea(area.areaId)}>
              <span>{index + 1}</span>
              <strong>{area.label}</strong>
              <small>{policyReason(area, policy)}</small>
              <b>{Math.round(policyScore(area, policy))}</b>
            </button>
          ))}
        </div>
      </article>
    </section>
  );
}
