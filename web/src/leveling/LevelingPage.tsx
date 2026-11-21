import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { api, type AreaEvent, type LevelEvent, type LevelingState } from "../api";
import { useServerEvent } from "../events";
import { useToast } from "../components/Toast";
import { Data, type RouteData } from "./engine";
import { BANDITS, CLASSES, buildRoute, defaultBuild, nextEdgeFor } from "./route";
import { importPob, type GemGroup } from "./pob";
import { AreaName, FragmentView, GemStepView } from "./Fragment";
import { LayoutViewer } from "./LayoutViewer";

interface Saved {
  build: RouteData.BuildData;
  requiredGems: RouteData.RequiredGem[];
  groups: GemGroup[];
  pobLabel: string | null;
  activeEdge: number;
  collapsed: string[];
  gemsOnly: boolean;
}

const STORAGE_KEY = "poe1.leveling.v1";

function load(): Saved {
  const fallback: Saved = { build: defaultBuild(), requiredGems: [], groups: [], pobLabel: null, activeEdge: 0, collapsed: [], gemsOnly: false };
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    return raw ? { ...fallback, ...JSON.parse(raw) } : fallback;
  } catch {
    return fallback;
  }
}

function usePersisted() {
  const [state, setState] = useState<Saved>(load);
  useEffect(() => {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(state));
    } catch {
      /* storage unavailable: keep in memory */
    }
  }, [state]);
  return [state, setState] as const;
}

