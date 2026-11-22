import { useCallback, useEffect, useRef, useState } from "react";
import { api, type BuildReport, type Character, type GemGroupReport, type SavedBuild, type SlotReport, type StageOption } from "../api";
import { useServerEvent } from "../events";
import { formatTime } from "../format";
import { useToast } from "../components/Toast";

const STATUS: Record<string, { label: string; cls: string }> = {
  ok: { label: "Đạt", cls: "st-ok" },
  partial: { label: "Gần đạt", cls: "st-partial" },
  missing: { label: "Chưa có", cls: "st-missing" },
  empty: { label: "Trống", cls: "st-missing" },
};

const REASONS: Record<string, string> = {
  similar: "Đã có đồ tương tự",
  unavailable: "Không mua/craft được",
};

const RARITY_CLASS: Record<string, string> = { UNIQUE: "r-unique", RELIC: "r-unique", RARE: "r-rare", MAGIC: "r-magic", NORMAL: "r-normal" };

const pct = (x: number) => `${Math.round(x * 100)}%`;

interface Choice {
  spec?: number;
  itemSet?: number;
  skillSet?: number;
}

export function BuildPage() {
  const [builds, setBuilds] = useState<SavedBuild[]>([]);
  const [buildId, setBuildId] = useState<number | null>(null);
  const [characters, setCharacters] = useState<Character[]>([]);
  const [character, setCharacter] = useState("");
  const [choice, setChoice] = useState<Choice>({});
  const [report, setReport] = useState<BuildReport | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [input, setInput] = useState("");
  const [importing, setImporting] = useState(false);
  const [onlyTodo, setOnlyTodo] = useState(false);
  const lastRefresh = useRef(0);
  const toast = useToast();

  const loadBuilds = useCallback(async (select?: number) => {
    const list = await api.builds();
    setBuilds(list);
    setBuildId((cur) => select ?? (cur && list.some((b) => b.id === cur) ? cur : list[0]?.id ?? null));
  }, []);

  useEffect(() => {
    loadBuilds();
    api.buildCharacters().then(setCharacters).catch(() => setCharacters([]));
  }, [loadBuilds]);

  // Character remembered per build.
  useEffect(() => {
    const b = builds.find((x) => x.id === buildId);
    setCharacter(b?.character ?? "");
    setChoice({});
  }, [buildId, builds]);

  const load = useCallback(
    async (refresh: boolean) => {
      if (buildId == null) return;
      setLoading(true);
      try {
        const r = await api.buildReport(buildId, { character: character || undefined, ...choice, refresh });
        setReport(r);
        setError(null);
        lastRefresh.current = Date.now();
      } catch (e) {
        setError((e as Error).message);
        setReport(null);
      } finally {
        setLoading(false);
      }
    },
    [buildId, character, choice],
  );

  useEffect(() => {
    load(false);
  }, [load]);

  // The game saves your character when you change zone: refresh then (at most once a minute).
  useServerEvent(
    "area",
    useCallback(() => {
      if (Date.now() - lastRefresh.current > 60_000) load(true);
    }, [load]),
  );

  const doImport = async () => {
    setImporting(true);
    try {
      const b = await api.importBuild(input);
      setInput("");
      await loadBuilds(b.id);
      toast(`Đã nhập build: ${b.name}`);
    } catch (e) {
      toast((e as Error).message, "error");
    } finally {
      setImporting(false);
    }
  };

  const remove = async () => {
    if (buildId == null || !window.confirm("Xoá build này?")) return;
    await api.deleteBuild(buildId);
    setReport(null);
    loadBuilds();
  };

  const rename = async () => {
    const b = builds.find((x) => x.id === buildId);
    const name = b && window.prompt("Tên build", b.name);
    if (!b || !name) return;
    await api.updateBuild(b.id, { name });
    loadBuilds();
  };

  const setIgnore = async (key: string, reason: string | null) => {
    if (buildId == null) return;
    await api.ignoreBuildItem(buildId, key, reason);
    load(false);
  };

  const stage = report?.stage;
  const nextStage = () => {
    if (!stage) return;
    const next = (opts: StageOption[], cur: number) => Math.min(cur + 1, Math.max(0, opts.length - 1));
    setChoice({ spec: next(stage.specs, stage.spec), itemSet: next(stage.itemSets, stage.itemSet), skillSet: next(stage.skillSets, stage.skillSet) });
  };

  const visible = (s: { status: string; ignored: string | null }) => !onlyTodo || (s.status !== "ok" && !s.ignored);

  return (
    <div className="stack">
      <div className="card">
        <div className="card-head">
          <h2>So build</h2>
          <span className="muted small">Dán link pobb.in / pastebin / poe.ninja hoặc mã export của Path of Building.</span>
        </div>
        <div className="row">
          <input
            type="text"
            style={{ flex: 1, minWidth: 260 }}
            placeholder="https://pobb.in/… hoặc eNrtfWl34ziS..."
            value={input}
            onChange={(e) => setInput(e.target.value)}
            onKeyDown={(e) => e.key === "Enter" && input.trim() && doImport()}
          />
          <button className="btn primary" onClick={doImport} disabled={!input.trim() || importing}>
            {importing ? "Đang nhập…" : "Nhập build"}
          </button>
        </div>
        {builds.length > 0 && (
          <div className="row" style={{ marginTop: 12 }}>
            <label className="row small" style={{ gap: 6 }}>
              Build
              <select value={buildId ?? ""} onChange={(e) => setBuildId(Number(e.target.value))}>
                {builds.map((b) => (
                  <option key={b.id} value={b.id}>
                    {b.name}
                  </option>
                ))}
              </select>
            </label>
            <button className="btn small ghost" onClick={rename}>
              Đổi tên
            </button>
            <button className="btn small ghost" onClick={remove}>
              Xoá
            </button>
            <label className="row small" style={{ gap: 6, marginLeft: 12 }}>
              Nhân vật
              <select
                value={character}
                onChange={(e) => {
                  setCharacter(e.target.value);
                  setChoice({});
                }}
              >
                <option value="">— chọn —</option>
                {characters.map((c) => (
                  <option key={c.name} value={c.name}>
                    {c.name} ({c.class} {c.level}
                    {c.league ? `, ${c.league}` : ""})
                  </option>
                ))}
              </select>
            </label>
            <span className="spacer" />
            {report && <span className="faint small">Đồ cập nhật lúc {formatTime(report.character.fetchedAt)}</span>}
            <button className="btn small" onClick={() => load(true)} disabled={loading}>
              {loading ? "Đang tải…" : "Làm mới đồ"}
            </button>
          </div>
        )}
        <p className="faint small" style={{ margin: "10px 0 0" }}>
          Đồ đọc từ pathofexile.com. Game lưu nhân vật lên server khi bạn đổi khu vực, nên đang chơi thì tab này tự làm mới mỗi lần bạn qua map
          (tối đa 1 lần/phút).
        </p>
      </div>

      {builds.length === 0 && <div className="card empty">Chưa có build nào. Dán link hoặc mã PoB ở trên.</div>}
      {error && <div className="notice error">{error}</div>}

      {report && stage && (
        <>
          <div className="card">
            <div className="card-head">
              <h2>
                Giai đoạn: <span className="gold">{stage.specs[stage.detectedSpec]?.title ?? "–"}</span>
              </h2>
              <span className="muted small">
                {report.character.name}
                {report.character.level ? ` · lv${report.character.level}` : ""} — build {report.buildAscendancy ?? report.buildClass} lv{report.buildLevel}
                {report.character.demo && " (dữ liệu demo)"}
              </span>
              <span className="spacer" />
              <button className="btn small" onClick={nextStage} disabled={stage.spec >= stage.specs.length - 1 && stage.itemSet >= stage.itemSets.length - 1}>
                Giai đoạn tiếp →
              </button>
              <button className="btn small ghost" onClick={() => setChoice({})}>
                Về giai đoạn hiện tại
              </button>
            </div>
            <div className="stage-grid">
              <StageSelect label="Cây passive" options={stage.specs} value={stage.spec} detected={stage.detectedSpec} onChange={(v) => setChoice({ ...choice, spec: v })} />
              <StageSelect label="Bộ đồ" options={stage.itemSets} value={stage.itemSet} detected={stage.detectedItemSet} onChange={(v) => setChoice({ ...choice, itemSet: v })} />
              <StageSelect label="Bộ gem" options={stage.skillSets} value={stage.skillSet} detected={stage.detectedSkillSet} onChange={(v) => setChoice({ ...choice, skillSet: v })} />
            </div>
            <div className="kpis" style={{ marginTop: 14 }}>
              <div className="kpi">
                <div className="label">Tiến độ</div>
                <div className="value gold">{pct(report.summary.score)}</div>
              </div>
              <div className="kpi">
                <div className="label">Đồ đạt / gần đạt / thiếu</div>
                <div className="value">
                  <span className="up">{report.summary.ok}</span> / <span className="warn">{report.summary.partial}</span> /{" "}
                  <span className="down">{report.summary.missing}</span>
                </div>
              </div>
              <div className="kpi">
                <div className="label">Nhóm gem đủ</div>
                <div className="value">
                  {report.summary.gemGroupsOk}/{report.summary.gemGroupsTotal}
                </div>
              </div>
              <div className="kpi">
                <div className="label">Cây passive khớp</div>
                <div className="value">
                  {report.tree.matched}/{report.tree.target}
                </div>
              </div>
              <div className="kpi">
                <div className="label">Đã bỏ qua</div>
                <div className="value muted">{report.summary.ignored}</div>
              </div>
            </div>
          </div>

          <div className="row">
            <label className="check small">
              <input type="checkbox" checked={onlyTodo} onChange={(e) => setOnlyTodo(e.target.checked)} />
              Chỉ hiện món còn thiếu
            </label>
          </div>

          <h3 className="section-title">Trang bị</h3>
          <div className="slot-grid">
            {report.slots.filter(visible).map((s) => (
              <SlotCard key={s.key} slot={s} onIgnore={setIgnore} />
            ))}
          </div>

          {report.gems.length > 0 && (
            <>
              <h3 className="section-title">Gem</h3>
              <div className="card">
                {report.gems.filter(visible).map((g) => (
                  <GemRow key={g.key} group={g} onIgnore={setIgnore} />
                ))}
              </div>
            </>
          )}

          {report.jewels.length > 0 && (
            <>
              <h3 className="section-title">Jewel trên cây</h3>
              <div className="slot-grid">
                {report.jewels.filter(visible).map((s) => (
                  <SlotCard key={s.key} slot={s} onIgnore={setIgnore} />
                ))}
              </div>
            </>
          )}

          <h3 className="section-title">Cây passive</h3>
          <div className="card">
            <div className="row">
              <span>
                Đã khớp <b>{report.tree.matched}</b>/{report.tree.target} điểm của cây "{stage.specs[stage.spec]?.title}". Bạn đang có {report.tree.allocated} điểm.
              </span>
              <span className="spacer" />
              {report.tree.url && (
                <a className="btn small" href={report.tree.url} target="_blank" rel="noreferrer">
                  Mở cây của build
                </a>
              )}
            </div>
            <div className="grid-2" style={{ marginTop: 12 }}>
              <div>
                <div className="muted small">Còn thiếu ({report.tree.missingNotables.length} notable/keystone + {report.tree.missingOther} điểm nhỏ)</div>
                <div className="chips">
                  {report.tree.missingNotables.map((n) => (
                    <span key={n} className="chip miss">
                      {n}
                    </span>
                  ))}
                </div>
              </div>
              <div>
                <div className="muted small">Đang có nhưng build không lấy ({report.tree.extraNotables.length} notable + {report.tree.extraOther} điểm nhỏ) — cần respec</div>
                <div className="chips">
                  {report.tree.extraNotables.map((n) => (
                    <span key={n} className="chip extra">
                      {n}
                    </span>
                  ))}
                </div>
              </div>
            </div>
          </div>
        </>
      )}
    </div>
  );
}

