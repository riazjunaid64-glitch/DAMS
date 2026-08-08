import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { ReactNode } from "react";
import { Link, useNavigate } from "react-router-dom";
import { api } from "../api/api.ts";
import type { User } from "../App.tsx";
import Button from "../lib/Button.tsx";
import VirtualInfiniteTable from "../lib/VirtualInfiniteTable.tsx";
import type { Column } from "../lib/VirtualInfiniteTable.tsx";
import { usePaginatedRows } from "../lib/usePaginatedRows.ts";
import { fetchFinanceChartData, type FinanceChartData, type FinancePeriod } from "../lib/financeChartData.ts";
import FinanceCharts from "../components/FinanceCharts.tsx";
import FinanceAttachmentField from "../components/FinanceAttachmentField.tsx";
import {
  financeApiError,
  openFinanceAttachment,
  type FinanceAttachmentInfo,
  type FinanceRecordKind,
} from "../api/financeAttachments.ts";

type Props = { user: User | null };

interface ProjectOption {
  id: number;
  projectName: string;
}

interface FinanceAccountOption {
  id: number;
  name: string;
  accountHolderName: string;
  isActive: boolean;
}

interface FinancialSummary {
  totalRevenue: number;
  automaticRevenue: number;
  manualRevenue: number;
  totalExpenses: number;
  netProfit: number;
  outstandingAmount: number;
  overdueAmount: number;
  accountOpeningBalance: number | null;
  accountCurrentBalance: number | null;
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
  financeAccountId: number | null;
  financeAccountName: string | null;
  accountHolderName: string | null;
  attachment: FinanceAttachmentInfo | null;
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
  financeAccountId: number | null;
  financeAccountName: string | null;
  accountHolderName: string | null;
  attachment: FinanceAttachmentInfo | null;
}

interface OutstandingLine {
  bookingReference: string;
  customerName: string;
  projectId: number | null;
  projectName: string;
  unitNumber: string;
  agreedSalePrice: number;
  receivedAmount: number;
  outstandingAmount: number;
}

interface OverdueLine {
  bookingReference: string;
  customerName: string;
  projectId: number | null;
  projectName: string;
  unitNumber: string;
  sequenceNumber: number;
  installmentType: string;
  dueDate: string;
  amount: number;
  paidAmount: number;
  overdueAmount: number;
}

interface NetProfitLine {
  date: string;
  projectName: string;
  label: string;
  kind: "revenue" | "expense";
  amount: number;
}

type AnyRow = RevenueLine | ExpenseLine | OutstandingLine | OverdueLine | NetProfitLine;

type View = "revenue" | "expense" | "netProfit" | "outstanding" | "overdue";

// API view query value for each card view.
const VIEW_PARAM: Record<View, string> = {
  revenue: "revenue",
  expense: "expense",
  netProfit: "netProfit",
  outstanding: "outstanding",
  overdue: "overdue",
};

const VIEW_TITLES: Record<View, string> = {
  revenue: "Revenue",
  expense: "Expenses",
  netProfit: "Net Profit Breakdown",
  outstanding: "Outstanding Balances",
  overdue: "Overdue Installments",
};

// Ancillary developer revenue (charges NOT auto-captured by booking/installment/possession
// payments). Grounded in standard Pakistani housing-society / developer charge heads.
const REVENUE_TYPES = [
  "Transfer Charges",
  "Development Charges",
  "Possession Charges",
  "Membership Charges",
  "Documentation Charges",
  "NOC / NDC Charges",
  "Utility Connection Charges",
  "Parking Charges",
  "Late Payment Surcharge",
  "Cancellation / Forfeiture",
  "Rental Income",
  "Commission Income",
  "Other Income",
];

// Sentinel used by the Revenue Type <select> to switch into free-text entry.
const CUSTOM_TYPE = "__custom__";

// Developer / construction expense heads. Original values kept for backward compatibility.
const EXPENSE_CATEGORIES = [
  "Material",
  "Labor",
  "Construction",
  "Land Acquisition",
  "Salary",
  "Marketing",
  "Commission",
  "Permits & Approvals",
  "Utility",
  "Transport",
  "Maintenance",
  "Legal",
  "Office",
  "Taxes & Fees",
  "Other",
];

function formatMoney(n: number) {
  const sign = n < 0 ? "-" : "";
  // Show paisa when present so rows visibly add up to the totals (whole amounts stay clean).
  return `${sign}Rs ${Math.abs(n).toLocaleString("en-PK", { maximumFractionDigits: 2 })}`;
}

function formatDate(date: string) {
  const parsed = new Date(date);
  if (Number.isNaN(parsed.getTime())) return "—";
  return parsed.toLocaleDateString("en-GB", { day: "2-digit", month: "short", year: "numeric" });
}

function todayInput() {
  return new Date().toISOString().slice(0, 10);
}

type Period = "today" | "month" | "year" | "all" | "custom";

const PERIODS: { value: Exclude<Period, "custom">; label: string }[] = [
  { value: "today", label: "Today" },
  { value: "month", label: "This Month" },
  { value: "year", label: "This Year" },
  { value: "all", label: "All" },
];

