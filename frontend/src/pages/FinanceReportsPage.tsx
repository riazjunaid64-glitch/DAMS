import { useCallback, useEffect, useId, useRef, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import type { User } from "../App";
import { api } from "../api/api";
import { useProjects } from "../contexts/projectsContextValue";
import {
  applyTrialFilters,
  calendarMonthStart,
  trialDetailsParams,
  trialFilterError,
  trialFiltersKey,
  trialSummaryParams,
  type AppliedTrialBalanceFilters,
  type BalanceType,
  type TrialBalanceDetails,
  type TrialBalanceFilters,
  type TrialBalanceReport,
  type TrialBalanceRow,
  type TrialDateMode,
} from "../features/finance/trialBalance.ts";
import { useFinancialYearStartMonth } from "../features/finance/useFinancialYearStartMonth";
import Button from "../lib/Button";
import Container from "../lib/Container";
import { buildPeriodRange, financePeriodLabel, pakistanToday } from "../lib/financePeriods";
import Modal from "../lib/Modal.tsx";

type Tab = "pnl" | "trial" | "balance";
type PnlLine = { categoryId: number | null; name: string; amount: number; priorAmount: number | null; transactionCount: number };
type Pnl = { periodStart: string; periodEnd: string; periodLabel: string; projectName: string | null; incomeLines: PnlLine[]; totalIncome: number; expenseLines: PnlLine[]; totalExpenses: number; netProfit: number; priorTotalIncome: number; priorTotalExpenses: number; priorNetProfit: number };
type BsLine = { accountId: number; ledgerCode: string | null; name: string; amount: number };
type BsGroup = { name: string; lines: BsLine[]; total: number };
type BalanceSheet = { asAt: string; assetGroups: BsGroup[]; totalAssets: number; liabilityGroups: BsGroup[]; totalLiabilities: number; capitalLines: BsLine[]; retainedProfit: number; unpostedFixedAssetCharge: number; retainedProfitStart: string | null; totalCapital: number; totalLiabilitiesAndCapital: number; isBalanced: boolean; imbalance: number; unbalancedAccounts: string[] };
type LoadedTrial = { report: TrialBalanceReport; filters: AppliedTrialBalanceFilters };

const money = (value: number) => `Rs ${value.toLocaleString("en-PK", { maximumFractionDigits: 2 })}`;

export default function FinanceReportsPage({ user }: { user: User | null }) {
  const navigate = useNavigate();
  const { projects } = useProjects();
  const [tab, setTab] = useState<Tab>("pnl");
  const [projectId, setProjectId] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [asAt, setAsAt] = useState(pakistanToday());
  const [trialDateMode, setTrialDateMode] = useState<TrialDateMode>("asAt");
  const [trialFrom, setTrialFrom] = useState(calendarMonthStart(pakistanToday()));
  const [trialTo, setTrialTo] = useState(pakistanToday());
  // null until read back — a P&L preset must not name a financial year the client has not set.
  const { startMonth, failed: startMonthFailed } = useFinancialYearStartMonth(user?.role === "Admin");
  const [pnl, setPnl] = useState<Pnl | null>(null);
  const [trial, setTrial] = useState<LoadedTrial | null>(null);
  const [balance, setBalance] = useState<BalanceSheet | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [exporting, setExporting] = useState(false);
  const reportRequestId = useRef(0);
  const reportController = useRef<AbortController | null>(null);
  const reportDebounceTimer = useRef<number | null>(null);

  useEffect(() => {
    if (user?.role !== "Admin") { navigate("/"); return; }
  }, [user, navigate]);

  const query = useCallback((includePeriod: boolean) => {
    const params = new URLSearchParams();
    if (projectId) params.set("projectId", projectId);
    if (includePeriod) {
      if (from) params.set("from", from);
      if (to) params.set("to", to);
    } else {
      params.set("asAt", asAt);
    }
    return params;
  }, [projectId, from, to, asAt]);

  const currentTrialFilters = useCallback((): TrialBalanceFilters => ({
    mode: trialDateMode,
    projectId,
    asAt,
    from: trialFrom,
    to: trialTo,
  }), [trialDateMode, projectId, asAt, trialFrom, trialTo]);

  const load = useCallback(async () => {
    if (user?.role !== "Admin") return;
    reportController.current?.abort();
    const controller = new AbortController();
    reportController.current = controller;
    const requestId = ++reportRequestId.current;
    setLoading(true); setError(null);
    try {
      if (tab === "pnl") {
        const response = await api(`/api/Finance/profit-and-loss?${query(true)}`, { signal: controller.signal });
        if (!response.ok) throw new Error((await response.json().catch(() => null))?.message ?? "Profit and loss could not be loaded.");
        const report = await response.json() as Pnl;
        if (controller.signal.aborted || requestId !== reportRequestId.current) return;
        setPnl(report);
      } else if (tab === "trial") {
        setTrial(null);
        const draft = currentTrialFilters();
        const filterError = trialFilterError(draft);
        if (filterError) throw new Error(filterError);
        const applied = applyTrialFilters(draft);
        const params = trialSummaryParams(applied);
        const response = await api(`/api/Finance/trial-balance?${params}`, { signal: controller.signal });
        if (!response.ok) throw new Error((await response.json().catch(() => null))?.message ?? "Trial balance could not be loaded.");
        const report = await response.json() as TrialBalanceReport;
        if (controller.signal.aborted || requestId !== reportRequestId.current) return;
        setTrial({ report, filters: applied });
      } else {
        const response = await api(`/api/Finance/balance-sheet?${query(false)}`, { signal: controller.signal });
        if (!response.ok) throw new Error((await response.json().catch(() => null))?.message ?? "Balance sheet could not be loaded.");
        const report = await response.json() as BalanceSheet;
        if (controller.signal.aborted || requestId !== reportRequestId.current) return;
        setBalance(report);
      }
    } catch (caught) {
      if (controller.signal.aborted || requestId !== reportRequestId.current) return;
      setError(caught instanceof Error ? caught.message : "The report could not be loaded.");
    } finally {
      if (requestId === reportRequestId.current) setLoading(false);
      if (reportController.current === controller) reportController.current = null;
    }
  }, [tab, query, currentTrialFilters, user]);

  // Report filters can be expensive. Coalesce quick edits and cancel the previous SQL request as
  // soon as a newer filter/tab supersedes it; the request id remains a second correctness guard.
  useEffect(() => {
    if (user?.role !== "Admin") return;
    // Invalidate immediately rather than waiting for the debounce timer, so an abort that settles
    // during those 250 ms cannot clear the loading state or paint the previous filter's report.
    reportController.current?.abort();
    reportController.current = null;
    reportRequestId.current += 1;
    setLoading(true);
    setError(null);
    const timer = window.setTimeout(() => {
      if (reportDebounceTimer.current === timer) reportDebounceTimer.current = null;
      void load();
    }, 250);
    reportDebounceTimer.current = timer;
    return () => {
      window.clearTimeout(timer);
      if (reportDebounceTimer.current === timer) reportDebounceTimer.current = null;
      reportController.current?.abort();
    };
  }, [load, user]);

  const refreshReport = () => {
    if (reportDebounceTimer.current !== null) {
      window.clearTimeout(reportDebounceTimer.current);
      reportDebounceTimer.current = null;
    }
    void load();
  };

  const applyYearPreset = (preset: "year" | "lastYear") => {
    const range = buildPeriodRange(preset, startMonth);
    setFrom(range.from); setTo(range.to);
  };

  const exportReport = async () => {
    if (exporting) return;
    setExporting(true); setError(null);
    try {
      let params: URLSearchParams;
      if (tab === "trial") {
        const draft = currentTrialFilters();
        const filterError = trialFilterError(draft);
        if (filterError) throw new Error(filterError);
        params = trialSummaryParams(applyTrialFilters(draft));
      } else {
        params = query(tab === "pnl");
      }
      params.set("format", "xlsx");
      const endpoint = tab === "pnl" ? "profit-and-loss" : tab === "trial" ? "trial-balance" : "balance-sheet";
      const response = await api(`/api/Finance/${endpoint}/export?${params}`);
      if (!response.ok) throw new Error((await response.json().catch(() => null))?.message ?? "Export failed.");
      const blob = await response.blob();
      const url = URL.createObjectURL(blob);
      try {
        const link = document.createElement("a"); link.href = url;
        link.download = `${endpoint}-${pakistanToday()}.xlsx`; document.body.appendChild(link); link.click(); link.remove();
      } finally { URL.revokeObjectURL(url); }
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Export failed.");
    } finally { setExporting(false); }
  };

  if (user?.role !== "Admin") return null;
  const currentTrialError = trialFilterError(currentTrialFilters());
  return <Container className="py-8">
    <div className="mb-6 flex flex-wrap items-end justify-between gap-4">
      <div><Link to="/finance" className="text-sm text-[var(--accent)]">← Finance dashboard</Link><h1 className="mt-1 text-3xl font-bold text-[var(--text-heading)]">Financial Reports</h1><p className="text-sm text-[var(--text-muted)]">Accrual performance and balanced account statements from one ledger.</p></div>
      <div className="flex gap-2"><Link to="/finance/partners"><Button variant="outline">Capital Partners</Button></Link><Button disabled={exporting || (tab === "trial" && currentTrialError !== null)} onClick={() => void exportReport()}>{exporting ? "Exporting…" : "Export XLSX"}</Button></div>
    </div>
    <div className="mb-5 flex flex-wrap gap-2" aria-label="Financial report type">{([ ["pnl", "Profit & Loss"], ["trial", "Trial Balance"], ["balance", "Balance Sheet"] ] as [Tab,string][]).map(([id,label]) => <button type="button" aria-pressed={tab === id} key={id} onClick={() => setTab(id)} className={`rounded-full px-4 py-2 text-sm font-semibold focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--accent)] ${tab === id ? "bg-[var(--accent)] text-[var(--btn-primary-text)]" : "border border-[var(--border)] bg-[var(--surface)]"}`}>{label}</button>)}</div>
    <div className="mb-5 flex flex-wrap items-end gap-3 rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-4">
      <label className="text-xs text-[var(--text-muted)]">Project<select className="mt-1 block rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-3 py-2 text-sm" value={projectId} onChange={(event) => setProjectId(event.target.value)}><option value="">All projects</option>{projects.map((project) => <option key={project.id} value={project.id}>{project.projectName}</option>)}</select></label>
      {tab === "pnl" ? <>
        {/* Disabled until the configured year start is known: the button both names a range and
            applies it, so an unresolved setting would make it wrong on both counts. */}
        <Button variant="outline" disabled={startMonth === null} onClick={() => applyYearPreset("year")}>{financePeriodLabel("year", startMonth)}</Button>
        <Button variant="outline" disabled={startMonth === null} onClick={() => applyYearPreset("lastYear")}>{financePeriodLabel("lastYear", startMonth)}</Button>
        {startMonthFailed && <p role="alert" className="text-xs text-amber-300">Financial year setting unavailable — use From/To.</p>}
        <DateField label="From" value={from} onChange={setFrom}/><DateField label="To" value={to} onChange={setTo}/>
      </> : tab === "trial" ? <>
        <TrialDateModeField value={trialDateMode} onChange={setTrialDateMode}/>
        {trialDateMode === "asAt"
          ? <DateField label="As at" value={asAt} onChange={setAsAt} required/>
          : <><DateField label="From" value={trialFrom} onChange={setTrialFrom} required/><DateField label="To" value={trialTo} onChange={setTrialTo} required/></>}
      </> : <DateField label="As at" value={asAt} onChange={setAsAt}/>}
      <Button disabled={tab === "trial" && currentTrialError !== null} onClick={refreshReport}>Refresh</Button>
      {tab === "trial" && currentTrialError && <p role="alert" className="w-full text-xs text-amber-300">{currentTrialError}</p>}
    </div>
    {error && <p role="alert" className="mb-4 rounded-xl border border-rose-500/30 bg-rose-500/10 p-4 text-sm text-rose-300">{error}</p>}
    {loading ? <p role="status" className="py-20 text-center text-[var(--text-muted)]">Loading report…</p> : tab === "pnl" && pnl ? <PnlView report={pnl}/> : tab === "trial" && trial ? <TrialView key={trialFiltersKey(trial.filters)} report={trial.report} filters={trial.filters}/> : tab === "balance" && balance ? <BalanceView report={balance}/> : null}
  </Container>;
}

function PnlView({ report }: { report: Pnl }) {
  return <ReportCard title={`Profit & Loss · ${report.periodLabel}`}>
    <PnlSummary report={report}/>
    
  </ReportCard>;
}
// The three figures the sheet exists to answer, and now the whole of it. They come straight off the
// report the server sent — this states them, it does not compute anything. The per-head breakdown
// and the prior-year column still come back in that response; they are simply no longer shown.
function PnlSummary({report}:{report:Pnl}){
  const rows = [
    {label:"Total Revenue", value:report.totalIncome, tone:"text-emerald-400"},
    {label:"Total Expenses", value:report.totalExpenses, tone:"text-rose-400"},
    {label:"Net Profit", value:report.netProfit, tone:report.netProfit >= 0 ? "text-sky-400" : "text-rose-400"},
  ];
  return <div className="mb-6 overflow-hidden rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)]">
    {rows.map((row,index)=><div key={row.label} className={`flex items-center justify-between gap-4 px-5 py-4 ${index ? "border-t border-[var(--border)]" : ""}`}>
      <span className="text-sm font-medium text-[var(--text-primary)]">{row.label}</span>
      <span className={`text-lg font-bold tabular-nums ${row.tone}`}>{money(row.value)}</span>
    </div>)}
  </div>;
}

function TrialView({ report, filters }: { report: TrialBalanceReport; filters: AppliedTrialBalanceFilters }) {
  const [selected, setSelected] = useState<TrialBalanceRow | null>(null);
  const columnIndex = Math.max(0, report.columnDates.length - 1);
  const totalDebit = report.columnDebitTotals[columnIndex] ?? 0;
  const totalCredit = report.columnCreditTotals[columnIndex] ?? 0;
  const difference = Math.abs(totalDebit - totalCredit);
  const balanced = report.columnBalanced[columnIndex] ?? difference < 0.005;

  return <>
    <ReportCard title="Trial Balance">
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <p className="text-sm text-[var(--text-muted)]">Closing balances as at <strong className="text-[var(--text-primary)]">{formatReportDate(filters.to)}</strong></p>
        <p role="status" className={`rounded-full px-3 py-1 text-xs font-semibold ${balanced ? "bg-emerald-500/10 text-emerald-300" : "bg-rose-500/10 text-rose-300"}`}>
          {balanced ? "Balanced" : `Out of balance by ${money(difference)}`}
        </p>
      </div>
      <div className="overflow-x-auto rounded-xl border border-[var(--border)]">
        <table className="data-table w-full min-w-[560px] text-sm">
          <caption className="sr-only">Trial Balance account summary as at {formatReportDate(filters.to)}</caption>
          <thead><tr><th scope="col" className="p-3 text-left sm:p-4">Account Name</th><th scope="col" className="p-3 text-right sm:p-4">Debit</th><th scope="col" className="p-3 text-right sm:p-4">Credit</th><th scope="col" className="p-3 text-center sm:p-4">Details</th></tr></thead>
          <tbody>
            {report.rows.map((row) => <tr key={row.accountKey}>
              <th scope="row" className="max-w-sm p-3 text-left font-semibold text-[var(--text-heading)] sm:p-4">{row.accountName}</th>
              <td className="p-3 text-right tabular-nums sm:p-4">{moneyOrDash(row.debitBalances[columnIndex] ?? 0)}</td>
              <td className="p-3 text-right tabular-nums sm:p-4">{moneyOrDash(row.creditBalances[columnIndex] ?? 0)}</td>
              <td className="p-3 text-center sm:p-4"><Button variant="outline" size="sm" aria-label={`View details for ${row.accountName}`} onClick={() => setSelected(row)}>Details</Button></td>
            </tr>)}
            {!report.rows.length && <tr><td colSpan={4} className="p-10 text-center text-[var(--text-muted)]">No account balances were found for these filters.</td></tr>}
          </tbody>
          <tfoot><tr className="border-t-2 border-[var(--border)] bg-[var(--surface-glass)] font-bold">
            <th scope="row" className="p-3 text-left sm:p-4">Total</th>
            <td className="p-3 text-right tabular-nums text-[var(--accent-light)] sm:p-4">{money(totalDebit)}</td>
            <td className="p-3 text-right tabular-nums text-[var(--accent-light)] sm:p-4">{money(totalCredit)}</td>
            <td className="p-3 sm:p-4"><span className="sr-only">{balanced ? "Debit and credit totals are balanced." : `Difference ${money(difference)}.`}</span></td>
          </tr></tfoot>
        </table>
      </div>
    </ReportCard>
    {selected && <TrialBalanceDetailsModal account={selected} filters={filters} onClose={() => setSelected(null)}/>}
  </>;
}

function TrialBalanceDetailsModal({ account, filters, onClose }: { account: TrialBalanceRow; filters: AppliedTrialBalanceFilters; onClose: () => void }) {
  const headingId = useId();
  const dialogRef = useRef<HTMLElement>(null);
  const returnFocusRef = useRef<HTMLElement | null>(null);
  const [details, setDetails] = useState<TrialBalanceDetails | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    returnFocusRef.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    const frame = window.requestAnimationFrame(() => dialogRef.current?.focus());
    return () => {
      window.cancelAnimationFrame(frame);
      document.body.style.overflow = previousOverflow;
      returnFocusRef.current?.focus();
    };
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true); setError(null); setDetails(null);
    const loadDetails = async () => {
      try {
        const params = trialDetailsParams(account.accountKey, filters);
        const response = await api(`/api/Finance/trial-balance/details?${params}`, { signal: controller.signal });
        if (!response.ok) throw new Error((await response.json().catch(() => null))?.message ?? "Account details could not be loaded.");
        const result = await response.json() as TrialBalanceDetails;
        if (!controller.signal.aborted) setDetails({ ...result, rows: Array.isArray(result.rows) ? result.rows : [] });
      } catch (caught) {
        if (!controller.signal.aborted) setError(caught instanceof Error ? caught.message : "Account details could not be loaded.");
      } finally {
        if (!controller.signal.aborted) setLoading(false);
      }
    };
    void loadDetails();
    return () => controller.abort();
  }, [account.accountKey, filters, attempt]);

  const handleKeyDown = (event: React.KeyboardEvent<HTMLElement>) => {
    if (event.key === "Escape") { event.preventDefault(); onClose(); return; }
    if (event.key !== "Tab") return;
    const controls = Array.from(event.currentTarget.querySelectorAll<HTMLElement>('button:not([disabled]), a[href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'))
      .filter((element) => element.offsetParent !== null);
    if (!controls.length) { event.preventDefault(); return; }
    const first = controls[0];
    const last = controls[controls.length - 1];
    if (event.shiftKey && (document.activeElement === first || document.activeElement === event.currentTarget)) { event.preventDefault(); last.focus(); }
    else if (!event.shiftKey && document.activeElement === event.currentTarget) { event.preventDefault(); first.focus(); }
    else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
  };

  return <Modal open onClose={onClose} align="top">
    <section ref={dialogRef} tabIndex={-1} onKeyDown={handleKeyDown} role="dialog" aria-modal="true" aria-labelledby={headingId} className="relative my-2 flex max-h-[calc(100dvh-2rem)] w-full max-w-6xl flex-col overflow-hidden rounded-2xl border border-[var(--border)] bg-[var(--modal-bg)] shadow-2xl sm:my-6">
      <header className="flex shrink-0 items-start justify-between gap-4 border-b border-[var(--border)] p-4 sm:p-6">
        <div><h2 id={headingId} className="text-lg font-bold text-[var(--text-heading)] sm:text-xl">{details?.accountName ?? account.accountName} — Account Details</h2>{(details?.ledgerCode ?? account.ledgerCode) && <p className="mt-1 text-xs text-[var(--text-muted)]">Ledger {(details?.ledgerCode ?? account.ledgerCode)}</p>}</div>
        <button type="button" aria-label="Close account details" onClick={onClose} className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg text-xl text-[var(--text-muted)] hover:bg-[var(--surface-glass-hover)] hover:text-[var(--text-primary)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--accent)]">×</button>
      </header>
      <div className="min-h-0 flex-1 overflow-y-auto p-4 sm:p-6">
        {loading && <p role="status" className="py-16 text-center text-sm text-[var(--text-muted)]">Loading account details…</p>}
        {!loading && error && <div role="alert" className="rounded-xl border border-rose-500/30 bg-rose-500/10 p-5 text-sm text-rose-200"><p>{error}</p><Button className="mt-4" variant="outline" size="sm" onClick={() => setAttempt((value) => value + 1)}>Try again</Button></div>}
        {!loading && details && <>
          <div className="mb-5 grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
            <DetailMetric label="From" value={formatReportDate(details.from)}/>
            <DetailMetric label="To" value={formatReportDate(details.to)}/>
            <DetailMetric label="Opening Balance" value={formatBalance(details.openingBalance, details.openingBalanceType)}/>
            <DetailMetric label="Closing Balance" value={formatBalance(details.closingBalance, details.closingBalanceType)}/>
          </div>
          <div className="overflow-x-auto rounded-xl border border-[var(--border)]">
            <table className="data-table w-full min-w-[900px] text-sm">
              <caption className="sr-only">Ledger entries for {details.accountName}, {formatReportDate(details.from)} to {formatReportDate(details.to)}</caption>
              <thead><tr><th scope="col" className="p-3 text-left">Date</th><th scope="col" className="p-3 text-left">Description</th><th scope="col" className="p-3 text-left">Reference</th><th scope="col" className="p-3 text-right">Debit</th><th scope="col" className="p-3 text-right">Credit</th><th scope="col" className="p-3 text-right">Running Balance</th></tr></thead>
              <tbody>
                {/* Server order is ledger order. Do not group same-date entries: each posting stays visible. */}
                {details.rows.map((entry, index) => <tr key={`${entry.id}-${index}`}>
                  <td className="whitespace-nowrap p-3">{formatReportDate(entry.date)}</td>
                  <td className="max-w-sm whitespace-normal p-3 font-medium text-[var(--text-heading)]">{entry.description || "—"}</td>
                  <td className="max-w-xs whitespace-normal p-3 text-[var(--text-muted)]">{entry.reference || "—"}</td>
                  <td className="p-3 text-right tabular-nums">{moneyOrDash(entry.debit)}</td>
                  <td className="p-3 text-right tabular-nums">{moneyOrDash(entry.credit)}</td>
                  <td className="whitespace-nowrap p-3 text-right font-semibold tabular-nums">{formatBalance(entry.runningBalance, entry.runningBalanceType)}</td>
                </tr>)}
                {!details.rows.length && <tr><td colSpan={6} className="p-10 text-center text-[var(--text-muted)]">No transactions were found in this period.</td></tr>}
              </tbody>
              <tfoot><tr className="border-t-2 border-[var(--border)] bg-[var(--surface-glass)] font-bold"><th scope="row" colSpan={3} className="p-3 text-left">Totals</th><td className="p-3 text-right tabular-nums">{money(details.totalDebit)}</td><td className="p-3 text-right tabular-nums">{money(details.totalCredit)}</td><td className="p-3"></td></tr></tfoot>
            </table>
          </div>
        </>}
      </div>
      <footer className="flex shrink-0 justify-end border-t border-[var(--border)] p-4 sm:px-6"><Button variant="ghost" size="sm" onClick={onClose}>Close</Button></footer>
    </section>
  </Modal>;
}

