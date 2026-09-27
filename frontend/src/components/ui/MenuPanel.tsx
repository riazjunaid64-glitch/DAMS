import type { CSSProperties, ReactNode, Ref } from "react";
import { cx } from "./cx.ts";
import { Portal } from "./Portal.tsx";

type MenuPanelProps = {
  ref?: Ref<HTMLDivElement>;
  id?: string;
  role: "listbox" | "menu";
  style: CSSProperties;
  label?: string;
  labelledBy?: string;
  className?: string;
  children: ReactNode;
};

/** The floating white panel shared by Dropdown and ActionsMenu: radius 12, menu shadow, 6px padding. */
export function MenuPanel({ ref, id, role, style, label, labelledBy, className, children }: MenuPanelProps) {
  return (
    <Portal>
      <div
        ref={ref}
        id={id}
        role={role}
        aria-label={label}
        aria-labelledby={labelledBy}
        style={style}
        className={cx(
          "animate-pop-in z-[200] overflow-y-auto overscroll-contain rounded-menu border border-line bg-card p-1.5 font-ui shadow-menu",
          className,
        )}
      >
        {children}
      </div>
    </Portal>
  );
}
