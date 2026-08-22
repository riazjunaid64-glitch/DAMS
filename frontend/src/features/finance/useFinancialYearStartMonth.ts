import { useEffect, useState } from "react";
import { getSettings } from "./whtApi.ts";

export interface FinancialYearStart {
  /** The configured start month, 1–12, or null while it is still unknown. */
  startMonth: number | null;
  /** True once the setting could not be read, so the screen can say so rather than sit blank. */
  failed: boolean;
}

/**
 * The month the client's financial year starts on.
 *
 * Deliberately starts as null rather than as July. July is the Pakistani default and is right for
 * most clients, but "right for most" is exactly the kind of assumption that produces a report
 * labelled with months it does not cover. Anything that states or applies a financial year has to
 * wait for the real answer, so this models "not known yet" as its own state instead of guessing
 * one and hoping the guess is replaced before anybody reads it.
 *
 * Presets that do not depend on the financial year — today, this month, a custom range — are
 * unaffected and stay usable throughout.
 */
export function useFinancialYearStartMonth(enabled = true): FinancialYearStart {
  const [state, setState] = useState<FinancialYearStart>({ startMonth: null, failed: false });

  useEffect(() => {
    if (!enabled) return;
    let live = true;
    getSettings()
      .then((settings) => {
        if (!live) return;
        const month = Number(settings.financialYearStartMonth);
        // A month outside 1–12 is not a usable answer, so it counts as a failure to read rather
        // than something to silently normalise into a range nobody asked for.
        setState(Number.isInteger(month) && month >= 1 && month <= 12
          ? { startMonth: month, failed: false }
          : { startMonth: null, failed: true });
      })
      .catch(() => {
        if (live) setState({ startMonth: null, failed: true });
      });
    return () => { live = false; };
  }, [enabled]);

  return state;
}