function DetailMetric({ label, value }: { label: string; value: string }) { return <div className="rounded-xl border border-[var(--border)] bg-[var(--surface-glass)] p-4"><p className="text-xs font-semibold uppercase tracking-wide text-[var(--text-muted)]">{label}</p><p className="mt-1 font-semibold text-[var(--text-heading)]">{value}</p></div>; }

function BalanceView({ report }: { report: BalanceSheet }) { return <ReportCard title={`Balance Sheet · ${new Date(report.asAt).toLocaleDateString("en-GB")}`}>
  {!report.isBalanced && <div className="mb-5 rounded-xl border border-rose-500/40 bg-rose-500/10 p-4 text-sm text-rose-200"><p className="font-bold">Statement is out of balance by {money(report.imbalance)}.</p><p className="mt-1">Review: {report.unbalancedAccounts.join(", ")}. No difference row has been inserted.</p></div>}
  {report.assetGroups.map((group)=><BsGroupView key={group.name} group={group}/>)}<BsTotal label="Total Assets" amount={report.totalAssets}/>
  {report.liabilityGroups.map((group)=><BsGroupView key={group.name} group={group}/>)}
  <div className="mt-5"><h3 className="font-bold">Capital</h3>{report.capitalLines.map((line)=><BsLineView key={line.accountId} line={line}/>)}<BsLineView line={{accountId:-1,ledgerCode:null,name:"Retained Profit (per the ledger)",amount:report.retainedProfit}}/></div>
  <BsTotal label="Total Liabilities & Capital" amount={report.totalLiabilitiesAndCapital}/><p className={`mt-4 rounded-xl p-3 text-center font-semibold ${report.isBalanced ? "bg-emerald-500/10 text-emerald-300" : "bg-rose-500/10 text-rose-300"}`}>{report.isBalanced ? "Balanced" : "Action required"}</p>
  {/* Spending Net Profit carries and this ledger position cannot, stated on the statement rather
      than left to be found. Described strictly against THIS sheet's own window: subtracting it from
      a P&L run for some other period is arithmetic on two different questions, so the panel names
      the window and does not invite the comparison. */}
  {report.unpostedFixedAssetCharge > 0 && <div className="mt-4 rounded-xl border border-amber-500/40 bg-amber-500/10 p-4 text-xs text-amber-200">
    <p className="font-bold">{money(report.unpostedFixedAssetCharge)} of fixed assets was bought {report.retainedProfitStart ? `between ${new Date(report.retainedProfitStart).toLocaleDateString("en-GB")} and this date` : "up to this date"} — the window Retained Profit above covers. Net Profit is charged with it; Retained Profit above is stated before it.</p>
    <p className="mt-1">Net Profit deducts that spending, because buying an asset spends the money. This sheet cannot deduct it as well: the books hold only <span className="font-semibold">Dr Fixed Asset / Cr Bank</span>, the asset is still carried above at full cost, and no account has been approved to take the balancing credit — so charging it here would put the statement out by exactly this amount rather than making it more correct. Nothing has been invented to absorb it.</p>
    <p className="mt-1 font-semibold">Open accounting decision: how the ledger should carry the balancing side while the asset stays at cost. Until the accountant settles it, this is a disclosure — the Balance Sheet and the Profit &amp; Loss are not formally reconciled, and comparing this window with a Profit &amp; Loss run for a different period will not make them so.</p>
  </div>}
  </ReportCard> }
