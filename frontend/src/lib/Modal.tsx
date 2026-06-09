import { createPortal } from "react-dom";
import type { ReactNode } from "react";

interface Props {
  open: boolean;
  onClose: () => void;
  children: ReactNode;
  align?: "center" | "top";
}

export default function Modal({ open, onClose, children, align = "center" }: Props) {
  if (!open) return null;

  return createPortal(
    <div
      className={`fixed inset-0 z-[100] flex p-4 ${
        align === "top" ? "items-start justify-center overflow-y-auto" : "items-center justify-center"
      }`}
    >
      <div
        className="absolute inset-0 bg-black/60 backdrop-blur-sm animate-fade-in"
        onClick={onClose}
        aria-hidden
      />
      {children}
    </div>,
    document.body,
  );
}