function StageSelect({ label, options, value, detected, onChange }: { label: string; options: StageOption[]; value: number; detected: number; onChange: (v: number) => void }) {
  if (options.length === 0) return null;
  return (
    <label className="stage-select">
      <span className="muted small">{label}</span>
      <select value={value} onChange={(e) => onChange(Number(e.target.value))} disabled={options.length < 2}>
        {options.map((o) => (
          <option key={o.index} value={o.index}>
            {o.title} — khớp {pct(o.match)}
            {o.index === detected ? " (bạn đang ở đây)" : ""}
          </option>
        ))}
      </select>
    </label>
  );
}

function IgnoreControl({ itemKey, ignored, onIgnore }: { itemKey: string; ignored: string | null; onIgnore: (key: string, reason: string | null) => void }) {
  return (
    <div className="ignore-row">
      <label className="check small">
        <input type="checkbox" checked={!!ignored} onChange={(e) => onIgnore(itemKey, e.target.checked ? "similar" : null)} />
        Bỏ qua
      </label>
      {ignored && (
        <select className="small" value={ignored} onChange={(e) => onIgnore(itemKey, e.target.value)}>
          {Object.entries(REASONS).map(([k, v]) => (
            <option key={k} value={k}>
              {v}
            </option>
          ))}
        </select>
      )}
    </div>
  );
}

