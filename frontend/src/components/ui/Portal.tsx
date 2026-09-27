import type { ReactNode } from "react";
import { createPortal } from "react-dom";

/** Renders at document.body so popups sit above sticky bars and are never clipped by a parent. */
export function Portal({ children }: { children: ReactNode }) {
  return createPortal(children, document.body);
}
