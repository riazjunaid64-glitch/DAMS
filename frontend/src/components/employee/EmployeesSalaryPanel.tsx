import { useCallback, useEffect, useMemo, useState } from "react";
import { api } from "../../api/api.ts";
import Button from "../../lib/Button.tsx";
import Modal from "../../lib/Modal.tsx";
import SalarySlip from "../SalarySlip.tsx";
import DateRangeFilter from "./DateRangeFilter.tsx";
import { defaultRange, todayIso, type DateRange } from "./dateRange";
import { formatPkr } from "../../utils/currency.ts";

interface BatchRow {
  employeeId: number;
  employeeName: string;
  department: string;
  jobTitle: string;
  amount: number;
  isPaid: boolean;
  salaryRecordId: number | null;
  projectName: string | null;
  payDate: string | null;
  editingAmount: boolean;
  saving: boolean;
}

interface HistoryRow {
  id: number;
  employeeId: number;
  employeeName: string;
  jobTitle: string;
  department: string;
  amount: number;
  payDate: string;
  projectName: string | null;
  notes: string | null;
}

type ReceiptData = {
  employeeName: string;
  jobTitle: string;
  department: string;
  phone: string;
  email: string | null;
  joinDate: string;
  projectName: string | null;
  amount: number;
  payDate: string;
  notes?: string | null;
};

interface FinanceAccountOption {
  id: number;
  name: string;
  accountHolderName: string;
}

function currentMonthYear() {
  const d = new Date();
  return { month: d.getMonth() + 1, year: d.getFullYear() };
}

const formatCurrency = formatPkr;

function formatDate(iso: string) {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return d.toLocaleDateString("en-US", { day: "numeric", month: "short", year: "numeric" });
}

// ─── Payroll Run (monthly sheet) ────────────────────────────────────────────

