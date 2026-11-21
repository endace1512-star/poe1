import { useEffect, useState } from "react";
import { api, type PriceChangeReport, type Settings } from "../api";
import { formatAgo, formatQty, formatSigned, formatTime, formatValue } from "../format";
import { ItemIcon } from "../components/ItemIcon";
import { useToast } from "../components/Toast";

const COMPARE = [
  { hours: 1, label: "1 giờ trước" },
  { hours: 6, label: "6 giờ trước" },
  { hours: 24, label: "hôm qua (24 giờ)" },
  { hours: 72, label: "3 ngày trước" },
  { hours: 168, label: "tuần trước (7 ngày)" },
];

interface Props {
  divine: number;
  dataVersion: number;
  settings: Settings | null;
  onSettings: (s: Settings) => void;
}

export function AlertsPage({ divine, dataVersion, settings, onSettings }: Props) {
  const [pct, setPct] = useState(settings?.alertMinChangePercent ?? 20);
  const [hours, setHours] = useState(settings?.alertCompareHours ?? 24);
  const [minHeld, setMinHeld] = useState(settings?.alertMinHeldChaos ?? 20);
  const [report, setReport] = useState<PriceChangeReport | null>(null);
  const toast = useToast();

  useEffect(() => {
    const t = window.setTimeout(() => {
      api.alerts({ pct, hours, minHeld }).then(setReport).catch(() => setReport(null));
    }, 250);
    return () => window.clearTimeout(t);
  }, [pct, hours, minHeld, dataVersion]);

  // Remember the thresholds so the header badge and desktop notifications use them too.
  useEffect(() => {
    if (!settings) return;
    if (settings.alertMinChangePercent === pct && settings.alertCompareHours === hours && settings.alertMinHeldChaos === minHeld) return;
    const t = window.setTimeout(() => {
      api.saveSettings({ alertMinChangePercent: pct, alertCompareHours: hours, alertMinHeldChaos: minHeld }).then(onSettings).catch(() => {});
    }, 800);
    return () => window.clearTimeout(t);
  }, [pct, hours, minHeld, settings, onSettings]);

  const toggleNotify = async (on: boolean) => {
    if (on && "Notification" in window && Notification.permission !== "granted") {
      const p = await Notification.requestPermission();
      if (p !== "granted") {
        toast("Trình duyệt chưa cho phép thông báo.", "error");
        return;
      }
    }
    onSettings(await api.saveSettings({ alertDesktopNotify: on }));
  };

  return (
    <div className="card">
      <div className="card-head">
        <h2>Món trong stash đổi giá mạnh</h2>
        <label className="row small muted" style={{ gap: 6 }}>
          Đổi ≥
          <input type="number" min={0} value={pct} onChange={(e) => setPct(Number(e.target.value) || 0)} />%
        </label>
        <label className="row small muted" style={{ gap: 6 }}>
          so với
          <select value={hours} onChange={(e) => setHours(Number(e.target.value))}>
            {COMPARE.map((c) => (
              <option key={c.hours} value={c.hours}>
                {c.label}
              </option>
            ))}
          </select>
        </label>
        <label className="row small muted" style={{ gap: 6 }}>
          giá trị đang giữ ≥
          <input type="number" min={0} value={minHeld} onChange={(e) => setMinHeld(Number(e.target.value) || 0)} />
          chaos
        </label>
        <label className="check small">
          <input type="checkbox" checked={settings?.alertDesktopNotify ?? false} onChange={(e) => toggleNotify(e.target.checked)} />
          Thông báo desktop sau mỗi snapshot
        </label>
      </div>
      {report?.currentTs && report.referenceTs ? (
        <p className="muted small" style={{ marginTop: -4 }}>
          So snapshot {formatTime(report.currentTs)} với {formatTime(report.referenceTs)} ({formatAgo(report.referenceTs)}).
          Chỉ tính món có giá ở cả hai lần.
        </p>
      ) : (
        <p className="muted small">Cần ít nhất 2 snapshot để so sánh giá.</p>
      )}

      {report && report.items.length > 0 ? (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th className="icon-cell" />
                <th>Item</th>
                <th>Ở tab</th>
                <th className="num">SL</th>
                <th className="num">Giá cũ</th>
                <th className="num">Giá mới</th>
                <th className="num">%</th>
                <th className="num">± Giá trị đang giữ</th>
              </tr>
            </thead>
            <tbody>
              {report.items.map((i) => (
                <tr key={i.itemKey}>
                  <td className="icon-cell">
                    <ItemIcon src={i.icon} />
                  </td>
                  <td>{i.name}</td>
                  <td className="dim-cell">{i.sources}</td>
                  <td className="num">{formatQty(i.quantity)}</td>
                  <td className="num dim-cell">{formatValue(i.oldUnit, divine)}</td>
                  <td className="num">{formatValue(i.newUnit, divine)}</td>
                  <td className={`num ${i.changePercent > 0 ? "up" : "down"}`}>
                    {i.changePercent > 0 ? "▲ +" : "▼ "}
                    {i.changePercent.toFixed(0)}%
                  </td>
                  <td className={`num ${i.heldChange > 0 ? "up" : "down"}`}>{formatSigned(i.heldChange, divine)}</td>
                </tr>
              ))}
            </tbody>
          </table>
          <div className="table-foot">
            <span>
              {report.rising} món tăng · {report.falling} món giảm
            </span>
            <strong className={report.netHeldChange >= 0 ? "up" : "down"}>{formatSigned(report.netHeldChange, divine)}</strong>
          </div>
        </div>
      ) : (
        report?.referenceTs && <div className="empty">Không có món nào đổi giá vượt ngưỡng.</div>
      )}
    </div>
  );
}
