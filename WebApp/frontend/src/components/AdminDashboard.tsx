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
import type { AdminCommand, AdminState, AiRecommendation, AiStatus, AreaPolicyValue } from "./types";

function policyFromState(state: AdminState): AdminPolicy {
  const policy: AdminPolicy = { priorityAreaIds: [], closedAreaIds: [], restrictedAreaIds: [] };
  Object.entries(state.areaPolicies ?? {}).forEach(([areaId, value]) => {
    if (value === "PRIORITY") policy.priorityAreaIds.push(areaId);
    if (value === "CLOSED") policy.closedAreaIds.push(areaId);
    if (value === "RESTRICTED") policy.restrictedAreaIds.push(areaId);
  });
  return policy;
}


const terminalCommandStatuses = new Set(["succeeded", "failed", "rejected", "expired", "timed_out"]);

function busyAreasFromCommands(commands: AdminCommand[]): Set<string> {
  const busy = new Set<string>();
  const resolved = new Set<string>();
  for (const command of commands) {
    const areaId = command.payload?.areaId;
    if (!areaId || resolved.has(areaId)) continue;
    resolved.add(areaId);
    if (!terminalCommandStatuses.has(command.status)) {
      busy.add(areaId);
    }
  }
  return busy;
}


type AdminNotification = {
  key: string;
  title: string;
  message: string;
  severity: "low" | "medium" | "high" | "info" | "success";
};

