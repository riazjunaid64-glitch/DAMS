import { useCallback, useEffect, useRef, useState } from "react";
import { api } from "../../../api/api.ts";
import { useIsPhone } from "../../../components/ui";

export const FINANCE_PAGE_SIZE = 20;

interface PagedResponse<T> {
  items: T[];
  hasMore: boolean;
  totalCount?: number;
}

export interface Loaded<T> {
  key: string;
  rows: T[];
  hasMore: boolean;
  totalCount: number;
  loading: boolean;
  error: string | null;
}

const NO_ROWS: never[] = [];

export const rowsKey = (
  view: string,
  projectId: string,
  fromDate: string,
  toDate: string,
  account: string,
) => JSON.stringify([view, projectId, fromDate, toDate, account]);

export function visibleRows<T>(loaded: Loaded<T>, key: string): Loaded<T> {
  return loaded.key === key
    ? loaded
    : { key, rows: NO_ROWS, hasMore: false, totalCount: 0, loading: true, error: null };
}

export function isCurrentRowsRequest(
  requestId: number,
  currentRequestId: number,
  signal: AbortSignal,
): boolean {
  return requestId === currentRequestId && !signal.aborted;
}

async function readError(response: Response): Promise<string> {
  const body = (await response.json().catch(() => null)) as { message?: string } | null;
  return body?.message || "Unable to load rows.";
}

/**
 * One finance-home list: 20 rows a page. Desktop replaces the page; a phone appends.
 * Rows stay tied to the filters that fetched them so a view switch cannot hand the new
 * columns a row from the previous list.
 */
export function useFinanceRows<T>(
  view: string,
  projectId: string,
  fromDate: string,
  toDate: string,
  account: string,
  enabled: boolean,
) {
  const isPhone = useIsPhone();
  const key = rowsKey(view, projectId, fromDate, toDate, account);
  const [page, setPage] = useState(1);
  const [loaded, setLoaded] = useState<Loaded<T>>({
    key, rows: NO_ROWS, hasMore: false, totalCount: 0, loading: true, error: null,
  });
  const [loadingMore, setLoadingMore] = useState(false);
  const pageRef = useRef(1);
  const shownRef = useRef(0);
  const reqIdRef = useRef(0);
  const controllerRef = useRef<AbortController | null>(null);
  const backedUp = useRef(false);

  const [keyForPage, setKeyForPage] = useState(key);
  if (keyForPage !== key) {
    setKeyForPage(key);
    pageRef.current = 1;
    setPage(1);
  }

  const load = useCallback(async (append: boolean) => {
    const requestKey = rowsKey(view, projectId, fromDate, toDate, account);
    if (append) {
      /* keep going */
    } else {
      controllerRef.current?.abort();
    }
    const reqId = ++reqIdRef.current;
    if (!enabled) {
      controllerRef.current = null;
      shownRef.current = 0;
      setLoadingMore(false);
      setLoaded({ key: requestKey, rows: NO_ROWS, hasMore: false, totalCount: 0, loading: false, error: null });
      return;
    }
    const controller = new AbortController();
    controllerRef.current = controller;
    const skip = append ? shownRef.current : isPhone ? 0 : (pageRef.current - 1) * FINANCE_PAGE_SIZE;
    const take = append || !isPhone
      ? FINANCE_PAGE_SIZE
      : Math.min(200, Math.max(FINANCE_PAGE_SIZE, shownRef.current || FINANCE_PAGE_SIZE));
    if (append) setLoadingMore(true);
    else setLoaded((prev) => prev.key === requestKey ? { ...prev, loading: true, error: null } : prev);

    try {
      const params = new URLSearchParams({ view, skip: String(skip), take: String(take) });
      if (projectId) params.set("projectId", projectId);
      if (fromDate) params.set("from", fromDate);
      if (toDate) params.set("to", toDate);
      if (account) params.set("account", account);
      const res = await api(`/api/Finance/rows?${params.toString()}`, { signal: controller.signal });
      if (!res.ok) throw new Error(await readError(res));
      const json = (await res.json()) as PagedResponse<T>;
      if (!isCurrentRowsRequest(reqId, reqIdRef.current, controller.signal)) return;
      if (!append && !isPhone && json.items.length === 0 && pageRef.current > 1 && !backedUp.current) {
        backedUp.current = true;
        pageRef.current -= 1;
        setPage(pageRef.current);
        return;
      }
      backedUp.current = false;
      const totalCount = json.totalCount ?? json.items.length;
      setLoaded((prev) => {
        const rows = append && prev.key === requestKey ? [...prev.rows, ...json.items] : json.items;
        shownRef.current = rows.length;
        return {
          key: requestKey,
          rows,
          hasMore: json.hasMore || rows.length < totalCount,
          totalCount,
          loading: false,
          error: null,
        };
      });
    } catch (caught) {
      if (!isCurrentRowsRequest(reqId, reqIdRef.current, controller.signal)) return;
      const message = caught instanceof Error && caught.message ? caught.message : "Unable to load rows.";
      setLoaded((prev) => prev.key === requestKey && (append || prev.rows.length > 0)
        ? { ...prev, loading: false, error: message }
        : { key: requestKey, rows: NO_ROWS, hasMore: false, totalCount: 0, loading: false, error: message });
    } finally {
      if (reqId === reqIdRef.current) {
        setLoadingMore(false);
        if (controllerRef.current === controller) controllerRef.current = null;
      }
    }
  }, [view, projectId, fromDate, toDate, account, enabled, isPhone]);

  useEffect(() => {
    pageRef.current = page;
    void load(false);
    return () => controllerRef.current?.abort();
  }, [load, page]);

  const current = visibleRows(loaded, key);

  const loadMore = useCallback(() => {
    if (!isPhone || loadingMore || !current.hasMore) return;
    void load(true);
  }, [isPhone, loadingMore, current.hasMore, load]);

  const reload = useCallback(() => {
    void load(false);
  }, [load]);

  return {
    rows: current.rows,
    totalCount: current.totalCount,
    loading: current.loading,
    loadingMore,
    error: current.error,
    page,
    setPage,
    loadMore,
    reload,
  };
}
