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
  A: {
    center: { x: 203.5, y: 164 },
    polygon: [[98.8, 87], [308.2, 87], [308.2, 241], [98.8, 241]]
  },
  B: {
    center: { x: 203.5, y: 328 },
    polygon: [[98.8, 251], [308.2, 251], [308.2, 405], [98.8, 405]]
  },
  C: {
    center: { x: 433.5, y: 164 },
    polygon: [[328.8, 87], [538.2, 87], [538.2, 241], [328.8, 241]]
  },
  D: {
    center: { x: 433.5, y: 328 },
    polygon: [[328.8, 251], [538.2, 251], [538.2, 405], [328.8, 405]]
  }
};

export function ParkingMap({ areas, mode, routePath }: Props) {
  return (
    <div className="mapFrame">
      <img
        src={mode === "heatmap" ? "/assets/heatmap.png" : "/assets/parking.png"}
        alt="駐車場マップ"
        className="mapImage"
      />
      <svg className="mapOverlay" viewBox="0 0 637 492" aria-hidden="true">
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
        {routePath ? <path d={routePath} className="routePath" /> : null}
      </svg>
    </div>
  );
}
