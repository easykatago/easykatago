import { useState } from "react";
import { DiagnosticsPage } from "./features/diagnostics/DiagnosticsPage";
import { HomePage } from "./features/home/HomePage";
import { InstallPage } from "./features/install/InstallPage";
import { LogsPage } from "./features/logs/LogsPage";
import { ProfilesPage } from "./features/profiles/ProfilesPage";
import { SettingsPage } from "./features/settings/SettingsPage";
import "./styles/app.css";

type PageId = "home" | "install" | "settings" | "profiles" | "logs" | "diagnostics";

const pageItems: Array<{ id: PageId; label: string }> = [
  { id: "home", label: "首页" },
  { id: "install", label: "安装向导" },
  { id: "settings", label: "设置" },
  { id: "profiles", label: "档案" },
  { id: "logs", label: "日志" },
  { id: "diagnostics", label: "诊断" }
];

export function App() {
  const [activePage, setActivePage] = useState<PageId>("home");

  return (
    <div className="app-shell">
      <aside>
        {pageItems.map((item) => (
          <button
            key={item.id}
            type="button"
            aria-pressed={activePage === item.id}
            onClick={() => setActivePage(item.id)}
          >
            {item.label}
          </button>
        ))}
      </aside>
      <main>
        {activePage === "home" ? <HomePage /> : null}
        {activePage === "install" ? <InstallPage /> : null}
        {activePage === "settings" ? <SettingsPage /> : null}
        {activePage === "profiles" ? <ProfilesPage /> : null}
        {activePage === "logs" ? <LogsPage /> : null}
        {activePage === "diagnostics" ? <DiagnosticsPage /> : null}
      </main>
    </div>
  );
}
