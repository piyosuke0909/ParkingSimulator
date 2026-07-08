import type { AdminState } from "../types";
import { guardNames } from "./constants";

export function ShiftTable({ guards }: { guards: AdminState["guards"] }) {
  const slots = ["9", "10", "11", "12", "13", "14", "15", "16", "17", "18"];

  function isWorking(shift: string, hour: string) {
    const [start, end] = shift.split("-").map((item) => Number(item.split(":")[0]));
    const value = Number(hour);
    return value >= start && value < end;
  }

  return (
    <div className="shiftTable">
      <div className="shiftHeader">
        <span>警備員</span>
        {slots.map((slot) => (
          <span key={slot}>{slot}:00</span>
        ))}
      </div>
      {guards.map((guard) => (
        <div className="shiftRow" key={guard.guardId}>
          <strong>{guardNames[guard.guardId] ?? guard.guardId}</strong>
          {slots.map((slot) => (
            <span key={slot} className={isWorking(guard.shift, slot) ? "work" : "off"} />
          ))}
        </div>
      ))}
    </div>
  );
}
