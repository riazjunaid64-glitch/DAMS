import { useCallback, useEffect, useState } from "react";
import { loansApi } from "./api.ts";
import type { CashAccount, Loan, LoanAccount } from "./types.ts";

type Loaded = { loans: Loan[]; loanAccounts: LoanAccount[]; cashAccounts: CashAccount[] };

/**
 * The loans and the two account lists the popups pick from. The loans decide the page's loading and
 * error state; the accounts only fill dropdowns, so a failure there is kept apart. A reload keeps
 * the old rows on screen until the new ones arrive.
 */
export function useLoansData(enabled: boolean) {
  const [attempt, setAttempt] = useState(0);
  const [data, setData] = useState<Loaded>({ loans: [], loanAccounts: [], cashAccounts: [] });
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [loanAccountsError, setLoanAccountsError] = useState<string | null>(null);
  const [cashAccountsError, setCashAccountsError] = useState<string | null>(null);

  useEffect(() => {
    if (!enabled) return;
    const controller = new AbortController();
    const { signal } = controller;
    void Promise.allSettled([loansApi.loans(signal), loansApi.loanAccounts(signal), loansApi.cashAccounts(signal)]).then(([loans, loanAccounts, cash]) => {
      if (signal.aborted) return;
      if (loans.status === "fulfilled") {
        setData((current) => ({
          loans: loans.value,
          loanAccounts: loanAccounts.status === "fulfilled" ? loanAccounts.value : current.loanAccounts,
          cashAccounts: cash.status === "fulfilled" ? cash.value : current.cashAccounts,
        }));
        setError(null);
      } else {
        setError(loans.reason instanceof Error ? loans.reason.message : "Loans could not be loaded.");
      }
      setLoanAccountsError(loanAccounts.status === "rejected" ? "The loan accounts could not be loaded." : null);
      setCashAccountsError(cash.status === "rejected" ? "The cash and bank accounts could not be loaded." : null);
      setLoading(false);
    });
    return () => controller.abort();
  }, [enabled, attempt]);

  const reload = useCallback(() => {
    setLoading(true);
    setAttempt((current) => current + 1);
  }, []);

  return { ...data, loading, error, loanAccountsError, cashAccountsError, reload };
}
