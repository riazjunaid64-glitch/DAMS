import type { ReactNode } from "react";
import { StatusBadge, cx, useIsPhone } from "../../components/ui";

type Props = {
  title: ReactNode;
  /** The status as the Bookings module names it (see features/bookings/statusNames). */
  status: string;
  subtitle?: ReactNode;
  /** Commission · Paid · Remaining: label over value on desktop, a line each on a phone. */
  figures: { label: string; value: ReactNode }[];
  /** Pay / Edit, or Apply / Edit: in a row on desktop, full width on a phone. */
  actions: ReactNode[];
  /** The payment rows under the block. */
  children?: ReactNode;
};

/**
 * One commission or the rebate as a simple block: who or what, its status, three figures and its
 * buttons, with the payments underneath. A phone shows the same content as a card.
 */
export function MoneyBlock({ title, status, subtitle, figures, actions, children }: Props) {
  const isPhone = useIsPhone();
  const buttons = actions.filter(Boolean);
  const heading = (
    <div className="min-w-0">
      <div className="flex flex-wrap items-center gap-x-2.5 gap-y-1">
        <h4 className="m-0 text-body font-extrabold text-ink md:text-section">{title}</h4>
        <StatusBadge status={status} />
      </div>
      {subtitle && <p className="m-0 mt-0.5 text-small text-ink-muted">{subtitle}</p>}
    </div>
  );

  if (isPhone) {
    return (
      <article className="rounded-card border border-line bg-card p-4 font-ui">
        {heading}
        <dl className="m-0 mt-2.5 flex flex-col gap-1">
          {figures.map((figure) => (
            <div key={figure.label} className="flex items-baseline justify-between gap-3">
              <dt className="text-body font-bold text-ink-2">{figure.label}</dt>
              <dd className="m-0 text-body font-extrabold tabular-nums text-ink">{figure.value}</dd>
            </div>
          ))}
        </dl>
        {children}
        {buttons.length > 0 && (
          <div className={cx("mt-3.5 grid gap-2.5", buttons.length > 1 ? "grid-cols-2" : "grid-cols-1")}>{buttons}</div>
        )}
      </article>
    );
  }

  return (
    <article className="border-t border-line-soft px-5 py-4 font-ui first:border-t-0">
      <div className="flex items-start justify-between gap-6">
        {heading}
        <div className="flex shrink-0 items-start gap-7">
          <dl className="m-0 flex gap-7">
            {figures.map((figure) => (
              <div key={figure.label} className="text-left">
                <dt className="text-caption font-bold uppercase tracking-[0.4px] text-ink-muted">{figure.label}</dt>
                <dd className="m-0 mt-0.5 whitespace-nowrap text-section font-extrabold tabular-nums text-ink">{figure.value}</dd>
              </div>
            ))}
          </dl>
          <div className="flex w-[124px] items-center justify-end gap-2">{buttons}</div>
        </div>
      </div>
      {children}
    </article>
  );
}
