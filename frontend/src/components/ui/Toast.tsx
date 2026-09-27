import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { cx } from "./cx.ts";
import { IconCheck, IconClose } from "./icons.tsx";
import { Portal } from "./Portal.tsx";
import { ToastContext, type ToastApi, type ToastKind } from "./toastContext.ts";

const DURATION_MS = 4000;

type ToastEntry = { id: number; message: string; kind: ToastKind };

/** Holds the toasts for the whole app; use `useToast()` anywhere below it. */
export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<ToastEntry[]>([]);
  const nextId = useRef(0);
  const dismiss = useCallback((id: number) => setToasts((all) => all.filter((toast) => toast.id !== id)), []);

  const api = useMemo<ToastApi>(() => {
    const show = (message: string, kind: ToastKind = "success") => {
      nextId.current += 1;
      const id = nextId.current;
      setToasts((all) => [...all.slice(-2), { id, message, kind }]);
    };
    return { show, success: (message) => show(message, "success"), error: (message) => show(message, "error") };
  }, []);

  return (
    <ToastContext.Provider value={api}>
      {children}
      <Portal>
        <div className="pointer-events-none fixed inset-x-4 bottom-[calc(76px+env(safe-area-inset-bottom))] z-[300] flex flex-col items-center gap-2 md:bottom-6">
          {toasts.map((toast) => <Toast key={toast.id} {...toast} onDismiss={dismiss} />)}
        </div>
      </Portal>
    </ToastContext.Provider>
  );
}

export function Toast({ id, message, kind, onDismiss }: ToastEntry & { onDismiss: (id: number) => void }) {
  useEffect(() => {
    const timer = window.setTimeout(() => onDismiss(id), DURATION_MS);
    return () => window.clearTimeout(timer);
  }, [id, onDismiss]);
  return (
    <div
      role={kind === "error" ? "alert" : "status"}
      className={cx(
        "animate-toast-in pointer-events-auto flex w-full max-w-[360px] items-center gap-2.5 rounded-field px-4 py-3 font-ui text-sm font-bold text-white shadow-menu",
        kind === "error" ? "bg-danger" : "bg-ink",
      )}
    >
      {kind === "error" ? <IconClose size={16} /> : <IconCheck size={16} />}
      <span className="min-w-0 flex-1">{message}</span>
    </div>
  );
}