// Format a Date to a local YYYY-MM-DD (avoids the UTC day-shift of toISOString).
function fmtLocal(d: Date) {
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

// Resolve a preset to a {from, to} range the existing endpoint already understands.
function periodRange(period: Period): { from: string; to: string } {
  const now = new Date();
  switch (period) {
    case "today": {
      const t = fmtLocal(now);
      return { from: t, to: t };
    }
    case "month":
      return {
        from: fmtLocal(new Date(now.getFullYear(), now.getMonth(), 1)),
        to: fmtLocal(new Date(now.getFullYear(), now.getMonth() + 1, 0)),
      };
    case "year":
      return {
        from: fmtLocal(new Date(now.getFullYear(), 0, 1)),
        to: fmtLocal(new Date(now.getFullYear(), 11, 31)),
      };
    default:
      return { from: "", to: "" };
  }
}

interface RevenueFormState {
  id: number | null;
  projectId: string;
  financeAccountId: string;
  amount: string;
  revenueType: string;
  description: string;
  reference: string;
  date: string;
  attachment: FinanceAttachmentInfo | null;
  selectedAttachment: File | null;
  removeAttachment: boolean;
}

interface ExpenseFormState {
  id: number | null;
  projectId: string;
  financeAccountId: string;
  amount: string;
  category: string;
  description: string;
  vendor: string;
  date: string;
  attachment: FinanceAttachmentInfo | null;
  selectedAttachment: File | null;
  removeAttachment: boolean;
}

const emptyRevenueForm = (): RevenueFormState => ({
  id: null,
  projectId: "",
  financeAccountId: "",
  amount: "",
  revenueType: REVENUE_TYPES[0],
  description: "",
  reference: "",
  date: todayInput(),
  attachment: null,
  selectedAttachment: null,
  removeAttachment: false,
});

const emptyExpenseForm = (): ExpenseFormState => ({
  id: null,
  projectId: "",
  financeAccountId: "",
  amount: "",
  category: EXPENSE_CATEGORIES[0],
  description: "",
  vendor: "",
  date: todayInput(),
  attachment: null,
  selectedAttachment: null,
  removeAttachment: false,
});

export default function FinanceDashboardPage({ user }: Props) {
  const navigate = useNavigate();
  const isAdmin = user?.role === "Admin";

  const [projects, setProjects] = useState<ProjectOption[]>([]);
  const [financeAccounts, setFinanceAccounts] = useState<FinanceAccountOption[]>([]);
  const [projectId, setProjectId] = useState<string>("");
  const [accountFilter, setAccountFilter] = useState<string>("");
  const [fromDate, setFromDate] = useState<string>("");
  const [toDate, setToDate] = useState<string>("");

  const [summary, setSummary] = useState<FinancialSummary | null>(null);
  const [summaryLoading, setSummaryLoading] = useState(true);
  const [view, setView] = useState<View>("revenue");

  const [chartData, setChartData] = useState<FinanceChartData | null>(null);
  const [chartLoading, setChartLoading] = useState(true);
  const [chartTick, setChartTick] = useState(0);

  // Paged rows for the active view (infinite scroll). Switching view or filters resets it.
  const { rows, loading, loadingMore, hasMore, error, loadMore, reload } =
    usePaginatedRows<AnyRow>(VIEW_PARAM[view], projectId, fromDate, toDate, accountFilter);

  const [revenueForm, setRevenueForm] = useState<RevenueFormState | null>(null);
  const [expenseForm, setExpenseForm] = useState<ExpenseFormState | null>(null);
  const [saving, setSaving] = useState(false);
  const savingRef = useRef(false);
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

  const loadFinanceAccounts = useCallback(async () => {
    try {
      const res = await api("/api/finance/accounts/options?includeInactive=true");
      if (res.ok) setFinanceAccounts(await res.json());
    } catch {
      /* The form will retain its validation message if accounts cannot be loaded. */
    }
  }, []);

  const loadSummary = useCallback(async () => {
    setSummaryLoading(true);
    try {
      const params = new URLSearchParams();
      if (projectId) params.set("projectId", projectId);
      if (fromDate) params.set("from", fromDate);
      if (toDate) params.set("to", toDate);
      if (accountFilter) params.set("account", accountFilter);
      const qs = params.toString();
      const res = await api(`/api/Finance/summary${qs ? `?${qs}` : ""}`);
      if (!res.ok) throw new Error("Failed to load summary");
      setSummary(await res.json());
    } catch {
      /* summary cards fall back to 0 */
    } finally {
      setSummaryLoading(false);
    }
  }, [projectId, fromDate, toDate, accountFilter]);

  // After a create/edit/delete, refresh the totals, the visible rows, and the charts.
  const refreshAll = useCallback(async () => {
    await loadSummary();
    reload();
    setChartTick((t) => t + 1);
  }, [loadSummary, reload]);

  useEffect(() => {
    if (!isAdmin) {
      navigate("/");
      return;
    }
    loadProjects();
    loadFinanceAccounts();
  }, [isAdmin, navigate, loadProjects, loadFinanceAccounts]);

  useEffect(() => {
    if (isAdmin) loadSummary();
  }, [isAdmin, loadSummary]);

  // Which quick-period chip (if any) matches the current from/to selection.
  const activePeriod = useMemo<Period>(() => {
    if (!fromDate && !toDate) return "all";
    for (const p of PERIODS) {
      if (p.value === "all") continue;
      const r = periodRange(p.value);
      if (r.from === fromDate && r.to === toDate) return p.value;
    }
    return "custom";
  }, [fromDate, toDate]);

  const applyPeriod = useCallback((period: Period) => {
    const r = periodRange(period);
    setFromDate(r.from);
    setToDate(r.to);
  }, []);

  // Charts are derived from the same summary endpoint the KPI cards use — bucketed over
  // the active date range and split per project — so they always match the totals and
  // respond to every filter (period, project, account, and the From/To range).
  useEffect(() => {
    if (!isAdmin) return;
    const controller = new AbortController();
    setChartLoading(true);
    fetchFinanceChartData(
      {
        projectId,
        from: fromDate,
        to: toDate,
        account: accountFilter,
        period: activePeriod as FinancePeriod,
        projects: projects.map((p) => ({ id: p.id, projectName: p.projectName })),
      },
      controller.signal,
    )
      .then((data) => {
        if (!controller.signal.aborted) {
          setChartData(data);
          setChartLoading(false);
        }
      })
      .catch(() => {
        if (!controller.signal.aborted) {
          setChartData({ series: [], distribution: [] });
          setChartLoading(false);
        }
      });
    return () => controller.abort();
  }, [isAdmin, projectId, fromDate, toDate, accountFilter, activePeriod, projects, chartTick]);

  const summaryCards = useMemo(() => {
    const s = summary;
    return [
      { label: "Total Revenue", value: s?.totalRevenue ?? 0, valueColor: "text-[var(--app-text)]", underline: "#34d399", view: "revenue" as View },
      { label: "Total Expenses", value: s?.totalExpenses ?? 0, valueColor: "text-[var(--app-text)]", underline: "#fb7185", view: "expense" as View },
      { label: "Net Profit", value: s?.netProfit ?? 0, valueColor: (s?.netProfit ?? 0) >= 0 ? "text-[var(--gold-bright)]" : "text-rose-400", underline: "#cba95c", view: "netProfit" as View },
      { label: "Outstanding", value: s?.outstandingAmount ?? 0, valueColor: "text-[var(--app-text)]", underline: "#60a5fa", view: "outstanding" as View },
      { label: "Overdue", value: s?.overdueAmount ?? 0, valueColor: "text-[var(--app-text-muted)]", underline: "#6b7280", view: "overdue" as View },
    ];
  }, [summary]);

  // Clicking any card just switches the view below in place (like the period
  // chips), keeping whatever project/period filters are already applied. No
  // page scroll — animating a smooth scroll while the table reloads/resizes
  // made the transition feel jerky.
  const focusView = useCallback((target: View) => {
    setView(target);
  }, []);

  const resetForms = () => {
    setRevenueForm(null);
    setExpenseForm(null);
    setFormError(null);
    setSaving(false);
    savingRef.current = false;
  };

  const startSaving = () => {
    if (savingRef.current) return false;
    savingRef.current = true;
    setSaving(true);
    return true;
  };

  const finishSaving = () => {
    savingRef.current = false;
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
    if (!revenueForm.financeAccountId) {
      setFormError("Select the account where this revenue was received.");
      return;
    }
    if (!startSaving()) return;
    try {
      const body = new FormData();
      if (revenueForm.projectId) body.append("projectId", revenueForm.projectId);
      body.append("financeAccountId", revenueForm.financeAccountId);
      body.append("amount", String(amount));
      body.append("revenueType", revenueForm.revenueType.trim());
      body.append("description", revenueForm.description.trim());
      body.append("reference", revenueForm.reference.trim());
      if (revenueForm.date) body.append("date", revenueForm.date);
      if (revenueForm.selectedAttachment) body.append("attachment", revenueForm.selectedAttachment);
      if (revenueForm.removeAttachment) body.append("removeAttachment", "true");
      const res = revenueForm.id
        ? await api(`/api/Finance/revenue/${revenueForm.id}/form`, { method: "PUT", body })
        : await api("/api/Finance/revenue/form", { method: "POST", body });
      if (!res.ok) {
        setFormError(await financeApiError(res, "Failed to save revenue entry."));
        return;
      }
      resetForms();
      await refreshAll();
    } catch {
      setFormError("The revenue entry could not be saved. Check your connection and try again.");
    } finally {
      finishSaving();
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
    if (!expenseForm.financeAccountId) {
      setFormError("Select the account this expense was paid from.");
      return;
    }
    if (!startSaving()) return;
    try {
      const body = new FormData();
      if (expenseForm.projectId) body.append("projectId", expenseForm.projectId);
      body.append("financeAccountId", expenseForm.financeAccountId);
      body.append("amount", String(amount));
      body.append("category", expenseForm.category.trim());
      body.append("description", expenseForm.description.trim());
      body.append("vendor", expenseForm.vendor.trim());
      if (expenseForm.date) body.append("date", expenseForm.date);
      if (expenseForm.selectedAttachment) body.append("attachment", expenseForm.selectedAttachment);
      if (expenseForm.removeAttachment) body.append("removeAttachment", "true");
      const res = expenseForm.id
        ? await api(`/api/Finance/expenses/${expenseForm.id}/form`, { method: "PUT", body })
        : await api("/api/Finance/expenses/form", { method: "POST", body });
      if (!res.ok) {
        setFormError(await financeApiError(res, "Failed to save expense."));
        return;
      }
      resetForms();
      await refreshAll();
    } catch {
      setFormError("The expense could not be saved. Check your connection and try again.");
    } finally {
      finishSaving();
    }
  };

  const deleteRevenue = async (id: number) => {
    if (!window.confirm("Delete this manual revenue entry?")) return;
    const res = await api(`/api/Finance/revenue/${id}`, { method: "DELETE" });
    if (res.ok) await refreshAll();
    else alert("Failed to delete revenue entry.");
  };

  const deleteExpense = async (id: number) => {
    if (!window.confirm("Delete this expense?")) return;
    const res = await api(`/api/Finance/expenses/${id}`, { method: "DELETE" });
    if (res.ok) await refreshAll();
    else alert("Failed to delete expense.");
  };

  const editRevenue = (row: RevenueLine) => {
    if (row.manualRevenueId == null) return;
    setExpenseForm(null);
    setFormError(null);
    setRevenueForm({
      id: row.manualRevenueId,
      projectId: row.projectId != null ? String(row.projectId) : "",
      financeAccountId: row.financeAccountId != null ? String(row.financeAccountId) : "",
      amount: String(row.amount),
      revenueType: row.revenueType,
      description: row.description ?? "",
      reference: row.reference ?? "",
      date: row.date.slice(0, 10),
      attachment: row.attachment,
      selectedAttachment: null,
      removeAttachment: false,
    });
  };

  const editExpense = (row: ExpenseLine) => {
    setRevenueForm(null);
    setFormError(null);
    setExpenseForm({
      id: row.id,
      projectId: row.projectId != null ? String(row.projectId) : "",
      financeAccountId: row.financeAccountId != null ? String(row.financeAccountId) : "",
      amount: String(row.amount),
      category: row.category,
      description: row.description ?? "",
      vendor: row.reference ?? "",
      date: row.date.slice(0, 10),
      attachment: row.attachment,
      selectedAttachment: null,
      removeAttachment: false,
    });
  };

  const accessAttachment = async (
    kind: FinanceRecordKind,
    id: number,
    attachment: FinanceAttachmentInfo,
    download: boolean,
  ) => {
    try {
      await openFinanceAttachment(kind, id, attachment.fileName, download);
    } catch (attachmentError) {
      window.alert(attachmentError instanceof Error ? attachmentError.message : "The attachment could not be opened.");
    }
  };

  if (!isAdmin) return null;

  const money = (n: number, cls = "text-[var(--text-secondary)]") => (
    <span className={`font-semibold whitespace-nowrap ${cls}`}>{formatMoney(n)}</span>
  );

  const attachmentCell = (
    kind: FinanceRecordKind,
    id: number | null,
    attachment: FinanceAttachmentInfo | null,
  ) => attachment && id != null ? (
    <span className="inline-flex items-center gap-2 whitespace-nowrap">
      <button type="button" onClick={() => void accessAttachment(kind, id, attachment, false)} className="rounded-full border border-indigo-500/20 bg-indigo-500/10 px-2.5 py-1 text-[10px] font-semibold text-indigo-300 hover:bg-indigo-500/20">Attached</button>
      <button type="button" aria-label={`Download ${attachment.fileName}`} title={`Download ${attachment.fileName}`} onClick={() => void accessAttachment(kind, id, attachment, true)} className="text-xs font-semibold text-[var(--text-muted)] hover:text-[var(--text-primary)]">↓</button>
    </span>
  ) : <span className="text-xs text-[var(--text-muted)]">None</span>;

  // Column config + horizontal min-width for the active view's virtualized table.
  const { columns, minWidth, emptyText } = ((): {
    columns: Column<AnyRow>[];
    minWidth: number;
    emptyText: string;
  } => {
    switch (view) {
      case "revenue":
        return {
          minWidth: 1080,
          emptyText: "No revenue for the selected filters.",
          columns: [
            { key: "date", header: "Date", width: "130px", render: (r) => <span className="text-[var(--text-secondary)]">{formatDate((r as RevenueLine).date)}</span> },
            { key: "project", header: "Project", width: "minmax(120px,1fr)", render: (r) => <span className="text-[var(--text-primary)]">{(r as RevenueLine).projectName}</span> },
            { key: "account", header: "Received In", width: "minmax(150px,1fr)", render: (r) => { const x=r as RevenueLine; return <span>{x.financeAccountName ?? "Unassigned"}<small className="block text-[var(--text-muted)]">{x.accountHolderName}</small></span>; } },
            { key: "type", header: "Revenue Type", width: "minmax(150px,1fr)", render: (r) => <span className="text-[var(--text-primary)]">{(r as RevenueLine).revenueType}</span> },
            { key: "amount", header: "Amount", width: "120px", align: "right", render: (r) => money((r as RevenueLine).amount, "text-emerald-400") },
            { key: "source", header: "Source", width: "150px", render: (r) => {
              const row = r as RevenueLine;
              return (
                <span className={`inline-flex rounded-full border px-2.5 py-1 text-[10px] font-semibold ${row.source === "Manual Revenue" ? "text-violet-400 bg-violet-500/10 border-violet-500/20" : "text-sky-400 bg-sky-500/10 border-sky-500/20"}`}>{row.source}</span>
              );
            } },
            { key: "reference", header: "Reference", width: "minmax(160px,1fr)", render: (r) => <span className="text-[var(--text-secondary)]">{(r as RevenueLine).reference || "—"}</span> },
            { key: "attachment", header: "Attachment", width: "130px", render: (r) => { const row = r as RevenueLine; return attachmentCell("revenue", row.manualRevenueId, row.attachment); } },
            { key: "actions", header: "", width: "120px", align: "right", render: (r) => {
              const row = r as RevenueLine;
              return row.manualRevenueId != null ? (
                <span className="inline-flex justify-end gap-2">
                  <button type="button" onClick={() => editRevenue(row)} className="fin-act" aria-label="Edit" title="Edit"><IconPencil /></button>
                  <button type="button" onClick={() => deleteRevenue(row.manualRevenueId!)} className="fin-act fin-act--del" aria-label="Delete" title="Delete"><IconTrash /></button>
                </span>
              ) : null;
            } },
          ],
        };
      case "expense":
        return {
          minWidth: 1080,
          emptyText: "No expenses for the selected filters.",
          columns: [
            { key: "date", header: "Date", width: "130px", render: (r) => <span className="text-[var(--text-secondary)]">{formatDate((r as ExpenseLine).date)}</span> },
            { key: "project", header: "Project", width: "minmax(120px,1fr)", render: (r) => <span className="text-[var(--text-primary)]">{(r as ExpenseLine).projectName}</span> },
            { key: "account", header: "Paid From", width: "minmax(150px,1fr)", render: (r) => { const x=r as ExpenseLine; return <span>{x.financeAccountName ?? "Unassigned"}<small className="block text-[var(--text-muted)]">{x.accountHolderName}</small></span>; } },
            { key: "category", header: "Category", width: "minmax(140px,1fr)", render: (r) => <span className="text-[var(--text-primary)]">{(r as ExpenseLine).category}</span> },
            { key: "amount", header: "Amount", width: "120px", align: "right", render: (r) => money((r as ExpenseLine).amount, "text-rose-400") },
            { key: "description", header: "Description", width: "minmax(160px,1fr)", render: (r) => <span className="text-[var(--text-secondary)]">{(r as ExpenseLine).description || "—"}</span> },
            { key: "reference", header: "Reference", width: "minmax(140px,1fr)", render: (r) => <span className="text-[var(--text-secondary)]">{(r as ExpenseLine).reference || "—"}</span> },
            { key: "attachment", header: "Attachment", width: "130px", render: (r) => { const row = r as ExpenseLine; return attachmentCell("expense", row.id, row.attachment); } },
            { key: "actions", header: "", width: "120px", align: "right", render: (r) => {
              const row = r as ExpenseLine;
              return (
                <span className="inline-flex justify-end gap-2">
                  <button type="button" onClick={() => editExpense(row)} className="fin-act" aria-label="Edit" title="Edit"><IconPencil /></button>
                  <button type="button" onClick={() => deleteExpense(row.id)} className="fin-act fin-act--del" aria-label="Delete" title="Delete"><IconTrash /></button>
                </span>
              );
            } },
          ],
        };
      case "netProfit":
        return {
          minWidth: 760,
          emptyText: "No activity for the selected filters.",
          columns: [
            { key: "date", header: "Date", width: "130px", render: (r) => <span className="text-[var(--text-secondary)]">{formatDate((r as NetProfitLine).date)}</span> },
            { key: "project", header: "Project", width: "minmax(120px,1fr)", render: (r) => <span className="text-[var(--text-primary)]">{(r as NetProfitLine).projectName}</span> },
            { key: "item", header: "Item", width: "minmax(160px,1fr)", render: (r) => <span className="text-[var(--text-primary)]">{(r as NetProfitLine).label}</span> },
            { key: "kind", header: "Type", width: "130px", render: (r) => {
              const row = r as NetProfitLine;
              return (
                <span className={`inline-flex rounded-full border px-2.5 py-1 text-[10px] font-semibold ${row.kind === "revenue" ? "text-emerald-400 bg-emerald-500/10 border-emerald-500/20" : "text-rose-400 bg-rose-500/10 border-rose-500/20"}`}>{row.kind === "revenue" ? "Revenue" : "Expense"}</span>
              );
            } },
            { key: "amount", header: "Amount", width: "140px", align: "right", render: (r) => {
              const row = r as NetProfitLine;
              return <span className={`font-semibold whitespace-nowrap ${row.amount >= 0 ? "text-emerald-400" : "text-rose-400"}`}>{row.amount >= 0 ? "+" : "−"}{formatMoney(Math.abs(row.amount))}</span>;
            } },
          ],
        };
      case "outstanding":
        return {
          minWidth: 920,
          emptyText: "No outstanding balances for the selected filters.",
          columns: [
            { key: "booking", header: "Booking", width: "140px", render: (r) => <span className="text-[var(--text-primary)] whitespace-nowrap">{(r as OutstandingLine).bookingReference}</span> },
            { key: "customer", header: "Customer", width: "minmax(140px,1fr)", render: (r) => <span className="text-[var(--text-primary)]">{(r as OutstandingLine).customerName}</span> },
            { key: "project", header: "Project", width: "minmax(120px,1fr)", render: (r) => <span className="text-[var(--text-secondary)]">{(r as OutstandingLine).projectName}</span> },
            { key: "unit", header: "Unit", width: "110px", render: (r) => <span className="text-[var(--text-secondary)]">{(r as OutstandingLine).unitNumber}</span> },
            { key: "agreed", header: "Agreed Price", width: "130px", align: "right", render: (r) => money((r as OutstandingLine).agreedSalePrice) },
            { key: "received", header: "Received", width: "130px", align: "right", render: (r) => money((r as OutstandingLine).receivedAmount, "text-emerald-400") },
            { key: "outstanding", header: "Outstanding", width: "130px", align: "right", render: (r) => money((r as OutstandingLine).outstandingAmount, "text-amber-400") },
          ],
        };
      default: // overdue
        return {
          minWidth: 1040,
          emptyText: "No overdue installments for the selected filters.",
          columns: [
            { key: "due", header: "Due Date", width: "130px", render: (r) => <span className="text-rose-400 whitespace-nowrap">{formatDate((r as OverdueLine).dueDate)}</span> },
            { key: "booking", header: "Booking", width: "140px", render: (r) => <span className="text-[var(--text-primary)] whitespace-nowrap">{(r as OverdueLine).bookingReference}</span> },
            { key: "customer", header: "Customer", width: "minmax(140px,1fr)", render: (r) => <span className="text-[var(--text-primary)]">{(r as OverdueLine).customerName}</span> },
            { key: "project", header: "Project", width: "minmax(110px,1fr)", render: (r) => <span className="text-[var(--text-secondary)]">{(r as OverdueLine).projectName}</span> },
            { key: "unit", header: "Unit", width: "100px", render: (r) => <span className="text-[var(--text-secondary)]">{(r as OverdueLine).unitNumber}</span> },
            { key: "inst", header: "Installment", width: "150px", render: (r) => { const row = r as OverdueLine; return <span className="text-[var(--text-secondary)] whitespace-nowrap">#{row.sequenceNumber} · {row.installmentType}</span>; } },
            { key: "amount", header: "Amount", width: "120px", align: "right", render: (r) => money((r as OverdueLine).amount) },
            { key: "paid", header: "Paid", width: "120px", align: "right", render: (r) => money((r as OverdueLine).paidAmount, "text-emerald-400") },
            { key: "overdue", header: "Overdue", width: "120px", align: "right", render: (r) => money((r as OverdueLine).overdueAmount, "text-orange-400") },
          ],
        };
    }
  })();

  const rowKey = (row: AnyRow, index: number): string => {
    switch (view) {
      case "revenue": {
        const r = row as RevenueLine;
        return `rev-${r.manualRevenueId ?? "p"}-${r.date}-${index}`;
      }
      case "expense":
        return `exp-${(row as ExpenseLine).id}`;
      case "outstanding":
        return `out-${(row as OutstandingLine).bookingReference}`;
      case "overdue": {
        const r = row as OverdueLine;
        return `ovd-${r.bookingReference}-${r.sequenceNumber}-${index}`;
      }
      default:
        return `np-${index}`;
    }
  };

  return (
    <>
      {/* Header */}
      <div className="fin-page fin-page--head py-6 sm:py-8">
        <div>
          <h1 className="mb-5 text-2xl font-bold text-[var(--text-heading)]">Finance Overview</h1>
          {/* Filters: period pills on the left, project / account / date range on the right */}
          <div className="fin-filters">
            <div className="fin-periods">
              {PERIODS.map((p) => (
                <button
                  key={p.value}
                  type="button"
                  onClick={() => applyPeriod(p.value)}
                  className={`fin-pill ${activePeriod === p.value ? "fin-pill--active" : ""}`}
                >
                  {p.label}
                </button>
              ))}
            </div>

            <div className="fin-controls">
              <select value={projectId} onChange={(e) => setProjectId(e.target.value)} className="fin-control" aria-label="Project">
                <option value="">All Projects</option>
                {projects.map((p) => (
                  <option key={p.id} value={p.id}>{p.projectName}</option>
                ))}
              </select>
              <select value={accountFilter} onChange={(e) => setAccountFilter(e.target.value)} className="fin-control" aria-label="Account">
                <option value="">All Accounts</option>
                {financeAccounts.map((a) => <option key={a.id} value={a.id}>{a.name}{a.isActive ? "" : " (Inactive)"}</option>)}
                <option value="unassigned">Unassigned</option>
              </select>
              <div className="fin-field">
                <span className="fin-field__label">From</span>
                <input type="date" value={fromDate} onChange={(e) => setFromDate(e.target.value)} className="fin-control fin-control--date" />
              </div>
              <div className="fin-field">
                <span className="fin-field__label">To</span>
                <input type="date" value={toDate} onChange={(e) => setToDate(e.target.value)} className="fin-control fin-control--date" />
              </div>
              {(fromDate || toDate) && (
                <Button variant="ghost" size="sm" onClick={() => { setFromDate(""); setToDate(""); }}>
                  Clear
                </Button>
              )}
            </div>
          </div>

          {/* Summary cards */}
          <div className="mt-7 grid grid-cols-2 gap-4 md:grid-cols-3 xl:grid-cols-5">
            {summaryCards.map((card) => {
              const active = view === card.view;
              return (
                <button
                  key={card.label}
                  type="button"
                  onClick={() => focusView(card.view)}
                  style={{ borderBottomColor: card.underline }}
                  className={`group relative cursor-pointer overflow-hidden rounded-2xl border border-[var(--border)] border-b-[3px] bg-[var(--bg-card)] px-5 pb-5 pt-4 text-left transition-all hover:-translate-y-0.5 hover:border-[var(--border-hover)] ${
                    active ? "ring-2 ring-[var(--accent)]" : ""
                  }`}
                >
                  <p className="text-[11px] font-semibold uppercase tracking-wider text-[var(--text-muted)]">
                    {card.label}
                  </p>
                  <p className={`mt-3 text-2xl font-bold leading-tight sm:text-[1.7rem] ${card.valueColor}`}>
                    {summaryLoading ? "…" : formatMoney(card.value)}
                  </p>
                </button>
              );
            })}
          </div>

          {summary && (
            <p className="mt-3 text-xs text-[var(--text-muted)]">
              Revenue breakdown: {formatMoney(summary.automaticRevenue)} from payments
              {" + "}
              {formatMoney(summary.manualRevenue)} manual
            </p>
          )}

          {summary && summary.accountCurrentBalance != null && (
            <div className="mt-4 flex flex-wrap items-center gap-x-8 gap-y-2 rounded-xl border border-[var(--border)] bg-[var(--bg-card)] px-4 py-3">
              <div>
                <p className="text-[10px] font-semibold uppercase tracking-wider text-[var(--text-muted)]">Current Balance{(fromDate || toDate) ? " (to period end)" : ""}</p>
                <p className={`text-lg font-bold ${summary.accountCurrentBalance >= 0 ? "text-emerald-400" : "text-rose-400"}`}>{formatMoney(summary.accountCurrentBalance)}</p>
              </div>
              <div>
                <p className="text-[10px] font-semibold uppercase tracking-wider text-[var(--text-muted)]">Opening Balance</p>
                <p className="text-sm font-semibold text-[var(--text-primary)]">{formatMoney(summary.accountOpeningBalance ?? 0)}</p>
              </div>
              <div>
                <p className="text-[10px] font-semibold uppercase tracking-wider text-[var(--text-muted)]">Net Movement{(fromDate || toDate) ? " (period)" : ""}</p>
                <p className={`text-sm font-semibold ${summary.netProfit >= 0 ? "text-indigo-400" : "text-rose-400"}`}>{formatMoney(summary.netProfit)}</p>
              </div>
            </div>
          )}
        </div>
      </div>

      {/* Content */}
      <div className="fin-page py-8">
        {/* View title + actions */}
        <div className="mb-5 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
          <div>
            <h2 className="text-xl font-bold text-[var(--text-heading)]">Transactions</h2>
            <p className="text-xs text-[var(--text-muted)]">{VIEW_TITLES[view]} — select a card above to switch views.</p>
          </div>
          <div className="flex flex-wrap gap-2.5">
            <Link to="/finance/accounts"><Button variant="outline">⚙ Manage Accounts</Button></Link>
            <Link to="/finance/commissions-rebates"><Button variant="outline">Commissions &amp; Rebates</Button></Link>
            <Button variant="outline" onClick={() => { setExpenseForm(null); setFormError(null); setRevenueForm(emptyRevenueForm()); }}>
              + Add Revenue
            </Button>
            <Button onClick={() => { setRevenueForm(null); setFormError(null); setExpenseForm(emptyExpenseForm()); }}>
              − Add Expense
            </Button>
          </div>
        </div>

        {error && (
          <div className="mb-4 rounded-2xl border border-rose-500/20 bg-rose-500/[0.06] px-5 py-4 text-sm text-rose-300">
            {error}
          </div>
        )}

        <VirtualInfiniteTable<AnyRow>
          columns={columns}
          rows={rows}
          rowKey={rowKey}
          loading={loading}
          loadingMore={loadingMore}
          hasMore={hasMore}
          onLoadMore={loadMore}
          emptyText={emptyText}
          minWidth={minWidth}
          resetKey={`${view}|${projectId}|${fromDate}|${toDate}`}
        />

        {/* Charts — driven by the same filters as everything above */}
        <FinanceCharts data={chartData} loading={chartLoading} formatMoney={formatMoney} />
      </div>

      {/* Revenue modal */}
      {revenueForm && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
          <div className="absolute inset-0 bg-black/60 backdrop-blur-sm animate-fade-in" onClick={() => { if (!saving) resetForms(); }} />
          <div className="relative z-10 max-h-[calc(100dvh-2rem)] w-[460px] max-w-[92vw] overflow-y-auto animate-scale-in rounded-2xl border border-[var(--border)] bg-[var(--modal-bg)] shadow-2xl">
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
              <FormSelect label="Received In Account" value={revenueForm.financeAccountId} onChange={(v) => setRevenueForm({ ...revenueForm, financeAccountId: v })}>
                <option value="">Select account</option>
                {financeAccounts.filter((a) => a.isActive || String(a.id) === revenueForm.financeAccountId).map((a) => <option key={a.id} value={a.id}>{a.name} — {a.accountHolderName}{a.isActive ? "" : " (Inactive)"}</option>)}
              </FormSelect>
              <FormSelect
                label="Revenue Type"
                value={REVENUE_TYPES.includes(revenueForm.revenueType) ? revenueForm.revenueType : CUSTOM_TYPE}
                onChange={(v) => setRevenueForm({ ...revenueForm, revenueType: v === CUSTOM_TYPE ? "" : v })}
              >
                {REVENUE_TYPES.map((t) => <option key={t} value={t}>{t}</option>)}
                <option value={CUSTOM_TYPE}>Custom (enter manually)…</option>
              </FormSelect>
              {!REVENUE_TYPES.includes(revenueForm.revenueType) && (
                <FormInput
                  label="Custom Revenue Type"
                  value={revenueForm.revenueType}
                  onChange={(v) => setRevenueForm({ ...revenueForm, revenueType: v })}
                />
              )}
              <FormInput label="Amount (Rs)" type="number" value={revenueForm.amount} onChange={(v) => setRevenueForm({ ...revenueForm, amount: v })} />
              <FormInput label="Date" type="date" value={revenueForm.date} onChange={(v) => setRevenueForm({ ...revenueForm, date: v })} />
              <FormInput label="Reference (optional)" value={revenueForm.reference} onChange={(v) => setRevenueForm({ ...revenueForm, reference: v })} />
              <FormInput label="Description (optional)" value={revenueForm.description} onChange={(v) => setRevenueForm({ ...revenueForm, description: v })} />
              <FinanceAttachmentField
                existing={revenueForm.attachment}
                selected={revenueForm.selectedAttachment}
                removeExisting={revenueForm.removeAttachment}
                disabled={saving}
                onSelected={(file) => setRevenueForm((current) => current ? { ...current, selectedAttachment: file } : current)}
                onRemoveExisting={(remove) => setRevenueForm((current) => current ? { ...current, removeAttachment: remove } : current)}
                onViewExisting={() => { if (revenueForm.id && revenueForm.attachment) void accessAttachment("revenue", revenueForm.id, revenueForm.attachment, false); }}
                onDownloadExisting={() => { if (revenueForm.id && revenueForm.attachment) void accessAttachment("revenue", revenueForm.id, revenueForm.attachment, true); }}
              />
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
          <div className="absolute inset-0 bg-black/60 backdrop-blur-sm animate-fade-in" onClick={() => { if (!saving) resetForms(); }} />
          <div className="relative z-10 max-h-[calc(100dvh-2rem)] w-[460px] max-w-[92vw] overflow-y-auto animate-scale-in rounded-2xl border border-[var(--border)] bg-[var(--modal-bg)] shadow-2xl">
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
              <FormSelect label="Paid From Account" value={expenseForm.financeAccountId} onChange={(v) => setExpenseForm({ ...expenseForm, financeAccountId: v })}>
                <option value="">Select account</option>
                {financeAccounts.filter((a) => a.isActive || String(a.id) === expenseForm.financeAccountId).map((a) => <option key={a.id} value={a.id}>{a.name} — {a.accountHolderName}{a.isActive ? "" : " (Inactive)"}</option>)}
              </FormSelect>
              <FormSelect
                label="Category"
                value={EXPENSE_CATEGORIES.includes(expenseForm.category) ? expenseForm.category : CUSTOM_TYPE}
                onChange={(v) => setExpenseForm({ ...expenseForm, category: v === CUSTOM_TYPE ? "" : v })}
              >
                {EXPENSE_CATEGORIES.map((c) => <option key={c} value={c}>{c}</option>)}
                <option value={CUSTOM_TYPE}>Custom (enter manually)…</option>
              </FormSelect>
              {!EXPENSE_CATEGORIES.includes(expenseForm.category) && (
                <FormInput
                  label="Custom Category"
                  value={expenseForm.category}
                  onChange={(v) => setExpenseForm({ ...expenseForm, category: v })}
                />
              )}
              <FormInput label="Amount (Rs)" type="number" value={expenseForm.amount} onChange={(v) => setExpenseForm({ ...expenseForm, amount: v })} />
              <FormInput label="Date" type="date" value={expenseForm.date} onChange={(v) => setExpenseForm({ ...expenseForm, date: v })} />
              <FormInput label="Vendor / Reference (optional)" value={expenseForm.vendor} onChange={(v) => setExpenseForm({ ...expenseForm, vendor: v })} />
              <FormInput label="Description (optional)" value={expenseForm.description} onChange={(v) => setExpenseForm({ ...expenseForm, description: v })} />
              <FinanceAttachmentField
                existing={expenseForm.attachment}
                selected={expenseForm.selectedAttachment}
                removeExisting={expenseForm.removeAttachment}
                disabled={saving}
                onSelected={(file) => setExpenseForm((current) => current ? { ...current, selectedAttachment: file } : current)}
                onRemoveExisting={(remove) => setExpenseForm((current) => current ? { ...current, removeAttachment: remove } : current)}
                onViewExisting={() => { if (expenseForm.id && expenseForm.attachment) void accessAttachment("expense", expenseForm.id, expenseForm.attachment, false); }}
                onDownloadExisting={() => { if (expenseForm.id && expenseForm.attachment) void accessAttachment("expense", expenseForm.id, expenseForm.attachment, true); }}
              />
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

function IconPencil() {
  return <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round"><path d="M12 20h9" /><path d="M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4Z" /></svg>;
}
function IconTrash() {
  return <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round"><polyline points="3 6 5 6 21 6" /><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2" /><line x1="10" y1="11" x2="10" y2="17" /><line x1="14" y1="11" x2="14" y2="17" /></svg>;
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
