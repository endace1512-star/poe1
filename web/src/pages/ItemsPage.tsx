import { useEffect, useMemo, useState } from "react";
import { api, type Item } from "../api";
import { CATEGORY_LABELS } from "../format";
import { ItemTable } from "../components/ItemTable";

export function ItemsPage({ divine, dataVersion }: { divine: number; dataVersion: number }) {
  const [items, setItems] = useState<Item[]>([]);
  const [search, setSearch] = useState("");
  const [category, setCategory] = useState("");
  const [minValue, setMinValue] = useState(1);

  useEffect(() => {
    api.items({}).then(setItems).catch(() => setItems([]));
  }, [dataVersion]);

  const categories = useMemo(() => Array.from(new Set(items.map((i) => i.category))).sort(), [items]);
  const shown = useMemo(
    () =>
      items.filter(
        (i) =>
          i.totalChaos >= minValue &&
          (!category || i.category === category) &&
          (!search || i.name.toLowerCase().includes(search.toLowerCase())),
      ),
    [items, search, category, minValue],
  );

  return (
    <div className="card">
      <div className="card-head">
        <h2>Tất cả item</h2>
        <span className="muted small">Gộp theo item trên mọi tab và nhân vật</span>
        <span className="spacer" />
        <input type="text" placeholder="Tìm item…" value={search} onChange={(e) => setSearch(e.target.value)} />
        <select value={category} onChange={(e) => setCategory(e.target.value)}>
          <option value="">Mọi loại</option>
          {categories.map((c) => (
            <option key={c} value={c}>
              {CATEGORY_LABELS[c] ?? c}
            </option>
          ))}
        </select>
        <label className="row small muted" style={{ gap: 6 }}>
          tổng ≥
          <input type="number" min={0} value={minValue} onChange={(e) => setMinValue(Number(e.target.value) || 0)} />
          chaos
        </label>
      </div>
      <ItemTable items={shown} divine={divine} showSource showCategory />
    </div>
  );
}
