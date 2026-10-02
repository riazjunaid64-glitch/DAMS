import { useCallback, useEffect, useState } from "react";
import { capitalApi } from "./api.ts";
import type { AccountOption, Partner } from "./types.ts";

type Loaded = { partners: Partner[]; cashAccounts: AccountOption[]; allAccounts: AccountOption[] };

/**
 * Everything the page and its popups read: the partners, the cash and bank accounts, and the full
 * account list (the Capital accounts a partner can be linked to). The partners decide the page's
 * loading and error state; the accounts only fill the dropdowns, so a failure there is kept apart.
 * A reload keeps the old rows on screen until the new ones arrive.
 */
export function useCapitalData(enabled: boolean) {
  const [attempt, setAttempt] = useState(0);
  const [data, setData] = useState<Loaded>({ partners: [], cashAccounts: [], allAccounts: [] });
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [accountsError, setAccountsError] = useState<string | null>(null);

  useEffect(() => {
    if (!enabled) return;
    const controller = new AbortController();
    const { signal } = controller;
    void Promise.allSettled([capitalApi.partners(signal), capitalApi.accounts(true, signal), capitalApi.accounts(false, signal)]).then(([partners, cash, all]) => {
      if (signal.aborted) return;
      if (partners.status === "fulfilled") {
        setData((current) => ({
          partners: partners.value,
          cashAccounts: cash.status === "fulfilled" ? cash.value : current.cashAccounts,
          allAccounts: all.status === "fulfilled" ? all.value : current.allAccounts,
        }));
        setError(null);
      } else {
        setError(partners.reason instanceof Error ? partners.reason.message : "Partners could not be loaded.");
      }
      setAccountsError(cash.status === "rejected" || all.status === "rejected" ? "The accounts could not be loaded." : null);
      setLoading(false);
    });
    return () => controller.abort();
  }, [enabled, attempt]);

  const reload = useCallback(() => {
    setLoading(true);
    setAttempt((current) => current + 1);
  }, []);

  return { ...data, loading, error, accountsError, reload };
}