function BsGroupView({group}:{group:BsGroup}){return <div className="mt-5"><h3 className="font-bold">{group.name}</h3>{group.lines.map((line)=><BsLineView key={line.accountId} line={line}/>)}<div className="flex justify-between border-t border-[var(--border)] px-3 py-2 font-semibold"><span>Total {group.name}</span><span>{money(group.total)}</span></div></div>}
function BsLineView({line}:{line:BsLine}){return <div className="flex justify-between px-3 py-2 text-sm"><span>{line.ledgerCode ? `${line.ledgerCode} · ` : ""}{line.name}</span><span>{money(line.amount)}</span></div>}
function BsTotal({label,amount}:{label:string;amount:number}){return <div className="mt-4 flex justify-between rounded-xl bg-[var(--surface-glass)] p-4 text-lg font-bold"><span>{label}</span><span>{money(amount)}</span></div>}
function moneyOrDash(value:number){return value ? money(value) : "—"}
function formatBalance(value:number,type:BalanceType){const normalized=String(type??"").toLowerCase();const suffix=normalized.startsWith("c")?"Cr":normalized.startsWith("d")?"Dr":"";return `${money(value)}${suffix?` ${suffix}`:""}`}
function formatReportDate(value:string){const match=/^(\d{4})-(\d{2})-(\d{2})/.exec(value);if(!match)return "—";const date=new Date(Number(match[1]),Number(match[2])-1,Number(match[3]));return Number.isNaN(date.getTime())?"—":date.toLocaleDateString("en-GB",{day:"2-digit",month:"short",year:"numeric"})}
function TrialDateModeField({value,onChange}:{value:TrialDateMode;onChange:(value:TrialDateMode)=>void}){return <fieldset><legend className="mb-1 text-xs text-[var(--text-muted)]">Date mode</legend><div className="inline-flex rounded-xl border border-[var(--border)] bg-[var(--input-bg)] p-1" aria-label="Trial Balance date mode">{([ ["asAt","As at"], ["range","Date range"] ] as [TrialDateMode,string][]).map(([mode,label])=><button type="button" aria-pressed={value===mode} key={mode} onClick={()=>onChange(mode)} className={`rounded-lg px-3 py-1.5 text-xs font-semibold focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--accent)] ${value===mode?"bg-[var(--accent)] text-[var(--btn-primary-text)]":"text-[var(--text-muted)] hover:text-[var(--text-primary)]"}`}>{label}</button>)}</div></fieldset>}
function ReportCard({title,children}:{title:string;children:React.ReactNode}){return <section className="rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-5"><h2 className="mb-5 text-xl font-bold text-[var(--text-heading)]">{title}</h2>{children}</section>}
function DateField({label,value,onChange,required=false}:{label:string;value:string;onChange:(value:string)=>void;required?:boolean}){return <label className="text-xs text-[var(--text-muted)]">{label}<input type="date" required={required} max={required?pakistanToday():undefined} className="mt-1 block rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-3 py-2 text-sm text-[var(--text-primary)] [color-scheme:dark]" value={value} onChange={(event)=>onChange(event.target.value)}/></label>}
