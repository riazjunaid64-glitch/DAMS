import type { ReactNode } from "react";
import { LoadMore, Pagination } from "../../components/ui";
import { cx } from "../../components/ui/cx.ts";
import { PAGE_SIZE } from "./lists.ts";

type FooterProps = {
  total: number;
  isPhone: boolean;
  /** Desktop: the page on show. */
  page: number;
  onPage: (page: number) => void;
  /** Phone: how many rows are on screen so far. */
  shown: number;
  onLoadMore: () => void;
  /** Phone: the next rows are on their way. */
  loading?: boolean;
};

/**
 * Under a finance list: numbered pages on a desktop, and on a phone Load more (twenty more each tap)
 * with the count under it. Works for a list the server pages and for one cut in the browser.
 */
export function ListFooter({ total, isPhone, page, onPage, shown, onLoadMore, loading = false }: FooterProps) {
  if (total === 0) return null;
  if (!isPhone) return <Pagination page={page} pageSize={PAGE_SIZE} totalCount={total} itemLabel="entries" onPageChange={onPage} />;
  const visible = Math.min(shown, total);
  return (
    <div className="flex flex-col items-center gap-2 font-ui">
      <LoadMore shown={visible} total={total} loading={loading} showCount={false} onLoadMore={onLoadMore} />
      <p className="m-0 text-small text-ink-muted">
        Showing <b className="font-extrabold text-ink">{visible.toLocaleString("en-PK")}</b> of <b className="font-extrabold text-ink">{total.toLocaleString("en-PK")}</b> entries
      </p>
    </div>
  );
}

/**
 * One small labelled value on a phone card ("TAX WITHHELD Rs 48,500"). The value never wraps: a long
 * amount shrinks to fit its third of the card instead.
 */
export function CardFigure({ label, children, gold = false }: { label: string; children: ReactNode; /** Tax figures read in gold. */ gold?: boolean }) {
  return (
    <div className="min-w-0">
      <p className="m-0 text-caption font-bold uppercase tracking-[0.4px] text-ink-muted">{label}</p>
      <p className={cx("m-0 mt-0.5 whitespace-nowrap font-extrabold tabular-nums", gold ? "text-gold-text" : "text-ink")} style={{ fontSize: "clamp(11px, 3.6vw, 15px)" }}>{children}</p>
    </div>
  );
}