function commandPolicyLabel(policy: AreaPolicyValue) {
  if (policy === "PRIORITY") return "優先案内";
  if (policy === "CLOSED") return "一時閉鎖";
  if (policy === "RESTRICTED") return "誘導制限";
  return "方針解除";
}


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
  const [commandNotice, setCommandNotice] = useState<AdminNotification | null>(null);
  const [dismissedNotificationKeys, setDismissedNotificationKeys] = useState<Set<string>>(() => new Set());
  const [commandBusyAreas, setCommandBusyAreas] = useState<Set<string>>(() => new Set());
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


  const notifications = useMemo(() => {
    const items: AdminNotification[] = [];
    const seen = new Set<string>();
    const add = (item: AdminNotification) => {
      const signature = `${item.title}|${item.message}`;
      if (seen.has(signature)) return;
      seen.add(signature);
      items.push(item);
    };

    if (error) {
      add({ key: `error-${error}`, title: "エラー", message: error, severity: "high" });
    }

    if (!state && !error) {
      add({ key: "snapshot-initial", title: "駐車場", message: "Unity snapshot の受信を待っています。", severity: "medium" });
    }

    if (state && !state.unityConnected) {
      add({ key: "unity-polling-offline", title: "Unity", message: "Command Pollingが停止しています。", severity: "high" });
    } else if (state?.unityConnected && state.stale) {
      add({ key: "snapshot-stale", title: "駐車場", message: "Unity snapshot の更新を待っています。", severity: "medium" });
    } else if (state?.unityConnected && !state.commandTarget) {
      add({ key: "command-target-wait", title: "Unity", message: "Command受信接続を待っています。", severity: "medium" });
    } else if (state?.unityConnected && state.commandTarget && !state.commandTargetMatchesSnapshot) {
      add({ key: "command-target-mismatch", title: "Unity", message: "表示中のUnity状態と操作先が一致するまでCommand操作を停止しています。", severity: "medium" });
    }

    if (commandNotice) {
      add(commandNotice);
    }

    for (const alert of state?.alerts ?? []) {
      add({
        key: `backend-${alert.type}-${alert.areaId ?? "parking"}-${alert.message}`,
        title: alert.areaId ? `${alert.areaId}エリア` : "駐車場",
        message: alert.message,
        severity: alert.severity
      });
    }

    return items;
  }, [commandNotice, error, state]);

  const visibleNotifications = notifications.filter((notice) => !dismissedNotificationKeys.has(notice.key));

  async function refresh() {
    try {
      const next = await fetchJson<AdminState>("/api/backend/admin/state");
      setState(next);
      setPolicy(policyFromState(next));
      setCommandBusyAreas(busyAreasFromCommands(next.commands ?? []));
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

  async function setAreaPolicy(areaId: string, nextPolicy: AreaPolicyValue) {
    const actionLabel = commandPolicyLabel(nextPolicy);
    const noticeKey = `command-${areaId}-${nextPolicy}-${Date.now()}`;
    setError(null);
    setCommandNotice({
      key: `${noticeKey}-sending`,
      title: `${areaId}エリア`,
      message: `${actionLabel}のCommandを送信しています。`,
      severity: "info"
    });
    setCommandBusyAreas((current) => new Set(current).add(areaId));
    try {
      await fetchJson<{ command: AdminCommand }>("/api/backend/admin/commands", {
        method: "POST",
        body: JSON.stringify({
          commandType: "SET_AREA_POLICY",
          idempotencyKey: `admin-${areaId}-${nextPolicy}-${Date.now()}`,
          payload: { areaId, policy: nextPolicy }
        })
      });
      setCommandNotice({
        key: `${noticeKey}-registered`,
        title: `${areaId}エリア`,
        message: `${actionLabel}のCommandを登録しました。`,
        severity: "success"
      });
      // Commandのbusy状態はBackendの最新Command状態から再計算する。
      // refreshに失敗しても3秒周期の次回refreshで復旧できるため、Frontendだけで永久ロックしない。
      await refresh();
    } catch (err) {
      setCommandNotice(null);
      setError(err instanceof Error ? err.message : "案内方針Commandの送信に失敗しました。");
      setCommandBusyAreas((current) => {
        const next = new Set(current);
        next.delete(areaId);
        return next;
      });
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


  useEffect(() => {
    if (!commandNotice || commandNotice.severity !== "success") return;
    const noticeKey = commandNotice.key;
    const timer = window.setTimeout(() => {
      setCommandNotice((current) => current?.key === noticeKey ? null : current);
    }, 5000);
    return () => window.clearTimeout(timer);
  }, [commandNotice]);

  useEffect(() => {
    const activeKeys = new Set(notifications.map((notice) => notice.key));
    setDismissedNotificationKeys((current) => {
      const next = new Set([...current].filter((key) => activeKeys.has(key)));
      if (next.size !== current.size) return next;
      for (const key of next) {
        if (!current.has(key)) return next;
      }
      return current;
    });
  }, [notifications]);

  return (
    <main className="adminConsole">
      <AdminSidebar
        view={view}
        state={state}
        open={menuOpen}
        onViewChange={(nextView) => {
          setView(nextView);
          setMenuOpen(false);
        }}
      />
      <button className={`adminMenuBackdrop ${menuOpen ? "open" : ""}`} type="button" aria-label="メニューを閉じる" onClick={() => setMenuOpen(false)} />

      <section className="adminMain">
        <AdminTopbar view={view} onRefresh={refresh} onMenuToggle={() => setMenuOpen((current) => !current)} />
        {visibleNotifications.length ? (
          <section className="opsAlertStack adminGlobalAlertStack" aria-label="管理画面通知">
            {visibleNotifications.map((notice) => (
              <article className={`opsPushAlert ${notice.severity}`} key={notice.key} role="alert">
                <div>
                  <strong>{notice.title}</strong>
                  <span>{notice.message}</span>
                </div>
                <button
                  type="button"
                  aria-label={`${notice.message}を閉じる`}
                  onClick={() => setDismissedNotificationKeys((current) => new Set(current).add(notice.key))}
                >
                  ×
                </button>
              </article>
            ))}
          </section>
        ) : null}

        {view === "overview" ? (
          <OverviewView
            state={state}
            ai={ai}
            aiStatus={aiStatus}
            instruction={instruction}
            generating={generating}
            unityBuildAvailable={unityBuildAvailable}
            policy={policy}
            commandBusyAreas={commandBusyAreas}
            onInstructionChange={setInstruction}
            onGenerateAi={generateAi}
            onSetAreaPolicy={setAreaPolicy}
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

        {view === "policy" ? (
          <PolicyView
            state={state}
            ai={ai}
            policy={policy}
            commandBusyAreas={commandBusyAreas}
            onSetAreaPolicy={setAreaPolicy}
            onOpenArea={openArea}
          />
        ) : null}

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
