"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import type { CSSProperties, PointerEvent } from "react";
import { fetchJson } from "./api";
import type { AdminState, AreaStatus, GuidanceResponse, ParkingMapSlot, RiskLevel } from "./types";

type DisplayMode = "map" | "aerial";
type RouteMode = "parking" | "exit";

type MapView = {
  tilt: number;
  rotate: number;
  panX: number;
  panY: number;
  preset: "vehicle" | "north" | "";
};

type MapDragState = {
  pointerId: number;
  x: number;
  y: number;
  view: MapView;
};

const areaGuidance: Record<string, { distance: string; eta: string; action: string; place: string; lane: string; route: string }> = {
  A: { distance: "36m", eta: "2分", action: "左折", place: "Aエリア接続通路", lane: "徐行", route: "M28 246 H160 V164 H204" },
  B: { distance: "45m", eta: "2分", action: "左折", place: "Bエリア接続通路", lane: "徐行", route: "M28 246 H160 V328 H204" },
  C: { distance: "74m", eta: "3分", action: "直進", place: "Cエリア奥通路", lane: "注意", route: "M28 246 H360 V164 H434" },
  D: { distance: "62m", eta: "3分", action: "右折", place: "Dエリア接続通路", lane: "注意", route: "M28 246 H360 V328 H434" }
};

const exitView = {
  distance: "52m",
  eta: "2分",
  action: "右折",
  place: "西出口レーン",
  lane: "出口",
  route: "M342 178 H264 V246 H28"
};

const riskLabel: Record<RiskLevel, string> = {
  low: "空きあり",
  medium: "やや混雑",
  high: "混雑"
};

const slotGridOrigin: Record<string, { x: number; y: number }> = {
  A: { x: 128, y: 122 },
  B: { x: 128, y: 286 },
  C: { x: 358, y: 122 },
  D: { x: 358, y: 286 }
};

function clamp(value: number, min: number, max: number) {
  return Math.min(max, Math.max(min, value));
}

function getArea(state: AdminState | null, areaId?: string) {
  return state?.areas.find((area) => area.areaId === areaId) ?? null;
}

function getGuide(areaId?: string) {
  return areaGuidance[areaId || ""] ?? areaGuidance.B;
}

function buildRoutePath(guidance: GuidanceResponse | null) {
  return guidance?.route?.svgPath || getGuide(guidance?.targetArea?.areaId).route;
}

function getRouteStartPoint(routePath: string) {
  const match = routePath.match(/M\s*(-?\d+(?:\.\d+)?)\s+(-?\d+(?:\.\d+)?)/);
  if (!match) {
    return null;
  }
  return { x: Number(match[1]), y: Number(match[2]) };
}

function guidanceTargetKey(guidance: GuidanceResponse | null) {
  if (!guidance) {
    return "";
  }
  return [
    guidance.status,
    guidance.targetArea?.areaId ?? "",
    guidance.recommendedSlotId ?? guidance.recommendedSlot?.slotId ?? "",
    guidance.route?.svgPath ?? ""
  ].join("|");
}

function guidanceLabel(guidance: GuidanceResponse | null) {
  return guidance?.recommendedSlot?.label || guidance?.targetArea?.label || "案内先";
}

function areaSlots(area: AreaStatus) {
  const origin = slotGridOrigin[area.areaId] ?? { x: 128, y: 122 };
  const usedCount = clamp(Math.round(area.occupancyRate * 20), 0, 20);
  const waitCount = clamp(Math.min(area.activeAreaReservations, 3), 0, 20 - usedCount);

  return Array.from({ length: 20 }, (_, index) => {
    const row = Math.floor(index / 5);
    const col = index % 5;
    const cls = index < usedCount ? " used" : index < usedCount + waitCount ? " wait" : "";
    return (
      <rect
        className={`slot-mini${cls}`}
        key={`${area.areaId}-${index}`}
        x={origin.x + col * 17}
        y={origin.y + row * 12}
        width="13"
        height="8"
      />
    );
  });
}

