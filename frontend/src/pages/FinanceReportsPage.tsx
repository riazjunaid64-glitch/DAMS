import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { api } from "../api/api.ts";
import type { User } from "../App.tsx";
import {
  Button,
  FilterBar,
  IconDownload,
  Notice,
  PageHeader,
  Tabs,
  useIsPhone,
  useToast,
  type FilterDef,
  type FilterValues,
  type TabItem,
} from "../components/ui";
import { pageAccess } from "../features/access/permissions.ts";
import { useProjects } from "../contexts/projectsContextValue.ts";
import { BalanceSheetReport } from "../features/finance/reports/BalanceSheetReport.tsx";
import { ProfitLossReport } from "../features/finance/reports/ProfitLossReport.tsx";
import { TrialBalanceReport, type LoadedTrial } from "../features/finance/reports/TrialBalanceReport.tsx";
import type { BalanceSheet, ProfitLoss } from "../features/finance/reports/types.ts";
import {
  applyTrialFilters,
  calendarMonthStart,
  trialFilterError,
  trialFiltersKey,
  trialSummaryParams,
  type AppliedTrialBalanceFilters,
  type TrialBalanceFilters,
  type TrialBalanceReport as TrialBalanceData,
  type TrialDateMode,
} from "../features/finance/trialBalance.ts";
import { useFinancialYearStartMonth } from "../features/finance/useFinancialYearStartMonth.ts";
import { buildPeriodRange, pakistanToday } from "../lib/financePeriods.ts";

type Tab = "pnl" | "trial" | "balance";

type Loaded =
  | { tab: "pnl"; data: ProfitLoss }
  | { tab: "trial"; data: LoadedTrial }
  | { tab: "balance"; data: BalanceSheet };

type ReportRequest = { tab: Tab; params: URLSearchParams; filters?: AppliedTrialBalanceFilters };

const TABS: readonly TabItem[] = [
  { id: "pnl", label: "Profit & loss" },
  { id: "trial", label: "Trial balance" },
  { id: "balance", label: "Balance sheet" },
];

const ENDPOINT: Record<Tab, string> = { pnl: "profit-and-loss", trial: "trial-balance", balance: "balance-sheet" };
const LOAD_ERROR: Record<Tab, string> = {
  pnl: "Profit and loss could not be loaded.",
  trial: "Trial balance could not be loaded.",
  balance: "Balance sheet could not be loaded.",
};

/** Report filters can be expensive: quick edits are coalesced, and a newer one cancels the call before it. */
const DEBOUNCE_MS = 250;

async function serverMessage(response: Response, fallback: string): Promise<string> {
  const body = await response.json().catch(() => null) as { message?: unknown } | null;
  return typeof body?.message === "string" && body.message.trim() ? body.message : fallback;
}

