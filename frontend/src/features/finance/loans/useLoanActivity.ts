import { useEffect, useState } from "react";
import { DEFAULT_PAGE_SIZE } from "../../../components/ui";
import { STATEMENT_MAX_TAKE, loansApi } from "./api.ts";
import type { LoanTransaction } from "./types.ts";

/** `pages`: a desktop shows one page at a time. `more`: a phone keeps every row it has and adds the next page under them. */
export type ActivityMode = "pages" | "more";

/** One request for a slice of a loan's activity. A new object is a new request, even for the same rows. */
type Query = { loanId: number; mode: ActivityMode; page: number; skip: number; take: number; append: boolean };
type Result = { query: Query | null; items: LoanTransaction[]; total: number; error: string | null };

const firstPage = (loanId: number | null, mode: ActivityMode, pageSize: number): Query | null =>
  loanId === null ? null : { loanId, mode, page: 1, skip: 0, take: pageSize, append: false };

/** Rows from an earlier request belong to this one only for the same loan read the same way. */
const sameRows = (a: Query | null, b: Query) => a !== null && a.loanId === b.loanId && a.mode === b.mode;

/**
 * The open loan's activity, newest first. A desktop moves page by page (`goTo`); a phone appends the
 * next page under the rows it has (`loadMore`). Changing loan or mode (a window resized across the
 * phone width) starts again at the first page, so a desktop never shows a phone's appended rows
 * under its page numbers and a phone never appends after a desktop's page 3. Only the newest request
 * may write its answer, so a slow reply for the previous loan can never land under this loan's heading.
 */
export function useLoanActivity(loanId: number | null, mode: ActivityMode, pageSize = DEFAULT_PAGE_SIZE) {
  const [query, setQuery] = useState(() => firstPage(loanId, mode, pageSize));
  const [seen, setSeen] = useState({ loanId, mode });
  if (seen.loanId !== loanId || seen.mode !== mode) {
    setSeen({ loanId, mode });
    setQuery(firstPage(loanId, mode, pageSize));
  }
  const [result, setResult] = useState<Result>({ query: null, items: [], total: 0, error: null });

  useEffect(() => {
    if (!query) return;
    const controller = new AbortController();
    loansApi.statement(query.loanId, query.skip, query.take, controller.signal).then(
      (statement) => {
        if (controller.signal.aborted) return;
        const total = statement.loan.transactionCount;
        // A delete can leave this page past the end: step back to the last page that has rows.
        if (!query.append && statement.items.length === 0 && query.page > 1 && total > 0) {
          const last = Math.ceil(total / pageSize);
          setQuery({ ...query, page: last, skip: (last - 1) * pageSize, take: pageSize });
          return;
        }
        setResult((current) => ({
          query,
          items: query.append && sameRows(current.query, query) ? [...current.items, ...statement.items] : statement.items,
          total,
          error: null,
        }));
      },
      (failure: unknown) => {
        if (controller.signal.aborted) return;
        setResult((current) => {
          const same = sameRows(current.query, query);
          return {
            query,
            items: same ? current.items : [],
            total: same ? current.total : 0,
            error: failure instanceof Error ? failure.message : "Loan activity could not be loaded.",
          };
        });
      },
    );
    return () => controller.abort();
  }, [query, pageSize]);

  // Until the first answer for a new loan or mode arrives, nothing old is shown under it.
  const mine = query !== null && sameRows(result.query, query);
  const items = mine ? result.items : [];
  const total = mine ? result.total : 0;
  const loading = query !== null && result.query !== query;

  const goTo = (page: number) => {
    if (!query || query.mode !== "pages") return;
    setQuery({ ...query, page, skip: (page - 1) * pageSize, take: pageSize, append: false });
  };

  const loadMore = () => {
    if (!query || query.mode !== "more") return;
    setQuery({ ...query, page: query.page + 1, skip: items.length, take: pageSize, append: true });
  };

  /** After a save or delete: the same page on a desktop; on a phone, every row it had, read again from the top. */
  const reload = () => {
    if (!query) return;
    setQuery(query.mode === "more"
      ? { ...query, page: 1, skip: 0, take: Math.min(STATEMENT_MAX_TAKE, Math.max(pageSize, items.length)), append: false }
      : { ...query });
  };

  return {
    items,
    total,
    page: query?.page ?? 1,
    pageSize,
    loading,
    error: result.query === query ? result.error : null,
    goTo,
    loadMore,
    reload,
  };
}
