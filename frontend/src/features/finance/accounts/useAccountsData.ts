import { useCallback, useEffect, useRef, useState } from "react";
import { api } from "../../../api/api.ts";
import { readFinanceAccountsPage } from "../accountPageReads.ts";
import type { Account, Overview } from "./accountGroups.ts";

export type AccountFilters = { search: string; status: string; typeFilter: string; holderFilter: string };

type Loaded = { accounts: Account[]; hasMore: boolean; overview: Overview | null };

/**
 * The page's accounts and the overview over all of them. A filter change or a reload asks again and
 * cancels the call before it; the old rows stay on screen until the new ones arrive. The overview
 * (the opening totals, the go-live date, the holder names) covers every account whatever the filters,
 * so it is asked for on the first load and after every reload, not on a plain filter change.
 */
export function useAccountsData(enabled: boolean, filters: AccountFilters) {
  const [attempt, setAttempt] = useState(0);
  const [data, setData] = useState<Loaded>({ accounts: [], hasMore: false, overview: null });
  const [settled, setSettled] = useState<{ key: string; error: string | null }>({ key: "", error: null });
  const overviewLoaded = useRef(false);
  const { search, status, typeFilter, holderFilter } = filters;
  const key = JSON.stringify([attempt, search, status, typeFilter, holderFilter]);

  useEffect(() => {
    if (!enabled) return;
    const controller = new AbortController();
    const { signal } = controller;
    readFinanceAccountsPage<Account, Overview>(
      { search, status, typeFilter, holderFilter },
      !overviewLoaded.current,
      (path) => api(path, { signal }),
    ).then((page) => {
      if (signal.aborted) return;
      if (page.overview) overviewLoaded.current = true;
      setData((current) => ({ accounts: page.items, hasMore: page.hasMore, overview: page.overview ?? current.overview }));
      setSettled({ key, error: null });
    }).catch((failure: unknown) => {
      if (signal.aborted) return;
      setSettled({ key, error: failure instanceof Error ? failure.message : "Accounts could not be loaded." });
    });
    return () => controller.abort();
  }, [enabled, key, search, status, typeFilter, holderFilter]);

  const reload = useCallback(() => {
    overviewLoaded.current = false;
    setAttempt((current) => current + 1);
  }, []);

  const loading = enabled && settled.key !== key;
  return { ...data, loading, error: loading ? null : settled.error, reload };
}
