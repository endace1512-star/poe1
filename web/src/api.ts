// Typed wrappers for the local server's /api endpoints.

export interface Snapshot {
  id: number;
  ts: string;
  league: string;
  totalChaos: number;
  divineChaos: number;
  itemCount: number;
  itemsPruned: boolean;
}

export interface Source {
  sourceKey: string;
  name: string;
  kind: "tab" | "character";
  idx: number;
  colour: string | null;
  itemCount: number;
  valueChaos: number;
}

export interface Item {
  itemKey: string;
  name: string;
  category: string;
  icon: string | null;
  frameType: number;
  quantity: number;
  unitChaos: number;
  totalChaos: number;
  sourceKey: string | null;
  sourceName: string | null;
}

export interface Overview {
  league: string;
  snapshot: Snapshot | null;
  perHourChaos?: number | null;
  sources?: Source[];
  topItems?: Item[];
}

export interface HistoryPoint {
  ts: string;
  totalChaos: number;
  divineChaos: number;
}

export interface Status {
  running: boolean;
  progress: string | null;
  lastError: string | null;
  lastSnapshotAt: string | null;
  nextSnapshotAt: string | null;
  pricesUpdatedAt: string | null;
  priceError: string | null;
  divineChaos: number;
}

export interface PriceChange {
  itemKey: string;
  name: string;
  icon: string | null;
  category: string;
  sources: string;
  quantity: number;
  oldUnit: number;
  newUnit: number;
  changePercent: number;
  heldChange: number;
}

export interface PriceChangeReport {
  currentTs: string | null;
  referenceTs: string | null;
  rising: number;
  falling: number;
  netHeldChange: number;
  items: PriceChange[];
}

export interface FarmSession {
  id: number;
  name: string;
  league: string;
  startTs: string;
  endTs: string | null;
  startSnapshotId: number;
  endSnapshotId: number | null;
  startChaos: number;
  endChaos: number | null;
  divineChaos: number;
  notes: string | null;
}

export interface ItemDelta {
  itemKey: string;
  name: string;
  icon: string | null;
  category: string;
  startQty: number;
  endQty: number;
  deltaQty: number;
  unitChaos: number;
  deltaValue: number;
}

export interface FarmReport {
  session: FarmSession;
  comparedTs: string;
  durationHours: number;
  netWorthChange: number;
  lootValue: number;
  perHour: number;
  gained: ItemDelta[];
  spent: ItemDelta[];
}

export interface Settings {
  accountName: string;
  realm: string;
  league: string;
  hasSessionId: boolean;
  autoSnapshot: boolean;
  snapshotIntervalMinutes: number;
  selectedTabIds: string[];
  includeCharacters: boolean;
  selectedCharacters: string[];
  priceRefreshMinutes: number;
  minListingCount: number;
  alertMinChangePercent: number;
  alertMinHeldChaos: number;
  alertCompareHours: number;
  alertDesktopNotify: boolean;
  clientLogPath: string;
  gamePath: string;
  dataDir: string;
}

export type SettingsUpdate = Partial<Omit<Settings, "hasSessionId" | "dataDir">> & { sessionId?: string };

export interface StashTab {
  id: string;
  name: string;
  index: number;
  type: string;
  colour: string | null;
}

export interface Character {
  name: string;
  league: string | null;
  class: string | null;
  level: number;
}

export interface AreaEvent {
  areaId: string;
  areaLevel: number;
  areaName: string | null;
  ts: string;
}

export interface LevelEvent {
  character: string;
  class: string | null;
  level: number;
  ts: string;
}

export interface LevelingState {
  watching: boolean;
  logPath: string | null;
  error: string | null;
  area: AreaEvent | null;
  character: LevelEvent | null;
}

export class ApiError extends Error {}

async function request<T>(method: string, url: string, body?: unknown): Promise<T> {
  const res = await fetch(url, {
    method,
    headers: body === undefined ? undefined : { "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (res.status === 204) return undefined as T;
  const text = await res.text();
  const data = text ? JSON.parse(text) : undefined;
  if (!res.ok) throw new ApiError(data?.error ?? `HTTP ${res.status}`);
  return data as T;
}

const qs = (params: Record<string, string | number | undefined | null>) => {
  const p = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) if (v !== undefined && v !== null && v !== "") p.set(k, String(v));
  const s = p.toString();
  return s ? `?${s}` : "";
};

export const api = {
  status: () => request<Status>("GET", "/api/status"),
  overview: () => request<Overview>("GET", "/api/overview"),
  history: (hours?: number) => request<HistoryPoint[]>("GET", `/api/history${qs({ hours })}`),
  sources: () => request<Source[]>("GET", "/api/sources"),
  items: (p: { source?: string; search?: string; category?: string; minValue?: number }) =>
    request<Item[]>("GET", `/api/items${qs(p)}`),
  alerts: (p: { pct?: number; minHeld?: number; hours?: number }) =>
    request<PriceChangeReport>("GET", `/api/alerts${qs(p)}`),
  takeSnapshot: () => request<{ started: boolean; id?: number; message?: string }>("POST", "/api/snapshots"),
  refreshPrices: () => request<{ lines: number; divineChaos: number; fetchedAt: string }>("POST", "/api/prices/refresh"),

  settings: () => request<Settings>("GET", "/api/settings"),
  saveSettings: (u: SettingsUpdate) => request<Settings>("PUT", "/api/settings", u),
  leagues: () => request<string[]>("GET", "/api/leagues"),
  stashTabs: () => request<StashTab[]>("GET", "/api/stash/tabs"),
  characters: () => request<Character[]>("GET", "/api/characters"),

  farmList: () => request<FarmSession[]>("GET", "/api/farm"),
  farmReport: (id: number) => request<FarmReport>("GET", `/api/farm/${id}`),
  farmStart: (name: string) => request<{ id: number }>("POST", "/api/farm/start", { name }),
  farmStop: (id: number) => request<FarmReport>("POST", `/api/farm/${id}/stop`),
  farmUpdate: (id: number, name: string, notes: string | null) => request<void>("PUT", `/api/farm/${id}`, { name, notes }),
  farmDelete: (id: number) => request<void>("DELETE", `/api/farm/${id}`),

  levelingState: () => request<LevelingState>("GET", "/api/leveling/state"),
  simulateArea: (areaId: string, level?: number) => request<void>("POST", "/api/leveling/simulate", { areaId, level }),
};