export function LevelingPage() {
  const [saved, setSaved] = usePersisted();
  const [log, setLog] = useState<LevelingState | null>(null);
  const [liveArea, setLiveArea] = useState<AreaEvent | null>(null);
  const [character, setCharacter] = useState<LevelEvent | null>(null);
  const [pobOpen, setPobOpen] = useState(false);
  const [viewArea, setViewArea] = useState<string | null>(null);
  const routeBox = useRef<HTMLDivElement>(null);
  const toast = useToast();

  const route = useMemo(
    () => buildRoute(saved.build, saved.requiredGems, saved.gemsOnly),
    [saved.build, saved.requiredGems, saved.gemsOnly],
  );
  const active = Math.min(saved.activeEdge, route.edges.length - 1);

  useEffect(() => {
    api.levelingState().then((s) => {
      setLog(s);
      setLiveArea(s.area);
      setCharacter(s.character);
    }).catch(() => {});
    const t = window.setInterval(() => api.levelingState().then(setLog).catch(() => {}), 10000);
    return () => window.clearInterval(t);
  }, []);

  useServerEvent(
    "area",
    useCallback(
      (a: AreaEvent) => {
        setLiveArea(a);
        setViewArea(null);
        setSaved((s) => {
          const next = nextEdgeFor(route.edges, Math.min(s.activeEdge, route.edges.length - 1), a.areaId);
          return next === null ? s : { ...s, activeEdge: next };
        });
      },
      [route.edges, setSaved],
    ),
  );
  useServerEvent("level", useCallback((l: LevelEvent) => setCharacter(l), []));

  // Keep the current step in view.
  useEffect(() => {
    const el = routeBox.current?.querySelector<HTMLElement>(".lv-step.current");
    if (el && routeBox.current) {
      const box = routeBox.current;
      const top = el.offsetTop - box.offsetTop - 60;
      box.scrollTo({ top, behavior: "smooth" });
    }
  }, [active]);

  const setActive = (edge: number) => setSaved((s) => ({ ...s, activeEdge: Math.max(0, Math.min(edge, route.edges.length - 1)) }));
  const setBuild = (patch: Partial<RouteData.BuildData>) =>
    setSaved((s) => ({ ...s, build: { ...s.build, ...patch } }));

  const toggleSection = (name: string) =>
    setSaved((s) => ({
      ...s,
      collapsed: s.collapsed.includes(name) ? s.collapsed.filter((x) => x !== name) : [...s.collapsed, name],
    }));

  const currentAreaId = route.edges[active];
  const shownArea = viewArea ?? (liveArea && Data.Areas[liveArea.areaId] ? liveArea.areaId : currentAreaId);
  const nextAreaId = route.edges[active + 1];
  const area = Data.Areas[shownArea];
  const currentSection = route.sections.find((s) => s.steps.some((st) => st.edge === active))?.name;

  const doImport = (code: string) => {
    try {
      const pob = importPob(code);
      setSaved((s) => ({
        ...s,
        build: pob.build,
        requiredGems: pob.requiredGems,
        groups: pob.groups,
        pobLabel: `${pob.ascendancy || pob.build.characterClass}${pob.level ? ` lv${pob.level}` : ""}`,
        activeEdge: 0,
      }));
      setPobOpen(false);
      toast(`Đã nhập build: ${pob.requiredGems.length} gem`);
    } catch (e) {
      toast(`Không đọc được mã PoB: ${(e as Error).message}`, "error");
    }
  };

  return (
    <div className="stack">
      <div className="card">
        <div className="lv-toolbar">
          <label className="row small" style={{ gap: 6 }}>
            Class
            <select value={saved.build.characterClass} onChange={(e) => setBuild({ characterClass: e.target.value })}>
              {CLASSES.map((c) => (
                <option key={c}>{c}</option>
              ))}
            </select>
          </label>
          <label className="row small" style={{ gap: 6 }}>
            Bandit
            <select value={saved.build.bandit} onChange={(e) => setBuild({ bandit: e.target.value as RouteData.BuildData["bandit"] })}>
              {BANDITS.map((b) => (
                <option key={b} value={b}>
                  {b === "None" ? "Giết cả 3" : b}
                </option>
              ))}
            </select>
          </label>
          <label className="check small">
            <input type="checkbox" checked={saved.build.leagueStart} onChange={(e) => setBuild({ leagueStart: e.target.checked })} />
            League start
          </label>
          <label className="check small">
            <input type="checkbox" checked={saved.build.library} onChange={(e) => setBuild({ library: e.target.checked })} />
            Library (Act 3)
          </label>
          {saved.requiredGems.length > 0 && (
            <label className="check small">
              <input type="checkbox" checked={saved.gemsOnly} onChange={(e) => setSaved((s) => ({ ...s, gemsOnly: e.target.checked }))} />
              Chỉ bước có gem
            </label>
          )}
          <button className="btn small" onClick={() => setPobOpen(true)}>
            {saved.pobLabel ? `Build: ${saved.pobLabel}` : "Nhập build PoB…"}
          </button>
          {saved.pobLabel && (
            <button className="btn small ghost" onClick={() => setSaved((s) => ({ ...s, requiredGems: [], groups: [], pobLabel: null }))}>
              Bỏ build
            </button>
          )}
          <span className="spacer" />
          <span className={`status-chip ${log?.watching ? "on" : "off"}`} title={log?.logPath ?? log?.error ?? ""}>
            <span className="dot" />
            {log?.watching ? "Đang đọc Client.txt" : "Chưa đọc được Client.txt"}
          </span>
          {character && (
            <span className="status-chip">
              {character.character} · {character.class} lv{character.level}
            </span>
          )}
          <button className="btn small" onClick={() => setActive(active - 1)} disabled={active === 0}>
            ◀ Lùi
          </button>
          <button className="btn small" onClick={() => setActive(active + 1)} disabled={active >= route.edges.length - 1}>
            Tiến ▶
          </button>
          <button
            className="btn small ghost"
            onClick={() => {
              if (window.confirm("Bắt đầu lại từ đầu Act 1?")) setActive(0);
            }}
          >
            Làm lại
          </button>
        </div>
        {!log?.watching && log?.error && (
          <p className="faint small" style={{ margin: "8px 0 0" }}>
            {log.error} Không có Client.txt vẫn dùng được: bấm vào một bước để đánh dấu đang ở đó.
          </p>
        )}
      </div>

      <div className="lv-layout">
        <div className="card">
          <div className="lv-route" ref={routeBox}>
            {route.sections.map((section) => {
              const collapsed = saved.collapsed.includes(section.name) && section.name !== currentSection;
              return (
                <div className="lv-section" key={section.name}>
                  <div className="lv-section-title" onClick={() => toggleSection(section.name)}>
                    <span>{collapsed ? "▸" : "▾"}</span>
                    {section.name}
                    <span className="count">{section.steps.length} bước</span>
                  </div>
                  {!collapsed &&
                    section.steps.map(({ step, edge, key }) => {
                      const cls = edge < active ? "done" : edge === active ? "current" : "";
                      return (
                        <div key={key} className={`lv-step ${cls}`} onClick={() => setActive(edge)} title="Bấm để đánh dấu đang ở bước này">
                          <div className="lv-step-body">
                            {step.type === "gem_step" ? (
                              <GemStepView step={step} />
                            ) : (
                              <>
                                <div className="lv-line">
                                  {step.parts.map((p, i) => (
                                    <FragmentView key={i} part={p} />
                                  ))}
                                </div>
                                {step.subSteps.map((sub, i) => (
                                  <div className="lv-sub" key={i}>
                                    {sub.parts.map((p, j) => (
                                      <FragmentView key={j} part={p} />
                                    ))}
                                  </div>
                                ))}
                              </>
                            )}
                          </div>
                        </div>
                      );
                    })}
                </div>
              );
            })}
          </div>
        </div>

        <div className="stack zone-panel">
          <div className="card">
            <div className="row" style={{ alignItems: "baseline" }}>
              <h3 className="zone-name">{area?.name ?? shownArea}</h3>
              {viewArea && (
                <button className="btn small ghost" onClick={() => setViewArea(null)}>
                  ← khu vực hiện tại
                </button>
              )}
            </div>
            <div className="zone-meta">
              {area && <span>Act {area.act}</span>}
              {area && <span>Area level {area.level}</span>}
              {area?.has_waypoint && <span>Có waypoint</span>}
              {liveArea?.areaId === shownArea && <span className="up">● đang ở đây</span>}
            </div>
            {nextAreaId && (
              <div className="next-zone">
                <span className="muted">Tiếp theo: </span>
                <AreaName id={nextAreaId} />
                {nextAreaId !== shownArea && Data.Areas[nextAreaId] && !Data.Areas[nextAreaId].is_town_area && (
                  <button className="btn small ghost" style={{ marginLeft: 8 }} onClick={() => setViewArea(nextAreaId)}>
                    xem layout
                  </button>
                )}
              </div>
            )}
            <LayoutViewer areaId={shownArea} />
            {area && area.connection_ids.length > 0 && (
              <div className="small muted" style={{ marginTop: 10 }}>
                Nối với:{" "}
                {area.connection_ids.map((id, i) => (
                  <span key={id}>
                    {i > 0 && ", "}
                    <a href="#/leveling" onClick={(e) => { e.preventDefault(); setViewArea(id); }}>
                      {Data.Areas[id]?.name ?? id}
                    </a>
                  </span>
                ))}
              </div>
            )}
            <p className="faint small" style={{ marginBottom: 0 }}>
              Ảnh layout: Exile-UI (Lailloken, MIT). Lộ trình: exile-leveling (HeartofPhos, MIT).
            </p>
          </div>

          {saved.groups.length > 0 && (
            <div className="card">
              <h2>Gem của build</h2>
              {saved.groups.map((g, i) => (
                <div key={i} className="small" style={{ marginBottom: 6 }}>
                  <span className="muted">{g.title}: </span>
                  {g.gems.join(" · ")}
                </div>
              ))}
            </div>
          )}
        </div>
      </div>

      {pobOpen && <PobDialog onClose={() => setPobOpen(false)} onImport={doImport} />}
    </div>
  );
}

function PobDialog({ onClose, onImport }: { onClose: () => void; onImport: (code: string) => void }) {
  const [code, setCode] = useState("");
  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div className="modal" onClick={(e) => e.stopPropagation()}>
        <h3>Nhập build từ Path of Building</h3>
        <p className="muted small">
          Trong PoB: Import/Export Build → Generate → Copy, rồi dán mã vào đây. Lộ trình sẽ hiện gem cần lấy từ quest và gem cần mua ở NPC
          cho class của build.
        </p>
        <textarea rows={8} value={code} onChange={(e) => setCode(e.target.value)} placeholder="eNrtfWt34ziS..." autoFocus />
        <div className="row" style={{ marginTop: 12, justifyContent: "flex-end" }}>
          <button className="btn ghost" onClick={onClose}>
            Huỷ
          </button>
          <button className="btn primary" onClick={() => onImport(code)} disabled={!code.trim()}>
            Nhập
          </button>
        </div>
      </div>
    </div>
  );
}
