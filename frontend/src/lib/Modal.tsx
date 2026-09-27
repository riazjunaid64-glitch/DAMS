import type { ReactNode } from "react";
import { Overlay } from "../components/ui";

interface Props {
  open: boolean;
  onClose: () => void;
  children: ReactNode;
  align?: "center" | "top";
}

/**
 * Backdrop-only popup for existing screens that draw their own panel as `children`. It shares the
 * shared Overlay (Esc, backdrop, scroll lock, focus kept inside); new screens use Modal from
 * components/ui, which draws the panel too.
 */
export default function Modal({ open, onClose, children, align = "center" }: Props) {
  return (
    <Overlay open={open} onClose={onClose} placement={align}>
      {children}
    </Overlay>
  );
}
