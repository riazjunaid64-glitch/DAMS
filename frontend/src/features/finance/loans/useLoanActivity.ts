import { useEffect, useState } from "react";
import { DEFAULT_PAGE_SIZE } from "../../../components/ui";
import { STATEMENT_MAX_TAKE, loansApi } from "./api.ts";
import type { LoanTransaction } from "./types.ts";

/** One request for a slice of a loan's activity. A new object is a new request, even for the same rows. */
type Query = { loanId: number; page: number; skip: number; take: number; append: boolean };
type Result = { query: Query | null; items: LoanTransaction[]; total: number; error: string | null };

const firstPage = (loanId: number | null, pageSize: number): Query | null =>
  loanId === null ? null : { loanId, page: 1, skip: 0, take: pageSize, append: false };

/**
 * The open loan's activity, newest first. A desktop moves page by page (`goTo`); a phone appends the
 * next page under the rows it has (`loadMore`). Only the newest request may write its answer, so a
 * slow reply for the previous loan can never land under this loan's heading.
 */
export function useLoanActivity(loanId: number | null, pageSize = DEFAULT_PAGE_SIZE) {
  const [query, setQuery] = useState(() => firstPage(loanId, pageSize));
  const [seen, setSeen] = useState(loanId);
  if (seen !== loanId) {
    setSeen(loanId);
    setQuery(firstPage(loanId, pageSize));
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
          items: query.append && current.query?.loanId === query.loanId ? [...current.items, ...statement.items] : statement.items,
          total,
          error: null,
        }));
      },
      (failure: unknown) => {
        if (controller.signal.aborted) return;
        setResult((current) => {
          const same = current.query?.loanId === query.loanId;
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

  const mine = loanId !== null && result.query?.loanId === loanId;
  const items = mine ? result.items : [];
  const total = mine ? result.total : 0;
  const loading = query !== null && result.query !== query;

  const goTo = (page: number) => {
    if (loanId === null) return;
    setQuery({ loanId, page, skip: (page - 1) * pageSize, take: pageSize, append: false });
  };

  const loadMore = () => {
    if (loanId === null || !query) return;
    setQuery({ loanId, page: query.page + 1, skip: items.length, take: pageSize, append: true });
  };

  /** After a save or delete: the same page on a desktop; on a phone, every row it had, read again from the top. */
  const reload = () => {
    if (!query) return;
    const appended = query.append || query.take > pageSize;
    setQuery(appended
      ? { loanId: query.loanId, page: 1, skip: 0, take: Math.min(STATEMENT_MAX_TAKE, Math.max(pageSize, items.length)), append: false }
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
