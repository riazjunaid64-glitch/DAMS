import { useCallback, useEffect, useState } from "react";
import { loansApi } from "../loans/api.ts";
import type { CashAccount } from "../loans/types.ts";

type Settled<T> = { data: T | null; key: string | null; doneFor: string | null; error: string | null };

/**
 * One read for the chosen From / To, kept until the next one answers.
 *
 * - Loading is "this exact request has not answered yet", so a new range or a reload shows the old
 *   rows with a progress line instead of blanking the list, and the first load has nothing to show.
 * - A failure clears the data: a list or a figure shown under an error would be one the screen no
 *   longer vouches for.
 * - `dataKey` names the range the data answers, so a caller can tell an old answer from the current one.
 * - Nothing is requested while `enabled` is false; it is requested again each time it turns true.
 */
export function useRangeData<T>(
  read: (from: string, to: string, signal: AbortSignal) => Promise<T>,
  from: string,
  to: string,
  enabled: boolean,
  fallback: string,
) {
  const [attempt, setAttempt] = useState(0);
  const [settled, setSettled] = useState<Settled<T>>({ data: null, key: null, doneFor: null, error: null });
  const key = `${from}|${to}`;
  const token = `${key}#${attempt}`;

  useEffect(() => {
    if (!enabled) return;
    const controller = new AbortController();
    read(from, to, controller.signal).then(
      (data) => {
        if (!controller.signal.aborted) setSettled({ data, key, doneFor: token, error: null });
      },
      (failure: unknown) => {
        if (!controller.signal.aborted) setSettled({ data: null, key, doneFor: token, error: failure instanceof Error ? failure.message : fallback });
      },
    );
    return () => controller.abort();
  }, [enabled, read, from, to, key, token, fallback]);

  const reload = useCallback(() => setAttempt((current) => current + 1), []);
  const loading = enabled && settled.doneFor !== token;
  return { data: settled.data, dataKey: settled.key, loading, error: loading ? null : settled.error, reload };
}

/** The cash and bank accounts a deposit is paid from. Only the popup needs them, so a failure shows there. */
export function useCashAccounts(enabled: boolean) {
  const [attempt, setAttempt] = useState(0);
  const [accounts, setAccounts] = useState<CashAccount[]>([]);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!enabled) return;
    const controller = new AbortController();
    loansApi.cashAccounts(controller.signal).then(
      (rows) => {
        if (controller.signal.aborted) return;
        setAccounts(rows);
        setError(null);
      },
      (failure: unknown) => {
        if (!controller.signal.aborted) setError(failure instanceof Error ? failure.message : "The cash and bank accounts could not be loaded.");
      },
    );
    return () => controller.abort();
  }, [enabled, attempt]);

  const reload = useCallback(() => setAttempt((current) => current + 1), []);
  return { accounts, error, reload };
}
