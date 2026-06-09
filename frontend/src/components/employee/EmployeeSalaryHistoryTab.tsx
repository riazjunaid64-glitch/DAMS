import { useCallback, useEffect, useState } from "react";
import { api } from "../../api/api.ts";
import Button from "../../lib/Button.tsx";
import Modal from "../../lib/Modal.tsx";
import SalarySlip from "../SalarySlip.tsx";
import type { EmployeeFromApi } from "../../utils/parseEmployee.ts";

interface SalaryRecord {
  id: number;
  amount: number;
  payDate: string;
  payMonth: number;
  payYear: number;
  projectName: string | null;
  notes: string | null;
  createdAt: string;
}

interface MonthSummary {
  month: number;
  year: number;
  monthLabel: string;
  count: number;
  totalAmount: number;
}

function formatCurrency(n: number) {
  return n.toLocaleString("en-US", { style: "currency", currency: "USD", minimumFractionDigits: 0 });
}

function formatDate(iso: string) {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return d.toLocaleDateString("en-US", { day: "numeric", month: "short", year: "numeric" });
}

interface Props {
  employee: EmployeeFromApi;
}

export default function EmployeeSalaryHistoryTab({ employee }: Props) {
  const [summaries, setSummaries] = useState<MonthSummary[]>([]);
  const [monthRecords, setMonthRecords] = useState<SalaryRecord[]>([]);
  const [selectedMonth, setSelectedMonth] = useState<MonthSummary | null>(null);
  const [loadingHistory, setLoadingHistory] = useState(false);
  const [loadingMonth, setLoadingMonth] = useState(false);
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
    notes: string | null;
  } | null>(null);

  const loadSummaries = useCallback(async () => {
    setLoadingHistory(true);
    try {
      const res = await api(`/api/Employee/${employee.id}/salary/summary`);
      if (!res.ok) return;
      const raw = await res.json() as Array<Record<string, unknown>>;
      setSummaries(raw.map(r => ({
        month: Number(r.month),
        year: Number(r.year),
        monthLabel: String(r.monthLabel ?? ""),
        count: Number(r.count),
        totalAmount: Number(r.totalAmount),
      })));
    } catch { /* ignore */ }
    finally { setLoadingHistory(false); }
  }, [employee.id]);

  useEffect(() => { loadSummaries(); }, [loadSummaries]);

  const loadMonthRecords = async (summary: MonthSummary) => {
    setSelectedMonth(summary);
    setLoadingMonth(true);
    try {
      const res = await api(`/api/Employee/${employee.id}/salary/month?month=${summary.month}&year=${summary.year}`);
      if (!res.ok) return;
      const raw = await res.json() as Array<Record<string, unknown>>;
      setMonthRecords(raw.map(r => ({
        id: Number(r.id),
        amount: Number(r.amount),
        payDate: String(r.payDate ?? ""),
        payMonth: Number(r.payMonth),
        payYear: Number(r.payYear),
        projectName: r.projectName != null ? String(r.projectName) : null,
        notes: r.notes != null ? String(r.notes) : null,
        createdAt: String(r.createdAt ?? ""),
      })));
    } catch { /* ignore */ }
    finally { setLoadingMonth(false); }
  };

  const openReceipt = (record: SalaryRecord) => {
    setReceiptData({
      employeeName: employee.fullName,
      jobTitle: employee.jobTitle,
      department: employee.department,
      phone: employee.phone,
      email: employee.email,
      joinDate: employee.joinDate,
      projectName: record.projectName,
      amount: record.amount,
      payDate: record.payDate,
      notes: record.notes,
    });
  };

  return (
    <div className="py-8 sm:py-10">
      <div className="mx-auto w-full max-w-7xl px-5 sm:px-8 lg:px-10">
        <div className="mb-6">
          <h2 className="section-title mb-1">Salary History</h2>
          <p className="text-sm text-[var(--text-muted)]">Past salary payments for this employee</p>
        </div>

        {loadingHistory ? (
          <div className="space-y-3">
            {[...Array(4)].map((_, i) => <div key={i} className="skeleton h-12 rounded-xl" />)}
          </div>
        ) : summaries.length === 0 ? (
          <div className="py-12 text-center text-sm text-[var(--text-muted)]">No salary records yet.</div>
        ) : (
          <div className="space-y-6">
            <div className="overflow-x-auto rounded-xl border border-[var(--border)] bg-[var(--bg-card)] shadow-sm">
              <table className="min-w-[600px] w-full border-collapse text-left">
                <thead>
                  <tr className="border-b border-[var(--border)] bg-[var(--surface-glass)]">
                    {["Month", "Records", "Total Amount", ""].map(h => (
                      <th key={h} className="px-5 py-3 text-[10px] font-bold uppercase tracking-wider text-[var(--text-muted)]">{h}</th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {summaries.map(s => (
                    <tr
                      key={`${s.year}-${s.month}`}
                      onClick={() => loadMonthRecords(s)}
                      className={`cursor-pointer border-b border-[var(--border)] transition-colors hover:bg-[var(--surface-glass-hover)] last:border-b-0 ${
                        selectedMonth?.year === s.year && selectedMonth?.month === s.month ? "bg-[var(--accent-glow)]" : ""
                      }`}
                    >
                      <td className="px-5 py-4 text-sm font-semibold text-[var(--text-primary)]">{s.monthLabel}</td>
                      <td className="px-5 py-4 text-sm text-[var(--text-secondary)]">{s.count}</td>
                      <td className="px-5 py-4 text-sm font-semibold text-rose-400">{formatCurrency(s.totalAmount)}</td>
                      <td className="px-5 py-4 text-right">
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" className="inline text-[var(--text-muted)]"><polyline points="9 18 15 12 9 6"/></svg>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            {selectedMonth && (
              <div>
                <h3 className="mb-4 text-sm font-semibold text-[var(--text-heading)]">
                  Records — {selectedMonth.monthLabel}
                </h3>
                {loadingMonth ? (
                  <div className="space-y-3">
                    {[...Array(3)].map((_, i) => <div key={i} className="skeleton h-12 rounded-xl" />)}
                  </div>
                ) : (
                  <div className="overflow-x-auto rounded-xl border border-[var(--border)] bg-[var(--bg-card)] shadow-sm">
                    <table className="min-w-[700px] w-full border-collapse text-left">
                      <thead>
                        <tr className="border-b border-[var(--border)] bg-[var(--surface-glass)]">
                          {["Pay Date", "Amount", "Project", "Notes", "Receipt"].map(h => (
                            <th key={h} className="px-5 py-3 text-[10px] font-bold uppercase tracking-wider text-[var(--text-muted)]">{h}</th>
                          ))}
                        </tr>
                      </thead>
                      <tbody>
                        {monthRecords.map(r => (
                          <tr key={r.id} className="border-b border-[var(--border)] transition-colors hover:bg-[var(--surface-glass-hover)] last:border-b-0">
                            <td className="px-5 py-4 text-sm font-medium text-[var(--text-primary)]">{formatDate(r.payDate)}</td>
                            <td className="px-5 py-4 text-sm font-semibold text-rose-400">{formatCurrency(r.amount)}</td>
                            <td className="px-5 py-4 text-sm text-[var(--text-secondary)]">{r.projectName ?? "General"}</td>
                            <td className="px-5 py-4 text-sm text-[var(--text-secondary)]">{r.notes ?? "—"}</td>
                            <td className="px-5 py-4">
                              <button
                                type="button"
                                onClick={() => openReceipt(r)}
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
            )}
          </div>
        )}
      </div>

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
