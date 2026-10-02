import type { AriaRole, ReactNode } from "react";
import { cx } from "./cx.ts";

export type NoticeTone = "gold" | "red" | "green" | "orange" | "blue";

const toneClass: Record<NoticeTone, { box: string; icon: string }> = {
  gold: { box: "border-gold-line bg-gold-soft", icon: "text-gold-text" },
  red: { box: "border-danger-line bg-danger-soft", icon: "text-danger" },
  green: { box: "border-line bg-success-soft", icon: "text-success" },
  orange: { box: "border-line bg-warning-soft", icon: "text-warning" },
  blue: { box: "border-line bg-info-soft", icon: "text-info" },
};

export type NoticeProps = {
  tone: NoticeTone;
  title: ReactNode;
  /** One short line under the title. */
  message?: ReactNode;
  /** Extra small text under the title, when one line is not enough. */
  children?: ReactNode;
  icon?: ReactNode;
  /** Right of the text on desktop, full width below it on phone. */
  action?: ReactNode;
  role?: AriaRole;
  className?: string;
};

/** Soft-coloured card that points at something needing attention. */
export function Notice({ tone, title, message, children, icon, action, role, className }: NoticeProps) {
  const colours = toneClass[tone];
  return (
    <div role={role} className={cx("flex flex-col gap-3 rounded-card border px-4 py-3.5 font-ui md:flex-row md:items-center", colours.box, className)}>
      <div className="flex min-w-0 flex-1 items-start gap-3">
        {icon && (
          <span className={cx("flex size-9 shrink-0 items-center justify-center rounded-field bg-card", colours.icon)}>{icon}</span>
        )}
        <div className="min-w-0 flex-1">
          <p className="m-0 text-sm font-extrabold text-ink">{title}</p>
          {message && <p className="m-0 mt-0.5 text-small text-ink-2">{message}</p>}
          {children && <div className="mt-0.5 text-small text-ink-2">{children}</div>}
        </div>
      </div>
      {action && <div className="*:w-full md:shrink-0 md:*:w-auto">{action}</div>}
    </div>
  );
}
