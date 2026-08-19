import { api } from "../api/api.ts";
import { financialYearWindow, pakistanToday } from "./financePeriods.ts";

/**
 * Finance chart data derived from the authoritative `/api/Finance/summary` endpoint.
 *
 * Rather than duplicating the revenue/expense aggregation on the backend (which risks
 * drifting from the KPI cards), we call the same summary endpoint the cards use — once
 * per time bucket for the trend, and once per project for the distribution. Every call
 * carries the active project/account filters, so the charts always stay consistent with
 * the KPI totals and respond to every filter change.
 *
 * Note: distribution issues one request per project. That is fine for this app's project
 * count; a system with hundreds of projects should move this to a single GROUP BY endpoint.
 */

export interface ChartBucket {
  label: string;
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
  /**
   * How many of the underlying summary requests failed. A failed slice contributes zero, and zero
   * revenue is a legitimate reading — so the count has to travel with the data and be shown, or a
   * timeout looks exactly like a month in which the business earned nothing.
   */
  failedRequests: number;
}

export type FinancePeriod = "today" | "month" | "year" | "lastYear" | "all" | "custom";

interface ProjectRef {
  id: number;
  projectName: string;
}

interface ChartFilters {
  projectId: string; // "" = all projects
  from: string; // yyyy-mm-dd or ""
  to: string; // yyyy-mm-dd or ""
  account: string; // "" = all accounts
  period: FinancePeriod;
  financialYearStartMonth: number | null;
  projects: ProjectRef[];
}

interface SummaryTotals {
  totalRevenue: number;
  totalExpenses: number;
}

const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