function PayrollRun({ onOpenReceipt }: { onOpenReceipt: (employeeId: number, salaryRecordId: number) => void }) {
  const [{ month, year }, setPeriod] = useState(currentMonthYear);
  const [rows, setRows] = useState<BatchRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  // Which account the payroll is paid from. A salary writes a real expense row, and an expense with
  // no paying account has no credit side — the Trial Balance and Balance Sheet then go out by the
  // salary amount. One account for the run, because a payroll is paid out of one account.
  const [accounts, setAccounts] = useState<FinanceAccountOption[]>([]);
  const [payFromId, setPayFromId] = useState("");

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const res = await api("/api/finance/accounts/options");
        if (!res.ok) return;
        const options = await res.json() as FinanceAccountOption[];
        if (cancelled) return;
        setAccounts(options);
        // Only pre-selected when there is nothing to choose between.
        if (options.length === 1) setPayFromId(String(options[0].id));
      } catch { /* The selector stays empty and paying is refused until it loads. */ }
    })();
    return () => { cancelled = true; };
  }, []);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await api(`/api/Employee/salary/batch?month=${month}&year=${year}`);
      if (!res.ok) { setError("Failed to load salary sheet."); return; }
      const raw = await res.json() as Array<Record<string, unknown>>;
      setRows(raw.map(r => ({
        employeeId: Number(r.employeeId),
        employeeName: String(r.employeeName ?? ""),
        department: String(r.department ?? ""),
        jobTitle: String(r.jobTitle ?? ""),
        amount: Number(r.isPaid ? (r.paidAmount ?? r.baseSalary) : r.baseSalary),
        isPaid: Boolean(r.isPaid),
        salaryRecordId: r.salaryRecordId != null ? Number(r.salaryRecordId) : null,
        projectName: r.projectName != null ? String(r.projectName) : null,
        payDate: r.payDate != null ? String(r.payDate) : null,
        editingAmount: false,
        saving: false,
      })));
    } catch { setError("Could not load salary sheet."); }
    finally { setLoading(false); }
  }, [month, year]);

  useEffect(() => { load(); }, [load]);

  const togglePaid = async (employeeId: number, checked: boolean) => {
    const row = rows.find(r => r.employeeId === employeeId);
    if (!row || row.isPaid || !checked) return;
    if (row.amount <= 0) { setError("Salary amount must be greater than zero."); return; }
    if (!payFromId) { setError("Choose the account this payroll is paid from first."); return; }

    setError(null);
    setRows(prev => prev.map(r => r.employeeId === employeeId ? { ...r, saving: true } : r));
    try {
      const payDate = `${year}-${String(month).padStart(2, "0")}-01`;
      const res = await api(`/api/Employee/${employeeId}/salary`, {
        method: "POST",
        body: JSON.stringify({ amount: row.amount, payDate, financeAccountId: Number(payFromId) }),
      });
      if (!res.ok) {
        const d = await res.json().catch(() => ({}));
        setError((d as { message?: string }).message ?? `Failed to record salary for ${row.employeeName}.`);
        return;
      }
      const created = await res.json() as Record<string, unknown>;
      const salaryId = Number(created.id);
      setRows(prev => prev.map(r =>
        r.employeeId === employeeId
          ? { ...r, isPaid: true, salaryRecordId: salaryId, saving: false, payDate: String(created.payDate ?? todayIso()) }
          : r
      ));
    } catch { setError("Something went wrong."); }
    finally {
      setRows(prev => prev.map(r => r.employeeId === employeeId ? { ...r, saving: false } : r));
    }
  };

  const totals = useMemo(() => {
    const paid = rows.filter(r => r.isPaid);
    const pending = rows.filter(r => !r.isPaid);
    return {
      paidCount: paid.length,
      total: rows.length,
      paidAmount: paid.reduce((s, r) => s + r.amount, 0),
      pendingAmount: pending.reduce((s, r) => s + r.amount, 0),
    };
  }, [rows]);

  const monthLabel = new Date(year, month - 1, 1).toLocaleString("en-US", { month: "long", year: "numeric" });

  return (
    <div>
      <div className="mb-5 flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <h2 className="section-title mb-1">Payroll Run — {monthLabel}</h2>
          <p className="text-sm text-[var(--text-muted)]">Process monthly salary for every employee</p>
        </div>
        <div className="flex flex-wrap items-end gap-4">
          <div>
            <label className="mb-1.5 block text-xs font-medium text-[var(--text-muted)]" htmlFor="payroll-paid-from">Paid From Account</label>
            <select
              id="payroll-paid-from"
              value={payFromId}
              onChange={e => setPayFromId(e.target.value)}
              className="rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-3 py-2 text-sm text-[var(--text-primary)] outline-none focus:border-[var(--accent)]"
            >
              <option value="">Select account</option>
              {accounts.map(a => (
                <option key={a.id} value={a.id}>{a.name} — {a.accountHolderName}</option>
              ))}
            </select>
          </div>
          <div>
            <label className="mb-1.5 block text-xs font-medium text-[var(--text-muted)]" htmlFor="payroll-month">Month</label>
            <input
              id="payroll-month"
              type="month"
              value={`${year}-${String(month).padStart(2, "0")}`}
              onChange={e => {
                const [y, m] = e.target.value.split("-").map(Number);
                if (y && m) setPeriod({ year: y, month: m });
              }}
              className="rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-3 py-2 text-sm text-[var(--text-primary)] outline-none focus:border-[var(--accent)]"
            />
          </div>
        </div>
      </div>

      {error && (
        <div className="mb-4 rounded-xl border border-rose-500/20 bg-rose-500/[0.06] px-4 py-3 text-sm text-rose-300">{error}</div>
      )}

      <div className="mb-6 grid grid-cols-2 gap-3 lg:grid-cols-4">
        <div className="rounded-xl border border-[var(--border)] bg-[var(--bg-card)] px-4 py-3 shadow-sm">
          <p className="text-2xl font-bold text-[var(--text-primary)]">{totals.paidCount}/{totals.total}</p>
          <p className="mt-0.5 text-[11px] font-semibold uppercase tracking-wider text-[var(--text-muted)]">Employees Paid</p>
        </div>
        <div className="rounded-xl border border-[var(--border)] bg-[var(--bg-card)] px-4 py-3 shadow-sm">
          <p className="text-2xl font-bold text-emerald-400">{formatCurrency(totals.paidAmount)}</p>
          <p className="mt-0.5 text-[11px] font-semibold uppercase tracking-wider text-[var(--text-muted)]">Total Paid</p>
        </div>
        <div className="rounded-xl border border-[var(--border)] bg-[var(--bg-card)] px-4 py-3 shadow-sm">
          <p className="text-2xl font-bold text-amber-400">{formatCurrency(totals.pendingAmount)}</p>
          <p className="mt-0.5 text-[11px] font-semibold uppercase tracking-wider text-[var(--text-muted)]">Pending</p>
        </div>
        <div className="rounded-xl border border-[var(--border)] bg-[var(--bg-card)] px-4 py-3 shadow-sm">
          <p className="text-2xl font-bold text-[var(--text-primary)]">{formatCurrency(totals.paidAmount + totals.pendingAmount)}</p>
          <p className="mt-0.5 text-[11px] font-semibold uppercase tracking-wider text-[var(--text-muted)]">Payroll Total</p>
        </div>
      </div>

      {loading ? (
        <div className="space-y-3">
          {[...Array(6)].map((_, i) => <div key={i} className="skeleton h-14 rounded-xl" />)}
        </div>
      ) : rows.length === 0 ? (
        <div className="py-12 text-center text-sm text-[var(--text-muted)]">No employees found.</div>
      ) : (
        <div className="overflow-x-auto rounded-xl border border-[var(--border)] bg-[var(--bg-card)] shadow-sm">
          <table className="data-table min-w-[900px] w-full border-collapse text-left">
            <thead>
              <tr className="border-b border-[var(--border)] bg-[var(--surface-glass)]">
                {["Employee", "Department", "Salary", "Paid", "Receipt"].map(h => (
                  <th key={h} className="px-5 py-3 text-[10px] font-bold uppercase tracking-wider text-[var(--text-muted)]">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {rows.map(row => (
                <tr key={row.employeeId} className="border-b border-[var(--border)] transition-colors hover:bg-[var(--surface-glass-hover)] last:border-b-0">
                  <td className="px-5 py-4">
                    <p className="text-sm font-semibold text-[var(--text-primary)]">{row.employeeName}</p>
                    <p className="text-xs text-[var(--text-muted)]">{row.jobTitle}</p>
                  </td>
                  <td className="px-5 py-4 text-sm text-[var(--text-secondary)]">{row.department}</td>
                  <td className="px-5 py-4">
                    {row.isPaid ? (
                      <span className="text-sm font-semibold text-[var(--text-primary)]">{formatCurrency(row.amount)}</span>
                    ) : row.editingAmount ? (
                      <input
                        type="number"
                        value={row.amount}
                        autoFocus
                        onChange={e => setRows(prev => prev.map(r =>
                          r.employeeId === row.employeeId ? { ...r, amount: Number(e.target.value) } : r
                        ))}
                        onBlur={() => setRows(prev => prev.map(r =>
                          r.employeeId === row.employeeId ? { ...r, editingAmount: false } : r
                        ))}
                        onKeyDown={e => {
                          if (e.key === "Enter") setRows(prev => prev.map(r =>
                            r.employeeId === row.employeeId ? { ...r, editingAmount: false } : r
                          ));
                        }}
                        className="w-28 rounded-lg border border-[var(--accent)] bg-[var(--input-bg)] px-2 py-1 text-sm text-[var(--text-primary)] outline-none"
                      />
                    ) : (
                      <span className="inline-flex items-center gap-2 text-sm font-semibold text-[var(--text-primary)]">
                        {formatCurrency(row.amount)}
                        <button
                          type="button"
                          onClick={() => setRows(prev => prev.map(r =>
                            r.employeeId === row.employeeId ? { ...r, editingAmount: true } : r
                          ))}
                          className="text-[var(--accent)] hover:text-[var(--accent-light)]"
                          title="Edit amount"
                        >
                          ✎
                        </button>
                      </span>
                    )}
                  </td>
                  <td className="px-5 py-4">
                    <button
                      type="button"
                      disabled={row.isPaid || row.saving || !payFromId}
                      title={!row.isPaid && !payFromId ? "Choose the account this payroll is paid from" : undefined}
                      onClick={() => togglePaid(row.employeeId, true)}
                      className={`flex h-5 w-5 items-center justify-center rounded border text-[10px] font-bold transition-colors ${
                        row.isPaid
                          ? "border-emerald-400 bg-emerald-400 text-white cursor-default"
                          : "border-[var(--border)] hover:border-emerald-400/50"
                      }`}
                    >
                      {row.isPaid ? "✓" : row.saving ? "…" : ""}
                    </button>
                  </td>
                  <td className="px-5 py-4">
                    {row.isPaid && row.salaryRecordId ? (
                      <button
                        type="button"
                        onClick={() => onOpenReceipt(row.employeeId, row.salaryRecordId!)}
                        className="inline-flex items-center gap-1.5 rounded-lg border border-[var(--accent-glow-strong)] bg-[var(--accent-glow)] px-3 py-1.5 text-xs font-semibold text-[var(--accent)] transition-colors hover:border-[var(--accent)] hover:bg-[var(--surface-glass-hover)]"
                      >
                        View Receipt
                      </button>
                    ) : (
                      <span className="text-xs text-[var(--text-muted)]">—</span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

// ─── Payment History ────────────────────────────────────────────────────────

function PaymentHistory({ onOpenReceipt }: { onOpenReceipt: (employeeId: number, salaryRecordId: number) => void }) {
  const [range, setRange] = useState<DateRange>(defaultRange);
  const [employeeFilter, setEmployeeFilter] = useState<string>("all");
  const [records, setRecords] = useState<HistoryRow[]>([]);
  const [employees, setEmployees] = useState<{ id: number; name: string }[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const res = await api("/api/Employee");
        if (!res.ok) return;
        const raw = await res.json() as Array<Record<string, unknown>>;
        if (cancelled) return;
        setEmployees(raw.map(e => ({ id: Number(e.id), name: String(e.fullName ?? e.FullName ?? "") }))
          .sort((a, b) => a.name.localeCompare(b.name)));
      } catch { /* ignore */ }
    })();
    return () => { cancelled = true; };
  }, []);

  const load = useCallback(async () => {
    if (!range.from || !range.to) return;
    setLoading(true);
    setError(null);
    try {
      const params = new URLSearchParams({ from: range.from, to: range.to });
      if (employeeFilter !== "all") params.set("employeeId", employeeFilter);
      const res = await api(`/api/Employee/salary/history?${params.toString()}`);
      if (!res.ok) { setError("Failed to load payment history."); return; }
      const raw = await res.json() as Array<Record<string, unknown>>;
      setRecords(raw.map(r => ({
        id: Number(r.id),
        employeeId: Number(r.employeeId),
        employeeName: String(r.employeeName ?? ""),
        jobTitle: String(r.jobTitle ?? ""),
        department: String(r.department ?? ""),
        amount: Number(r.amount),
        payDate: String(r.payDate ?? ""),
        projectName: r.projectName != null ? String(r.projectName) : null,
        notes: r.notes != null ? String(r.notes) : null,
      })));
    } catch { setError("Could not load payment history."); }
    finally { setLoading(false); }
  }, [range.from, range.to, employeeFilter]);

  useEffect(() => { load(); }, [load]);

  const totals = useMemo(() => ({
    amount: records.reduce((s, r) => s + r.amount, 0),
    count: records.length,
    employees: new Set(records.map(r => r.employeeId)).size,
  }), [records]);

  return (
    <div>
      <div className="mb-5">
        <h2 className="section-title mb-1">Payment History</h2>
        <p className="text-sm text-[var(--text-muted)]">All salary payments for the selected period</p>
      </div>

      <div className="mb-5 flex flex-wrap items-end gap-3">
        <DateRangeFilter value={range} onChange={setRange} />
        <div>
          <label className="mb-1.5 block text-xs font-medium text-[var(--text-muted)]">Employee</label>
          <select
            value={employeeFilter}
            onChange={e => setEmployeeFilter(e.target.value)}
            className="rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-3 py-2 text-sm text-[var(--text-primary)] outline-none focus:border-[var(--accent)]"
          >
            <option value="all">All employees</option>
            {employees.map(e => <option key={e.id} value={e.id}>{e.name}</option>)}
          </select>
        </div>
      </div>

      {error && (
        <div className="mb-4 rounded-xl border border-rose-500/20 bg-rose-500/[0.06] px-4 py-3 text-sm text-rose-300">{error}</div>
      )}

      <div className="mb-6 grid grid-cols-3 gap-3">
        <div className="rounded-xl border border-[var(--border)] bg-[var(--bg-card)] px-4 py-3 shadow-sm">
          <p className="text-2xl font-bold text-emerald-400">{formatCurrency(totals.amount)}</p>
          <p className="mt-0.5 text-[11px] font-semibold uppercase tracking-wider text-[var(--text-muted)]">Total Paid</p>
        </div>
        <div className="rounded-xl border border-[var(--border)] bg-[var(--bg-card)] px-4 py-3 shadow-sm">
          <p className="text-2xl font-bold text-[var(--text-primary)]">{totals.count}</p>
          <p className="mt-0.5 text-[11px] font-semibold uppercase tracking-wider text-[var(--text-muted)]">Payments</p>
        </div>
        <div className="rounded-xl border border-[var(--border)] bg-[var(--bg-card)] px-4 py-3 shadow-sm">
          <p className="text-2xl font-bold text-[var(--text-primary)]">{totals.employees}</p>
          <p className="mt-0.5 text-[11px] font-semibold uppercase tracking-wider text-[var(--text-muted)]">Employees</p>
        </div>
      </div>

      {loading ? (
        <div className="space-y-3">
          {[...Array(5)].map((_, i) => <div key={i} className="skeleton h-12 rounded-xl" />)}
        </div>
      ) : records.length === 0 ? (
        <div className="py-12 text-center text-sm text-[var(--text-muted)]">No payments recorded for this period.</div>
      ) : (
        <div className="overflow-x-auto rounded-xl border border-[var(--border)] bg-[var(--bg-card)] shadow-sm">
          <table className="data-table min-w-[820px] w-full border-collapse text-left">
            <thead>
              <tr className="border-b border-[var(--border)] bg-[var(--surface-glass)]">
                {["Employee", "Pay Date", "Amount", "Project", "Notes", "Receipt"].map(h => (
                  <th key={h} className="px-5 py-3 text-[10px] font-bold uppercase tracking-wider text-[var(--text-muted)]">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {records.map(r => (
                <tr key={r.id} className="border-b border-[var(--border)] transition-colors hover:bg-[var(--surface-glass-hover)] last:border-b-0">
                  <td className="px-5 py-4">
                    <p className="text-sm font-semibold text-[var(--text-primary)]">{r.employeeName}</p>
                    <p className="text-xs text-[var(--text-muted)]">{r.jobTitle}</p>
                  </td>
                  <td className="px-5 py-4 text-sm font-medium text-[var(--text-primary)]">{formatDate(r.payDate)}</td>
                  <td className="px-5 py-4 text-sm font-semibold text-emerald-400">{formatCurrency(r.amount)}</td>
                  <td className="px-5 py-4 text-sm text-[var(--text-secondary)]">{r.projectName ?? "General"}</td>
                  <td className="px-5 py-4 text-sm text-[var(--text-secondary)]">{r.notes ?? "—"}</td>
                  <td className="px-5 py-4">
                    <button
                      type="button"
                      onClick={() => onOpenReceipt(r.employeeId, r.id)}
                      className="inline-flex items-center gap-1.5 rounded-lg border border-[var(--accent-glow-strong)] bg-[var(--accent-glow)] px-3 py-1.5 text-xs font-semibold text-[var(--accent)] transition-colors hover:border-[var(--accent)] hover:bg-[var(--surface-glass-hover)]"
                    >
                      View Receipt
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

// ─── Panel shell ────────────────────────────────────────────────────────────

export default function EmployeesSalaryPanel() {
  const [view, setView] = useState<"run" | "history">("run");
  const [receiptData, setReceiptData] = useState<ReceiptData | null>(null);

  const openReceipt = useCallback(async (employeeId: number, salaryRecordId: number) => {
    try {
      const [salaryRes, empRes] = await Promise.all([
        api(`/api/Employee/salary/${salaryRecordId}`),
        api(`/api/Employee/${employeeId}`),
      ]);
      if (!salaryRes.ok || !empRes.ok) return;
      const salary = await salaryRes.json() as Record<string, unknown>;
      const emp = await empRes.json() as Record<string, unknown>;
      setReceiptData({
        employeeName: String(salary.employeeName ?? ""),
        jobTitle: String(salary.jobTitle ?? ""),
        department: String(salary.department ?? ""),
        phone: String(emp.phone ?? emp.Phone ?? ""),
        email: emp.email != null ? String(emp.email) : emp.Email != null ? String(emp.Email) : null,
        joinDate: String(emp.joinDate ?? emp.JoinDate ?? ""),
        projectName: salary.projectName != null ? String(salary.projectName) : null,
        amount: Number(salary.amount ?? 0),
        payDate: String(salary.payDate ?? todayIso()),
        notes: salary.notes != null ? String(salary.notes) : null,
      });
    } catch { /* ignore */ }
  }, []);

  return (
    <div>
      <div className="mb-6 inline-flex rounded-xl border border-[var(--border)] bg-[var(--surface-glass)] p-1">
        {([["run", "Payroll Run"], ["history", "Payment History"]] as const).map(([id, label]) => (
          <button
            key={id}
            type="button"
            onClick={() => setView(id)}
            className={`rounded-lg px-4 py-2 text-sm font-semibold transition-colors ${
              view === id ? "bg-[var(--accent)] text-[#1c1810] shadow-sm" : "text-[var(--text-muted)] hover:text-[var(--text-primary)]"
            }`}
          >
            {label}
          </button>
        ))}
      </div>

      {view === "run" ? <PayrollRun onOpenReceipt={openReceipt} /> : <PaymentHistory onOpenReceipt={openReceipt} />}

      <Modal open={receiptData != null} onClose={() => setReceiptData(null)} align="top">
        {receiptData && (
          <div className="relative z-10 my-8 w-full max-w-4xl animate-scale-in">
            <div className="no-print mb-4 flex justify-end gap-2">
              <Button variant="ghost" onClick={() => setReceiptData(null)}>Close</Button>
              <Button onClick={() => window.print()}>Print</Button>
            </div>
            <SalarySlip data={receiptData} />
          </div>
        )}
      </Modal>
    </div>
  );
}
