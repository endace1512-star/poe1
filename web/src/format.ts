/** "30.61 div" when worth at least one divine, otherwise "183 c" (like poe.ninja / Wealthy Exile). */
export function formatValue(chaos: number, divine: number, unit: "auto" | "chaos" | "divine" = "auto"): string {
  if (!Number.isFinite(chaos)) return "–";
  const useDivine = unit === "divine" || (unit === "auto" && divine > 0 && Math.abs(chaos) >= divine);
  if (useDivine && divine > 0) return `${(chaos / divine).toFixed(2)} div`;
  // Fractions matter for cheap currency (an Alteration is ~0.2 c).
  if (chaos !== 0 && Math.abs(chaos) < 1) return `${Number(chaos.toFixed(2))} c`;
  return `${Math.round(chaos).toLocaleString("en-US")} c`;
}

export function formatSigned(chaos: number, divine: number): string {
  const s = formatValue(Math.abs(chaos), divine);
  return `${chaos > 0 ? "+" : chaos < 0 ? "−" : ""}${s}`;
}

export function formatQty(q: number): string {
  return Number.isInteger(q) ? q.toLocaleString("en-US") : q.toFixed(1);
}

const pad = (n: number) => String(n).padStart(2, "0");

/** "12:16:49 20/11/2026" in local time. */
export function formatTime(iso: string | null | undefined, withSeconds = true): string {
  if (!iso) return "–";
  const d = new Date(iso);
  const time = withSeconds ? `${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}` : `${pad(d.getHours())}:${pad(d.getMinutes())}`;
  return `${time} ${pad(d.getDate())}/${pad(d.getMonth() + 1)}/${d.getFullYear()}`;
}

export function formatAgo(iso: string | null | undefined): string {
  if (!iso) return "–";
  const sec = (Date.now() - new Date(iso).getTime()) / 1000;
  if (sec < 60) return "vừa xong";
  if (sec < 3600) return `${Math.round(sec / 60)} phút trước`;
  if (sec < 86400) return `${(sec / 3600).toFixed(1)} giờ trước`;
  return `${(sec / 86400).toFixed(1)} ngày trước`;
}

export function formatDuration(hours: number): string {
  const totalMin = Math.round(hours * 60);
  const h = Math.floor(totalMin / 60);
  const m = totalMin % 60;
  return h > 0 ? `${h} giờ ${pad(m)} phút` : `${m} phút`;
}

export const CATEGORY_LABELS: Record<string, string> = {
  currency: "Currency",
  catalyst: "Catalyst",
  fragment: "Fragment",
  card: "Div card",
  essence: "Essence",
  delve: "Fossil/Resonator",
  scarab: "Thread/Scarab",
  map: "Map",
  beast: "Beast",
  unique: "Unique",
  gem: "Gem",
  base: "Base",
  cluster: "Cluster jewel",
  other: "Khác",
};
