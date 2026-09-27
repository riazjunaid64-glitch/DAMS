import { Button } from "./Button.tsx";
import { cx } from "./cx.ts";
import { DEFAULT_PAGE_SIZE, pageItems, pageRange } from "./pageItems.ts";

export type PaginationProps = {
  /** 1-based current page. */
  page: number;
  onPageChange: (page: number) => void;
  /** Total matching items; drives the "Showing 1–20 of 437 items" line and the page count. */
  totalCount?: number;
  /** Page count from the API; derived from totalCount / pageSize when omitted. */
  totalPages?: number;
  pageSize?: number;
  /** Noun for the summary line ("leads"). */
  itemLabel?: string;
  className?: string;
};

const pageButton = "h-10 min-w-10 cursor-pointer items-center justify-center rounded-field px-2 text-sm font-extrabold transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary";

/** Desktop pagination: summary on the left, Previous · pages · Next on the right. */
export function Pagination({ page, onPageChange, totalCount, totalPages, pageSize = DEFAULT_PAGE_SIZE, itemLabel = "items", className }: PaginationProps) {
  const pages = totalPages ?? (totalCount != null ? Math.ceil(totalCount / pageSize) : 1);
  if (totalCount === 0 || (pages <= 1 && totalCount == null)) return null;
  const [from, to] = totalCount != null ? pageRange(page, pageSize, totalCount) : [0, 0];

  return (
    <nav
      aria-label="Pagination"
      className={cx("flex flex-wrap items-center justify-between gap-3 rounded-card border border-line bg-table-head px-4 py-3 font-ui", className)}
    >
      {totalCount != null && (
        <p className="m-0 text-sm text-ink-2">
          Showing <b className="font-extrabold text-ink">{from}–{to}</b> of <b className="font-extrabold text-ink">{totalCount.toLocaleString("en-PK")}</b> {itemLabel}
        </p>
      )}
      {pages > 1 && (
        <div className="ml-auto flex items-center gap-1.5">
          <Button variant="outline" disabled={page <= 1} onClick={() => onPageChange(page - 1)}>Previous</Button>
          {/* Narrow screens: the numbers give way to "5 / 22" so the bar never scrolls sideways. */}
          <span className="px-2 text-sm font-extrabold text-ink sm:hidden">{page} / {pages}</span>
          {pageItems(page, pages).map((item, index) =>
            item === "gap" ? (
              <span key={`gap-${index}`} aria-hidden="true" className="hidden w-6 text-center text-ink-faint sm:inline">…</span>
            ) : (
              <button
                key={item}
                type="button"
                aria-label={`Page ${item}`}
                aria-current={item === page ? "page" : undefined}
                onClick={() => item !== page && onPageChange(item)}
                className={cx(pageButton, "hidden sm:flex", item === page ? "bg-primary text-white shadow-menu" : "border border-line-input bg-card text-ink hover:bg-page")}
              >
                {item}
              </button>
            ),
          )}
          <Button variant="outline" disabled={page >= pages} onClick={() => onPageChange(page + 1)}>Next</Button>
        </div>
      )}
    </nav>
  );
}

export type LoadMoreProps = {
  /** Items already on screen. */
  shown: number;
  /** All matching items. The button hides once everything is shown. */
  total: number;
  onLoadMore: () => void;
  loading?: boolean;
  /** Shows the "Showing 20 of 200" line. */
  showCount?: boolean;
  className?: string;
};

/** Phone lists: a full-width "Load more" that appends the next page. */
export function LoadMore({ shown, total, onLoadMore, loading = false, showCount = true, className }: LoadMoreProps) {
  if (shown >= total) return null;
  return (
    <div className={cx("flex flex-col items-center gap-2 font-ui", className)}>
      <Button variant="outline" size="lg" fullWidth loading={loading} onClick={onLoadMore}>Load more</Button>
      {showCount && <p className="m-0 text-small text-ink-muted">Showing {shown.toLocaleString("en-PK")} of {total.toLocaleString("en-PK")}</p>}
    </div>
  );
}
