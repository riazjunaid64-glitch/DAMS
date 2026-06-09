import { useCallback, useEffect, useMemo, useState } from "react";
import type { ReactNode } from "react";
import { useNavigate } from "react-router-dom";
import { api } from "../api/api.ts";
import type { User } from "../App.tsx";
import Container from "../lib/Container.tsx";
import Button from "../lib/Button.tsx";

type Props = { user: User | null };

interface ProjectOption {
  id: number;
  projectName: string;
}

interface FinancialSummary {
  totalRevenue: number;
  automaticRevenue: number;
  manualRevenue: number;
  totalExpenses: number;
  netProfit: number;
  outstandingAmount: number;
  overdueAmount: number;
}

interface RevenueLine {
  date: string;
  projectId: number | null;
  projectName: string;
  revenueType: string;
  amount: number;
  source: string;
  reference: string | null;
  description: string | null;
  manualRevenueId: number | null;
}

interface ExpenseLine {
  id: number;
  date: string;
  projectId: number | null;
  projectName: string;
  category: string;
  amount: number;
  description: string | null;
  reference: string | null;
}

interface DashboardData {
  summary: FinancialSummary;
  revenue: RevenueLine[];
  expenses: ExpenseLine[];
}

const REVENUE_TYPES = [
  "Transfer Charges",
  "Documentation Charges",
  "Parking Charges",
  "Other Income",
];

const EXPENSE_CATEGORIES = [
  "Material",
  "Labor",
  "Salary",
  "Marketing",
  "Utility",
  "Transport",
  "Legal",
  "Office",
  "Other",
];

function formatMoney(n: number) {
  const sign = n < 0 ? "-" : "";
  return `${sign}Rs ${Math.abs(n).toLocaleString("en-PK", { maximumFractionDigits: 0 })}`;
}

function formatDate(date: string) {
  const parsed = new Date(date);
  if (Number.isNaN(parsed.getTime())) return "—";
  return parsed.toLocaleDateString("en-GB", { day: "2-digit", month: "short", year: "numeric" });
}

function todayInput() {
  return new Date().toISOString().slice(0, 10);
}

interface RevenueFormState {
  id: number | null;
  projectId: string;
  amount: string;
  revenueType: string;
  description: string;
  reference: string;
  date: string;
}

interface ExpenseFormState {
  id: number | null;
  projectId: string;
  amount: string;
  category: string;
  description: string;
  vendor: string;
  date: string;
}

const emptyRevenueForm = (): RevenueFormState => ({
  id: null,
  projectId: "",
  amount: "",
  revenueType: REVENUE_TYPES[0],
  description: "",
  reference: "",
  date: todayInput(),
});

const emptyExpenseForm = (): ExpenseFormState => ({
  id: null,
  projectId: "",
  amount: "",
  category: EXPENSE_CATEGORIES[0],
  description: "",
  vendor: "",
  date: todayInput(),
});

