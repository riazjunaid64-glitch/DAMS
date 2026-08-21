import { api } from "../api/api.ts";

/**
 * The Finance dashboard's data — cards, trend and revenue-by-project — in ONE request to
 * `/api/Finance/dashboard`.
 *
 * It used to be assembled here instead: one `/api/Finance/summary` call per chart bucket, another
 * per project, one for the distribution total and one for the cards. Twelve buckets and ten
 * projects was twenty-four summary requests for a single refresh, and a summary is not one database
 * aggregate but twenty-odd — so the work grew with the client's project list, uncapped, on a screen
 * an admin leaves open all day. The aggregation now happens once, in SQL, on the server.
 *
 * The buckets come back from the server too, built from the same bounds the cards use. That is what
 * stops the chart and the figures above it from describing different periods, and it is why there is
 * no bucket-building code left in the browser.
 */

export interface ChartBucket {
  label: string;
  /** Inclusive bounds of the bar, as `yyyy-mm-ddThh:mm:ss` — every day of the selected range is in
   *  exactly one bucket, so the bars total the cards. */
  from: string;
  to: string;
  revenue: number;
  expense: number;
}

export interface DistributionSlice {
  name: string;
  revenue: number;
  percent: number;
}

export interface FinanceChartData {
  series: ChartBucket[];
  distribution: DistributionSlice[];
}

export interface FinanceDashboardFilters {
  projectId: string; // "" = all projects
  from: string; // yyyy-mm-dd or ""
  to: string; // yyyy-mm-dd or ""
  account: string; // "" = all accounts
}

/**
 * Why a custom range is not usable yet, or null when it is.
 *
 * One end alone used to be accepted by the cards and quietly re-read as "all time" by the chart, and
 * a backwards range was quietly re-read as the financial year — so the screen answered two different
 * questions at once and said nothing about it. A date outside what the database can store, meanwhile,
 * reached the server and came back as a 500. All three are now refused, in the browser and again in
 * the controller — the browser only so the operator gets the specific message instead of a failed
 * request.
 */
/** SQL Server's `datetime` floor, and the server's own lower bound. */
const MIN_FILTER_DATE = "1753-01-01";
/** One day short of the maximum representable date, because every query compares against To + 1 day. */
const MAX_FILTER_DATE = "9999-12-30";

export function financeRangeError(from: string, to: string): string | null {
  if (!from && !to) return null;
  if (!from || !to) return "Enter both a From and a To date, or clear them both.";
  for (const [value, label] of [[from, "From date"], [to, "To date"]] as const) {
    if (value < MIN_FILTER_DATE) return `${label} cannot be before 01 Jan 1753.`;
    if (value > MAX_FILTER_DATE) return `${label} cannot be after 30 Dec 9999.`;
  }
  if (from > to) return "From date cannot be after To date.";
  return null;
}

export async function fetchFinanceDashboard<TSummary>(
  filters: FinanceDashboardFilters,
  signal?: AbortSignal,
): Promise<{ summary: TSummary; charts: FinanceChartData }> {
  const invalid = financeRangeError(filters.from, filters.to);
  if (invalid) throw new Error(invalid);

  const params = new URLSearchParams();
  if (filters.projectId) params.set("projectId", filters.projectId);
  if (filters.from) params.set("from", filters.from);
  if (filters.to) params.set("to", filters.to);
  if (filters.account) params.set("account", filters.account);

  const query = params.toString();
  const res = await api(`/api/Finance/dashboard${query ? `?${query}` : ""}`, { signal });
  if (!res.ok) {
    const body = (await res.json().catch(() => null)) as { message?: string } | null;
    throw new Error(body?.message ?? `Dashboard request failed (${res.status}).`);
  }
  const data = await res.json();
  return {
    summary: data.summary as TSummary,
    charts: {
      series: Array.isArray(data.trend) ? (data.trend as ChartBucket[]) : [],
      distribution: Array.isArray(data.distribution) ? (data.distribution as DistributionSlice[]) : [],
    },
  };
}