function ItemName({ name, base, rarity }: { name: string; base: string; rarity: string }) {
  const showBase = base && base !== name;
  return (
    <div>
      <div className={RARITY_CLASS[rarity] ?? ""}>{name}</div>
      {showBase && <div className="faint small">{base}</div>}
    </div>
  );
}

function SlotCard({ slot, onIgnore }: { slot: SlotReport; onIgnore: (key: string, reason: string | null) => void }) {
  const st = STATUS[slot.status];
  const [open, setOpen] = useState(slot.status !== "ok");
  const mods = slot.mods;
  return (
    <div className={`card slot-card ${slot.ignored ? "ignored" : ""}`}>
      <div className="slot-head">
        <span className="slot-name">{slot.slot}</span>
        <span className={`status ${slot.ignored ? "st-ignored" : st.cls}`}>{slot.ignored ? "Bỏ qua" : st.label}</span>
      </div>
      <div className="slot-compare">
        <div>
          <div className="faint small">Build</div>
          <ItemName name={slot.build.name} base={slot.build.baseType} rarity={slot.build.rarity} />
          {slot.build.links >= 4 && <div className="faint small">{slot.build.links}L ({slot.build.sockets})</div>}
        </div>
        <div>
          <div className="faint small">Bạn đang có</div>
          {slot.current ? (
            <>
              <ItemName name={slot.current.name} base={slot.current.baseType} rarity={slot.current.rarity} />
              {slot.build.links >= 4 && <div className="faint small">{slot.current.links}L</div>}
            </>
          ) : (
            <div className="muted">— trống —</div>
          )}
        </div>
      </div>
      {slot.notes.map((n) => (
        <div key={n} className="slot-note">
          {n}
        </div>
      ))}
      {slot.stats.length > 0 && (
        <div className="stat-chips">
          {slot.stats.map((s) => (
            <span key={s.stat} className={`chip ${s.current >= s.build * 0.9 ? "have" : "miss"}`} title={`Build ${s.build}, bạn ${s.current}`}>
              {s.stat} {Math.round(s.current)}/{Math.round(s.build)}
            </span>
          ))}
        </div>
      )}
      {mods.length > 0 && (
        <>
          <button className="btn small ghost" style={{ marginTop: 6, padding: "2px 6px" }} onClick={() => setOpen(!open)}>
            {open ? "▾" : "▸"} Mod của build ({mods.filter((m) => m.state === "ok").length}/{mods.length} có)
          </button>
          {open && (
            <ul className="mod-list">
              {mods.map((m, i) => (
                <li key={i} className={`mod-${m.state}`} title={m.current ? `Bạn có: ${m.current}` : undefined}>
                  <span className="mod-mark">{m.state === "ok" ? "✓" : m.state === "low" ? "↓" : "✗"}</span>
                  {m.text}
                  {m.state === "low" && m.current && <span className="faint"> (bạn: {m.current})</span>}
                </li>
              ))}
            </ul>
          )}
        </>
      )}
      {slot.status !== "ok" && <IgnoreControl itemKey={slot.key} ignored={slot.ignored} onIgnore={onIgnore} />}
    </div>
  );
}

