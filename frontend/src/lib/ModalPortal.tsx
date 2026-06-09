import { createPortal } from "react-dom";
import type { ReactNode } from "react";

/** Renders modals at document.body so they sit above sticky tab bars and other page layers. */
export default function ModalPortal({ children }: { children: ReactNode }) {
  return createPortal(children, document.body);
}
