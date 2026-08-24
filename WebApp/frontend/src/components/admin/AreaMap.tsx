import { useState } from "react";
import type { AdminState, AreaStatus } from "../types";
import type { MapMode } from "./types";
import { guardAreaPoint, slotClass } from "./utils";

type Props = {
  state: AdminState | null;
  mode: MapMode;
  selectedAreaId?: string;
  selectedSlotId?: string;
  priorityAreas?: Set<string>;
  showGuards?: boolean;
};

type Point = { x: number; y: number };
type Layout = { center: Point; polygon: [number, number][] };

const MAP_WIDTH = 825;
const MAP_HEIGHT = 619;

/*
 * Pixel-accurate geometry measured from admin-parking-map.png.
 * Each vertical parking bar has ten spaces on each side.  One area therefore
 * consists of three bars x two sides x ten spaces = 60 slots.
 */
const LEFT_SLOT_X = [75, 111, 203, 239, 330, 366];
const RIGHT_SLOT_X = [458, 494, 586, 622, 713, 749];
const TOP_SLOT_Y = [64.5, 87.5, 109.75, 132.25, 155, 178, 200.75, 223.25, 245.5, 268];
const BOTTOM_SLOT_Y = [348, 370.5, 393, 415.5, 438, 461, 483.75, 506.25, 528.5, 551.5];

/*
 * Unity quadrant layout:
 *   B | D
 *  ---+---
 *   A | C
 * The rectangles follow the visible parking-group bounds in the supplied map.
 */
const ADMIN_AREA_LAYOUTS: Record<string, Layout> = {
  B: {
    center: { x: 220.5, y: 166 },
    polygon: [[43, 37], [398, 37], [398, 295], [43, 295]]
  },
  D: {
    center: { x: 604.5, y: 166 },
    polygon: [[427, 37], [782, 37], [782, 295], [427, 295]]
  },
  A: {
    center: { x: 220.5, y: 449.5 },
    polygon: [[43, 320], [398, 320], [398, 579], [43, 579]]
  },
  C: {
    center: { x: 604.5, y: 449.5 },
    polygon: [[427, 320], [782, 320], [782, 579], [427, 579]]
  }
};

function normalizeAreaCode(value?: string) {
  const raw = String(value ?? "").trim().toUpperCase();
  const normalized = raw.startsWith("AREA-") ? raw.slice(5) : raw;
  return normalized === "A" || normalized === "B" || normalized === "C" || normalized === "D" ? normalized : "";
}

function displayLayout(area: AreaStatus): Layout {
  const code = normalizeAreaCode(area.areaId);
  return ADMIN_AREA_LAYOUTS[code] ?? area.layout;
}

function parseSlotNumber(slotId: string) {
  const match = String(slotId ?? "").trim().toUpperCase().match(/^(?:AREA-)?([ABCD])[-_ ]?(\d{1,2})$/);
  if (!match) {
    return null;
  }
  const number = Number(match[2]);
  if (!Number.isInteger(number) || number < 1 || number > 60) {
    return null;
  }
  return { areaCode: match[1], number };
}

function exactSlotPoint(slotId: string, fallbackAreaId: string, fallback?: Point | null): Point | null {
  const parsed = parseSlotNumber(slotId);
  const areaCode = parsed?.areaCode || normalizeAreaCode(fallbackAreaId);
  if (parsed && areaCode) {
    const zeroBased = parsed.number - 1;
    const lane = Math.floor(zeroBased / 10);
    const rowInLane = zeroBased % 10;
    const physicalRow = lane % 2 === 0 ? rowInLane : 9 - rowInLane;
    const xs = areaCode === "A" || areaCode === "B" ? LEFT_SLOT_X : RIGHT_SLOT_X;
    const ys = areaCode === "A" || areaCode === "C" ? BOTTOM_SLOT_Y : TOP_SLOT_Y;

    return {
      x: xs[lane],
      // Unity slot numbering runs from the lower end of each lane toward the upper end.
      y: ys[9 - physicalRow]
    };
  }

  /* Fallback for non-standard slot IDs: repair the backend's mirrored 637x492
     map coordinates, then scale them into the supplied 825x619 image. */
  if (!fallback) {
    return null;
  }
  const correctedX = 637 - fallback.x;
  const correctedY = 492 - fallback.y;
  return {
    x: 58 + ((correctedX - 56) / (584 - 56)) * (766 - 58),
    y: 52 + ((correctedY - 62) / (449 - 62)) * (564 - 52)
  };
}

