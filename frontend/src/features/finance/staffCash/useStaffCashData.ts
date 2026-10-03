import { useCallback, useEffect, useState } from "react";
import { listCategories, vendorOptions } from "../whtApi.ts";
import type { ExpenseCategory, VendorOption } from "../whtTypes.ts";
import type { CashAccount } from "../loans/types.ts";
import { staffCashApi } from "./api.ts";
import type { Overview } from "./types.ts";

type Loaded = {
  overview: Overview | null;
  cashAccounts: CashAccount[];
  categories: ExpenseCategory[];
  vendors: VendorOption[];
};

/**
 * The overview, and the lists the popups pick from. The overview decides the page's loading and
 * error state; the lists only fill dropdowns, so a failure there is shown inside the popup that
 * needs them instead of taking the page down. A reload keeps the old figures on screen until the
 * new ones arrive.
 */
export function useStaffCashData(enabled: boolean) {
  const [attempt, setAttempt] = useState(0);
  const [data, setData] = useState<Loaded>({ overview: null, cashAccounts: [], categories: [], vendors: [] });
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [cashAccountsError, setCashAccountsError] = useState<string | null>(null);
  const [lookupsError, setLookupsError] = useState<string | null>(null);

  useEffect(() => {
    if (!enabled) return;
    const controller = new AbortController();
    const { signal } = controller;
    void Promise.allSettled([
      staffCashApi.overview(signal),
      staffCashApi.cashAccounts(signal),
      listCategories(),
      vendorOptions(),
    ]).then(([overview, cash, categories, vendors]) => {
      if (signal.aborted) return;
      setData((current) => ({
        overview: overview.status === "fulfilled" ? overview.value : current.overview,
        cashAccounts: cash.status === "fulfilled" ? cash.value : current.cashAccounts,
        categories: categories.status === "fulfilled" ? categories.value : current.categories,
        vendors: vendors.status === "fulfilled" ? vendors.value : current.vendors,
      }));
      setError(overview.status === "rejected" ? (overview.reason instanceof Error ? overview.reason.message : "Could not load staff cash.") : null);
      setCashAccountsError(cash.status === "rejected" ? "The cash and bank accounts could not be loaded." : null);
      setLookupsError(categories.status === "rejected" || vendors.status === "rejected" ? "The expense categories or vendors could not be loaded." : null);
      setLoading(false);
    });
    return () => controller.abort();
  }, [enabled, attempt]);

  const reload = useCallback(() => {
    setLoading(true);
    setAttempt((current) => current + 1);
  }, []);

  return { ...data, loading, error, cashAccountsError, lookupsError, reload };
}
