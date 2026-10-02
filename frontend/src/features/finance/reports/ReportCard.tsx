import type { ReactNode } from "react";
import { cx } from "../../../components/ui";

/**
 * The card all three reports sit in: a title with a second line on the left, an optional badge on
 * the right, then the body. A page part for this page, not a shared component.
 */
export function ReportCard({
  title,
  subtitle,
  badge,
  busy = false,
  children,
  className,
}: {
  title: ReactNode;
  subtitle?: ReactNode;
  badge?: ReactNode;
  /** A refresh is running over rows that are already on screen. */
  busy?: boolean;
  children: ReactNode;
  className?: string;
}) {
  return (
    <section className={cx("relative overflow-hidden rounded-card border border-line bg-card font-ui", className)} aria-busy={busy || undefined}>
      {busy && (
        <div className="absolute inset-x-0 top-0 z-10 h-0.5 overflow-hidden bg-track" role="progressbar" aria-label="Loading">
          <div className="h-full w-1/3 animate-pulse bg-primary" />
        </div>
      )}
      <header className="flex items-start justify-between gap-3 border-b border-line-soft px-[18px] py-4 md:px-5">
        <div className="min-w-0">
          <h2 className="m-0 text-body font-extrabold text-ink md:text-section">{title}</h2>
          {subtitle && <p className="m-0 mt-0.5 text-small text-ink-muted">{subtitle}</p>}
        </div>
        {badge && <div className="shrink-0">{badge}</div>}
      </header>
      {children}
    </section>
  );
}
