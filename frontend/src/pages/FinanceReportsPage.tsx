import { useCallback, useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import type { User } from "../App";
import { api } from "../api/api";
import { useFinancialYearStartMonth } from "../features/finance/useFinancialYearStartMonth";
import Button from "../lib/Button";
import Container from "../lib/Container";
import { buildPeriodRange, financePeriodLabel, pakistanToday } from "../lib/financePeriods";

type Tab = "pnl" | "trial" | "balance";
type Project = { id: number; projectName: string };
type PnlLine = { categoryId: number | null; name: string; amount: number; priorAmount: number | null; transactionCount: number };
type Pnl = { periodStart: string; periodEnd: string; periodLabel: string; projectName: string | null; incomeLines: PnlLine[]; totalIncome: number; expenseLines: PnlLine[]; totalExpenses: number; netProfit: number; priorTotalIncome: number; priorTotalExpenses: number; priorNetProfit: number };
type TrialRow = { accountId: number; ledgerCode: string | null; accountName: string; debitBalances: number[]; creditBalances: number[] };
type Trial = { columnDates: string[]; rows: TrialRow[]; columnDebitTotals: number[]; columnCreditTotals: number[]; columnBalanced: boolean[] };
type BsLine = { accountId: number; ledgerCode: string | null; name: string; amount: number };
type BsGroup = { name: string; lines: BsLine[]; total: number };
type BalanceSheet = { asAt: string; assetGroups: BsGroup[]; totalAssets: number; liabilityGroups: BsGroup[]; totalLiabilities: number; capitalLines: BsLine[]; retainedProfit: number; totalCapital: number; totalLiabilitiesAndCapital: number; isBalanced: boolean; imbalance: number; unbalancedAccounts: string[] };

const money = (value: number) => `Rs ${value.toLocaleString("en-PK", { maximumFractionDigits: 2 })}`;

export default function FinanceReportsPage({ user }: { user: User | null }) {
  const navigate = useNavigate();
  const [tab, setTab] = useState<Tab>("pnl");
  const [projects, setProjects] = useState<Project[]>([]);
  const [projectId, setProjectId] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [asAt, setAsAt] = useState(pakistanToday());
  const [monthsBack, setMonthsBack] = useState("12");
  // null until read back — a P&L preset must not name a financial year the client has not set.
  const { startMonth, failed: startMonthFailed } = useFinancialYearStartMonth(user?.role === "Admin");
  const [pnl, setPnl] = useState<Pnl | null>(null);
  const [trial, setTrial] = useState<Trial | null>(null);
  const [balance, setBalance] = useState<BalanceSheet | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (user?.role !== "Admin") { navigate("/"); return; }
    void Promise.all([
      api("/api/Project", undefined, false).then(async (response) => {
        if (!response.ok) return;
        const rows = await response.json() as Project[];
        setProjects(Array.isArray(rows) ? rows : []);
      }),
    ]);
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

  const load = useCallback(async () => {
    if (user?.role !== "Admin") return;
    setLoading(true); setError(null);
    try {
      if (tab === "pnl") {
        const response = await api(`/api/Finance/profit-and-loss?${query(true)}`);
        if (!response.ok) throw new Error((await response.json().catch(() => null))?.message ?? "Profit and loss could not be loaded.");
        setPnl(await response.json());
      } else if (tab === "trial") {
        const params = query(false); params.set("monthsBack", monthsBack);
        const response = await api(`/api/Finance/trial-balance?${params}`);
        if (!response.ok) throw new Error((await response.json().catch(() => null))?.message ?? "Trial balance could not be loaded.");
        setTrial(await response.json());
      } else {
        const response = await api(`/api/Finance/balance-sheet?${query(false)}`);
        if (!response.ok) throw new Error((await response.json().catch(() => null))?.message ?? "Balance sheet could not be loaded.");
        setBalance(await response.json());
      }
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "The report could not be loaded.");
    } finally { setLoading(false); }
  }, [tab, query, monthsBack, user]);

  useEffect(() => { void load(); }, [load]);

  const applyYearPreset = (preset: "year" | "lastYear") => {
    const range = buildPeriodRange(preset, startMonth);
    setFrom(range.from); setTo(range.to);
  };

  const exportReport = async () => {
    const params = query(tab === "pnl");
    if (tab === "trial") params.set("monthsBack", monthsBack);
    params.set("format", "xlsx");
    const endpoint = tab === "pnl" ? "profit-and-loss" : tab === "trial" ? "trial-balance" : "balance-sheet";
    const response = await api(`/api/Finance/${endpoint}/export?${params}`);
    if (!response.ok) { setError((await response.json().catch(() => null))?.message ?? "Export failed."); return; }
    const blob = await response.blob();
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a"); link.href = url;
    link.download = `${endpoint}-${pakistanToday()}.xlsx`; document.body.appendChild(link); link.click(); link.remove();
    URL.revokeObjectURL(url);
  };

  if (user?.role !== "Admin") return null;
  return <Container className="py-8">
    <div className="mb-6 flex flex-wrap items-end justify-between gap-4">
      <div><Link to="/finance" className="text-sm text-[var(--accent)]">← Finance dashboard</Link><h1 className="mt-1 text-3xl font-bold text-[var(--text-heading)]">Financial Reports</h1><p className="text-sm text-[var(--text-muted)]">Accrual performance and balanced account statements from one ledger.</p></div>
      <div className="flex gap-2"><Link to="/finance/partners"><Button variant="outline">Capital Partners</Button></Link><Button onClick={() => void exportReport()}>Export XLSX</Button></div>
    </div>
    <div className="mb-5 flex flex-wrap gap-2">{([ ["pnl", "Profit & Loss"], ["trial", "Trial Balance"], ["balance", "Balance Sheet"] ] as [Tab,string][]).map(([id,label]) => <button key={id} onClick={() => setTab(id)} className={`rounded-full px-4 py-2 text-sm font-semibold ${tab === id ? "bg-[var(--accent)] text-white" : "border border-[var(--border)] bg-[var(--surface)]"}`}>{label}</button>)}</div>
    <div className="mb-5 flex flex-wrap items-end gap-3 rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-4">
      <label className="text-xs text-[var(--text-muted)]">Project<select className="mt-1 block rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-3 py-2 text-sm" value={projectId} onChange={(event) => setProjectId(event.target.value)}><option value="">All projects</option>{projects.map((project) => <option key={project.id} value={project.id}>{project.projectName}</option>)}</select></label>
      {tab === "pnl" ? <>
        {/* Disabled until the configured year start is known: the button both names a range and
            applies it, so an unresolved setting would make it wrong on both counts. */}
        <Button variant="outline" disabled={startMonth === null} onClick={() => applyYearPreset("year")}>{financePeriodLabel("year", startMonth)}</Button>
        <Button variant="outline" disabled={startMonth === null} onClick={() => applyYearPreset("lastYear")}>{financePeriodLabel("lastYear", startMonth)}</Button>
        {startMonthFailed && <p role="alert" className="text-xs text-amber-300">Financial year setting unavailable — use From/To.</p>}
        <DateField label="From" value={from} onChange={setFrom}/><DateField label="To" value={to} onChange={setTo}/>
      </> : <>
        <DateField label="As at" value={asAt} onChange={setAsAt}/>
        {tab === "trial" && <label className="text-xs text-[var(--text-muted)]">Months back<select className="mt-1 block rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-3 py-2 text-sm" value={monthsBack} onChange={(event) => setMonthsBack(event.target.value)}><option value="6">6</option><option value="12">12</option><option value="24">24</option></select></label>}
      </>}
      <Button onClick={() => void load()}>Refresh</Button>
    </div>
    {error && <p className="mb-4 rounded-xl border border-rose-500/30 bg-rose-500/10 p-4 text-sm text-rose-300">{error}</p>}
    {loading ? <p className="py-20 text-center text-[var(--text-muted)]">Loading report…</p> : tab === "pnl" && pnl ? <PnlView report={pnl}/> : tab === "trial" && trial ? <TrialView report={trial}/> : tab === "balance" && balance ? <BalanceView report={balance}/> : null}
  </Container>;
}

