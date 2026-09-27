import { useEffect, useEffectEvent, useLayoutEffect } from "react";

/*
 * Popups, sheets and menus stack: a dropdown opened inside a modal sits above it. Esc must close
 * only the top one, so every open layer registers here and one document listener asks the top
 * layer to close. The same stack locks page scrolling while any blocking layer is open.
 */

type Layer = { close: () => void; blocking: boolean };

const stack: Layer[] = [];
let lockedOverflow: string | null = null;

function onKeyDown(event: KeyboardEvent) {
  if (event.key !== "Escape" || stack.length === 0) return;
  event.preventDefault();
  stack[stack.length - 1]!.close();
}

function syncScrollLock() {
  const blocking = stack.some((layer) => layer.blocking);
  if (blocking && lockedOverflow === null) {
    lockedOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
  } else if (!blocking && lockedOverflow !== null) {
    document.body.style.overflow = lockedOverflow;
    lockedOverflow = null;
  }
}

/**
 * Registers an open layer. `blocking` layers (modals, sheets) also lock page scroll; menus do not.
 */
export function useLayer(open: boolean, onClose: () => void, blocking = false) {
  const close = useEffectEvent(onClose);
  useEffect(() => {
    if (!open) return;
    const layer: Layer = { close: () => close(), blocking };
    stack.push(layer);
    if (stack.length === 1) document.addEventListener("keydown", onKeyDown);
    syncScrollLock();
    return () => {
      const index = stack.indexOf(layer);
      if (index >= 0) stack.splice(index, 1);
      if (stack.length === 0) document.removeEventListener("keydown", onKeyDown);
      syncScrollLock();
    };
  }, [open, blocking]);
}

const FOCUSABLE =
  'a[href], button:not([disabled]), input:not([disabled]):not([type="hidden"]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

export function focusableIn(root: HTMLElement): HTMLElement[] {
  return Array.from(root.querySelectorAll<HTMLElement>(FOCUSABLE)).filter((el) => el.getClientRects().length > 0);
}

/**
 * Moves focus into a dialog when it opens, keeps Tab cycling inside it, and hands focus back to
 * whatever had it once the dialog closes.
 */
export function useFocusTrap(open: boolean, getRoot: () => HTMLElement | null) {
  const root = useEffectEvent(getRoot);
  useLayoutEffect(() => {
    if (!open) return;
    const container = root();
    if (!container) return;
    const previous = document.activeElement as HTMLElement | null;
    const first = container.querySelector<HTMLElement>("[data-autofocus]") ?? focusableIn(container)[0] ?? container;
    first.focus({ preventScroll: true });

    const onTab = (event: KeyboardEvent) => {
      if (event.key !== "Tab") return;
      const items = focusableIn(container);
      if (items.length === 0) { event.preventDefault(); return; }
      const head = items[0]!;
      const tail = items[items.length - 1]!;
      if (event.shiftKey && (document.activeElement === head || !container.contains(document.activeElement))) {
        event.preventDefault();
        tail.focus();
      } else if (!event.shiftKey && (document.activeElement === tail || !container.contains(document.activeElement))) {
        event.preventDefault();
        head.focus();
      }
    };
    container.addEventListener("keydown", onTab);
    return () => {
      container.removeEventListener("keydown", onTab);
      if (previous?.isConnected) previous.focus({ preventScroll: true });
    };
  }, [open]);
}
