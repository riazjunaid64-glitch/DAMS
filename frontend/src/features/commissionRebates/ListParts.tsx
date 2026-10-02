import type { ReactNode } from "react";
import { Link } from "react-router-dom";
import { Button, LoadMore, Notice, Pagination } from "../../components/ui";
import { cx } from "../../components/ui/cx.ts";
import type { UsePagedListResult } from "../../lib/usePagedList.ts";
import { formatPkr } from "../../utils/currency.ts";

/** The booking number as a link to the booking, where the money is entered, paid and reversed. */
export function BookingLink({ id, reference }: { id: number; reference: string }) {
  return (
    <Link to={`/confirmed-bookings/${id}`} className="whitespace-nowrap font-bold text-ink underline underline-offset-4 hover:text-primary">
      {reference}
    </Link>
  );
}

/** An amount that never wraps; Rs 0 reads lighter than a real figure. */
export function Amount({ value, strong = false, className }: { value: number; strong?: boolean; className?: string }) {
  return (
    <span className={cx("whitespace-nowrap tabular-nums", strong || value > 0 ? "font-bold" : "font-normal", value > 0 || strong ? "text-ink" : "text-ink-faint", className)}>
      {formatPkr(value)}
    </span>
  );
}

/** One labelled figure on a phone card ("AMOUNT Rs 1,000"). */
export function CardFigure({ label, value, strong = false }: { label: string; value: number; strong?: boolean }) {
  return (
    <div className="min-w-0">
      <p className="m-0 text-caption font-bold uppercase tracking-[0.4px] text-ink-2">{label}</p>
      <Amount value={value} strong={strong} className="text-sm" />
    </div>
  );
}

/** The frame of a phone card: the rows above sit on the left, the buttons (if any) on the right. */
export function PhoneCard({ children, aside }: { children: ReactNode; aside?: ReactNode }) {
  return (
    <article className="flex items-start gap-3 rounded-card border border-line bg-card p-4 font-ui">
      <div className="min-w-0 flex-1">{children}</div>
      {aside}
    </article>
  );
}

/** Numbered pages on desktop; "Showing 7 of 20 entries" and Load more on a phone. */
export function ListFooter<T>({ list }: { list: UsePagedListResult<T> }) {
  const shown = list.rows.length;
  const total = list.total;
  return (
    <>
      <div className="hidden md:block">
        <Pagination {...list.pagination} itemLabel="entries" />
      </div>
      <div className="flex flex-col items-center gap-2 md:hidden">
        {((total != null && shown < total) || (total == null && list.hasMore)) && <LoadMore {...list.loadMoreBar} showCount={false} />}
        {total != null && total > 0 && (
          <p className="m-0 text-small text-ink-muted">
            Showing {shown.toLocaleString("en-PK")} of {total.toLocaleString("en-PK")} entries
          </p>
        )}
      </div>
    </>
  );
}

/** The red notice every list shows when its rows could not be loaded. */
export function LoadError({ message, onRetry }: { message: string; onRetry: () => void }) {
  return <Notice tone="red" role="alert" title={message} action={<Button variant="outline" onClick={onRetry}>Try again</Button>} />;
}
