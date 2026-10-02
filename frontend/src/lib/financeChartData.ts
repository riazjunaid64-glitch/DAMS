import { api } from "../api/api.ts";
import { financeRangeError } from "../components/ui/dateRange.ts";

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
 * The shared From/To check. It used to live here; filter bars use the same function so a range
 * the dashboard would refuse is refused before it is sent, with the same words.
 */
export { financeRangeError };

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
