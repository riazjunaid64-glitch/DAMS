import { useRef, type ReactNode } from "react";
import { cx } from "./cx.ts";
import { useFocusTrap, useLayer } from "./layers.ts";
import { Portal } from "./Portal.tsx";

export type OverlayPlacement = "center" | "top" | "bottom" | "fullscreen";

type OverlayProps = {
  open: boolean;
  onClose: () => void;
  children: ReactNode;
  /** Where the panel sits: centred, pinned near the top (tall forms), a bottom sheet, or full screen. */
  placement?: OverlayPlacement;
  /** Backdrop taps close the overlay unless this is false (e.g. while saving). */
  closeOnBackdrop?: boolean;
  /** Traps Tab inside and moves focus in on open. Off for callers that manage focus themselves. */
  trapFocus?: boolean;
};

const placementClass: Record<OverlayPlacement, string> = {
  center: "items-center justify-center p-4",
  top: "items-start justify-center overflow-y-auto p-4 sm:py-10",
  bottom: "items-end justify-center",
  fullscreen: "items-stretch justify-stretch",
};

/**
 * The shared base under every blocking popup: portal, dark backdrop, Esc to close, page scroll
 * lock and focus kept inside. Modal, BottomSheet and the photo viewer are all built on it.
 */
export function Overlay({ open, onClose, children, placement = "center", closeOnBackdrop = true, trapFocus = true }: OverlayProps) {
  const rootRef = useRef<HTMLDivElement>(null);
  useLayer(open, onClose, true);
  useFocusTrap(open && trapFocus, () => rootRef.current);
  if (!open) return null;
  return (
    <Portal>
      <div ref={rootRef} className={cx("fixed inset-0 z-[100] flex", placementClass[placement])}>
        <div
          aria-hidden="true"
          className="animate-fade-in absolute inset-0 bg-backdrop"
          onClick={closeOnBackdrop ? onClose : undefined}
        />
        {children}
      </div>
    </Portal>
  );
}