export default function FinanceReportsPage({ user }: { user: User | null }) {
  const access = pageAccess(user?.role, "finance");
  const allowed = access === "allow";
  const navigate = useNavigate();
  const isPhone = useIsPhone();
  const toast = useToast();
  const { projects } = useProjects();
  const { startMonth, failed: yearFailed } = useFinancialYearStartMonth(allowed);
  const yearStatus = yearFailed ? "error" : startMonth === null ? "loading" : "ready";
  const today = pakistanToday();

  const [tab, setTab] = useState<Tab>("pnl");
  const [projectId, setProjectId] = useState("");
  // null follows the default (this financial year) once the year start is known.
  const [pnlRange, setPnlRange] = useState<{ from: string; to: string } | null>(null);
  const [asAt, setAsAt] = useState(today);
  const [trialMode, setTrialMode] = useState<TrialDateMode>("asAt");
  const [trialFrom, setTrialFrom] = useState(calendarMonthStart(today));
  const [trialTo, setTrialTo] = useState(today);
  const [trialRangeError, setTrialRangeError] = useState<string | null>(null);

  const [loaded, setLoaded] = useState<Loaded | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);
  const [exporting, setExporting] = useState(false);
  const requestId = useRef(0);

  useEffect(() => {
    if (access === "deny") navigate("/");
  }, [access, navigate]);

  // While the year start is unknown both dates stay empty and the server uses the current financial year.
  const defaultPnl = useMemo(() => buildPeriodRange("year", startMonth), [startMonth]);
  const pnl = pnlRange ?? defaultPnl;
  const defaultTrialFrom = calendarMonthStart(today);

  const request = useMemo<ReportRequest | null>(() => {
    const params = new URLSearchParams();
    if (tab === "trial") {
      const draft: TrialBalanceFilters = { mode: trialMode, projectId, asAt, from: trialFrom, to: trialTo };
      if (trialFilterError(draft)) return null;
      const filters = applyTrialFilters(draft);
      return { tab, params: trialSummaryParams(filters), filters };
    }
    if (projectId) params.set("projectId", projectId);
    if (tab === "pnl") {
      if (pnl.from) params.set("from", pnl.from);
      if (pnl.to) params.set("to", pnl.to);
    } else {
      if (!asAt) return null;
      params.set("asAt", asAt);
    }
    return { tab, params };
  }, [tab, projectId, pnl.from, pnl.to, asAt, trialMode, trialFrom, trialTo]);

  useEffect(() => {
    if (!allowed) return;
    if (!request) {
      setLoading(false);
      return;
    }
    // Invalidate now rather than after the debounce, so an answer for the filters the user just left
    // cannot clear the loading state or paint over the new ones.
    const ticket = ++requestId.current;
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    const timer = window.setTimeout(async () => {
      try {
        const response = await api(`/api/Finance/${ENDPOINT[request.tab]}?${request.params}`, { signal: controller.signal });
        if (!response.ok) throw new Error(await serverMessage(response, LOAD_ERROR[request.tab]));
        const body = await response.json();
        if (controller.signal.aborted || ticket !== requestId.current) return;
        if (request.tab === "trial" && request.filters) {
          setLoaded({ tab: "trial", data: { report: body as TrialBalanceData, filters: request.filters } });
        } else if (request.tab === "pnl") {
          setLoaded({ tab: "pnl", data: body as ProfitLoss });
        } else {
          setLoaded({ tab: "balance", data: body as BalanceSheet });
        }
      } catch (caught) {
        if (controller.signal.aborted || ticket !== requestId.current) return;
        setLoaded(null);
        setError(caught instanceof Error && caught.message.trim() ? caught.message : LOAD_ERROR[request.tab]);
      } finally {
        if (ticket === requestId.current) setLoading(false);
      }
    }, DEBOUNCE_MS);
    return () => {
      window.clearTimeout(timer);
      controller.abort();
    };
  }, [allowed, request, attempt]);

  const exportReport = async () => {
    if (exporting || !request) return;
    setExporting(true);
    try {
      const params = new URLSearchParams(request.params);
      params.set("format", "xlsx");
      const endpoint = ENDPOINT[request.tab];
      const response = await api(`/api/Finance/${endpoint}/export?${params}`);
      if (!response.ok) throw new Error(await serverMessage(response, "Export failed."));
      const url = URL.createObjectURL(await response.blob());
      try {
        const link = document.createElement("a");
        link.href = url;
        link.download = `${endpoint}-${pakistanToday()}.xlsx`;
        document.body.appendChild(link);
        link.click();
        link.remove();
      } finally {
        URL.revokeObjectURL(url);
      }
    } catch (caught) {
      toast.error(caught instanceof Error && caught.message.trim() ? caught.message : "Export failed.");
    } finally {
      setExporting(false);
    }
  };

  const onTab = (id: string) => {
    const next = id as Tab;
    // Balance sheet accepts a later date than the Trial balance does; the date is shared between them.
    if (next === "trial" && asAt > today) setAsAt(today);
    setTab(next);
  };

  const rangeFor = useCallback((preset: Parameters<typeof buildPeriodRange>[0]) => buildPeriodRange(preset, startMonth), [startMonth]);

  const projectFilter: FilterDef = {
    type: "select",
    key: "project",
    label: "Project",
    allLabel: "All",
    options: projects.map((project) => ({ value: String(project.id), label: project.projectName })),
  };

  let filters: FilterDef[];
  let values: FilterValues;
  let defaults: FilterValues;
  if (tab === "pnl") {
    filters = [
      projectFilter,
      { type: "period", key: "period", fromKey: "pnlFrom", toKey: "pnlTo", rangeFor, financialYear: yearStatus, presets: ["year", "lastYear"], customLabel: "Custom dates", monthsInLabel: false, emptyPreset: "year" },
      { type: "dateRange", fromKey: "pnlFrom", toKey: "pnlTo" },
    ];
    values = { project: projectId, pnlFrom: pnl.from, pnlTo: pnl.to };
    defaults = { project: "", pnlFrom: defaultPnl.from, pnlTo: defaultPnl.to };
  } else if (tab === "trial") {
    filters = [
      projectFilter,
      { type: "select", key: "mode", label: "Show", allLabel: "As at", options: [{ value: "range", label: "Date range" }] },
      { type: "date", key: "asAt", label: "As at", max: today, required: true, when: (v) => v.mode !== "range" },
      { type: "dateRange", fromKey: "trialFrom", toKey: "trialTo", max: today, required: true, when: (v) => v.mode === "range" },
    ];
    values = { project: projectId, mode: trialMode === "range" ? "range" : "", asAt, trialFrom, trialTo };
    defaults = { project: "", mode: "", asAt: today, trialFrom: defaultTrialFrom, trialTo: today };
  } else {
    filters = [projectFilter, { type: "date", key: "asAt", label: "As at", required: true }];
    values = { project: projectId, asAt };
    defaults = { project: "", asAt: today };
  }

  const onFilterChange = (changes: FilterValues) => {
    if ("project" in changes) setProjectId(changes.project ?? "");
    if ("pnlFrom" in changes || "pnlTo" in changes) setPnlRange({ from: changes.pnlFrom ?? pnl.from, to: changes.pnlTo ?? pnl.to });
    if ("mode" in changes) setTrialMode(changes.mode === "range" ? "range" : "asAt");
    if ("asAt" in changes) setAsAt(changes.asAt ?? "");
    if ("trialFrom" in changes) setTrialFrom(changes.trialFrom ?? "");
    if ("trialTo" in changes) setTrialTo(changes.trialTo ?? "");
  };

  const onReset = () => {
    setProjectId("");
    if (tab === "pnl") {
      setPnlRange(null);
    } else if (tab === "trial") {
      setTrialMode("asAt");
      setAsAt(today);
      setTrialFrom(defaultTrialFrom);
      setTrialTo(today);
    } else {
      setAsAt(today);
    }
  };

  if (!allowed) {
    return access === "deny" ? null : (
      <div role="status" aria-busy="true" className="mx-auto flex w-full max-w-[1500px] flex-col gap-4 px-4 py-5 md:px-8 md:py-7">
        <span className="sr-only">Loading</span>
        <span aria-hidden="true" className="h-9 w-64 animate-pulse rounded bg-track" />
        <span aria-hidden="true" className="h-64 animate-pulse rounded-card bg-track" />
      </div>
    );
  }

  const exportDisabled = !request || (tab === "trial" && trialRangeError !== null);
  const shown = loaded?.tab === tab ? loaded : null;

  return (
    <div className="mx-auto flex w-full max-w-[1500px] flex-col gap-4 px-4 py-5 md:gap-5 md:px-8 md:py-7">
      <PageHeader
        title="Financial reports"
        className="max-md:flex-row max-md:items-center max-md:justify-between"
        actions={isPhone ? (
          <Button iconOnly variant="outline" icon={<IconDownload size={18} />} aria-label="Export to Excel" loading={exporting} disabled={exportDisabled} onClick={() => void exportReport()} />
        ) : (
          <Button icon={<IconDownload size={16} />} loading={exporting} disabled={exportDisabled} onClick={() => void exportReport()}>
            {exporting ? "Exporting…" : "Export to Excel"}
          </Button>
        )}
      />
      <Tabs items={TABS} value={tab} onChange={onTab} aria-label="Financial report type" className="md:self-start" />
      <FilterBar
        key={tab}
        filters={filters}
        values={values}
        defaults={defaults}
        onChange={onFilterChange}
        onReset={onReset}
        onRangeError={setTrialRangeError}
      />
      {tab === "pnl" && yearFailed && (
        <Notice tone="gold" role="alert" title="Financial year setting unavailable — use a custom From/To range." />
      )}
      {error ? (
        <Notice tone="red" role="alert" title={error} action={<Button variant="outline" onClick={() => setAttempt((value) => value + 1)}>Try again</Button>} />
      ) : tab === "pnl" ? (
        <ProfitLossReport report={shown?.tab === "pnl" ? shown.data : null} loading={loading} />
      ) : tab === "trial" ? (
        <TrialBalanceReport
          // The current filters, not the loaded ones: Details closes the moment a filter changes,
          // while the old rows stay on screen until the new report arrives.
          key={request?.filters ? trialFiltersKey(request.filters) : "none"}
          loaded={shown?.tab === "trial" ? shown.data : null}
          loading={loading}
        />
      ) : (
        <BalanceSheetReport report={shown?.tab === "balance" ? shown.data : null} loading={loading} />
      )}
    </div>
  );
}