function fmtLocal(d: Date): string {
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

async function fetchSummary(
  filters: Pick<ChartFilters, "projectId" | "account">,
  from: string,
  to: string,
  projectIdOverride?: string,
  signal?: AbortSignal,
): Promise<SummaryTotals> {
  const params = new URLSearchParams();
  const pid = projectIdOverride ?? filters.projectId;
  if (pid) params.set("projectId", pid);
  if (from) params.set("from", from);
  if (to) params.set("to", to);
  if (filters.account) params.set("account", filters.account);

  const res = await api(`/api/Finance/summary?${params.toString()}`, { signal });
  if (!res.ok) throw new Error(`Summary request failed (${res.status}).`);
  const data = await res.json();
  return {
    totalRevenue: Number(data.totalRevenue ?? 0),
    totalExpenses: Number(data.totalExpenses ?? 0),
  };
}

/**
 * Split the active date range into a small number of labelled buckets. The granularity
 * adapts to the span so "This Year" reads as quarters, "This Month" as weeks, etc.
 */
function buildBuckets(
  from: string,
  to: string,
  period: FinancePeriod,
  startMonth: number | null,
): { label: string; from: string; to: string }[] {
  const now = new Date(`${pakistanToday()}T00:00:00`);
  let start: Date;
  let end: Date;

  if (from && to) {
    start = new Date(`${from}T00:00:00`);
    end = new Date(`${to}T00:00:00`);
  } else {
    if (period === "all" || period === "custom") {
      return [{ label: "All time", from: "", to: "" }];
    }
    if (startMonth === null) return [];
    const fiscalYear = financialYearWindow(now, startMonth);
    start = fiscalYear.from;
    end = new Date(fiscalYear.toExclusive.getFullYear(), fiscalYear.toExclusive.getMonth(), 0);
  }

  if (Number.isNaN(start.getTime()) || Number.isNaN(end.getTime()) || end < start) {
    if (startMonth === null) return [];
    const fiscalYear = financialYearWindow(now, startMonth);
    start = fiscalYear.from;
    end = new Date(fiscalYear.toExclusive.getFullYear(), fiscalYear.toExclusive.getMonth(), 0);
  }

  const spanDays = Math.round((end.getTime() - start.getTime()) / 86_400_000);
  const buckets: { label: string; from: string; to: string }[] = [];

  if (spanDays <= 8) {
    // Daily
    for (let d = new Date(start); d <= end; d.setDate(d.getDate() + 1)) {
      const day = new Date(d);
      buckets.push({ label: `${MONTHS[day.getMonth()]} ${day.getDate()}`, from: fmtLocal(day), to: fmtLocal(day) });
    }
  } else if (spanDays <= 45) {
    // Weekly
    let weekStart = new Date(start);
    let idx = 1;
    while (weekStart <= end) {
      const weekEnd = new Date(weekStart);
      weekEnd.setDate(weekEnd.getDate() + 6);
      buckets.push({ label: `Wk ${idx}`, from: fmtLocal(weekStart), to: fmtLocal(weekEnd > end ? end : weekEnd) });
      weekStart = new Date(weekEnd);
      weekStart.setDate(weekStart.getDate() + 1);
      idx += 1;
    }
  } else if (spanDays <= 200) {
    // Monthly
    let m = new Date(start.getFullYear(), start.getMonth(), 1);
    while (m <= end) {
      const mEnd = new Date(m.getFullYear(), m.getMonth() + 1, 0);
      buckets.push({
        label: MONTHS[m.getMonth()],
        from: fmtLocal(m < start ? start : m),
        to: fmtLocal(mEnd > end ? end : mEnd),
      });
      m = new Date(m.getFullYear(), m.getMonth() + 1, 1);
    }
  } else {
    // Quarterly — walk quarter-by-quarter across every year the range covers
    // (a multi-year custom range must not collapse to just the start year).
    const qLabels = ["Jan-Mar", "Apr-Jun", "Jul-Sep", "Oct-Dec"];
    const multiYear = start.getFullYear() !== end.getFullYear();
    const endQuarterIndex = end.getFullYear() * 4 + Math.floor(end.getMonth() / 3);
    let y = start.getFullYear();
    let q = Math.floor(start.getMonth() / 3);
    while (y * 4 + q <= endQuarterIndex) {
      const qStart = new Date(y, q * 3, 1);
      const qEnd = new Date(y, q * 3 + 3, 0);
      buckets.push({
        label: multiYear ? `${qLabels[q]} '${String(y).slice(2)}` : qLabels[q],
        from: fmtLocal(qStart < start ? start : qStart),
        to: fmtLocal(qEnd > end ? end : qEnd),
      });
      q += 1;
      if (q > 3) { q = 0; y += 1; }
    }
  }

  // Safety cap so we never fan out into an unreasonable number of requests.
  if (buckets.length > 12) {
    const step = Math.ceil(buckets.length / 12);
    return buckets.filter((_, i) => i % step === 0);
  }
  return buckets;
}

function buildDistribution(
  totalRevenue: number,
  perProject: { name: string; revenue: number }[],
): DistributionSlice[] {
  const projectRevenue = perProject.reduce((sum, p) => sum + p.revenue, 0);
  const unassigned = Math.max(0, totalRevenue - projectRevenue);

  const slices = perProject.filter((p) => p.revenue > 0);
  if (unassigned > 0.005) slices.push({ name: "General Operations", revenue: unassigned });

  slices.sort((a, b) => b.revenue - a.revenue);

  const denom = totalRevenue > 0 ? totalRevenue : 1;
  const TOP = 4;
  if (slices.length <= TOP) {
    return slices.map((s) => ({ ...s, percent: (s.revenue / denom) * 100 }));
  }

  const top = slices.slice(0, TOP);
  const rest = slices.slice(TOP);
  const restRevenue = rest.reduce((sum, s) => sum + s.revenue, 0);
  const result: DistributionSlice[] = top.map((s) => ({ ...s, percent: (s.revenue / denom) * 100 }));
  if (restRevenue > 0) {
    result.push({ name: "Other Projects", revenue: restRevenue, percent: (restRevenue / denom) * 100 });
  }
  return result;
}

export async function fetchFinanceChartData(
  filters: ChartFilters,
  signal?: AbortSignal,
): Promise<FinanceChartData> {
  const buckets = buildBuckets(filters.from, filters.to, filters.period, filters.financialYearStartMonth);

  // One flaky per-bucket / per-project request must not blank the whole chart, so a failed slice
  // still contributes zero — but it is COUNTED, and the caller shows that the picture is incomplete.
  // Silently drawing a zero bar would state that there was no revenue, which is a different claim
  // from "this could not be loaded". A real abort re-throws so the effect cancels.
  let failedRequests = 0;
  const safeSummary = async (from: string, to: string, projectIdOverride?: string): Promise<SummaryTotals> => {
    try {
      return await fetchSummary(filters, from, to, projectIdOverride, signal);
    } catch (err) {
      if (signal?.aborted) throw err;
      failedRequests += 1;
      return { totalRevenue: 0, totalExpenses: 0 };
    }
  };

  // Time series: one summary call per bucket (carrying project + account filters).
  const seriesPromise = Promise.all(
    buckets.map(async (b) => {
      const totals = await safeSummary(b.from, b.to);
      return { label: b.label, revenue: totals.totalRevenue, expense: totals.totalExpenses };
    }),
  );

  // Distribution: overall total for the range + one call per project.
  const totalPromise = safeSummary(filters.from, filters.to, filters.projectId);

  // When a single project is already selected, distribution is just that project.
  const perProjectPromise = filters.projectId
    ? Promise.resolve<{ name: string; revenue: number }[]>([])
    : Promise.all(
        filters.projects.map(async (p) => {
          const totals = await safeSummary(filters.from, filters.to, String(p.id));
          return { name: p.projectName, revenue: totals.totalRevenue };
        }),
      );

  const [series, total, perProject] = await Promise.all([seriesPromise, totalPromise, perProjectPromise]);

  const distribution = filters.projectId
    ? [{ name: filters.projects.find((p) => String(p.id) === filters.projectId)?.projectName ?? "Selected project", revenue: total.totalRevenue, percent: 100 }]
    : buildDistribution(total.totalRevenue, perProject);

  return { series, distribution, failedRequests };
}