function slotStateClass(slot: ParkingMapSlot) {
  const state = slot.state.toLowerCase();
  if (slot.isLeaving || state.includes("leaving")) {
    return " leaving";
  }
  if (state.includes("disabled") || state.includes("closed")) {
    return " disabled";
  }
  if (slot.sensorOccupied || state.includes("occupied") || state.includes("parking")) {
    return " used";
  }
  if (state.includes("reserved")) {
    return " wait";
  }
  return "";
}

function mapSlots(state: AdminState | null, guidance: GuidanceResponse | null) {
  const slots = state?.slots?.filter((slot) => slot.mapPosition) ?? [];
  if (!slots.length) {
    return (state?.areas ?? []).flatMap(areaSlots);
  }

  const targetSlotId = guidance?.recommendedSlotId ?? guidance?.recommendedSlot?.slotId;
  return slots.map((slot) => {
    const point = slot.mapPosition!;
    const cls = slotStateClass(slot);
    return (
      <rect
        className={`slot-mini synced${cls}${slot.slotId === targetSlotId ? " target" : ""}`}
        key={slot.slotId}
        x={point.x - 12}
        y={point.y - 5}
        width="24"
        height="10"
      />
    );
  });
}

function focusPan(guidance: GuidanceResponse | null) {
  const point = guidance?.recommendedSlot?.mapPosition;
  if (!point) {
    return { x: 0, y: 0 };
  }
  return {
    x: clamp((318.5 - point.x) * 0.28, -76, 76),
    y: clamp((246 - point.y) * 0.04, -10, 10)
  };
}

function pointPan(point: { x: number; y: number }) {
  const frameWidth = 650;
  const frameHeight = 520;
  const mapWidth = 637;
  const mapHeight = 492;
  const frameLeft = -110;
  const frameTop = 190;
  const focusX = 215;
  const focusY = 338;

  return {
    x: clamp(focusX - (frameLeft + (point.x / mapWidth) * frameWidth), -240, 320),
    y: clamp(focusY - (frameTop + (point.y / mapHeight) * frameHeight), -220, 120)
  };
}

function shouldSkipMapDrag(event: PointerEvent<HTMLElement>) {
  const target = event.target;
  return target instanceof Element && Boolean(target.closest("button, a, input, textarea, select, [data-no-map-drag]"));
}

