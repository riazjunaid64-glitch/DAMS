import { useCallback, useEffect, useRef, useState } from "react";
import { DEFAULT_PAGE_SIZE } from "../components/ui/pageItems.ts";
import { useIsPhone } from "../components/ui/useMediaQuery.ts";

/** A response may update the table only while it is both the newest request and still wanted. */
export function isCurrentRowsRequest(
  requestId: number,
  currentRequestId: number,
  signal: AbortSignal,
): boolean {
  return requestId === currentRequestId && !signal.aborted;
}

export type PagedListQuery = {
  /** 1-based page. Desktop asks for one page; a phone delete may ask for the pages loaded so far. */
  page: number;
  skip: number;
  take: number;
  signal: AbortSignal;
};

export type PagedListPage<T> = {
  items: T[];
  hasMore?: boolean;
  totalCount?: number | null;
};

export type UsePagedListOptions<T> = {
  /** Filter identity. A change resets the list to page 1. */
  queryKey: string;
  /** One page. Must honour `skip` and `take`. A late or aborted answer is ignored. */
  fetchPage: (query: PagedListQuery) => Promise<PagedListPage<T>>;
  pageSize?: number;
};

export type UsePagedListResult<T> = {
  rows: T[];
  /** Matching rows, or null when the server did not send a total. */
  total: number | null;
  loading: boolean;
  loadingMore: boolean;
  error: string | null;
  /** 1-based page on desktop; how many pages have been appended on a phone. */
  page: number;
  setPage: (page: number) => void;
  loadMore: () => void;
  /** Refetches. If the current page is now empty and it is not the first, steps back one page. */
  afterDelete: () => void;
  reload: () => void;
  hasMore: boolean;
  /** Props for the shared Pagination. */
  pagination: { page: number; onPageChange: (page: number) => void; totalCount?: number; pageSize: number };
  /** Props for the shared LoadMore. */
  loadMoreBar: { shown: number; total: number; onLoadMore: () => void; loading: boolean };
};

type Request = {
  page: number;
  append: boolean;
  refresh: number;
  intent: "load" | "delete";
  /** Captured with the request so a layout change refetches without setting state inside the effect. */
  phone: boolean;
  pageSize: number;
};

/**
 * The page to show after a delete. An empty page that is not the first steps back one, so the
 * user is not left on a blank last page.
 */
export function pageAfterEmptyDelete(page: number, itemsOnPage: number): number {
  return page > 1 && itemsOnPage === 0 ? page - 1 : page;
}

/**
 * Page number + filters, one request at a time.
 *
 * Desktop replaces the rows for the page. A phone appends. A filter change (a new `queryKey`)
 * goes back to page 1 and the rows for the previous key are withheld in that same render, so a
 * view switch never hands the new columns a row from the old one. Answers that arrive late are
 * ignored (`isCurrentRowsRequest`).
 */
