import { useEffect, useState } from "react";
import { api, type Character, type Settings, type StashTab } from "../api";
import { useToast } from "../components/Toast";

export function SettingsPage({ onSaved }: { onSaved: (s: Settings) => void }) {
  const [s, setS] = useState<Settings | null>(null);
  const [sessionId, setSessionId] = useState("");
  const [leagues, setLeagues] = useState<string[]>([]);
  const [tabs, setTabs] = useState<StashTab[] | null>(null);
  const [chars, setChars] = useState<Character[] | null>(null);
  const [loading, setLoading] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const toast = useToast();

  useEffect(() => {
    api.settings().then(setS);
    api.leagues().then(setLeagues).catch(() => setLeagues([]));
  }, []);

  if (!s) return <div className="card">Đang tải…</div>;

  const set = <K extends keyof Settings>(key: K, value: Settings[K]) => {
    setS({ ...s, [key]: value });
    setSaved(false);
  };

  const save = async () => {
    try {
      const { hasSessionId: _h, dataDir: _d, ...rest } = s;
      const next = await api.saveSettings({ ...rest, ...(sessionId ? { sessionId } : {}) });
      setS(next);
      setSessionId("");
      setSaved(true);
      onSaved(next);
    } catch (e) {
      toast((e as Error).message, "error");
    }
  };

  const clearSession = async () => {
    const next = await api.saveSettings({ sessionId: "" });
    setS(next);
    onSaved(next);
  };

  const loadTabs = async () => {
    setLoading("tabs");
    try {
      await save();
      setTabs(await api.stashTabs());
    } catch (e) {
      toast((e as Error).message, "error");
    } finally {
      setLoading(null);
    }
  };

  const loadChars = async () => {
    setLoading("chars");
    try {
      await save();
      setChars((await api.characters()).filter((c) => c.league?.toLowerCase() === s.league.toLowerCase()));
    } catch (e) {
      toast((e as Error).message, "error");
    } finally {
      setLoading(null);
    }
  };

  const toggle = (list: string[], value: string, on: boolean) =>
    on ? Array.from(new Set([...list, value])) : list.filter((x) => x !== value);

  const leagueOptions = leagues.includes(s.league) || !s.league ? leagues : [s.league, ...leagues];

  return (
    <div className="stack" style={{ maxWidth: 980 }}>
      <div className="card">
        <h2>Tài khoản Path of Exile</h2>
        <div className="form-grid">
          <label htmlFor="acc">Tên tài khoản</label>
          <input id="acc" type="text" placeholder="TenTaiKhoan#1234" value={s.accountName} onChange={(e) => set("accountName", e.target.value)} />
          <div className="hint">Ghi đủ cả phần #1234 như trên trang pathofexile.com.</div>

          <label htmlFor="sess">POESESSID</label>
          <div className="row">
            <input
              id="sess"
              type="password"
              autoComplete="off"
              placeholder={s.hasSessionId ? "Đã lưu (nhập để thay)" : "32 ký tự hex"}
              value={sessionId}
              onChange={(e) => {
                setSessionId(e.target.value);
                setSaved(false);
              }}
              style={{ flex: 1, maxWidth: 360 }}
            />
            {s.hasSessionId && (
              <button className="btn small danger" onClick={clearSession}>
                Xoá
              </button>
            )}
          </div>
          <div className="hint">
            Lấy từ cookie POESESSID khi đăng nhập pathofexile.com (F12 → Application → Cookies). Chỉ lưu trên máy này
            {navigator.userAgent.includes("Windows") ? ", được mã hoá bằng Windows DPAPI" : ""} và chỉ gửi tới pathofexile.com.
          </div>

          <label htmlFor="league">League</label>
          <select id="league" value={s.league} onChange={(e) => set("league", e.target.value)}>
            {leagueOptions.map((l) => (
              <option key={l}>{l}</option>
            ))}
          </select>

          <label htmlFor="realm">Realm</label>
          <select id="realm" value={s.realm} onChange={(e) => set("realm", e.target.value)}>
            <option value="pc">PC</option>
            <option value="xbox">Xbox</option>
            <option value="sony">PlayStation</option>
          </select>
        </div>
      </div>

      <div className="card">
        <h2>Snapshot</h2>
        <div className="form-grid">
          <label>Tự động chụp</label>
          <div className="row">
            <label className="check">
              <input type="checkbox" checked={s.autoSnapshot} onChange={(e) => set("autoSnapshot", e.target.checked)} />
              mỗi
            </label>
            <input type="number" min={5} value={s.snapshotIntervalMinutes} onChange={(e) => set("snapshotIntervalMinutes", Number(e.target.value) || 15)} />
            phút
          </div>

          <label>Tab stash</label>
          <div>
            <div className="row">
              <span className="muted">{s.selectedTabIds.length === 0 ? "Tất cả tab" : `${s.selectedTabIds.length} tab đã chọn`}</span>
              <button className="btn small" onClick={loadTabs} disabled={loading === "tabs"}>
                {loading === "tabs" ? "Đang tải…" : "Chọn tab…"}
              </button>
              {s.selectedTabIds.length > 0 && (
                <button className="btn small ghost" onClick={() => set("selectedTabIds", [])}>
                  Dùng tất cả
                </button>
              )}
            </div>
            {tabs && (
              <div className="checklist" style={{ marginTop: 8 }}>
                {tabs.map((t) => (
                  <label key={t.id} className="check small">
                    <input
                      type="checkbox"
                      checked={s.selectedTabIds.length === 0 || s.selectedTabIds.includes(t.id)}
                      onChange={(e) => {
                        const base = s.selectedTabIds.length === 0 ? tabs.map((x) => x.id) : s.selectedTabIds;
                        set("selectedTabIds", toggle(base, t.id, e.target.checked));
                      }}
                    />
                    <span className="source-dot" style={{ background: t.colour ?? "#555" }} />
                    {t.name} <span className="faint">({t.type.replace("Stash", "")})</span>
                  </label>
                ))}
              </div>
            )}
          </div>

          <label>Nhân vật</label>
          <div>
            <div className="row">
              <label className="check">
                <input type="checkbox" checked={s.includeCharacters} onChange={(e) => set("includeCharacters", e.target.checked)} />
                Tính đồ trên người và trong túi
              </label>
              <span className="muted">{s.selectedCharacters.length === 0 ? "(mọi nhân vật trong league)" : `(${s.selectedCharacters.length} nhân vật)`}</span>
              <button className="btn small" onClick={loadChars} disabled={loading === "chars"}>
                {loading === "chars" ? "Đang tải…" : "Chọn nhân vật…"}
              </button>
            </div>
            {chars && (
              <div className="checklist" style={{ marginTop: 8 }}>
                {chars.length === 0 && <span className="muted small">Không có nhân vật nào trong league này.</span>}
                {chars.map((c) => (
                  <label key={c.name} className="check small">
                    <input
                      type="checkbox"
                      checked={s.selectedCharacters.length === 0 || s.selectedCharacters.includes(c.name)}
                      onChange={(e) => {
                        const base = s.selectedCharacters.length === 0 ? chars.map((x) => x.name) : s.selectedCharacters;
                        set("selectedCharacters", toggle(base, c.name, e.target.checked));
                      }}
                    />
                    {c.name} <span className="faint">({c.class} {c.level})</span>
                  </label>
                ))}
              </div>
            )}
          </div>
        </div>
      </div>

      <div className="card">
        <h2>Giá (poe.ninja)</h2>
        <div className="form-grid">
          <label>Làm mới giá mỗi</label>
          <div className="row">
            <input type="number" min={5} value={s.priceRefreshMinutes} onChange={(e) => set("priceRefreshMinutes", Number(e.target.value) || 30)} />
            phút
            <button
              className="btn small"
              onClick={async () => {
                try {
                  const r = await api.refreshPrices();
                  toast(`Đã tải ${r.lines.toLocaleString("en-US")} giá, 1 divine = ${Math.round(r.divineChaos)} c`);
                } catch (e) {
                  toast((e as Error).message, "error");
                }
              }}
            >
              Tải giá ngay
            </button>
          </div>
          <label>Bỏ qua giá có ít hơn</label>
          <div className="row">
            <input type="number" min={0} value={s.minListingCount} onChange={(e) => set("minListingCount", Number(e.target.value) || 0)} />
            listing (unique, gem, base…)
          </div>
        </div>
      </div>

      <div className="card">
        <h2>Leveling &amp; game</h2>
        <div className="form-grid">
          <label htmlFor="log">Client.txt</label>
          <input
            id="log"
            type="text"
            placeholder="Để trống để tự tìm, hoặc C:\...\Path of Exile\logs\Client.txt"
            value={s.clientLogPath}
            onChange={(e) => set("clientLogPath", e.target.value)}
          />
          <div className="hint">Dùng để biết bạn đang ở khu vực nào và tự chuyển bước trong Leveling.</div>
          <label htmlFor="game">Thư mục game</label>
          <input id="game" type="text" placeholder="C:\Program Files (x86)\Grinding Gear Games\Path of Exile" value={s.gamePath} onChange={(e) => set("gamePath", e.target.value)} />
          <div className="hint">Dành cho Patch game (sắp có).</div>
        </div>
      </div>

      <div className="row">
        <button className="btn primary" onClick={save}>
          Lưu cài đặt
        </button>
        {saved && <span className="up">Đã lưu.</span>}
        <span className="spacer" />
        <span className="faint small">Dữ liệu lưu tại: {s.dataDir}</span>
      </div>
    </div>
  );
}
