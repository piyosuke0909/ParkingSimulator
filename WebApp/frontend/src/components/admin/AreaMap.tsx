import type { AdminState } from "../types";
import type { MapMode } from "./types";
import { guardAreaPoint, slotClass } from "./utils";

type Props = {
  state: AdminState | null;
  mode: MapMode;
  selectedAreaId?: string;
  priorityAreas?: Set<string>;
  showGuards?: boolean;
};

export function AreaMap({ state, mode, selectedAreaId, priorityAreas, showGuards }: Props) {
  const areas = state?.areas ?? [];
  const slots = state?.slots?.filter((slot) => slot.mapPosition) ?? [];

  return (
    <div className="adminMapViewer">
      <img
        className="adminParkingImage"
        src={mode === "heatmap" ? "/assets/heatmap.png" : "/assets/parking.png"}
        alt="駐車場マップ"
      />
      <svg className="adminMapOverlay" viewBox="0 0 637 492" aria-hidden="true">
        {areas.map((area) => (
          <g key={area.areaId}>
            <polygon
              points={area.layout.polygon.map(([x, y]) => `${x},${y}`).join(" ")}
              className={`adminAreaPolygon ${area.riskLevel}${selectedAreaId === area.areaId ? " selected" : ""}${priorityAreas?.has(area.areaId) ? " priority" : ""}`}
            />
            <text x={area.layout.center.x} y={area.layout.center.y + 5} textAnchor="middle" className="adminAreaText">
              {area.areaId}
            </text>
          </g>
        ))}
        {slots.map((slot) => (
          <rect
            key={slot.slotId}
            x={(slot.mapPosition?.x ?? 0) - 7}
            y={(slot.mapPosition?.y ?? 0) - 4}
            width="14"
            height="8"
            className={`adminSlotDot ${slotClass(slot)}${selectedAreaId === slot.areaId ? " selected" : ""}`}
          />
        ))}
        {showGuards
          ? (state?.guards ?? []).map((guard, index) => {
              const area = areas.find((item) => item.areaId === guard.currentArea);
              const point = guardAreaPoint(area, index);
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
      </svg>
    </div>
  );
}