export function usePagedList<T>({ queryKey, fetchPage, pageSize = DEFAULT_PAGE_SIZE }: UsePagedListOptions<T>): UsePagedListResult<T> {
  const isPhone = useIsPhone();
  const fetchRef = useRef(fetchPage);
  useEffect(() => {
    fetchRef.current = fetchPage;
  }, [fetchPage]);

  const [activeKey, setActiveKey] = useState(queryKey);
  const [request, setRequest] = useState<Request>({ page: 1, append: false, refresh: 0, intent: "load", phone: isPhone, pageSize });
  const [rows, setRows] = useState<T[]>([]);
  const [total, setTotal] = useState<number | null>(null);
  const [hasMore, setHasMore] = useState(false);
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const reqIdRef = useRef(0);

  if (activeKey !== queryKey) {
    setActiveKey(queryKey);
    setRequest({ page: 1, append: false, refresh: 0, intent: "load", phone: isPhone, pageSize });
    setRows([]);
    setTotal(null);
    setHasMore(false);
    setError(null);
    setLoading(true);
    setLoadingMore(false);
  } else if (request.phone !== isPhone || request.pageSize !== pageSize) {
    setRequest({ ...request, append: false, refresh: request.refresh + 1, intent: "load", phone: isPhone, pageSize });
    setLoading(true);
    setLoadingMore(false);
  }

  useEffect(() => {
    const id = ++reqIdRef.current;
    const controller = new AbortController();
    const { page, append, intent, phone, pageSize: takeSize } = request;
    const covering = intent === "delete" && phone;
    const skip = covering ? 0 : (page - 1) * takeSize;
    const take = covering ? page * takeSize : takeSize;

    fetchRef.current({ page: covering ? 1 : page, skip, take, signal: controller.signal })
      .then((result) => {
        if (!isCurrentRowsRequest(id, reqIdRef.current, controller.signal)) return;
        const items = result.items ?? [];
        if (intent === "delete") {
          const nextPage = pageAfterEmptyDelete(page, covering ? (items.length <= (page - 1) * takeSize ? 0 : items.length) : items.length);
          if (nextPage !== page) {
            setLoading(true);
            setRequest((current) => current.page === page && current.intent === "delete"
              ? { ...current, page: nextPage, append: false, refresh: current.refresh + 1, intent: "load" }
              : current);
            return;
          }
        }
        setRows((prev) => (append ? [...prev, ...items] : items));
        setTotal(result.totalCount ?? null);
        setHasMore(Boolean(result.hasMore) || (result.totalCount != null && skip + items.length < result.totalCount));
        setError(null);
        setLoading(false);
      })
      .catch((caught: unknown) => {
        if (!isCurrentRowsRequest(id, reqIdRef.current, controller.signal)) return;
        if (caught instanceof DOMException && caught.name === "AbortError") return;
        const message = caught instanceof Error && caught.message.trim() ? caught.message : "Unable to load rows.";
        setError(message);
        setLoading(false);
        if (!append) setRows([]);
      })
      .finally(() => {
        if (id === reqIdRef.current) setLoadingMore(false);
      });

    return () => controller.abort();
  }, [activeKey, request]);

  const setPage = useCallback((page: number) => {
    setLoading(true);
    setLoadingMore(false);
    setRequest((current) => ({ ...current, page, append: false, refresh: current.refresh + 1, intent: "load" }));
  }, []);

  const loadMore = useCallback(() => {
    if (isPhone) setLoadingMore(true);
    else {
      setLoading(true);
      setLoadingMore(false);
    }
    setRequest((current) => ({
      ...current,
      page: current.page + 1,
      append: current.phone,
      refresh: current.refresh + 1,
      intent: "load",
    }));
  }, [isPhone]);

  const reload = useCallback(() => {
    setLoading(true);
    setLoadingMore(false);
    setRequest((current) => ({ ...current, append: false, refresh: current.refresh + 1, intent: "load" }));
  }, []);

  const afterDelete = useCallback(() => {
    setLoading(true);
    setLoadingMore(false);
    setRequest((current) => ({ ...current, append: false, refresh: current.refresh + 1, intent: "delete" }));
  }, []);

  const switching = activeKey !== queryKey;
  const shownRows = switching ? [] : rows;
  const shownTotal = switching ? null : total;
  const loadMoreTotal = shownTotal != null ? shownTotal : hasMore ? shownRows.length + pageSize : shownRows.length;

  return {
    rows: shownRows,
    total: shownTotal,
    loading: switching || loading,
    loadingMore: switching ? false : loadingMore,
    error: switching ? null : error,
    page: request.page,
    setPage,
    loadMore,
    afterDelete,
    reload,
    hasMore,
    pagination: {
      page: request.page,
      onPageChange: setPage,
      totalCount: shownTotal ?? undefined,
      pageSize,
    },
    loadMoreBar: {
      shown: shownRows.length,
      total: loadMoreTotal,
      onLoadMore: loadMore,
      loading: switching ? false : loadingMore,
    },
  };
}
