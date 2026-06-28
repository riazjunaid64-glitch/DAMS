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

/**
 * Infinite-scroll data source for a finance view. Fetches the first 100 rows, then
 * appends 100-row chunks on demand. Resets whenever the view or filters change, and
 * ignores stale responses from superseded requests.
 */
export function usePaginatedRows<T>(
  view: string,
  projectId: string,
  fromDate: string,
  toDate: string
): PaginatedRows<T> {
  const [rows, setRows] = useState<T[]>([]);
  const [hasMore, setHasMore] = useState(false);
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const skipRef = useRef(0);
  const reqIdRef = useRef(0);
  const inFlightRef = useRef(false);

  const load = useCallback(
    async (reset: boolean) => {
      if (inFlightRef.current && !reset) return;
      const reqId = ++reqIdRef.current;
      inFlightRef.current = true;
      const skip = reset ? 0 : skipRef.current;
      if (reset) setLoading(true);
      else setLoadingMore(true);

      try {
        const params = new URLSearchParams({ view, skip: String(skip), take: String(PAGE_SIZE) });
        if (projectId) params.set("projectId", projectId);
        if (fromDate) params.set("from", fromDate);
        if (toDate) params.set("to", toDate);

        const res = await api(`/api/Finance/rows?${params.toString()}`);
        if (!res.ok) throw new Error("request failed");
        const json: PagedResponse<T> = await res.json();
        if (reqId !== reqIdRef.current) return; // superseded by a newer request

        setRows((prev) => (reset ? json.items : [...prev, ...json.items]));
        skipRef.current = skip + json.items.length;
        setHasMore(json.hasMore);
        setError(null);
      } catch {
        if (reqId === reqIdRef.current) setError("Unable to load rows.");
      } finally {
        if (reqId === reqIdRef.current) {
          setLoading(false);
          setLoadingMore(false);
        }
        inFlightRef.current = false;
      }
    },
    [view, projectId, fromDate, toDate]
  );

  // Reset + fetch first page whenever the view or filters change.
  useEffect(() => {
    void load(true);
  }, [load]);

  const loadMore = useCallback(() => {
    if (inFlightRef.current || !hasMore) return;
    void load(false);
  }, [load, hasMore]);

  const reload = useCallback(() => {
    void load(true);
  }, [load]);

  return { rows, hasMore, loading, loadingMore, error, loadMore, reload };
}
