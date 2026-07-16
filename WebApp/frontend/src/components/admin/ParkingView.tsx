import type { AdminState, AreaStatus, ParkingMapSlot } from "../types";
import { AreaMap } from "./AreaMap";
import { riskLabel } from "./constants";
import type { MapMode } from "./types";
import { percent, riskClass, slotClass, slotLabel } from "./utils";

type Props = {
  state: AdminState | null;
  mapMode: MapMode;
  selectedArea: AreaStatus | null;
  selectedAreaId: string;
  selectedAreaSlots: ParkingMapSlot[];
  selectedSlot: ParkingMapSlot | null;
  priorityAreas: Set<string>;
  onMapModeChange: (mode: MapMode) => void;
  onAreaChange: (areaId: string) => void;
  onSlotChange: (slotId: string) => void;
};

export function ParkingView({
  state,
  mapMode,
  selectedArea,
  selectedAreaId,
  selectedAreaSlots,
  selectedSlot,
  priorityAreas,
  onMapModeChange,
  onAreaChange,
  onSlotChange
}: Props) {
  return (
    <section className="parkingLayout">
      <article className="adminPanel">
        <div className="adminPanelHeader parkingHeader">
          <div>
            <h2>駐車場マップ</h2>
            <p>区画を選ぶと、Unity snapshot のスロット一覧を確認できます。</p>
          </div>
          <div className="viewToggle">
            <button className={mapMode === "normal" ? "active" : ""} type="button" onClick={() => onMapModeChange("normal")}>
              通常
            </button>
            <button className={mapMode === "heatmap" ? "active" : ""} type="button" onClick={() => onMapModeChange("heatmap")}>
              ヒートマップ
            </button>
          </div>
        </div>
        <div className="areaTabs">
          {(state?.areas ?? []).map((area) => (
            <button
              key={area.areaId}
              type="button"
              className={`areaTab ${riskClass(area.riskLevel)}${selectedAreaId === area.areaId ? " active" : ""}`}
              onClick={() => onAreaChange(area.areaId)}
            >
              {area.label}
              <span>空き {area.effectiveAvailable} / {percent(area.occupancyRate)}</span>
            </button>
          ))}
        </div>
        <AreaMap state={state} mode={mapMode} selectedAreaId={selectedArea?.areaId} priorityAreas={priorityAreas} />
      </article>

      <aside className="adminPanel selectedPanel">
        <div className="adminPanelHeader">
          <div>
            <h2>{selectedArea?.label ?? "区画"} 詳細</h2>
            <p>{selectedArea ? `${selectedArea.capacity}台 / 空き ${selectedArea.effectiveAvailable}台` : "Unity受信待ち"}</p>
          </div>
          <span className={`badge ${selectedArea?.riskLevel ?? "low"}`}>{selectedArea ? riskLabel[selectedArea.riskLevel] : "-"}</span>
        </div>
        {selectedSlot ? (
          <div className="slotDetail">
            <div>
              <span>選択スロット</span>
              <strong>{selectedSlot.slotId}</strong>
            </div>
            <div>
              <span>状態</span>
              <strong>{slotLabel(selectedSlot)}</strong>
            </div>
            <div>
              <span>access waypoint</span>
              <strong>{selectedSlot.accessWaypointId ?? "-"}</strong>
            </div>
          </div>
        ) : (
          <div className="slotDetail empty">スロットを選択してください。</div>
        )}
        <div className="areaSummary">
          待機車両 {selectedArea?.waitingCars ?? 0}台 / 退出中 {selectedArea?.leavingCars ?? 0}台 / 有効空き {selectedArea?.effectiveAvailable ?? 0}台
        </div>
        <div className="spaceList" aria-label="駐車枠一覧">
          {selectedAreaSlots.map((slot) => (
            <button
              key={slot.slotId}
              type="button"
              className={`space ${slotClass(slot)}${selectedSlot?.slotId === slot.slotId ? " selected" : ""}${priorityAreas.has(slot.areaId) ? " priorityAi" : ""}`}
              onClick={() => onSlotChange(slot.slotId)}
            >
              <span className="spaceId">{slot.slotId}</span>
              <span className="spaceState">{slotLabel(slot)}</span>
            </button>
          ))}
        </div>
      </aside>
    </section>
  );
}
