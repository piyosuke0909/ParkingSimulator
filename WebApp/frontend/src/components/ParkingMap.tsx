import type { AreaStatus } from "./types";

type Props = {
  areas: AreaStatus[];
  mode: "normal" | "heatmap";
  routePath?: string;
};

const riskColor = {
  low: "rgba(22, 163, 74, 0.22)",
  medium: "rgba(234, 179, 8, 0.32)",
  high: "rgba(220, 38, 38, 0.36)"
};

const displayAreaLayout: Record<string, AreaStatus["layout"]> = {
  B: { center: { x: 220.5, y: 166 }, polygon: [[43, 37], [398, 37], [398, 295], [43, 295]] },
  D: { center: { x: 604.5, y: 166 }, polygon: [[427, 37], [782, 37], [782, 295], [427, 295]] },
  A: { center: { x: 220.5, y: 449.5 }, polygon: [[43, 320], [398, 320], [398, 579], [43, 579]] },
  C: { center: { x: 604.5, y: 449.5 }, polygon: [[427, 320], [782, 320], [782, 579], [427, 579]] }
};

const legacyRouteTransform = "matrix(1.340909 0 0 1.322997 -17.0909 -30.0258)";

export function ParkingMap({ areas, mode, routePath }: Props) {
  return (
    <div className="mapFrame">
      <img
        src={mode === "heatmap" ? "/assets/admin-heatmap-map.png" : "/assets/admin-parking-map.png"}
        alt="駐車場マップ"
        className="mapImage"
      />
      <svg className="mapOverlay" viewBox="0 0 825 619" aria-hidden="true">
        {areas.map((area) => {
          const layout = displayAreaLayout[area.areaId] ?? area.layout;

          return (
            <g key={area.areaId}>
              <polygon
                points={layout.polygon.map(([x, y]) => `${x},${y}`).join(" ")}
                fill={mode === "heatmap" ? riskColor[area.riskLevel] : "rgba(14, 116, 144, 0.14)"}
                stroke={mode === "heatmap" ? "rgba(127, 29, 29, 0.9)" : "rgba(8, 145, 178, 0.9)"}
                strokeWidth="2"
              />
              <circle cx={layout.center.x} cy={layout.center.y} r="17" className={`areaDot ${area.riskLevel}`} />
              <text x={layout.center.x} y={layout.center.y + 5} textAnchor="middle" className="areaText">
                {area.areaId}
              </text>
            </g>
          );
        })}
        {routePath ? <g transform={legacyRouteTransform}><path d={routePath} className="routePath" /></g> : null}
      </svg>
    </div>
  );
}
