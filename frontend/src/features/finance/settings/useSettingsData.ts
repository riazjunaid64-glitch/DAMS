import { useCallback, useEffect, useState } from "react";
import { listRevenueCategories, type RevenueCategory } from "../revenueCategoryApi.ts";
import * as whtApi from "../whtApi.ts";
import type { ExpenseCategory, FinanceSettings } from "../whtTypes.ts";

type Shared = { settings: FinanceSettings; categories: ExpenseCategory[]; revenueCategories: RevenueCategory[] };
type Settled = { data: Shared | null; doneFor: number | null; error: string | null };

/**
 * What every tab but Vendors shows: the settings, and every expense and revenue category (retired
 * ones too), read together. A reload keeps the old rows on screen until the new ones arrive, and a
 * failed reload keeps them too, under the error. An answer to an older read is dropped.
 */
export function useSettingsData() {
  const [attempt, setAttempt] = useState(0);
  const [settled, setSettled] = useState<Settled>({ data: null, doneFor: null, error: null });

  useEffect(() => {
    const controller = new AbortController();
    const { signal } = controller;
    Promise.all([whtApi.getSettings(signal), whtApi.listCategories(true, signal), listRevenueCategories(true, signal)]).then(
      ([settings, categories, revenueCategories]) => {
        if (!signal.aborted) setSettled({ data: { settings, categories, revenueCategories }, doneFor: attempt, error: null });
      },
      (failure: unknown) => {
        if (signal.aborted) return;
        const error = failure instanceof Error ? failure.message : "Finance settings could not be loaded.";
        setSettled((current) => ({ data: current.data, doneFor: attempt, error }));
      },
    );
    return () => controller.abort();
  }, [attempt]);

  const reload = useCallback(() => setAttempt((current) => current + 1), []);
  const loading = settled.doneFor !== attempt;
  return { data: settled.data, loading, error: loading ? null : settled.error, reload };
}
