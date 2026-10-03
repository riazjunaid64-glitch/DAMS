import { useLayoutEffect, useRef, useState, type ReactNode } from "react";
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

export type StatCardState = "ready" | "loading" | "error";

export type StatCardProps = {
  label: ReactNode;
  value: ReactNode;
  /** Colours the label (e.g. green for "Won"). */
  tone?: StatusTone;
  /** Used as a filter and currently applied: navy border, pressed state. */
  selected?: boolean;
  /** Soft gold tint, and a gold number, for a card that needs attention. Uses the same tokens as a gold Notice. */
  highlight?: boolean;
  /** Small grey line under the number, e.g. "As of today". */
  note?: ReactNode;
  /**
   * Loading draws a grey block instead of the number; error draws an em dash. Neither is
   * clickable. A page that does not have a number yet must pass one of these — never "Rs 0".
   */
  state?: StatCardState;
  /** Makes the card a button (e.g. apply its filter). Ignored while loading or in error. */
  onClick?: () => void;
  className?: string;
};

const VALUE_SIZE = 26;

/**
 * The full amount on one line. When it is wider than the card (a long figure on a phone), the
 * type shrinks until it fits. It is never wrapped or clipped.
 */
function FittedValue({ children, gold = false }: { children: ReactNode; gold?: boolean }) {
  const outerRef = useRef<HTMLSpanElement>(null);
  const innerRef = useRef<HTMLSpanElement>(null);
  const [size, setSize] = useState<number | null>(null);

  useLayoutEffect(() => {
    const outer = outerRef.current;
    const inner = innerRef.current;
    if (!outer || !inner) return;
    const fit = () => {
      const available = outer.clientWidth;
      if (available <= 0) return;
      const rendered = parseFloat(getComputedStyle(inner).fontSize) || VALUE_SIZE;
      const natural = inner.scrollWidth * (VALUE_SIZE / rendered);
      // Floor so the line fits inside the card. A rounded-up size would be clipped.
      const next = natural > available ? Math.floor(((VALUE_SIZE * available) / natural) * 10) / 10 : null;
      setSize((current) => {
        if (next == null) return current == null ? current : null;
        if (current != null && Math.abs(current - next) < 0.05) return current;
        return next;
      });
    };
    fit();
    const observer = new ResizeObserver(fit);
    observer.observe(outer);
    return () => observer.disconnect();
  }, [children]);

  return (
    <span ref={outerRef} className="mt-0.5 block w-full min-w-0">
      <span
        ref={innerRef}
        className={cx("block whitespace-nowrap font-extrabold leading-tight tabular-nums", gold ? "text-gold-text" : "text-ink")}
        style={{ fontSize: size ?? VALUE_SIZE }}
      >
        {children}
      </span>
    </span>
  );
}

/** Label + big number. Clickable when it acts as a filter and the number is ready. */
export function StatCard({ label, value, tone = "grey", selected = false, highlight = false, note, state = "ready", onClick, className }: StatCardProps) {
  const interactive = Boolean(onClick) && state === "ready";
  const body = (
    <>
      <span className={cx("block text-small font-bold", labelTone[tone])}>{label}</span>
      {state === "loading" ? (
        <span aria-hidden="true" className="mt-1.5 block h-7 w-24 animate-pulse rounded bg-track" />
      ) : state === "error" ? (
        <span className="mt-0.5 block text-[26px] font-extrabold leading-tight text-ink">—</span>
      ) : (
        <FittedValue gold={highlight}>{value}</FittedValue>
      )}
      {state !== "loading" && note && <span className="mt-0.5 block text-small text-ink-muted">{note}</span>}
    </>
  );
  const frame = cx(
    "block w-full min-w-0 rounded-card border px-4 py-3.5 text-left font-ui md:px-5",
    highlight ? "bg-gold-soft" : "bg-card",
    selected && state === "ready" ? "border-primary ring-1 ring-primary" : highlight ? "border-gold-line" : "border-line",
    className,
  );
  return interactive ? (
    <button
      type="button"
      aria-pressed={selected}
      onClick={onClick}
      className={cx(frame, "cursor-pointer transition-colors hover:border-ink-faint focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary")}
    >
      {body}
    </button>
  ) : (
    <div className={frame} aria-busy={state === "loading" || undefined}>{body}</div>
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
  total: { label: ReactNode; value: ReactNode; /** Makes Total a button, e.g. to clear a status filter. */ onClick?: () => void; selected?: boolean };
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
      {total.onClick ? (
        <button
          type="button"
          aria-pressed={total.selected}
          onClick={total.onClick}
          className="flex w-full cursor-pointer items-center justify-between rounded-btn-phone border-0 bg-primary px-4 py-3 text-left text-white focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary md:hidden"
        >
          <span className="text-sm font-bold">{total.label}</span>
          <span className="text-[22px] font-extrabold tabular-nums">{total.value}</span>
        </button>
      ) : (
        <div className="flex items-center justify-between rounded-btn-phone bg-primary px-4 py-3 text-white md:hidden">
          <span className="text-sm font-bold">{total.label}</span>
          <span className="text-[22px] font-extrabold tabular-nums">{total.value}</span>
        </div>
      )}
      <div
        className={cx("mt-2.5 grid gap-2.5 md:mt-0 md:gap-4", phoneColumns === 3 ? "grid-cols-3" : "grid-cols-2", "md:[grid-template-columns:repeat(var(--stat-cols),minmax(0,1fr))]")}
        style={{ ["--stat-cols" as string]: items.length + 1 }}
      >
        <StatCard label={total.label} value={total.value} selected={total.selected} onClick={total.onClick} className="max-md:hidden" />
        {items.map((item, index) => <StatCard key={index} {...item} />)}
      </div>
    </section>
  );
}
