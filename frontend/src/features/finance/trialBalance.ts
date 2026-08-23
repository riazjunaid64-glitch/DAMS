import { financeRangeError } from "../../lib/financeChartData.ts";

export type TrialDateMode = "asAt" | "range";

export interface TrialBalanceFilters {
  mode: TrialDateMode;
  projectId: string;
  asAt: string;
  from: string;
  to: string;
}

/** The exact bounds that produced a summary and must also be used by its drill-down. */
export interface AppliedTrialBalanceFilters {
  projectId: string;
  from: string;
  to: string;
}

export interface TrialBalanceRow {
  accountId: number;
  accountKey: string;
  ledgerCode: string | null;
  accountName: string;
  debitBalances: number[];
  creditBalances: number[];
}

export interface TrialBalanceReport {
  columnDates: string[];
  rows: TrialBalanceRow[];
  columnDebitTotals: number[];
  columnCreditTotals: number[];
  columnBalanced: boolean[];
}

export type BalanceType = "Debit" | "Credit" | string;

export interface TrialBalanceDetailRow {
  id: string | number;
  date: string;
  description: string;
  reference: string | null;
  debit: number;
  credit: number;
  runningBalance: number;
  runningBalanceType: BalanceType;
}

export interface TrialBalanceDetails {
  accountName: string;
  ledgerCode: string | null;
  from: string;
  to: string;
  openingBalance: number;
  openingBalanceType: BalanceType;
  closingBalance: number;
  closingBalanceType: BalanceType;
  totalDebit: number;
  totalCredit: number;
  rows: TrialBalanceDetailRow[];
}

/** First calendar day of the selected as-at month, without UTC/date-zone conversion. */
export function calendarMonthStart(value: string): string {
  return /^\d{4}-\d{2}-\d{2}$/.test(value) ? `${value.slice(0, 7)}-01` : "";
}

export function trialFilterError(filters: TrialBalanceFilters): string | null {
  if (filters.mode === "asAt") {
    return filters.asAt ? null : "Select an As at date.";
  }

  if (!filters.from || !filters.to) return "Select both a From and a To date.";
  return financeRangeError(filters.from, filters.to);
}

export function applyTrialFilters(filters: TrialBalanceFilters): AppliedTrialBalanceFilters {
  if (filters.mode === "asAt") {
    return {
      projectId: filters.projectId,
      from: calendarMonthStart(filters.asAt),
      to: filters.asAt,
    };
  }

  return { projectId: filters.projectId, from: filters.from, to: filters.to };
}

/**
 * The summary remains a single as-at snapshot. A range only changes the ledger window; its closing
 * date is the summary date. `monthsBack=0` is deliberately fixed so month-history columns can never
 * return to the manager-facing table.
 */
export function trialSummaryParams(filters: AppliedTrialBalanceFilters): URLSearchParams {
  const params = new URLSearchParams({ asAt: filters.to, monthsBack: "0" });
  if (filters.projectId) params.set("projectId", filters.projectId);
  return params;
}

export function trialDetailsParams(
  accountKey: string,
  filters: AppliedTrialBalanceFilters,
): URLSearchParams {
  const params = new URLSearchParams({ accountKey, from: filters.from, to: filters.to });
  if (filters.projectId) params.set("projectId", filters.projectId);
  return params;
}

export function trialFiltersKey(filters: AppliedTrialBalanceFilters): string {
  return JSON.stringify([filters.projectId, filters.from, filters.to]);
}

