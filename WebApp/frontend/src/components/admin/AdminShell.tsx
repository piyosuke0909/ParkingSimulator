import type { AdminState } from "../types";
import { navItems } from "./constants";
import type { AdminView } from "./types";
import { formatTime } from "./utils";

type SidebarProps = {
  view: AdminView;
  state: AdminState | null;
  open: boolean;
  onViewChange: (view: AdminView) => void;
};

export function AdminSidebar({ view, state, open, onViewChange }: SidebarProps) {
  const connectionLabel = !state
    ? "確認中"
    : !state.unityConnected
      ? "Unity未接続"
      : state.stale
        ? "Snapshot更新待ち"
        : "Unity接続中";
  const connectionClass = !state ? "" : !state.unityConnected || state.stale ? "stale" : "live";

  return (
    <aside className={`adminSidebar ${open ? "open" : ""}`}>
      <div className="adminSidebarHead">
        <div className="adminBrand">
          <strong>SmartParking</strong>
          <span>Operations Console</span>
        </div>
      </div>
      <nav className="adminNav" aria-label="管理メニュー">
        {navItems.map((item) => (
          <button key={item.id} className={view === item.id ? "active" : ""} type="button" onClick={() => onViewChange(item.id)}>
            {item.label}
          </button>
        ))}
      </nav>
      <div className="sideNote">
        <span className={`statusPill ${connectionClass}`}>{connectionLabel}</span>
        <p>最終更新 {formatTime(state?.updatedAt)}</p>
        <p>snapshot v{state?.snapshotVersion ?? 0}</p>
        <p>Command {state?.unityConnected && state?.commandTarget ? "操作可能" : "受信待ち"}</p>
      </div>
    </aside>
  );
}

type TopbarProps = {
  view: AdminView;
  onRefresh: () => void;
  onMenuToggle: () => void;
};

export function AdminTopbar({ view, onRefresh, onMenuToggle }: TopbarProps) {
  return (
    <header className="adminTopbar">
      <div className="adminTitleGroup">
        <button className="adminMenuButton" type="button" aria-label="管理メニューを開く" onClick={onMenuToggle}>
          <span />
          <span />
          <span />
        </button>
      </div>
    </header>
  );
}
