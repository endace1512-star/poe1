import { useEffect } from "react";

type Handler = (payload: any) => void;
const handlers = new Map<string, Set<Handler>>();
let source: EventSource | null = null;

function ensureConnected() {
  if (source) return;
  source = new EventSource("/api/events");
  source.onmessage = (e) => {
    try {
      const { type, payload } = JSON.parse(e.data);
      handlers.get(type)?.forEach((h) => h(payload));
      handlers.get("*")?.forEach((h) => h({ type, payload }));
    } catch {
      /* ignore malformed events */
    }
  };
}

/** Subscribe to a server event ("snapshot", "snapshot-progress", "area", "level", …). */
export function useServerEvent(type: string, handler: Handler) {
  useEffect(() => {
    ensureConnected();
    let set = handlers.get(type);
    if (!set) handlers.set(type, (set = new Set()));
    set.add(handler);
    return () => {
      set!.delete(handler);
    };
  }, [type, handler]);
}