const GEM_STATE: Record<string, string> = { ok: "Có", low: "Level thấp", elsewhere: "Ở chỗ khác", missing: "Thiếu" };

function GemRow({ group, onIgnore }: { group: GemGroupReport; onIgnore: (key: string, reason: string | null) => void }) {
  const st = STATUS[group.status];
  return (
    <div className={`gem-row ${group.ignored ? "ignored" : ""}`}>
      <div className="gem-row-head">
        <b>{group.label}</b>
        {group.slot && <span className="faint small">{group.slot}</span>}
        <span className={`status ${group.ignored ? "st-ignored" : st.cls}`}>{group.ignored ? "Bỏ qua" : st.label}</span>
        {!group.linked && <span className="warn small">chưa nối link chung</span>}
        <span className="spacer" />
        {group.status !== "ok" && <IgnoreControl itemKey={group.key} ignored={group.ignored} onIgnore={onIgnore} />}
      </div>
      <div className="chips">
        {group.gems.map((g) => (
          <span
            key={g.name}
            className={`chip gem-${g.state}`}
            title={`${GEM_STATE[g.state]}${g.currentLevel ? ` — lv${g.currentLevel}` : ""}${g.where ? ` ở ${g.where}` : ""}; build lv${g.buildLevel}/${g.buildQuality}%`}
          >
            {g.name}
            {g.currentLevel ? ` ${g.currentLevel}/${g.buildLevel}` : ` –/${g.buildLevel}`}
            {g.state === "elsewhere" && g.where ? ` (${g.where})` : ""}
          </span>
        ))}
      </div>
    </div>
  );
}
