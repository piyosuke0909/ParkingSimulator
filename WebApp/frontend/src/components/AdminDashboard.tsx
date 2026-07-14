"use client";

import { useEffect, useMemo, useState } from "react";
import { fetchJson } from "./api";
import { AdminSidebar, AdminTopbar } from "./admin/AdminShell";
import { AiPanel } from "./admin/AiPanel";
import { defaultInstruction } from "./admin/constants";
import { GuardsView } from "./admin/GuardsView";
import { LogsView } from "./admin/LogsView";
import { OverviewView } from "./admin/OverviewView";
import { ParkingView } from "./admin/ParkingView";
import { PolicyView } from "./admin/PolicyView";
import type { AdminPolicy, AdminView, MapMode } from "./admin/types";
import { selectedPriorityAreas } from "./admin/utils";
import type { AdminState, AiRecommendation, AiStatus } from "./types";

export function AdminDashboard() {
  const [view, setView] = useState<AdminView>("overview");
  const [menuOpen, setMenuOpen] = useState(false);
  const [state, setState] = useState<AdminState | null>(null);
  const [mapMode, setMapMode] = useState<MapMode>("normal");
  const [policy, setPolicy] = useState<AdminPolicy>({ priorityAreaIds: [], closedAreaIds: [], restrictedAreaIds: [] });
  const [selectedAreaId, setSelectedAreaId] = useState("A");
  const [selectedSlotId, setSelectedSlotId] = useState<string | null>(null);
  const [instruction, setInstruction] = useState(defaultInstruction);
  const [ai, setAi] = useState<AiRecommendation | null>(null);
  const [aiStatus, setAiStatus] = useState<AiStatus | null>(null);
  const [generating, setGenerating] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [unityBuildAvailable, setUnityBuildAvailable] = useState<boolean | null>(null);

  const selectedArea = useMemo(
    () => state?.areas.find((area) => area.areaId === selectedAreaId) ?? state?.areas[0] ?? null,
    [selectedAreaId, state]
  );

  const selectedAreaSlots = useMemo(
    () => (state?.slots ?? []).filter((slot) => slot.areaId === selectedArea?.areaId),
    [selectedArea, state]
  );

  const selectedSlot = useMemo(
    () => selectedAreaSlots.find((slot) => slot.slotId === selectedSlotId) ?? selectedAreaSlots[0] ?? null,
    [selectedAreaSlots, selectedSlotId]
  );

  const priorityAreas = useMemo(() => {
    const areas = selectedPriorityAreas(ai, state?.areas ?? []);
    policy.priorityAreaIds.forEach((areaId) => areas.add(areaId));
    return areas;
  }, [ai, policy.priorityAreaIds, state]);

  const busiestArea = useMemo(
    () => [...(state?.areas ?? [])].sort((a, b) => b.riskScore - a.riskScore)[0] ?? null,
    [state]
  );

  async function refresh() {
    try {
      const next = await fetchJson<AdminState>("/api/backend/admin/state");
      setState(next);
      if (!next.areas.some((area) => area.areaId === selectedAreaId) && next.areas[0]) {
        setSelectedAreaId(next.areas[0].areaId);
      }
      if (!ai && next.lastAiRecommendation) {
        setAi(next.lastAiRecommendation);
      }
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "管理画面データの取得に失敗しました。");
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
      setView("ai");
      await refresh();
    } catch (err) {
      setError(err instanceof Error ? err.message : "AI提案の生成に失敗しました。");
    } finally {
      setGenerating(false);
    }
  }

  function openArea(areaId: string) {
    setSelectedAreaId(areaId);
    setSelectedSlotId(null);
    setView("parking");
    setMenuOpen(false);
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
    fetch("/unity-build/index.html", { method: "HEAD", cache: "no-store" })
      .then((response) => setUnityBuildAvailable(response.ok))
      .catch(() => setUnityBuildAvailable(false));
  }, []);

  return (
    <main className="adminConsole">
      <AdminSidebar
        view={view}
        state={state}
        open={menuOpen}
        onClose={() => setMenuOpen(false)}
        onViewChange={(nextView) => {
          setView(nextView);
          setMenuOpen(false);
        }}
      />
      <button className={`adminMenuBackdrop ${menuOpen ? "open" : ""}`} type="button" aria-label="メニューを閉じる" onClick={() => setMenuOpen(false)} />

      <section className="adminMain">
        <AdminTopbar view={view} onRefresh={refresh} onMenuToggle={() => setMenuOpen((current) => !current)} />
        {error ? <p className="adminNotice danger">{error}</p> : null}

        {view === "overview" ? (
          <OverviewView
            state={state}
            ai={ai}
            aiStatus={aiStatus}
            instruction={instruction}
            generating={generating}
            unityBuildAvailable={unityBuildAvailable}
            policy={policy}
            onInstructionChange={setInstruction}
            onGenerateAi={generateAi}
            onPolicyChange={setPolicy}
            onOpenArea={openArea}
          />
        ) : null}

        {view === "parking" ? (
          <ParkingView
            state={state}
            mapMode={mapMode}
            selectedArea={selectedArea}
            selectedAreaId={selectedAreaId}
            selectedAreaSlots={selectedAreaSlots}
            selectedSlot={selectedSlot}
            priorityAreas={priorityAreas}
            onMapModeChange={setMapMode}
            onAreaChange={(areaId) => {
              setSelectedAreaId(areaId);
              setSelectedSlotId(null);
            }}
            onSlotChange={setSelectedSlotId}
          />
        ) : null}

        {view === "policy" ? <PolicyView state={state} ai={ai} policy={policy} onPolicyChange={setPolicy} onOpenArea={openArea} /> : null}

        {view === "guards" ? <GuardsView state={state} ai={ai} busiestArea={busiestArea} priorityAreas={priorityAreas} /> : null}

        {view === "ai" ? (
          <AiPanel
            state={state}
            ai={ai}
            aiStatus={aiStatus}
            instruction={instruction}
            generating={generating}
            onInstructionChange={setInstruction}
            onGenerate={generateAi}
          />
        ) : null}

        {view === "logs" ? <LogsView state={state} /> : null}
      </section>
    </main>
  );
}
