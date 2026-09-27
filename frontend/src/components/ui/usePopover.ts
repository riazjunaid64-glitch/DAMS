import { useEffect, useEffectEvent, useLayoutEffect, useRef, useState, type CSSProperties } from "react";
import { useLayer } from "./layers.ts";

const GAP = 6;
const VIEWPORT_PADDING = 8;

/**
 * Positions a floating panel (dropdown list, actions menu) against its trigger. The panel is
 * portalled and fixed, so it is never cut off by a popup or sheet it was opened from; it flips
 * above the trigger when there is no room below and stays inside the viewport horizontally.
 * Closes on an outside press or Esc.
 */
export function usePopover<A extends HTMLElement, P extends HTMLElement>({
  open,
  onClose,
  onEscape,
  matchWidth = false,
  preferredHeight = 320,
}: {
  open: boolean;
  onClose: () => void;
  /** Esc handler when it should differ from an outside press (e.g. to return focus). */
  onEscape?: () => void;
  /** The panel is at least as wide as the trigger (dropdowns). */
  matchWidth?: boolean;
  preferredHeight?: number;
}) {
  const anchorRef = useRef<A>(null);
  const panelRef = useRef<P>(null);
  const [style, setStyle] = useState<CSSProperties>({ position: "fixed", visibility: "hidden" });

  const place = useEffectEvent(() => {
    const anchor = anchorRef.current;
    const panel = panelRef.current;
    if (!anchor || !panel) return;
    const rect = anchor.getBoundingClientRect();
    const width = Math.max(panel.offsetWidth, matchWidth ? rect.width : 0);
    const below = window.innerHeight - rect.bottom - VIEWPORT_PADDING - GAP;
    const above = rect.top - VIEWPORT_PADDING - GAP;
    const wanted = Math.min(preferredHeight, panel.scrollHeight);
    const flip = below < wanted && above > below;
    const maxHeight = Math.max(120, Math.min(preferredHeight, flip ? above : below));
    const maxLeft = window.innerWidth - VIEWPORT_PADDING - width;
    // Menus hang from the trigger's left edge unless that would push them off the right side,
    // in which case they line up with the trigger's right edge instead.
    const left = rect.left + width <= window.innerWidth - VIEWPORT_PADDING
      ? rect.left
      : Math.max(VIEWPORT_PADDING, Math.min(maxLeft, rect.right - width));
    setStyle({
      position: "fixed",
      left,
      top: flip ? undefined : rect.bottom + GAP,
      bottom: flip ? window.innerHeight - rect.top + GAP : undefined,
      minWidth: matchWidth ? rect.width : undefined,
      maxWidth: window.innerWidth - VIEWPORT_PADDING * 2,
      maxHeight,
    });
  });

  useLayoutEffect(() => {
    if (open) place();
  }, [open]);

  const close = useEffectEvent(onClose);
  useEffect(() => {
    if (!open) return;
    const onPointerDown = (event: PointerEvent) => {
      const target = event.target as Node;
      if (anchorRef.current?.contains(target) || panelRef.current?.contains(target)) return;
      close();
    };
    const reposition = () => place();
    document.addEventListener("pointerdown", onPointerDown);
    window.addEventListener("resize", reposition);
    window.addEventListener("scroll", reposition, true);
    return () => {
      document.removeEventListener("pointerdown", onPointerDown);
      window.removeEventListener("resize", reposition);
      window.removeEventListener("scroll", reposition, true);
    };
  }, [open]);

  useLayer(open, onEscape ?? onClose);

  return { anchorRef, panelRef, style };
}
