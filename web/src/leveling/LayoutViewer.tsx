import { useEffect, useMemo, useState } from "react";

let indexPromise: Promise<string[]> | null = null;
/** Names of the bundled layout images ("<areaId> <path>"), from Exile-UI's act-decoder. */
function layoutIndex() {
  indexPromise ??= fetch("/layouts/index.json").then((r) => r.json());
  return indexPromise;
}

const url = (areaId: string, path: string) => `/layouts/${encodeURIComponent(`${areaId} ${path}`)}.jpg`;

const label = (path: string) => {
  const last = path.split("_").pop()!;
  if (last === "x") return "khác";
  if (last === "y") return "chung";
  return `#${last}`;
};

/**
 * Zone layouts are a small tree per area: "1", "2", … show what the start of the zone looks like on the
 * minimap; "1_1", "1_2", … show where to go once you know which variant you have, and so on.
 * Pick the picture that matches your minimap to drill down.
 */
export function LayoutViewer({ areaId }: { areaId: string }) {
  const [all, setAll] = useState<string[] | null>(null);
  const [path, setPath] = useState<string | null>(null);
  const [legend, setLegend] = useState(false);

  useEffect(() => {
    layoutIndex().then(setAll).catch(() => setAll([]));
  }, []);

  useEffect(() => setPath(null), [areaId]);

  const paths = useMemo(
    () => (all ?? []).filter((n) => n.startsWith(`${areaId} `)).map((n) => n.slice(areaId.length + 1)),
    [all, areaId],
  );
  const childrenOf = (p: string | null) =>
    paths.filter((x) => (p === null ? !x.includes("_") : x.startsWith(`${p}_`) && !x.slice(p.length + 1).includes("_")));

  if (all === null) return null;
  if (paths.length === 0) return <div className="faint small" style={{ marginTop: 12 }}>Không có ảnh layout cho khu vực này.</div>;

  const top = childrenOf(null);
  // With a single entry variant there is nothing to choose: start from it.
  const current = path ?? (top.length === 1 ? top[0] : null);
  const options = childrenOf(current);
  const crumbs = current ? current.split("_").map((_, i, a) => a.slice(0, i + 1).join("_")) : [];

  if (current === null && !legend) {
    return (
      <div>
        <div className="faint small" style={{ marginTop: 12 }}>
          Có {top.length} kiểu bản đồ. Chọn ảnh giống minimap lúc bạn vừa vào khu vực:
        </div>
        <div className="layout-thumbs large">
          {top.map((p) => (
            <button key={p} className="layout-thumb" onClick={() => setPath(p)} title={label(p)}>
              <img src={url(areaId, p)} alt="" loading="lazy" />
              <span className="tag">{label(p)}</span>
            </button>
          ))}
        </div>
        <label className="check small" style={{ marginTop: 8 }}>
          <input type="checkbox" checked={legend} onChange={(e) => setLegend(e.target.checked)} />
          Chú thích
        </label>
      </div>
    );
  }

  return (
    <div>
      <div className="layout-main">
        {legend || current === null ? (
          <img src="/layouts/explanation.jpg" alt="Chú thích" />
        ) : (
          <img src={url(areaId, current)} alt={`Layout ${current}`} />
        )}
      </div>
      <div className="crumbs">
        <button className="btn small ghost" onClick={() => setPath(null)} disabled={!current || top.length === 1}>
          Tất cả kiểu
        </button>
        {crumbs.map((c) => (
          <button key={c} className="btn small ghost" onClick={() => setPath(c)} disabled={c === current}>
            {label(c)}
          </button>
        ))}
        <span className="spacer" />
        <label className="check small">
          <input type="checkbox" checked={legend} onChange={(e) => setLegend(e.target.checked)} />
          Chú thích
        </label>
      </div>
      {options.length > 0 && (
        <>
          <div className="faint small" style={{ marginTop: 8 }}>
            Bước tiếp theo:
          </div>
          <div className="layout-thumbs">
            {options.map((p) => (
              <button key={p} className="layout-thumb" onClick={() => setPath(p)} title={label(p)}>
                <img src={url(areaId, p)} alt="" loading="lazy" />
                <span className="tag">{label(p)}</span>
              </button>
            ))}
          </div>
        </>
      )}
    </div>
  );
}