function PnlView({ report }: { report: Pnl }) {
  return <ReportCard title={`Profit & Loss · ${report.periodLabel}`}>
    <TableHeader/><SectionRows title="Income" lines={report.incomeLines}/><TotalRow label="Total Income" current={report.totalIncome} prior={report.priorTotalIncome}/>
    <SectionRows title="Expenses" lines={report.expenseLines}/><TotalRow label="Total Expenses" current={report.totalExpenses} prior={report.priorTotalExpenses}/>
    <div className={`mt-4 grid grid-cols-3 rounded-xl p-4 font-bold ${report.netProfit >= 0 ? "bg-emerald-500/10 text-emerald-300" : "bg-rose-500/10 text-rose-300"}`}><span>Net Profit</span><span className="text-right">{money(report.netProfit)}</span><span className="text-right">{money(report.priorNetProfit)}</span></div>
    <p className="mt-4 text-xs text-amber-300">Unit sales are recognised in full at possession, on the possession date. Customer payments taken before possession are a deposit liability, not income, and do not appear here. Construction and site work is an expense on the day it is paid — it is not held as work in progress. Fixed assets bought in the period are a cost of it, at their full price, and appear above as Fixed Asset Purchases; the assets themselves stay on the Balance Sheet.</p>
  </ReportCard>;
}
function TableHeader(){return <div className="grid grid-cols-3 border-b border-[var(--border)] px-3 pb-2 text-xs font-semibold uppercase text-[var(--text-muted)]"><span>Account</span><span className="text-right">Current</span><span className="text-right">Prior year</span></div>}
function SectionRows({title,lines}:{title:string;lines:PnlLine[]}){return <div className="mt-4"><h3 className="px-3 text-sm font-bold text-[var(--text-heading)]">{title}</h3>{lines.length ? lines.map((line)=><div key={`${line.categoryId}-${line.name}`} className="grid grid-cols-3 border-b border-[var(--border)]/60 px-3 py-2 text-sm"><span>{line.name}<small className="ml-2 text-[var(--text-muted)]">{line.transactionCount} tx</small></span><span className="text-right">{money(line.amount)}</span><span className="text-right text-[var(--text-muted)]">{money(line.priorAmount ?? 0)}</span></div>):<p className="px-3 py-4 text-sm text-[var(--text-muted)]">No {title.toLowerCase()} in this period.</p>}</div>}
function TotalRow({label,current,prior}:{label:string;current:number;prior:number}){return <div className="grid grid-cols-3 px-3 py-3 font-semibold"><span>{label}</span><span className="text-right">{money(current)}</span><span className="text-right">{money(prior)}</span></div>}

