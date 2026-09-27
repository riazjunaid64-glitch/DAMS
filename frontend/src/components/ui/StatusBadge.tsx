import type { ReactNode } from "react";
import { cx } from "./cx.ts";
import { statusLabel, statusTone, type StatusTone } from "./statusTone.ts";

const toneClass: Record<StatusTone, string> = {
  blue: "bg-info-soft text-info",
  green: "bg-success-soft text-success",
  red: "bg-danger-soft text-danger",
  orange: "bg-warning-soft text-warning",
  grey: "bg-steel-soft text-steel",
  gold: "bg-gold-soft text-gold-text",
};

export type StatusBadgeProps = {
  /** The status as the data has it; colour and text come from it. */
  status: string;
  /** Overrides the displayed text (e.g. a translated or grouped label). */
  children?: ReactNode;
  /** Overrides the colour picked from the status. */
  tone?: StatusTone;
  className?: string;
};

/** Round status pill, 12/700. Screens pass only the status. */
export function StatusBadge({ status, children, tone, className }: StatusBadgeProps) {
  return (
    <span
      className={cx(
        "inline-flex h-6 shrink-0 items-center whitespace-nowrap rounded-full px-2.5 font-ui text-label font-bold",
        toneClass[tone ?? statusTone(status)],
        className,
      )}
    >
      {children ?? statusLabel(status)}
    </span>
  );
}
