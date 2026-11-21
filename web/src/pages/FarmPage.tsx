import { useCallback, useEffect, useState } from "react";
import { api, type FarmReport, type FarmSession, type ItemDelta } from "../api";
import { formatDuration, formatQty, formatSigned, formatTime, formatValue } from "../format";
import { ItemIcon } from "../components/ItemIcon";
import { useToast } from "../components/Toast";

function DeltaTable({ rows, divine, title }: { rows: ItemDelta[]; divine: number; title: string }) {
  if (rows.length === 0) return null;
  return (
    <div className="card">
      <h2>{title}</h2>
      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <th className="icon-cell" />
              <th>Item</th>
              <th className="num">Trước</th>
              <th className="num">Sau</th>
              <th className="num">±SL</th>
              <th className="num">Đơn giá</th>
              <th className="num">± Giá trị</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.itemKey}>
                <td className="icon-cell">
                  <ItemIcon src={r.icon} />
                </td>
                <td>{r.name}</td>
                <td className="num dim-cell">{formatQty(r.startQty)}</td>
                <td className="num dim-cell">{formatQty(r.endQty)}</td>
                <td className={`num ${r.deltaQty > 0 ? "up" : "down"}`}>
                  {r.deltaQty > 0 ? "+" : ""}
                  {formatQty(r.deltaQty)}
                </td>
                <td className="num dim-cell">{formatValue(r.unitChaos, divine)}</td>
                <td className={`num ${r.deltaValue >= 0 ? "up" : "down"}`}>{formatSigned(r.deltaValue, divine)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

export function FarmPage({ divine, dataVersion }: { divine: number; dataVersion: number }) {
  const [sessions, setSessions] = useState<FarmSession[]>([]);
  const [selected, setSelected] = useState<number | null>(null);
  const [report, setReport] = useState<FarmReport | null>(null);
  const [name, setName] = useState("");
  const [busy, setBusy] = useState(false);
  const toast = useToast();

  const load = useCallback(() => {
    api.farmList().then((list) => {
      setSessions(list);
      setSelected((cur) => cur ?? list[0]?.id ?? null);
    });
  }, []);

  useEffect(load, [load, dataVersion]);

  useEffect(() => {
    if (selected == null) {
      setReport(null);
      return;
    }
    api.farmReport(selected).then(setReport).catch(() => setReport(null));
  }, [selected, dataVersion]);

  const active = sessions.find((s) => !s.endTs);

  const run = async (fn: () => Promise<unknown>) => {
    setBusy(true);
    try {
      await fn();
      load();
    } catch (e) {
      toast((e as Error).message, "error");
    } finally {
      setBusy(false);
    }
  };

  const start = () =>
    run(async () => {
      const r = await api.farmStart(name);
      setName("");
      setSelected(r.id);
    });

  const stop = (id: number) =>
    run(async () => {
      setReport(await api.farmStop(id));
    });

  const remove = (id: number) =>
    run(async () => {
      if (!window.confirm("Xoá phiên farm này?")) return;
      await api.farmDelete(id);
      if (selected === id) setSelected(null);
    });

  const r = report;
  return (
    <div className="stack">
      <div className="card">
        <div className="card-head">
          <h2>Phiên farm</h2>
          <span className="muted small">
            Bắt đầu: chụp snapshot. Kết thúc: chụp lần nữa. Lợi nhuận = số item thay đổi × giá lúc kết thúc.
          </span>
          <span className="spacer" />
          {active ? (
            <button className="btn primary" disabled={busy} onClick={() => stop(active.id)}>
              {busy ? "Đang chụp…" : `Kết thúc "${active.name}"`}
            </button>
          ) : (
            <>
              <input type="text" placeholder="Tên phiên (vd. T17 Abyss)" value={name} onChange={(e) => setName(e.target.value)} />
              <button className="btn primary" disabled={busy} onClick={start}>
                {busy ? "Đang chụp…" : "Bắt đầu phiên"}
              </button>
            </>
          )}
        </div>
        {sessions.length === 0 ? (
          <div className="empty">Chưa có phiên nào.</div>
        ) : (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Tên</th>
                  <th>Bắt đầu</th>
                  <th>Kết thúc</th>
                  <th className="num">Net worth ±</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {sessions.map((s) => (
                  <tr key={s.id} className={`clickable ${s.id === selected ? "selected" : ""}`} onClick={() => setSelected(s.id)}>
                    <td>
                      {!s.endTs && <span className="live-dot" style={{ marginRight: 8 }} />}
                      {s.name}
                    </td>
                    <td className="dim-cell">{formatTime(s.startTs, false)}</td>
                    <td className="dim-cell">{s.endTs ? formatTime(s.endTs, false) : "đang chạy"}</td>
                    <td className={`num ${(s.endChaos ?? s.startChaos) - s.startChaos >= 0 ? "up" : "down"}`}>
                      {s.endChaos != null ? formatSigned(s.endChaos - s.startChaos, divine) : "–"}
                    </td>
                    <td className="num">
                      <button className="btn ghost small" onClick={(e) => { e.stopPropagation(); remove(s.id); }}>
                        Xoá
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {r && (
        <>
          <div className="card">
            <div className="card-head">
              <h2>{r.session.name}</h2>
              <span className="muted small">
                {formatTime(r.session.startTs)} → {r.session.endTs ? formatTime(r.session.endTs) : `snapshot ${formatTime(r.comparedTs)} (đang chạy)`}
              </span>
            </div>
            <div className="kpis">
              <div className="kpi">
                <div className="label">Thời gian</div>
                <div className="value">{formatDuration(r.durationHours)}</div>
              </div>
              <div className="kpi">
                <div className="label">Giá trị loot</div>
                <div className={`value ${r.lootValue >= 0 ? "up" : "down"}`}>{formatSigned(r.lootValue, divine)}</div>
              </div>
              <div className="kpi">
                <div className="label">Mỗi giờ</div>
                <div className="value gold">{formatValue(r.perHour, divine)}/h</div>
              </div>
              <div className="kpi">
                <div className="label">Net worth ± (gồm biến động giá)</div>
                <div className={`value ${r.netWorthChange >= 0 ? "up" : "down"}`}>{formatSigned(r.netWorthChange, divine)}</div>
              </div>
            </div>
          </div>
          <div className="grid-2">
            <DeltaTable rows={r.gained} divine={divine} title="Thu được" />
            <DeltaTable rows={r.spent} divine={divine} title="Đã dùng / bán" />
          </div>
        </>
      )}
    </div>
  );
}
