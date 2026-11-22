import { useCallback, useEffect, useState } from "react";
import { api, type Overview, type Settings, type Status } from "./api";
import { useServerEvent } from "./events";
import { formatTime, formatValue } from "./format";
import { useToast } from "./components/Toast";
import { OverviewPage } from "./pages/OverviewPage";
import { TabsPage } from "./pages/TabsPage";
import { ItemsPage } from "./pages/ItemsPage";
import { AlertsPage } from "./pages/AlertsPage";
import { FarmPage } from "./pages/FarmPage";
import { SettingsPage } from "./pages/SettingsPage";
import { ComingSoonPage } from "./pages/ComingSoonPage";
import { LevelingPage } from "./leveling/LevelingPage";
import { BuildPage } from "./pages/BuildPage";

const PAGES = [
  { id: "overview", label: "Tổng quan" },
  { id: "tabs", label: "Theo tab" },
  { id: "items", label: "Item" },
  { id: "alerts", label: "Cảnh báo giá" },
  { id: "farm", label: "Phiên farm" },
  { id: "leveling", label: "Leveling" },
  { id: "build", label: "So build" },
  { id: "timeless", label: "Timeless", soon: true },
  { id: "flip", label: "Flip", soon: true },
  { id: "regex", label: "Regex", soon: true },
  { id: "patch", label: "Patch game", soon: true },
  { id: "settings", label: "Cài đặt" },
] as const;

type PageId = (typeof PAGES)[number]["id"];

function pageFromHash(): PageId {
  // "#/tabs?tab:abc" → "tabs"; the part after "?" is page state.
  const h = window.location.hash.replace(/^#\/?/, "").split("?")[0];
  return (PAGES.find((p) => p.id === h)?.id ?? "overview") as PageId;
}

export function App() {
  const [page, setPage] = useState<PageId>(pageFromHash);
  const [status, setStatus] = useState<Status | null>(null);
  const [overview, setOverview] = useState<Overview | null>(null);
  const [settings, setSettings] = useState<Settings | null>(null);
  const [alertCount, setAlertCount] = useState(0);
  const [dataVersion, setDataVersion] = useState(0);
  const toast = useToast();

  useEffect(() => {
    const onHash = () => setPage(pageFromHash());
    window.addEventListener("hashchange", onHash);
    return () => window.removeEventListener("hashchange", onHash);
  }, []);

  const go = (id: PageId) => {
    window.location.hash = `#/${id}`;
  };

  const refreshStatus = useCallback(() => api.status().then(setStatus).catch(() => {}), []);
  const refreshData = useCallback(async () => {
    try {
      const [o, a] = await Promise.all([api.overview(), api.alerts({})]);
      setOverview(o);
      setAlertCount(a.items.length);
      return a;
    } catch {
      return null;
    }
  }, []);

  useEffect(() => {
    refreshStatus();
    refreshData();
    api.settings().then(setSettings).catch(() => {});
    const t = window.setInterval(refreshStatus, 10000);
    return () => window.clearInterval(t);
  }, [refreshStatus, refreshData]);

  useServerEvent(
    "snapshot-progress",
    useCallback((p: { running: boolean; text?: string }) => {
      setStatus((s) => (s ? { ...s, running: p.running, progress: p.text ?? null } : s));
      if (!p.running) refreshStatus();
    }, [refreshStatus]),
  );

  useServerEvent(
    "snapshot",
    useCallback(async () => {
      setDataVersion((v) => v + 1);
      refreshStatus();
      const report = await refreshData();
      const s = await api.settings().catch(() => null);
      if (s) setSettings(s);
      if (report && report.items.length > 0 && s?.alertDesktopNotify && "Notification" in window && Notification.permission === "granted") {
        const up = report.rising;
        const down = report.falling;
        new Notification("poe1 · Cảnh báo giá", {
          body: `${up} món tăng, ${down} món giảm (≥ ${s.alertMinChangePercent}%). Mạnh nhất: ${report.items[0].name} ${report.items[0].changePercent > 0 ? "+" : ""}${report.items[0].changePercent.toFixed(0)}%`,
        });
      }
    }, [refreshStatus, refreshData]),
  );

  useServerEvent(
    "snapshot-error",
    useCallback((p: { message: string }) => toast(`Snapshot lỗi: ${p.message}`, "error"), [toast]),
  );

  const takeSnapshot = async () => {
    try {
      const r = await api.takeSnapshot();
      if (!r.started) toast(r.message ?? "Đang có snapshot chạy.");
    } catch (e) {
      toast((e as Error).message, "error");
    }
  };

  // Values are shown with the divine price of the latest snapshot so the header, tables and chart agree.
  const divine = overview?.snapshot?.divineChaos || status?.divineChaos || 0;
  const currentDivine = status?.divineChaos || divine;
  const snap = overview?.snapshot;

  return (
    <>
      <header className="app-header">
        <div className="brand">
          PoE1 <small>· local</small>
        </div>
        <div className="stat">
          <span className="label">Net worth</span>
          <span className="value big">{snap ? formatValue(snap.totalChaos, divine, "divine") : "–"}</span>
        </div>
        <div className="stat">
          <span className="label">Divine</span>
          <span className="value">{currentDivine ? `${Math.round(currentDivine)} c` : "–"}</span>
        </div>
        <div className="stat">
          <span className="label">Snapshot gần nhất</span>
          <span className="value">{formatTime(snap?.ts)}</span>
        </div>
        <div className="stat">
          <span className="label">League</span>
          <span className="value">{settings?.league ?? "–"}</span>
        </div>
        <div className="header-actions">
          {status?.running && <span className="progress-text">{status.progress ?? "Đang chụp…"}</span>}
          {!status?.running && status?.nextSnapshotAt && (
            <span className="progress-text">Lần tới: {formatTime(status.nextSnapshotAt)}</span>
          )}
          <button className="btn primary" onClick={takeSnapshot} disabled={status?.running}>
            {status?.running ? "Đang chụp…" : "Chụp snapshot"}
          </button>
        </div>
      </header>

      <nav className="nav">
        {PAGES.map((p) => (
          <button key={p.id} className={page === p.id ? "active" : ""} onClick={() => go(p.id)}>
            {p.label}
            {p.id === "alerts" && alertCount > 0 && <span className="badge">{alertCount}</span>}
            {"soon" in p && p.soon && <span className="badge soft">sắp có</span>}
          </button>
        ))}
      </nav>

      <main>
        {status?.lastError && !status.running && page !== "leveling" && page !== "build" && (
          <div className="notice error" style={{ marginBottom: 16 }}>
            Snapshot gần nhất lỗi: {status.lastError}
          </div>
        )}
        {page === "overview" && <OverviewPage overview={overview} divine={divine} dataVersion={dataVersion} onGo={go} />}
        {page === "tabs" && <TabsPage divine={divine} dataVersion={dataVersion} />}
        {page === "items" && <ItemsPage divine={divine} dataVersion={dataVersion} />}
        {page === "alerts" && settings && (
          <AlertsPage divine={divine} dataVersion={dataVersion} settings={settings} onSettings={setSettings} />
        )}
        {page === "farm" && <FarmPage divine={divine} dataVersion={dataVersion} />}
        {page === "leveling" && <LevelingPage />}
        {page === "build" && <BuildPage />}
        {page === "settings" && <SettingsPage onSaved={setSettings} />}
        {(page === "timeless" || page === "flip" || page === "regex" || page === "patch") && <ComingSoonPage page={page} />}
      </main>
    </>
  );
}
