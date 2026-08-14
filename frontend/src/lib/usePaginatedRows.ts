import { useCallback, useEffect, useRef, useState } from "react";
import { api } from "../api/api.ts";

const PAGE_SIZE = 100;

interface PagedResponse<T> {
  items: T[];
  hasMore: boolean;
}

export interface PaginatedRows<T> {
  rows: T[];
  hasMore: boolean;
  loading: boolean;
  loadingMore: boolean;
  error: string | null;
  loadMore: () => void;
  reload: () => void;
}

/** Shared so an empty result keeps a stable identity across renders. */
const NO_ROWS: never[] = [];

export interface Loaded<T> {
  /** The view + filters these rows were fetched for. */
  key: string;
  rows: T[];
  hasMore: boolean;
  loading: boolean;
  error: string | null;
}

/**
 * The key a set of rows belongs to — rows are only ever valid for the exact filters that fetched
 * them. Serialised rather than concatenated so no combination of values can produce the same key
 * as a different combination.
 */
export const rowsKey = (
  view: string,
  projectId: string,
  fromDate: string,
  toDate: string,
  account: string,
) => JSON.stringify([view, projectId, fromDate, toDate, account]);

/**
 * What the caller may see for the key it is currently rendering.
 *
 * Rows carrying any other key are withheld and reported as still loading. Exported so the rule can
 * be tested directly: it is the one thing standing between a view switch and a column reaching into
 * a row shape that has no such field.
 */
export function visibleRows<T>(loaded: Loaded<T>, key: string): Loaded<T> {
  return loaded.key === key
    ? loaded
    : { key, rows: NO_ROWS, hasMore: false, loading: true, error: null };
}

/**
 * Infinite-scroll data source for a finance view. Fetches the first 100 rows, then
 * appends 100-row chunks on demand, and ignores stale responses from superseded requests.
 *
 * Rows are stored together with the key they belong to, and a key that no longer matches the
 * requested one reads as empty-and-loading. That coupling is load-bearing rather than tidiness:
 * the caller picks its table columns from the same view these rows are fetched for, and each
 * view's columns reach into fields only that view's rows have. Clearing the rows in an effect
 * instead — as this did — leaves one render where the new view's columns are handed the old
 * view's rows, and a column that does arithmetic on a field the old shape lacks takes the whole
 * screen down. Deriving it during render means the two can never be a render out of step.
 */
export function usePaginatedRows<T>(
  view: string,
  projectId: string,
  fromDate: string,
  toDate: string,
  account: string = ""
): PaginatedRows<T> {
  const key = rowsKey(view, projectId, fromDate, toDate, account);

  const [loaded, setLoaded] = useState<Loaded<T>>(
    { key, rows: NO_ROWS, hasMore: false, loading: true, error: null });
  const [loadingMore, setLoadingMore] = useState(false);

  const skipRef = useRef(0);
  const reqIdRef = useRef(0);
  const inFlightRef = useRef(false);

  const load = useCallback(
    async (reset: boolean) => {
      if (inFlightRef.current && !reset) return;
      const reqId = ++reqIdRef.current;
      const requestKey = rowsKey(view, projectId, fromDate, toDate, account);
      inFlightRef.current = true;
      const skip = reset ? 0 : skipRef.current;
      if (reset) setLoaded((prev) => prev.key === requestKey ? { ...prev, loading: true } : prev);
      else setLoadingMore(true);

      try {
        const params = new URLSearchParams({ view, skip: String(skip), take: String(PAGE_SIZE) });
        if (projectId) params.set("projectId", projectId);
        if (fromDate) params.set("from", fromDate);
        if (toDate) params.set("to", toDate);
        if (account) params.set("account", account);

        const res = await api(`/api/Finance/rows?${params.toString()}`);
        if (!res.ok) throw new Error("request failed");
        const json: PagedResponse<T> = await res.json();
        if (reqId !== reqIdRef.current) return; // superseded by a newer request

        skipRef.current = skip + json.items.length;
        setLoaded((prev) => ({
          key: requestKey,
          // Only append onto rows already belonging to this key; anything else is replaced.
          rows: reset || prev.key !== requestKey ? json.items : [...prev.rows, ...json.items],
          hasMore: json.hasMore,
          loading: false,
          error: null,
        }));
      } catch {
        if (reqId !== reqIdRef.current) return;
        setLoaded((prev) => prev.key === requestKey && !reset
          // A failed "load more" keeps what is already on screen.
          ? { ...prev, loading: false, error: "Unable to load rows." }
          : { key: requestKey, rows: NO_ROWS, hasMore: false, loading: false, error: "Unable to load rows." });
      } finally {
        if (reqId === reqIdRef.current) setLoadingMore(false);
        inFlightRef.current = false;
      }
    },
    [view, projectId, fromDate, toDate, account]
  );

  // Reset + fetch first page whenever the view or filters change.
  useEffect(() => {
    void load(true);
  }, [load]);

  // Rows from a superseded key are never handed out; the caller sees an empty, loading table
  // until the data for what it is actually rendering arrives.
  const current = visibleRows(loaded, key);

  const loadMore = useCallback(() => {
    if (inFlightRef.current || !current.hasMore) return;
    void load(false);
  }, [load, current.hasMore]);

  const reload = useCallback(() => {
    void load(true);
  }, [load]);

  return {
    rows: current.rows,
    hasMore: current.hasMore,
    loading: current.loading,
    loadingMore,
    error: current.error,
    loadMore,
    reload,
  };
}
