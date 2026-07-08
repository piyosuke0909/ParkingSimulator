import type { AdminState } from "../types";
import { navItems } from "./constants";
import type { AdminView } from "./types";
import { formatTime } from "./utils";

type SidebarProps = {
  view: AdminView;
  state: AdminState | null;
  onViewChange: (view: AdminView) => void;
};

export function AdminSidebar({ view, state, onViewChange }: SidebarProps) {
  const connectionLabel = !state ? "確認中" : state.stale ? "Unity未受信" : "Unity接続中";
  const connectionClass = !state ? "" : state.stale ? "stale" : "live";

  return (
    <aside className="adminSidebar">
      <div className="adminBrand">
        <strong>SmartParking</strong>
        <span>Operations Console</span>
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
      </div>
    </aside>
  );
}

type TopbarProps = {
  view: AdminView;
  onRefresh: () => void;
};

export function AdminTopbar({ view, onRefresh }: TopbarProps) {
  return (
    <header className="adminTopbar">
      <div>
        <h1>{navItems.find((item) => item.id === view)?.label}</h1>
        <p>Unity snapshot を基準に、駐車案内と警備員配置を管理します。</p>
      </div>
      <div className="adminTopActions">
        <a className="adminLinkButton" href="/">
          ユーザー画面
        </a>
        <button className="adminButton" type="button" onClick={onRefresh}>
          更新
        </button>
      </div>
    </header>
  );
}
