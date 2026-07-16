import type { AdminState } from "../types";
import { formatTime } from "./utils";

export function LogsView({ state }: { state: AdminState | null }) {
  return (
    <section className="adminPanel">
      <div className="adminPanelHeader">
        <div>
          <h2>管理ログ</h2>
          <p>backend の in-memory log を表示します。</p>
        </div>
      </div>
      <div className="adminLogList">
        {(state?.logs ?? []).slice().reverse().map((log) => (
          <p key={log.id}>
            <span>{formatTime(log.timestamp)}</span>
            <b>{log.type}</b>
            {log.message}
          </p>
        ))}
      </div>
    </section>
  );
}
