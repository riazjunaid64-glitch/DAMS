import type { ReactNode } from "react";
import { Link } from "react-router-dom";
import { Button } from "./Button.tsx";
import { cx } from "./cx.ts";
import { StatusBadge } from "./StatusBadge.tsx";

export type CardProps = {
  /** Small uppercase heading. */
  title?: ReactNode;
  /** Right-side link action next to the title ("See all", "Edit"). */
  action?: { label: ReactNode; onClick: () => void };
  children: ReactNode;
  className?: string;
};

/** White section card: border, radius 14, padding 18–20. */
export function Card({ title, action, children, className }: CardProps) {
  return (
    <section className={cx("rounded-card border border-line bg-card p-[18px] font-ui md:p-5", className)}>
      {(title || action) && (
        <header className="mb-3.5 flex items-center justify-between gap-3">
          {title && <h3 className="m-0 text-label font-extrabold uppercase tracking-[0.4px] text-ink-2">{title}</h3>}
          {action && <Button variant="link" className="ml-auto" onClick={action.onClick}>{action.label}</Button>}
        </header>
      )}
      {children}
    </section>
  );
}

export type KeyValueItem = { label: ReactNode; value?: ReactNode };

/** Label (12, muted) over value (14/600), two columns. Empty values show "—". */
export function KeyValueGrid({ items, columns = 2, className }: { items: KeyValueItem[]; columns?: 1 | 2 | 3; className?: string }) {
  return (
    <dl className={cx("m-0 grid gap-x-6 gap-y-3.5", columns === 1 ? "grid-cols-1" : columns === 3 ? "grid-cols-2 md:grid-cols-3" : "grid-cols-2", className)}>
      {items.map((item, index) => (
        <div key={index} className="min-w-0">
          <dt className="text-label text-ink-muted">{item.label}</dt>
          <dd className="m-0 mt-0.5 break-words text-sm font-semibold text-ink">
            {item.value == null || item.value === "" ? "—" : item.value}
          </dd>
        </div>
      ))}
    </dl>
  );
}

export type ListCardProps = {
  /** Small reference shown top-left (e.g. lead ID). */
  reference?: ReactNode;
  /** Status for the badge top-right. */
  status?: string;
  /** Custom badge text / node instead of the default status label. */
  badge?: ReactNode;
  title: ReactNode;
  /** One line of detail under the title. */
  detail?: ReactNode;
  /** Main value under the divider (e.g. price, next action). */
  value?: ReactNode;
  /** The whole card opens this route… */
  to?: string;
  /** …or runs this. */
  onClick?: () => void;
  /** Navy border, the same selected treatment as StatCard. For a phone list item that is open beside the list. */
  selected?: boolean;
  className?: string;
};

/** A list item as a card; the whole card is the tap target (no Details button). */
export function ListCard({ reference, status, badge, title, detail, value, to, onClick, selected = false, className }: ListCardProps) {
  const frame = cx(
    "block w-full rounded-card border bg-card p-4 text-left font-ui text-ink no-underline transition-colors",
    selected ? "border-primary ring-1 ring-primary" : "border-line",
    (to || onClick) && "cursor-pointer hover:border-ink-faint focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary",
    className,
  );
  const body = (
    <>
      {(reference || status || badge) && (
        <span className="mb-1.5 flex items-center justify-between gap-2">
          <span className="min-w-0 truncate text-small font-bold text-ink-2">{reference}</span>
          {status ? <StatusBadge status={status}>{badge}</StatusBadge> : badge}
        </span>
      )}
      <span className="block text-section font-extrabold text-primary">{title}</span>
      {detail && <span className="mt-0.5 block text-small font-bold text-ink-2">{detail}</span>}
      {value && <span className="mt-3 block border-t border-line-soft pt-2.5 text-body font-extrabold text-ink">{value}</span>}
    </>
  );
  if (to) return <Link to={to} aria-current={selected ? "true" : undefined} className={frame}>{body}</Link>;
  if (onClick) return <button type="button" aria-pressed={selected} onClick={onClick} className={frame}>{body}</button>;
  return <div className={frame} aria-current={selected ? "true" : undefined}>{body}</div>;
}
