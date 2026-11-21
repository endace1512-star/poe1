import { createContext, useCallback, useContext, useRef, useState, type ReactNode } from "react";

type ShowToast = (text: string, kind?: "info" | "error") => void;
const ToastContext = createContext<ShowToast>(() => {});

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toast, setToast] = useState<{ text: string; kind: "info" | "error" } | null>(null);
  const timer = useRef<number | undefined>(undefined);
  const show = useCallback<ShowToast>((text, kind = "info") => {
    setToast({ text, kind });
    window.clearTimeout(timer.current);
    timer.current = window.setTimeout(() => setToast(null), kind === "error" ? 7000 : 3500);
  }, []);
  return (
    <ToastContext.Provider value={show}>
      {children}
      {toast && (
        <div className={`toast ${toast.kind === "error" ? "error" : ""}`} role="status" onClick={() => setToast(null)}>
          {toast.text}
        </div>
      )}
    </ToastContext.Provider>
  );
}

export const useToast = () => useContext(ToastContext);