function TrialView({ report }: { report: Trial }) { return <ReportCard title="Trial Balance"><div className="overflow-x-auto"><table className="min-w-max text-xs"><thead><tr><th rowSpan={2} className="sticky left-0 bg-[var(--surface)] p-2 text-left">Account</th>{report.columnDates.map((date,index)=><th key={date} colSpan={2} className={`border-l border-[var(--border)] p-2 ${report.columnBalanced[index] ? "text-emerald-300" : "text-rose-300"}`}>{new Date(date).toLocaleDateString("en-GB")} · {report.columnBalanced[index] ? "Balanced" : "Mismatch"}</th>)}</tr><tr>{report.columnDates.flatMap((date)=>[<th key={`${date}-d`} className="border-l border-[var(--border)] p-2 text-right">Debit</th>,<th key={`${date}-c`} className="p-2 text-right">Credit</th>])}</tr></thead><tbody>{report.rows.map((row)=><tr key={row.accountId} className="border-t border-[var(--border)]"><td className="sticky left-0 min-w-56 bg-[var(--surface)] p-2"><span className="text-[var(--text-muted)]">{row.ledgerCode ?? "—"}</span> · {row.accountName}</td>{report.columnDates.flatMap((_,index)=>[<td key={`${row.accountId}-${index}-d`} className="border-l border-[var(--border)] p-2 text-right">{row.debitBalances[index] ? money(row.debitBalances[index]) : "—"}</td>,<td key={`${row.accountId}-${index}-c`} className="p-2 text-right">{row.creditBalances[index] ? money(row.creditBalances[index]) : "—"}</td>])}</tr>)}</tbody><tfoot><tr className="border-t-2 border-[var(--border)] font-bold"><td className="sticky left-0 bg-[var(--surface)] p-2">Totals</td>{report.columnDates.flatMap((_,index)=>[<td key={`${index}-td`} className="border-l border-[var(--border)] p-2 text-right">{money(report.columnDebitTotals[index])}</td>,<td key={`${index}-tc`} className="p-2 text-right">{money(report.columnCreditTotals[index])}</td>])}</tr></tfoot></table></div></ReportCard> }

