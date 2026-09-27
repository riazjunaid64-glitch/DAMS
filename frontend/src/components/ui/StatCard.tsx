import type { ReactNode } from "react";
import { cx } from "./cx.ts";
import type { StatusTone } from "./statusTone.ts";

const labelTone: Record<StatusTone, string> = {
  blue: "text-info",
  green: "text-success",
  red: "text-danger",
  orange: "text-warning",
  grey: "text-ink-2",
  gold: "text-gold-text",
};

export type StatCardProps = {
  label: ReactNode;
  value: ReactNode;
  /** Colours the label (e.g. green for "Won"). */
  tone?: StatusTone;
  /** Used as a filter and currently applied: navy border, pressed state. */
  selected?: boolean;
  /** Makes the card a button (e.g. apply its filter). */
  onClick?: () => void;
  className?: string;
};

/** Label + big number. Clickable when it acts as a filter. */
export function StatCard({ label, value, tone = "grey", selected = false, onClick, className }: StatCardProps) {
  const body = (
    <>
      <span className={cx("block text-small font-bold", labelTone[tone])}>{label}</span>
      <span className="mt-0.5 block text-[26px] font-extrabold leading-tight tabular-nums text-ink">{value}</span>
    </>
  );
  const frame = cx(
    "block w-full rounded-card border bg-card px-4 py-3.5 text-left font-ui md:px-5",
    selected ? "border-primary ring-1 ring-primary" : "border-line",
    className,
  );
  return onClick ? (
    <button
      type="button"
      aria-pressed={selected}
      onClick={onClick}
      className={cx(frame, "cursor-pointer transition-colors hover:border-ink-faint focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary")}
    >
      {body}
    </button>
  ) : (
    <div className={frame}>{body}</div>
  );
}

export type InfoCardProps = {
  label: ReactNode;
  value: ReactNode;
  detail?: ReactNode;
  /** Gold-soft version for the one thing to notice (next follow-up, unit type). */
  highlight?: boolean;
  className?: string;
};

/** Small uppercase label, main value, small detail line. */
export function InfoCard({ label, value, detail, highlight = false, className }: InfoCardProps) {
  return (
    <div className={cx("rounded-card border px-4 py-3.5 font-ui", highlight ? "border-gold-line bg-gold-soft" : "border-line bg-card", className)}>
      <p className={cx("m-0 text-caption font-bold uppercase tracking-[0.4px]", highlight ? "text-gold-text" : "text-ink-muted")}>{label}</p>
      <p className="m-0 mt-1 text-[17px] font-extrabold text-ink">{value}</p>
      {detail && <p className={cx("m-0 mt-0.5 text-small", highlight ? "text-gold-text" : "text-ink-muted")}>{detail}</p>}
    </div>
  );
}

export type StatSummaryProps = {
  /** The headline count: a StatCard on desktop, the navy Total bar on phone. */
  total: { label: ReactNode; value: ReactNode };
  items: StatCardProps[];
  /** Phone grid under the Total bar. */
  phoneColumns?: 2 | 3;
  className?: string;
};

/**
 * A row of stat cards on desktop; on phone the navy "Total" bar with the other cards in a grid
 * below it. One component so every list page summarises the same way.
 */
export function StatSummary({ total, items, phoneColumns = 2, className }: StatSummaryProps) {
  return (
    <section className={cx("font-ui", className)}>
      <div className="flex items-center justify-between rounded-btn-phone bg-primary px-4 py-3 text-white md:hidden">
        <span className="text-sm font-bold">{total.label}</span>
        <span className="text-[22px] font-extrabold tabular-nums">{total.value}</span>
      </div>
      <div
        className={cx("mt-2.5 grid gap-2.5 md:mt-0 md:gap-4", phoneColumns === 3 ? "grid-cols-3" : "grid-cols-2", "md:[grid-template-columns:repeat(var(--stat-cols),minmax(0,1fr))]")}
        style={{ ["--stat-cols" as string]: items.length + 1 }}
      >
        <StatCard label={total.label} value={total.value} className="max-md:hidden" />
        {items.map((item, index) => <StatCard key={index} {...item} />)}
      </div>
    </section>
  );
}