export default function FinanceDashboardPage({ user }: Props) {
  const navigate = useNavigate();
  const isAdmin = user?.role === "Admin";

  const [projects, setProjects] = useState<ProjectOption[]>([]);
  const [projectId, setProjectId] = useState<string>("");
  const [fromDate, setFromDate] = useState<string>("");
  const [toDate, setToDate] = useState<string>("");

  const [data, setData] = useState<DashboardData | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [tab, setTab] = useState<"revenue" | "expense">("revenue");

  const [revenueForm, setRevenueForm] = useState<RevenueFormState | null>(null);
  const [expenseForm, setExpenseForm] = useState<ExpenseFormState | null>(null);
  const [saving, setSaving] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  const loadProjects = useCallback(async () => {
    try {
      const res = await api("/api/Project", undefined, false);
      if (!res.ok) return;
      const raw: unknown = await res.json();
      if (Array.isArray(raw)) {
        setProjects(
          raw
            .map((p) => {
              const obj = p as Record<string, unknown>;
              return { id: Number(obj.id), projectName: String(obj.projectName ?? "") };
            })
            .filter((p) => Number.isFinite(p.id) && p.projectName)
        );
      }
    } catch {
      /* dropdown is non-critical */
    }
  }, []);

  const loadDashboard = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const params = new URLSearchParams();
      if (projectId) params.set("projectId", projectId);
      if (fromDate) params.set("from", fromDate);
      if (toDate) params.set("to", toDate);
      const qs = params.toString();
      const res = await api(`/api/Finance/dashboard${qs ? `?${qs}` : ""}`);
      if (!res.ok) throw new Error("Failed to load dashboard");
      const json: DashboardData = await res.json();
      setData(json);
    } catch {
      setError("Unable to load the financial dashboard.");
    } finally {
      setLoading(false);
    }
  }, [projectId, fromDate, toDate]);

  useEffect(() => {
    if (!isAdmin) {
      navigate("/");
      return;
    }
    loadProjects();
  }, [isAdmin, navigate, loadProjects]);

  useEffect(() => {
    if (isAdmin) loadDashboard();
  }, [isAdmin, loadDashboard]);

  const summaryCards = useMemo(() => {
    const s = data?.summary;
    return [
      { label: "Total Revenue", value: s?.totalRevenue ?? 0, color: "text-emerald-400", bg: "bg-emerald-500/10", border: "border-emerald-500/20" },
      { label: "Total Expenses", value: s?.totalExpenses ?? 0, color: "text-rose-400", bg: "bg-rose-500/10", border: "border-rose-500/20" },
      { label: "Net Profit", value: s?.netProfit ?? 0, color: (s?.netProfit ?? 0) >= 0 ? "text-indigo-400" : "text-rose-400", bg: "bg-indigo-500/10", border: "border-indigo-500/20" },
      { label: "Outstanding", value: s?.outstandingAmount ?? 0, color: "text-amber-400", bg: "bg-amber-500/10", border: "border-amber-500/20" },
      { label: "Overdue", value: s?.overdueAmount ?? 0, color: "text-orange-400", bg: "bg-orange-500/10", border: "border-orange-500/20" },
    ];
  }, [data]);

  const resetForms = () => {
    setRevenueForm(null);
    setExpenseForm(null);
    setFormError(null);
    setSaving(false);
  };

  const submitRevenue = async () => {
    if (!revenueForm) return;
    setFormError(null);
    const amount = Number(revenueForm.amount);
    if (!Number.isFinite(amount) || amount <= 0) {
      setFormError("Enter a valid amount greater than zero.");
      return;
    }
    if (!revenueForm.revenueType.trim()) {
      setFormError("Revenue type is required.");
      return;
    }
    setSaving(true);
    try {
      const body = JSON.stringify({
        projectId: revenueForm.projectId ? Number(revenueForm.projectId) : null,
        amount,
        revenueType: revenueForm.revenueType.trim(),
        description: revenueForm.description.trim() || null,
        reference: revenueForm.reference.trim() || null,
        date: revenueForm.date || null,
      });
      const res = revenueForm.id
        ? await api(`/api/Finance/revenue/${revenueForm.id}`, { method: "PUT", body })
        : await api("/api/Finance/revenue", { method: "POST", body });
      if (!res.ok) {
        const d = await res.json().catch(() => ({}));
        setFormError(d.message || "Failed to save revenue entry.");
        return;
      }
      resetForms();
      await loadDashboard();
    } catch {
      setFormError("Something went wrong while saving.");
    } finally {
      setSaving(false);
    }
  };

  const submitExpense = async () => {
    if (!expenseForm) return;
    setFormError(null);
    const amount = Number(expenseForm.amount);
    if (!Number.isFinite(amount) || amount <= 0) {
      setFormError("Enter a valid amount greater than zero.");
      return;
    }
    if (!expenseForm.category.trim()) {
      setFormError("Category is required.");
      return;
    }
    setSaving(true);
    try {
      const body = JSON.stringify({
        projectId: expenseForm.projectId ? Number(expenseForm.projectId) : null,
        amount,
        category: expenseForm.category.trim(),
        description: expenseForm.description.trim() || null,
        vendor: expenseForm.vendor.trim() || null,
        date: expenseForm.date || null,
      });
      const res = expenseForm.id
        ? await api(`/api/Finance/expenses/${expenseForm.id}`, { method: "PUT", body })
        : await api("/api/Finance/expenses", { method: "POST", body });
      if (!res.ok) {
        const d = await res.json().catch(() => ({}));
        setFormError(d.message || "Failed to save expense.");
        return;
      }
      resetForms();
      await loadDashboard();
    } catch {
      setFormError("Something went wrong while saving.");
    } finally {
      setSaving(false);
    }
  };

  const deleteRevenue = async (id: number) => {
    if (!window.confirm("Delete this manual revenue entry?")) return;
    const res = await api(`/api/Finance/revenue/${id}`, { method: "DELETE" });
    if (res.ok) await loadDashboard();
    else alert("Failed to delete revenue entry.");
  };

  const deleteExpense = async (id: number) => {
    if (!window.confirm("Delete this expense?")) return;
    const res = await api(`/api/Finance/expenses/${id}`, { method: "DELETE" });
    if (res.ok) await loadDashboard();
    else alert("Failed to delete expense.");
  };

  const editRevenue = (row: RevenueLine) => {
    if (row.manualRevenueId == null) return;
    setExpenseForm(null);
    setFormError(null);
    setRevenueForm({
      id: row.manualRevenueId,
      projectId: row.projectId != null ? String(row.projectId) : "",
      amount: String(row.amount),
      revenueType: row.revenueType,
      description: row.description ?? "",
      reference: row.reference ?? "",
      date: row.date.slice(0, 10),
    });
  };

  const editExpense = (row: ExpenseLine) => {
    setRevenueForm(null);
    setFormError(null);
    setExpenseForm({
      id: row.id,
      projectId: row.projectId != null ? String(row.projectId) : "",
      amount: String(row.amount),
      category: row.category,
      description: row.description ?? "",
      vendor: row.reference ?? "",
      date: row.date.slice(0, 10),
    });
  };

  if (!isAdmin) return null;

  const revenue = data?.revenue ?? [];
  const expenses = data?.expenses ?? [];

  return (
    <>
      {/* Header */}
      <div className="relative overflow-hidden border-b border-[var(--border)]">
        <div className="absolute inset-0 mesh-gradient-subtle" />
        <Container className="relative py-8 sm:py-10">
          <div className="flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
            <div>
              <div className="mb-2 inline-flex items-center gap-2 rounded-full bg-indigo-500/[0.08] px-3 py-1">
                <span className="h-1.5 w-1.5 rounded-full bg-indigo-500" />
                <span className="text-[10px] font-semibold uppercase tracking-wider text-indigo-500">
                  Admin Module
                </span>
              </div>
              <h1 className="text-2xl font-bold text-[var(--text-heading)] sm:text-3xl">
                Financial Dashboard
              </h1>
              <p className="mt-1 text-sm text-[var(--text-muted)]">
                Revenue, expenses and profitability across all projects
              </p>
            </div>

            {/* Filters */}
            <div className="flex flex-wrap items-end gap-3">
              <div className="flex flex-col gap-1">
                <label className="text-[10px] font-semibold uppercase tracking-wider text-[var(--text-muted)]">Project</label>
                <select
                  value={projectId}
                  onChange={(e) => setProjectId(e.target.value)}
                  className="rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-4 py-2.5 text-sm text-[var(--text-primary)] transition-all focus:border-[var(--accent)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-glow)]"
                >
                  <option value="">All Projects</option>
                  {projects.map((p) => (
                    <option key={p.id} value={p.id}>{p.projectName}</option>
                  ))}
                </select>
              </div>
              <div className="flex flex-col gap-1">
                <label className="text-[10px] font-semibold uppercase tracking-wider text-[var(--text-muted)]">From</label>
                <input
                  type="date"
                  value={fromDate}
                  onChange={(e) => setFromDate(e.target.value)}
                  className="rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-3 py-2.5 text-sm text-[var(--text-primary)] transition-all focus:border-[var(--accent)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-glow)]"
                />
              </div>
              <div className="flex flex-col gap-1">
                <label className="text-[10px] font-semibold uppercase tracking-wider text-[var(--text-muted)]">To</label>
                <input
                  type="date"
                  value={toDate}
                  onChange={(e) => setToDate(e.target.value)}
                  className="rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-3 py-2.5 text-sm text-[var(--text-primary)] transition-all focus:border-[var(--accent)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-glow)]"
                />
              </div>
              {(fromDate || toDate) && (
                <Button variant="ghost" size="sm" onClick={() => { setFromDate(""); setToDate(""); }}>
                  Clear
                </Button>
              )}
            </div>
          </div>

          {/* Summary cards */}
          <div className="mt-6 grid grid-cols-2 gap-3 md:grid-cols-3 xl:grid-cols-5">
            {summaryCards.map((card) => (
              <div
                key={card.label}
                className={`rounded-xl border bg-[var(--bg-card)] px-4 py-4 shadow-sm ${card.border}`}
              >
                <p className="text-xs text-[var(--text-muted)]">{card.label}</p>
                <p className={`mt-1.5 text-lg font-bold sm:text-xl ${card.color}`}>
                  {loading ? "…" : formatMoney(card.value)}
                </p>
              </div>
            ))}
          </div>

          {data && (
            <p className="mt-3 text-xs text-[var(--text-muted)]">
              Revenue breakdown: {formatMoney(data.summary.automaticRevenue)} from payments
              {" + "}
              {formatMoney(data.summary.manualRevenue)} manual
            </p>
          )}
        </Container>
      </div>

      {/* Content */}
      <Container className="py-8">
        {/* Tabs + actions */}
        <div className="mb-5 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
          <div className="inline-flex rounded-xl border border-[var(--border)] bg-[var(--surface-glass)] p-1">
            <button
              type="button"
              onClick={() => setTab("revenue")}
              className={`rounded-lg px-4 py-2 text-sm font-semibold transition-all ${
                tab === "revenue"
                  ? "bg-[var(--bg-card)] text-[var(--accent)] shadow-sm"
                  : "text-[var(--text-secondary)] hover:text-[var(--text-primary)]"
              }`}
            >
              Revenue
            </button>
            <button
              type="button"
              onClick={() => setTab("expense")}
              className={`rounded-lg px-4 py-2 text-sm font-semibold transition-all ${
                tab === "expense"
                  ? "bg-[var(--bg-card)] text-[var(--accent)] shadow-sm"
                  : "text-[var(--text-secondary)] hover:text-[var(--text-primary)]"
              }`}
            >
              Expenses
            </button>
          </div>
          <div className="flex gap-2">
            <Button size="sm" variant="outline" onClick={() => { setExpenseForm(null); setFormError(null); setRevenueForm(emptyRevenueForm()); }}>
              + Add Revenue
            </Button>
            <Button size="sm" onClick={() => { setRevenueForm(null); setFormError(null); setExpenseForm(emptyExpenseForm()); }}>
              + Add Expense
            </Button>
          </div>
        </div>

        {error && (
          <div className="mb-4 rounded-2xl border border-rose-500/20 bg-rose-500/[0.06] px-5 py-4 text-sm text-rose-300">
            {error}
          </div>
        )}

        {/* Revenue table */}
        {tab === "revenue" && (
          <div className="overflow-x-auto rounded-xl border border-[var(--border)] bg-[var(--bg-card)] shadow-sm">
            <table className="min-w-[900px] w-full border-collapse text-left">
              <thead>
                <tr className="border-b border-[var(--border)] bg-[var(--surface-glass)]">
                  {["Date", "Project", "Revenue Type", "Amount", "Source", "Reference", ""].map((h) => (
                    <th key={h} className="px-5 py-3 text-[10px] font-bold uppercase tracking-wider text-[var(--text-muted)]">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {loading ? (
                  <tr><td colSpan={7} className="px-5 py-10 text-center text-sm text-[var(--text-muted)]">Loading…</td></tr>
                ) : revenue.length === 0 ? (
                  <tr><td colSpan={7} className="px-5 py-10 text-center text-sm text-[var(--text-muted)]">No revenue for the selected filters.</td></tr>
                ) : (
                  revenue.map((row, i) => (
                    <tr key={`${row.source}-${row.manualRevenueId ?? "p"}-${i}`} className="border-b border-[var(--border)] transition-colors hover:bg-[var(--surface-glass-hover)] last:border-b-0">
                      <td className="px-5 py-3.5 text-sm text-[var(--text-secondary)] whitespace-nowrap">{formatDate(row.date)}</td>
                      <td className="px-5 py-3.5 text-sm text-[var(--text-primary)]">{row.projectName}</td>
                      <td className="px-5 py-3.5 text-sm text-[var(--text-primary)]">{row.revenueType}</td>
                      <td className="px-5 py-3.5 text-sm font-semibold text-emerald-400 whitespace-nowrap">{formatMoney(row.amount)}</td>
                      <td className="px-5 py-3.5">
                        <span className={`inline-flex rounded-full border px-2.5 py-1 text-[10px] font-semibold ${
                          row.source === "Manual Revenue"
                            ? "text-violet-400 bg-violet-500/10 border-violet-500/20"
                            : "text-sky-400 bg-sky-500/10 border-sky-500/20"
                        }`}>
                          {row.source}
                        </span>
                      </td>
                      <td className="px-5 py-3.5 text-sm text-[var(--text-secondary)] max-w-[260px] truncate">{row.reference || "—"}</td>
                      <td className="px-5 py-3.5 whitespace-nowrap text-right">
                        {row.manualRevenueId != null && (
                          <div className="inline-flex gap-2">
                            <button onClick={() => editRevenue(row)} className="text-xs font-semibold text-[var(--accent)] hover:underline">Edit</button>
                            <button onClick={() => deleteRevenue(row.manualRevenueId!)} className="text-xs font-semibold text-rose-400 hover:underline">Delete</button>
                          </div>
                        )}
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        )}

        {/* Expense table */}
        {tab === "expense" && (
          <div className="overflow-x-auto rounded-xl border border-[var(--border)] bg-[var(--bg-card)] shadow-sm">
            <table className="min-w-[900px] w-full border-collapse text-left">
              <thead>
                <tr className="border-b border-[var(--border)] bg-[var(--surface-glass)]">
                  {["Date", "Project", "Category", "Amount", "Description", "Reference", ""].map((h) => (
                    <th key={h} className="px-5 py-3 text-[10px] font-bold uppercase tracking-wider text-[var(--text-muted)]">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {loading ? (
                  <tr><td colSpan={7} className="px-5 py-10 text-center text-sm text-[var(--text-muted)]">Loading…</td></tr>
                ) : expenses.length === 0 ? (
                  <tr><td colSpan={7} className="px-5 py-10 text-center text-sm text-[var(--text-muted)]">No expenses for the selected filters.</td></tr>
                ) : (
                  expenses.map((row) => (
                    <tr key={row.id} className="border-b border-[var(--border)] transition-colors hover:bg-[var(--surface-glass-hover)] last:border-b-0">
                      <td className="px-5 py-3.5 text-sm text-[var(--text-secondary)] whitespace-nowrap">{formatDate(row.date)}</td>
                      <td className="px-5 py-3.5 text-sm text-[var(--text-primary)]">{row.projectName}</td>
                      <td className="px-5 py-3.5 text-sm text-[var(--text-primary)]">{row.category}</td>
                      <td className="px-5 py-3.5 text-sm font-semibold text-rose-400 whitespace-nowrap">{formatMoney(row.amount)}</td>
                      <td className="px-5 py-3.5 text-sm text-[var(--text-secondary)] max-w-[260px] truncate">{row.description || "—"}</td>
                      <td className="px-5 py-3.5 text-sm text-[var(--text-secondary)] max-w-[200px] truncate">{row.reference || "—"}</td>
                      <td className="px-5 py-3.5 whitespace-nowrap text-right">
                        <div className="inline-flex gap-2">
                          <button onClick={() => editExpense(row)} className="text-xs font-semibold text-[var(--accent)] hover:underline">Edit</button>
                          <button onClick={() => deleteExpense(row.id)} className="text-xs font-semibold text-rose-400 hover:underline">Delete</button>
                        </div>
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        )}
      </Container>

      {/* Revenue modal */}
      {revenueForm && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
          <div className="absolute inset-0 bg-black/60 backdrop-blur-sm animate-fade-in" onClick={resetForms} />
          <div className="relative z-10 w-[460px] max-w-[92vw] animate-scale-in rounded-2xl border border-[var(--border)] bg-[var(--modal-bg)] shadow-2xl">
            <div className="border-b border-[var(--border)] px-6 py-4">
              <h3 className="text-lg font-semibold text-[var(--text-heading)]">
                {revenueForm.id ? "Edit Manual Revenue" : "Add Manual Revenue"}
              </h3>
            </div>
            <div className="space-y-4 p-6">
              {formError && <p className="rounded-lg bg-rose-500/10 px-3 py-2 text-sm text-rose-300">{formError}</p>}
              <FormSelect label="Project" value={revenueForm.projectId} onChange={(v) => setRevenueForm({ ...revenueForm, projectId: v })}>
                <option value="">General (no specific project)</option>
                {projects.map((p) => <option key={p.id} value={p.id}>{p.projectName}</option>)}
              </FormSelect>
              <FormSelect label="Revenue Type" value={revenueForm.revenueType} onChange={(v) => setRevenueForm({ ...revenueForm, revenueType: v })}>
                {REVENUE_TYPES.map((t) => <option key={t} value={t}>{t}</option>)}
              </FormSelect>
              <FormInput label="Amount (Rs)" type="number" value={revenueForm.amount} onChange={(v) => setRevenueForm({ ...revenueForm, amount: v })} />
              <FormInput label="Date" type="date" value={revenueForm.date} onChange={(v) => setRevenueForm({ ...revenueForm, date: v })} />
              <FormInput label="Reference (optional)" value={revenueForm.reference} onChange={(v) => setRevenueForm({ ...revenueForm, reference: v })} />
              <FormInput label="Description (optional)" value={revenueForm.description} onChange={(v) => setRevenueForm({ ...revenueForm, description: v })} />
            </div>
            <div className="flex justify-end gap-3 border-t border-[var(--border)] px-6 py-4">
              <Button variant="ghost" onClick={resetForms} disabled={saving}>Cancel</Button>
              <Button onClick={submitRevenue} disabled={saving}>{saving ? "Saving…" : "Save"}</Button>
            </div>
          </div>
        </div>
      )}

      {/* Expense modal */}
      {expenseForm && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
          <div className="absolute inset-0 bg-black/60 backdrop-blur-sm animate-fade-in" onClick={resetForms} />
          <div className="relative z-10 w-[460px] max-w-[92vw] animate-scale-in rounded-2xl border border-[var(--border)] bg-[var(--modal-bg)] shadow-2xl">
            <div className="border-b border-[var(--border)] px-6 py-4">
              <h3 className="text-lg font-semibold text-[var(--text-heading)]">
                {expenseForm.id ? "Edit Expense" : "Add Expense"}
              </h3>
            </div>
            <div className="space-y-4 p-6">
              {formError && <p className="rounded-lg bg-rose-500/10 px-3 py-2 text-sm text-rose-300">{formError}</p>}
              <FormSelect label="Project" value={expenseForm.projectId} onChange={(v) => setExpenseForm({ ...expenseForm, projectId: v })}>
                <option value="">General (no specific project)</option>
                {projects.map((p) => <option key={p.id} value={p.id}>{p.projectName}</option>)}
              </FormSelect>
              <FormSelect label="Category" value={expenseForm.category} onChange={(v) => setExpenseForm({ ...expenseForm, category: v })}>
                {EXPENSE_CATEGORIES.map((c) => <option key={c} value={c}>{c}</option>)}
              </FormSelect>
              <FormInput label="Amount (Rs)" type="number" value={expenseForm.amount} onChange={(v) => setExpenseForm({ ...expenseForm, amount: v })} />
              <FormInput label="Date" type="date" value={expenseForm.date} onChange={(v) => setExpenseForm({ ...expenseForm, date: v })} />
              <FormInput label="Vendor / Reference (optional)" value={expenseForm.vendor} onChange={(v) => setExpenseForm({ ...expenseForm, vendor: v })} />
              <FormInput label="Description (optional)" value={expenseForm.description} onChange={(v) => setExpenseForm({ ...expenseForm, description: v })} />
            </div>
            <div className="flex justify-end gap-3 border-t border-[var(--border)] px-6 py-4">
              <Button variant="ghost" onClick={resetForms} disabled={saving}>Cancel</Button>
              <Button onClick={submitExpense} disabled={saving}>{saving ? "Saving…" : "Save"}</Button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}

function FormInput({ label, value, onChange, type = "text" }: { label: string; value: string; onChange: (v: string) => void; type?: string }) {
  return (
    <div className="flex flex-col gap-1.5">
      <label className="text-xs font-medium text-[var(--text-secondary)]">{label}</label>
      <input
        type={type}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className="w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-3 py-2.5 text-sm text-[var(--text-primary)] placeholder:text-[var(--text-muted)] transition-all focus:border-[var(--accent)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-glow)]"
      />
    </div>
  );
}

function FormSelect({ label, value, onChange, children }: { label: string; value: string; onChange: (v: string) => void; children: ReactNode }) {
  return (
    <div className="flex flex-col gap-1.5">
      <label className="text-xs font-medium text-[var(--text-secondary)]">{label}</label>
      <select
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className="w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-3 py-2.5 text-sm text-[var(--text-primary)] transition-all focus:border-[var(--accent)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-glow)]"
      >
        {children}
      </select>
    </div>
  );
}