function BalanceView({ report }: { report: BalanceSheet }) { return <ReportCard title={`Balance Sheet · ${new Date(report.asAt).toLocaleDateString("en-GB")}`}>
  {!report.isBalanced && <div className="mb-5 rounded-xl border border-rose-500/40 bg-rose-500/10 p-4 text-sm text-rose-200"><p className="font-bold">Statement is out of balance by {money(report.imbalance)}.</p><p className="mt-1">Review: {report.unbalancedAccounts.join(", ")}. No difference row has been inserted.</p></div>}
  {report.assetGroups.map((group)=><BsGroupView key={group.name} group={group}/>)}<BsTotal label="Total Assets" amount={report.totalAssets}/>
  {report.liabilityGroups.map((group)=><BsGroupView key={group.name} group={group}/>)}
  <div className="mt-5"><h3 className="font-bold">Capital</h3>{report.capitalLines.map((line)=><BsLineView key={line.accountId} line={line}/>)}<BsLineView line={{accountId:-1,ledgerCode:null,name:"Retained Profit",amount:report.retainedProfit}}/></div>
  <BsTotal label="Total Liabilities & Capital" amount={report.totalLiabilitiesAndCapital}/><p className={`mt-4 rounded-xl p-3 text-center font-semibold ${report.isBalanced ? "bg-emerald-500/10 text-emerald-300" : "bg-rose-500/10 text-rose-300"}`}>{report.isBalanced ? "Balanced" : "Action required"}</p>
  {/* Named on the statement itself, because a Capital line nobody can explain is worse than no line
      at all: Retained Profit is already down by what the period spent on fixed assets, and this is
      where that amount went. Together they still add up to the profit the company actually made. */}
  {report.capitalLines.some((line) => line.accountId === -2) && <p className="mt-4 text-xs text-[var(--text-muted)]">Fixed assets bought are charged to Net Profit in full, so Retained Profit is net of them. The assets stay in Fixed Assets above at cost, and "Fixed assets charged to profit" holds the same amount inside Capital — which is what keeps the statement balanced.</p>}
  </ReportCard> }
function BsGroupView({group}:{group:BsGroup}){return <div className="mt-5"><h3 className="font-bold">{group.name}</h3>{group.lines.map((line)=><BsLineView key={line.accountId} line={line}/>)}<div className="flex justify-between border-t border-[var(--border)] px-3 py-2 font-semibold"><span>Total {group.name}</span><span>{money(group.total)}</span></div></div>}
function BsLineView({line}:{line:BsLine}){return <div className="flex justify-between px-3 py-2 text-sm"><span>{line.ledgerCode ? `${line.ledgerCode} · ` : ""}{line.name}</span><span>{money(line.amount)}</span></div>}
function BsTotal({label,amount}:{label:string;amount:number}){return <div className="mt-4 flex justify-between rounded-xl bg-[var(--surface-glass)] p-4 text-lg font-bold"><span>{label}</span><span>{money(amount)}</span></div>}
function ReportCard({title,children}:{title:string;children:React.ReactNode}){return <section className="rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-5"><h2 className="mb-5 text-xl font-bold text-[var(--text-heading)]">{title}</h2>{children}</section>}
function DateField({label,value,onChange}:{label:string;value:string;onChange:(value:string)=>void}){return <label className="text-xs text-[var(--text-muted)]">{label}<input type="date" className="mt-1 block rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-3 py-2 text-sm" value={value} onChange={(event)=>onChange(event.target.value)}/></label>}
