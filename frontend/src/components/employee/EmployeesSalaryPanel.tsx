import { useCallback, useEffect, useState } from "react";
import { api } from "../../api/api.ts";
import Button from "../../lib/Button.tsx";
import Modal from "../../lib/Modal.tsx";
import SalarySlip from "../SalarySlip.tsx";

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

function currentMonthYear() {
  const d = new Date();
  return { month: d.getMonth() + 1, year: d.getFullYear() };
}

function formatCurrency(n: number) {
  return n.toLocaleString("en-US", { style: "currency", currency: "USD", minimumFractionDigits: 0 });
}

function todayIso() {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

export default function EmployeesSalaryPanel() {
  const [{ month, year }, setPeriod] = useState(currentMonthYear);
  const [showSheet, setShowSheet] = useState(false);
  const [rows, setRows] = useState<BatchRow[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [receiptData, setReceiptData] = useState<{
    employeeName: string;
    jobTitle: string;
    department: string;
    phone: string;
    email: string | null;
    joinDate: string;
    projectName: string | null;
    amount: number;
    payDate: string;
  } | null>(null);

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

  useEffect(() => {
    if (showSheet) load();
  }, [showSheet, load]);

  const openSheet = () => {
    setShowSheet(true);
    setError(null);
  };

  const togglePaid = async (employeeId: number, checked: boolean) => {
    const row = rows.find(r => r.employeeId === employeeId);
    if (!row || row.isPaid || !checked) return;
    if (row.amount <= 0) { setError("Salary amount must be greater than zero."); return; }

    setError(null);
    setRows(prev => prev.map(r => r.employeeId === employeeId ? { ...r, saving: true } : r));
    try {
      const payDate = `${year}-${String(month).padStart(2, "0")}-01`;
      const res = await api(`/api/Employee/${employeeId}/salary`, {
        method: "POST",
        body: JSON.stringify({ amount: row.amount, payDate }),
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

  const openReceipt = async (row: BatchRow) => {
    if (!row.salaryRecordId) return;
    try {
      const [salaryRes, empRes] = await Promise.all([
        api(`/api/Employee/salary/${row.salaryRecordId}`),
        api(`/api/Employee/${row.employeeId}`),
      ]);
      if (!salaryRes.ok || !empRes.ok) return;
      const salary = await salaryRes.json() as Record<string, unknown>;
      const emp = await empRes.json() as Record<string, unknown>;
      setReceiptData({
        employeeName: String(salary.employeeName ?? row.employeeName),
        jobTitle: String(salary.jobTitle ?? row.jobTitle),
        department: String(salary.department ?? row.department),
        phone: String(emp.phone ?? emp.Phone ?? ""),
        email: emp.email != null ? String(emp.email) : emp.Email != null ? String(emp.Email) : null,
        joinDate: String(emp.joinDate ?? emp.JoinDate ?? ""),
        projectName: salary.projectName != null ? String(salary.projectName) : row.projectName,
        amount: Number(salary.amount ?? row.amount),
        payDate: String(salary.payDate ?? row.payDate ?? todayIso()),
      });
    } catch { /* ignore */ }
  };

  const monthLabel = new Date(year, month - 1, 1).toLocaleString("en-US", { month: "long", year: "numeric" });

  return (
    <div>
      <div className="mb-6 flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <h2 className="section-title mb-1">Salary Payments</h2>
          <p className="text-sm text-[var(--text-muted)]">Generate monthly salary for all employees</p>
        </div>
        {!showSheet && (
          <Button onClick={openSheet}>
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z"/><polyline points="14 2 14 8 20 8"/></svg>
            Open Salary Sheet
          </Button>
        )}
      </div>

      {!showSheet && (
        <div className="py-16 text-center">
          <p className="text-sm text-[var(--text-muted)]">Click <span className="font-medium text-[var(--text-secondary)]">Open Salary Sheet</span> to view and process salaries for {monthLabel}.</p>
        </div>
      )}

      {showSheet && (
        <>
          <div className="mb-4 flex flex-wrap items-end gap-3">
            <div>
              <label className="mb-1.5 block text-xs font-medium text-[var(--text-muted)]">Month</label>
              <input
                type="month"
                value={`${year}-${String(month).padStart(2, "0")}`}
                onChange={e => {
                  const [y, m] = e.target.value.split("-").map(Number);
                  if (y && m) setPeriod({ year: y, month: m });
                }}
                className="rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-3 py-2 text-sm text-[var(--text-primary)] outline-none focus:border-[var(--accent)]"
              />
            </div>
            <Button variant="ghost" onClick={() => setShowSheet(false)}>Close Sheet</Button>
          </div>

          {error && (
            <div className="mb-4 rounded-xl border border-rose-500/20 bg-rose-500/[0.06] px-4 py-3 text-sm text-rose-300">{error}</div>
          )}

          {loading ? (
            <div className="space-y-3">
              {[...Array(6)].map((_, i) => <div key={i} className="skeleton h-14 rounded-xl" />)}
            </div>
          ) : (
            <div className="overflow-x-auto rounded-xl border border-[var(--border)] bg-[var(--bg-card)] shadow-sm">
              <table className="min-w-[900px] w-full border-collapse text-left">
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
                          disabled={row.isPaid || row.saving}
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
                            onClick={() => openReceipt(row)}
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
        </>
      )}

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