export function AreaMap({ state, mode, selectedAreaId, selectedSlotId, priorityAreas, showGuards }: Props) {
  const [hoveredSlot, setHoveredSlot] = useState<{ slotId: string; point: Point } | null>(null);
  const areas = state?.areas ?? [];
  const slots = state?.slots ?? [];
  const imagePath = mode === "heatmap" ? "/assets/admin-heatmap-map.png" : "/assets/admin-parking-map.png";

  return (
    <div className="adminMapViewer adminMapViewerPrecise" style={{ aspectRatio: `${MAP_WIDTH} / ${MAP_HEIGHT}` }}>
      <img className="adminParkingImage" src={imagePath} alt="駐車場マップ" />
      <svg className="adminMapOverlay" viewBox={`0 0 ${MAP_WIDTH} ${MAP_HEIGHT}`} aria-hidden="true">
        {areas.map((area) => {
          const layout = displayLayout(area);
          return (
            <g key={area.areaId}>
              <polygon
                points={layout.polygon.map(([x, y]) => `${x},${y}`).join(" ")}
                className={`adminAreaPolygon ${area.riskLevel}${selectedAreaId === area.areaId ? " selected" : ""}${priorityAreas?.has(area.areaId) ? " priority" : ""}`}
              />
              <text x={layout.center.x} y={layout.center.y + 5} textAnchor="middle" className="adminAreaText">
                {normalizeAreaCode(area.areaId) || area.areaId}
              </text>
            </g>
          );
        })}
        {slots.map((slot) => {
          const point = exactSlotPoint(slot.slotId, slot.areaId, slot.mapPosition ?? null);
          if (!point) {
            return null;
          }
          const isSelectedSlot = selectedSlotId === slot.slotId;
          return (
            <g key={slot.slotId} className={`adminSlotHit${isSelectedSlot ? " slotSelected" : ""}`}>
              {isSelectedSlot ? (
                <rect
                  x={point.x - 10}
                  y={point.y - 7}
                  width="20"
                  height="14"
                  rx="2"
                  className="adminSelectedSlotOutline"
                />
              ) : null}
              <rect
                x={point.x - 6}
                y={point.y - 3}
                width="12"
                height="6"
                className={`adminSlotDot ${slotClass(slot)}${selectedAreaId === slot.areaId ? " selected" : ""}${isSelectedSlot ? " slotSelected" : ""}`}
              />
              <rect
                x={point.x - 10}
                y={point.y - 8}
                width="20"
                height="16"
                fill="transparent"
                className="adminSlotHoverTarget"
                onMouseEnter={() => setHoveredSlot({ slotId: slot.slotId, point })}
                onMouseLeave={() => setHoveredSlot(null)}
              />
            </g>
          );
        })}
        {showGuards
          ? (state?.guards ?? []).map((guard, index) => {
              const area = areas.find((item) => normalizeAreaCode(item.areaId) === normalizeAreaCode(guard.currentArea));
              const point = guardAreaPoint(
                area
                  ? { ...area, layout: displayLayout(area) }
                  : undefined,
                index
              );
              return (
                <g key={guard.guardId} className="guardSvgMarker" transform={`translate(${point.x} ${point.y})`}>
                  <circle r="13" />
                  <text y="4" textAnchor="middle">
                    {index + 1}
                  </text>
                </g>
              );
            })
          : null}
        {hoveredSlot ? (
          <g className="adminSlotTooltip" transform={`translate(${hoveredSlot.point.x} ${hoveredSlot.point.y - 18})`}>
            <rect x="-25" y="-15" width="50" height="20" rx="3" />
            <text x="0" y="-1" textAnchor="middle">{hoveredSlot.slotId}</text>
          </g>
        ) : null}
      </svg>
    </div>
  );
}