export function UserGuidance() {
  const [state, setState] = useState<AdminState | null>(null);
  const [guidance, setGuidance] = useState<GuidanceResponse | null>(null);
  const [pendingGuidance, setPendingGuidance] = useState<GuidanceResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [displayMode, setDisplayMode] = useState<DisplayMode>("map");
  const [routeMode, setRouteMode] = useState<RouteMode>("parking");
  const [sheetCollapsed, setSheetCollapsed] = useState(false);
  const [isMapDragging, setIsMapDragging] = useState(false);
  const [mapView, setMapView] = useState<MapView>({ tilt: 54, rotate: -5, panX: 0, panY: 12, preset: "" });
  const dragRef = useRef<MapDragState | null>(null);
  const guidanceRef = useRef<GuidanceResponse | null>(null);

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

  const targetArea = getArea(state, guidance?.targetArea?.areaId);
  const guide = getGuide(guidance?.targetArea?.areaId);
  const destinationLabel = guidanceLabel(guidance) || "案内先取得中";
  const isGuidanceActive = routeMode === "parking" && guidance?.status === "active" && Boolean(guidance?.reservation);
  const routePath = routeMode === "parking" ? buildRoutePath(guidance) : exitView.route;
  const routeStartPosition = getRouteStartPoint(routePath);
  const assignedCarPosition = guidance?.assignedCar?.mapPosition ?? null;
  const currentPosition = isGuidanceActive ? assignedCarPosition : routeStartPosition;
  const currentGuide = routeMode === "parking" ? guide : exitView;
  const freeCount = guidance?.summary?.effectiveAvailable ?? state?.summary.emptyCount ?? 0;
  const crowd = guidance?.summary?.congestionLevel ?? targetArea?.riskLevel ?? "low";
  const showRoutePreview = routeMode === "parking" && !isGuidanceActive;
  const showRouteLine = routeMode === "exit" || isGuidanceActive || showRoutePreview;
  const showCurrentMarker = routeMode === "exit" || (isGuidanceActive && Boolean(assignedCarPosition));
  const candidateChanged = routeMode === "parking" && !isGuidanceActive && Boolean(pendingGuidance);
  const pendingLabel = guidanceLabel(pendingGuidance);
  const routeFocusPan = isGuidanceActive && mapView.preset !== "vehicle" ? focusPan(guidance) : { x: 0, y: 0 };
  const topText =
    routeMode === "parking"
      ? isGuidanceActive
        ? `${currentGuide.action}して${destinationLabel}へ`
        : `${destinationLabel}がおすすめ候補`
      : "西出口方面へ退出";
  const locationText =
    routeMode === "parking"
      ? isGuidanceActive
        ? `${destinationLabel}へ案内中`
        : `ルートプレビュー: ${destinationLabel}`
      : `${destinationLabel}付近から出口案内中`;

  async function refresh() {
    try {
      setError(null);
      const [nextState, nextGuidance] = await Promise.all([
        fetchJson<AdminState>("/api/backend/admin/state"),
        fetchJson<GuidanceResponse>(`/api/backend/parking/recommendation?userSessionId=${encodeURIComponent(userSessionId)}`)
      ]);
      setState(nextState);
      const currentGuidance = guidanceRef.current;
      const shouldReplaceGuidance =
        !currentGuidance ||
        currentGuidance.status === "active" ||
        nextGuidance.status === "active" ||
        guidanceTargetKey(currentGuidance) === guidanceTargetKey(nextGuidance);

      if (shouldReplaceGuidance) {
        guidanceRef.current = nextGuidance;
        setGuidance(nextGuidance);
        setPendingGuidance(null);
      } else {
        setPendingGuidance(nextGuidance);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "データ取得に失敗しました。");
    }
  }

  async function startGuidance() {
    const guidanceToStart = pendingGuidance ?? guidance;
    if (!guidanceToStart?.targetArea || routeMode !== "parking") {
      return;
    }
    setLoading(true);
    try {
      const response = await fetchJson<GuidanceResponse>("/api/backend/guidance/start", {
        method: "POST",
        body: JSON.stringify({ userSessionId, targetAreaId: guidanceToStart.targetArea.areaId })
      });
      guidanceRef.current = response;
      setGuidance(response);
      setPendingGuidance(null);
      await refresh();
    } catch (err) {
      setError(err instanceof Error ? err.message : "案内開始に失敗しました。");
    } finally {
      setLoading(false);
    }
  }

  async function cancelGuidance() {
    if (!guidance?.reservation || routeMode !== "parking") {
      return;
    }
    setLoading(true);
    try {
      await fetchJson<{ cancelledReservationIds: string[] }>("/api/backend/guidance/cancel", {
        method: "POST",
        body: JSON.stringify({ userSessionId, reservationId: guidance.reservation.reservationId })
      });
      guidanceRef.current = null;
      setGuidance(null);
      setPendingGuidance(null);
      setMapView((current) => ({ ...current, preset: "" }));
      await refresh();
    } catch (err) {
      setError(err instanceof Error ? err.message : "案内中止に失敗しました。");
    } finally {
      setLoading(false);
    }
  }

  function handlePrimaryAction() {
    if (isGuidanceActive) {
      cancelGuidance();
    } else {
      startGuidance();
    }
  }

  function focusVehiclePosition() {
    if (!showCurrentMarker || !currentPosition) {
      return;
    }
    const pan = pointPan(currentPosition);
    setMapView((current) => ({
      tilt: 54,
      rotate: current.rotate,
      panX: pan.x,
      panY: pan.y,
      preset: "vehicle"
    }));
  }

  function setNorthPreset() {
    setMapView({ tilt: 54, rotate: 0, panX: 0, panY: -18, preset: "north" });
  }

  function startMapDrag(event: PointerEvent<HTMLElement>) {
    if ((event.pointerType === "mouse" && event.button !== 0) || shouldSkipMapDrag(event)) {
      return;
    }
    dragRef.current = { pointerId: event.pointerId, x: event.clientX, y: event.clientY, view: mapView };
    setIsMapDragging(true);
    event.currentTarget.setPointerCapture(event.pointerId);
  }

  function moveMapDrag(event: PointerEvent<HTMLElement>) {
    if (!dragRef.current || dragRef.current.pointerId !== event.pointerId) {
      return;
    }
    event.preventDefault();
    const dx = event.clientX - dragRef.current.x;
    const dy = event.clientY - dragRef.current.y;
    setMapView({
      tilt: clamp(dragRef.current.view.tilt - dy * 0.32, 24, 74),
      rotate: dragRef.current.view.rotate + dx * 0.55,
      panX: clamp(dragRef.current.view.panX + dx * 0.38, -240, 320),
      panY: clamp(dragRef.current.view.panY + dy * 0.28, -220, 120),
      preset: ""
    });
  }

  function endMapDrag(event?: PointerEvent<HTMLElement>) {
    if (event && dragRef.current?.pointerId !== event.pointerId) {
      return;
    }
    if (event?.currentTarget.hasPointerCapture(event.pointerId)) {
      event.currentTarget.releasePointerCapture(event.pointerId);
    }
    dragRef.current = null;
    setIsMapDragging(false);
  }

  useEffect(() => {
    refresh();
    const timer = window.setInterval(refresh, 1000);
    return () => window.clearInterval(timer);
  }, [userSessionId]);

  return (
    <main className="userDemoShell" aria-label="U22 SmartParking Navi">
      <section className="phone">
        <header className="guide-card">
          <div className="turn" aria-hidden="true">
            <svg viewBox="0 0 40 40" width="36" height="36">
              <path d="M23 7v14H11l6-6-3-3L3 23l11 11 3-3-6-6h16V7z" fill="currentColor" />
            </svg>
          </div>
          <div className="guide-main">
            <h1>{currentGuide.distance}</h1>
            <p>{topText}</p>
          </div>
          <div className="guide-meta">
            <span>{routeMode === "parking" ? destinationLabel : "西出口"}</span>
            <b>{currentGuide.eta}</b>
          </div>
        </header>

        <div className="mode-bar">
          <div className="location-chip">{locationText}</div>
          <div className="view-switch" aria-label="表示切替">
            <button className={`view-button ${displayMode === "map" ? "active" : ""}`} type="button" onClick={() => setDisplayMode("map")}>
              地図
            </button>
            <button className={`view-button ${displayMode === "aerial" ? "active" : ""}`} type="button" onClick={() => setDisplayMode("aerial")}>
              上空
            </button>
          </div>
        </div>

        <section
          className={`screen map-screen ${displayMode === "map" ? "active" : ""} ${isMapDragging ? "dragging" : ""}`}
          aria-label="駐車場地図"
          onPointerDown={startMapDrag}
          onPointerMove={moveMapDrag}
          onPointerUp={endMapDrag}
          onPointerCancel={endMapDrag}
          onLostPointerCapture={endMapDrag}
        >
          <div className="maneuver-strip" data-turn={routeMode === "parking" && currentGuide.action === "左折" ? "left" : "exit"}>
            <div className="maneuver-icon" aria-hidden="true">
              <svg viewBox="0 0 40 40" width="30" height="30">
                <path d="M23 7v14H11l6-6-3-3L3 23l11 11 3-3-6-6h16V7z" fill="currentColor" />
              </svg>
            </div>
            <div className="maneuver-copy">
              <span>{currentGuide.distance}先</span>
              <strong>{currentGuide.action}</strong>
              <small>{currentGuide.place}</small>
            </div>
            <div className="maneuver-lane">
              <small>次</small>
              <b>{currentGuide.lane}</b>
            </div>
          </div>

          <div
            className="unity-frame"
            style={{
              "--map-tilt": `${mapView.tilt}deg`,
              "--map-rotate": `${mapView.rotate}deg`,
              "--map-pan-x": `${mapView.panX + routeFocusPan.x}px`,
              "--map-pan-y": `${mapView.panY + routeFocusPan.y}px`
            } as CSSProperties}
          >
            <div className="unity-label">Unity 上空マップ</div>
            <img className="unity-parking-image" src="/assets/parking.png" alt="Unity駐車場マップ" />
            <svg className="map-route-overlay" viewBox="0 0 637 492" preserveAspectRatio="none" aria-hidden="true">
              <g>{mapSlots(state, guidance)}</g>
              {showRouteLine ? (
                <>
                  <path className={`route-main ${showRoutePreview ? "preview" : ""}`} d={routePath} />
                  <path className={`route-dash ${showRoutePreview ? "preview" : ""}`} d={routePath} />
                </>
              ) : null}
              {showCurrentMarker && currentPosition ? (
                <g className="current-car-marker" style={{ transform: `translate(${currentPosition.x}px, ${currentPosition.y}px)` }}>
                  <circle r="16" />
                  <path d="M0 -18 L8 6 L0 2 L-8 6 Z" />
                </g>
              ) : null}
              {guidance?.recommendedSlot?.mapPosition && routeMode === "parking" ? (
                <g>
                  <circle className="target-ring" cx={guidance.recommendedSlot.mapPosition.x} cy={guidance.recommendedSlot.mapPosition.y} r="15" />
                  <text className="target-label" x={guidance.recommendedSlot.mapPosition.x} y={guidance.recommendedSlot.mapPosition.y - 20} textAnchor="middle">
                    {guidance.recommendedSlot.label}
                  </text>
                </g>
              ) : null}
            </svg>
          </div>

          <div className="map-controls" aria-label="地図表示調整">
            <button
              className={`map-control primary current-location ${mapView.preset === "vehicle" ? "active" : ""}`}
              type="button"
              onClick={focusVehiclePosition}
              disabled={!showCurrentMarker}
              aria-label="現在地に合わせる"
            >
              <span className="heading-icon" aria-hidden="true">⌖</span>
              <span>現在地</span>
            </button>
            <button className={`map-control ${mapView.preset === "north" ? "active" : ""}`} type="button" onClick={setNorthPreset} aria-label="北を上">
              N
            </button>
          </div>
          <div className="gesture-hint"><span>左右スワイプで回転 / 上下スワイプで角度調整</span></div>
        </section>

        <section className={`screen mr-screen ${displayMode === "aerial" ? "active" : ""}`} aria-label="上空映像案内">
          <div className="mr-status">
            <div className="mr-pill">上空カメラ: 西入口</div>
            <div className="mr-pill">{state?.stale ? "Unity接続確認中" : "導線をリアルタイム更新"}</div>
          </div>
          <div className="aerial-feed" aria-label="上空映像">
            <div className="camera-header">
              <span className="live-badge"><span className="live-dot" />LIVE AERIAL</span>
              <span className="camera-meta">snapshot v{state?.snapshotVersion ?? 0}</span>
            </div>
            <img className="aerial-image" src="/assets/parking.png" alt="" />
            <svg className="aerial-map" viewBox="0 0 637 492" preserveAspectRatio="none" aria-hidden="true">
              {showRouteLine ? (
                <>
                  <path className={`mr-route-main ${showRoutePreview ? "preview" : ""}`} d={routePath} />
                  <path className={`mr-route-core ${showRoutePreview ? "preview" : ""}`} d={routePath} />
                </>
              ) : null}
            </svg>
            <div className="route-progress" aria-hidden="true"><span /></div>
            <div className="mr-target">{routeMode === "parking" ? destinationLabel : "西出口"}</div>
          </div>
        </section>

        <section className={`bottom-sheet ${sheetCollapsed ? "collapsed" : ""}`} aria-label="ルート情報" data-route={routeMode}>
          <button className="handle" type="button" aria-label="メニューを切り替える" aria-expanded={!sheetCollapsed} onClick={() => setSheetCollapsed(!sheetCollapsed)} />
          <div className="route-tabs" aria-label="案内切替">
            <span className="route-indicator" aria-hidden="true" />
            <button className={`route-tab ${routeMode === "parking" ? "active" : ""}`} type="button" onClick={() => setRouteMode("parking")} aria-pressed={routeMode === "parking"}>
              駐車案内
            </button>
            <button className={`route-tab ${routeMode === "exit" ? "active" : ""}`} type="button" onClick={() => setRouteMode("exit")} aria-pressed={routeMode === "exit"}>
              出口案内
            </button>
          </div>

          <div className="route-carousel">
            <article className="route-panel">
              <div className="summary">
                <div>
                  <h2>{routeMode === "parking" ? (isGuidanceActive ? `${destinationLabel}へ案内` : `${destinationLabel}の候補`) : "西出口へ案内"}</h2>
                  <p>
                    {routeMode === "parking"
                      ? isGuidanceActive
                        ? `${guidance?.targetArea?.label ?? "推奨エリア"}の空き状況をもとに、Unity snapshot 由来の案内先を表示しています。`
                        : `${guidance?.targetArea?.label ?? "推奨エリア"}の空き状況をもとにした候補です。ルートはプレビューとして表示しています。`
                      : "出口案内は現在デモ表示です。駐車案内データとは分けて表示しています。"}
                  </p>
                </div>
                <div className="eta"><small>{routeMode === "parking" ? "到着" : "退出"}</small><b>{currentGuide.eta}</b></div>
              </div>
              <div className="map-stat-row">
                <div className="map-stat"><small>有効空き</small><b>{freeCount}台</b></div>
                <div className="map-stat"><small>混雑</small><b>{riskLabel[crowd]}</b></div>
                <div className="map-stat"><small>案内先</small><b>{routeMode === "parking" ? destinationLabel : "西出口"}</b></div>
              </div>
              {candidateChanged ? <p className="demo-notice">より良い候補として {pendingLabel} が見つかりました。案内開始時に最新候補へ更新します。</p> : null}
              {isGuidanceActive && !assignedCarPosition ? <p className="demo-notice warning">自車位置を確認中です。Unity snapshot の車両位置を待っています。</p> : null}
              {guidance?.replanReason ? <p className="demo-notice">{guidance.replanReason}</p> : null}
              {state?.stale ? <p className="demo-notice warning">Unity snapshot が止まっています。管理者画面の Unity 表示を確認してください。</p> : null}
              {error ? <p className="demo-notice warning">{error}</p> : null}
              <button className="primary-action" type="button" onClick={handlePrimaryAction} disabled={loading || routeMode !== "parking" || (!isGuidanceActive && !guidance?.targetArea)}>
                {routeMode === "exit" ? "出口案内を表示中" : loading ? (isGuidanceActive ? "中止中..." : "予約中...") : isGuidanceActive ? "案内を中止" : "案内を開始"}
              </button>
              {guidance?.reservation ? (
                <p className="route-detail">予約期限: {new Date(guidance.reservation.expiresAt).toLocaleTimeString("ja-JP")}</p>
              ) : null}
              {state ? (
                <p className="route-detail">Unity更新 v{state.snapshotVersion} / 最終受信 {new Date(state.updatedAt).toLocaleTimeString("ja-JP")}</p>
              ) : null}
            </article>
          </div>
        </section>
      </section>
    </main>
  );
}
