import { useEffect, useState } from "react";
import { api, type HistoryPoint, type Overview } from "../api";
import { formatValue, formatQty } from "../format";
import { NetWorthChart } from "../components/NetWorthChart";
import { ItemIcon } from "../components/ItemIcon";

interface Props {
  overview: Overview | null;
  divine: number;
  dataVersion: number;
  onGo: (page: "settings" | "tabs" | "items") => void;
}

const RANGES = [
  { label: "24 giờ", hours: 24 },
  { label: "7 ngày", hours: 24 * 7 },
  { label: "Tất cả", hours: undefined },
];

export function OverviewPage({ overview, divine, dataVersion, onGo }: Props) {
  const [history, setHistory] = useState<HistoryPoint[]>([]);
  const [unit, setUnit] = useState<"divine" | "chaos">("divine");
  const [range, setRange] = useState<number | undefined>(undefined);

  useEffect(() => {
    api.history(range).then(setHistory).catch(() => setHistory([]));
  }, [range, dataVersion]);

  if (overview && !overview.snapshot) {
    return (
      <div className="card empty">
        <h3>Chưa có snapshot nào</h3>
        <p>
          Nhập tên tài khoản và POESESSID trong <a href="#/settings">Cài đặt</a>, rồi bấm <b>Chụp snapshot</b>.
        </p>
        <button className="btn primary" onClick={() => onGo("settings")}>
          Mở Cài đặt
        </button>
      </div>
    );
  }

  const perHour = overview?.perHourChaos;
  return (
    <div className="stack">
      <div className="card">
        <div className="card-head">
          <h2>Net worth theo thời gian</h2>
          <select value={unit} onChange={(e) => setUnit(e.target.value as "divine" | "chaos")}>
            <option value="divine">divine</option>
            <option value="chaos">chaos</option>
          </select>
          {perHour != null && (
            <span className="muted">
              {perHour >= 0 ? "" : "−"}
              {formatValue(Math.abs(perHour), divine, unit === "divine" ? "divine" : "chaos")}/giờ trên toàn bộ lịch sử
            </span>
          )}
          <span className="spacer" />
          <div className="segmented">
            {RANGES.map((r) => (
              <button key={r.label} className={range === r.hours ? "active" : ""} onClick={() => setRange(r.hours)}>
                {r.label}
              </button>
            ))}
          </div>
        </div>
        {history.length > 0 ? <NetWorthChart points={history} unit={unit} /> : <div className="empty">Chưa có dữ liệu.</div>}
      </div>

      <div className="grid-2">
        <div className="card">
          <h2>Theo tab / nhân vật</h2>
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Nguồn</th>
                  <th className="num">Item</th>
                  <th className="num">Giá trị</th>
                </tr>
              </thead>
              <tbody>
                {overview?.sources?.map((s) => (
                  <tr key={s.sourceKey} className="clickable" onClick={() => (window.location.hash = `#/tabs?${encodeURIComponent(s.sourceKey)}`)}>
                    <td>
                      {s.kind === "character" ? (
                        <span title="Nhân vật" style={{ marginRight: 6 }}>🧍</span>
                      ) : (
                        <span className="source-dot" style={{ background: s.colour ?? "#555" }} />
                      )}
                      {s.name}
                    </td>
                    <td className="num dim-cell">{formatQty(s.itemCount)}</td>
                    <td className="num">{formatValue(s.valueChaos, divine)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
        <div className="card">
          <h2>Top item</h2>
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th className="icon-cell" />
                  <th>Item</th>
                  <th className="num">SL</th>
                  <th className="num">Tổng</th>
                </tr>
              </thead>
              <tbody>
                {overview?.topItems?.map((i) => (
                  <tr key={i.itemKey}>
                    <td className="icon-cell">
                      <ItemIcon src={i.icon} />
                    </td>
                    <td>{i.name}</td>
                    <td className="num dim-cell">{formatQty(i.quantity)}</td>
                    <td className="num">{formatValue(i.totalChaos, divine)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </div>
  );
}
