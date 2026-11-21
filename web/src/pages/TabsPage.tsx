import { useEffect, useMemo, useState } from "react";
import { api, type Item, type Source } from "../api";
import { formatQty, formatValue } from "../format";
import { ItemTable } from "../components/ItemTable";

function selectedFromHash() {
  const q = window.location.hash.split("?")[1];
  return q ? decodeURIComponent(q) : null;
}

export function TabsPage({ divine, dataVersion }: { divine: number; dataVersion: number }) {
  const [sources, setSources] = useState<Source[]>([]);
  const [selected, setSelected] = useState<string | null>(selectedFromHash);
  const [items, setItems] = useState<Item[]>([]);
  const [search, setSearch] = useState("");
  const [hideZero, setHideZero] = useState(true);

  useEffect(() => {
    api.sources().then((s) => {
      setSources(s);
      setSelected((cur) => cur ?? s[0]?.sourceKey ?? null);
    });
  }, [dataVersion]);

  useEffect(() => {
    if (!selected) return;
    window.history.replaceState(null, "", `#/tabs?${encodeURIComponent(selected)}`);
    api.items({ source: selected }).then(setItems).catch(() => setItems([]));
  }, [selected, dataVersion]);

  const shown = useMemo(
    () =>
      items.filter(
        (i) => (!hideZero || i.totalChaos > 0) && (!search || i.name.toLowerCase().includes(search.toLowerCase())),
      ),
    [items, search, hideZero],
  );

  if (sources.length === 0) return <div className="card empty">Chưa có snapshot nào.</div>;
  const current = sources.find((s) => s.sourceKey === selected);

  return (
    <div className="grid-2" style={{ gridTemplateColumns: "minmax(260px, 340px) 1fr" }}>
      <div className="card">
        <h2>Tab / nhân vật</h2>
        <table>
          <tbody>
            {sources.map((s) => (
              <tr
                key={s.sourceKey}
                className={`clickable ${s.sourceKey === selected ? "selected" : ""}`}
                onClick={() => setSelected(s.sourceKey)}
              >
                <td>
                  {s.kind === "character" ? (
                    <span style={{ marginRight: 6 }}>🧍</span>
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
      <div className="card">
        <div className="card-head">
          <h2>{current?.name ?? "–"}</h2>
          <span className="muted">{current ? formatValue(current.valueChaos, divine) : ""}</span>
          <span className="spacer" />
          <input type="text" placeholder="Tìm item…" value={search} onChange={(e) => setSearch(e.target.value)} />
          <label className="check small">
            <input type="checkbox" checked={hideZero} onChange={(e) => setHideZero(e.target.checked)} />
            Ẩn món 0 c
          </label>
        </div>
        <ItemTable items={shown} divine={divine} />
      </div>
    </div>
  );
}
