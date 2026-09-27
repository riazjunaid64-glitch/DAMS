import type { ReactNode } from "react";
import { cx } from "./cx.ts";
import { IconImage } from "./icons.tsx";

export type EmptyStateProps = {
  title: ReactNode;
  /** One short helper line. */
  message?: ReactNode;
  icon?: ReactNode;
  /** Optional button (e.g. an outline Button). */
  action?: ReactNode;
  className?: string;
};

/** Dashed box for "nothing here yet". */
export function EmptyState({ title, message, icon = <IconImage size={26} />, action, className }: EmptyStateProps) {
  return (
    <div className={cx("flex flex-col items-center justify-center gap-1.5 rounded-card border border-dashed border-line-input bg-card px-6 py-10 text-center font-ui", className)}>
      <span className="mb-1 text-ink-faint">{icon}</span>
      <p className="m-0 text-body font-extrabold text-ink">{title}</p>
      {message && <p className="m-0 max-w-sm text-small text-ink-muted">{message}</p>}
      {action && <div className="mt-2.5">{action}</div>}
    </div>
  );
}
