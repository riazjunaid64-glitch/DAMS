import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { Link } from "react-router-dom";
import { useNavigate } from "react-router-dom";
import { api } from "../api/api.ts";
import { financeApiError, openFinanceAttachment, type FinanceAttachmentInfo, type FinanceRecordKind } from "../api/financeAttachments.ts";
import type { User } from "../App.tsx";
import {
  BottomSheet,
  Button,
  ConfirmDialog,
  DataTable,
  DatePicker,
  Dropdown,
  EmptyState,
  IconBuilding,
  IconDownload,
  IconFile,
  IconFolder,
  IconMore,
  IconPaperclip,
  IconPencil,
  IconPlus,
  IconSettings,
  IconTarget,
  IconTrash,
  IconUsers,
  IconWallet,
  LoadMore,
  Notice,
  PageHeader,
  Pagination,
  StatCard,
  StatusBadge,
  useIsPhone,
  useToast,
  type DataTableColumn,
} from "../components/ui";
import { pageAccess } from "../features/access/permissions.ts";
import { AssetDialog, ExpenseDialog, RevenueDialog, accountLabel } from "../features/finance/home/FinanceDialogs.tsx";
import { FINANCE_PAGE_SIZE, useFinanceRows } from "../features/finance/home/useFinanceRows.ts";
import { isStaffFloat, type FinanceAccountOption } from "../features/finance/home/types.ts";
import type {
  AssetPurchaseFormState,
  AssetPurchaseLine,
  CostLine,
  CustomerDepositLine,
  ExpenseFormState,
  ExpenseLine,
  FinancialSummary,
  HomeRow,
  HomeView,
  OverdueLine,
  RevenueCategory,
  RevenueFormState,
  RevenueLine,
} from "../features/finance/home/types.ts";
import { listCategories, payableSummary, vendorOptions } from "../features/finance/whtApi.ts";
import { useFinancialYearStartMonth } from "../features/finance/useFinancialYearStartMonth.ts";
import { emptyWht, formatRs, type ExpenseCategory, type VendorOption } from "../features/finance/whtTypes.ts";
import { useProjects } from "../contexts/projectsContextValue.ts";
import { formatDay } from "../lib/dates.ts";
import { fetchFinanceDashboard, financeRangeError } from "../lib/financeChartData.ts";
import { buildPeriodRange, financePeriodLabel, pakistanToday, type FinancePeriodPreset } from "../lib/financePeriods.ts";
import { moneyRequest, useIdempotencyKeys } from "../lib/idempotency.ts";

const VIEW_PARAM: Record<HomeView, string> = {
  revenue: "revenue",
  totalExpenses: "totalExpenses",
  customerDeposits: "customerDeposits",
  overdue: "overdue",
};

const PERIODS: FinancePeriodPreset[] = ["all", "today", "month", "lastMonth", "year", "lastYear"];

function draftPeriod(from: string, to: string, startMonth: number | null): FinancePeriodPreset {
  if (!from && !to) return "all";
  for (const preset of PERIODS) {
    if (preset === "all") continue;
    const range = buildPeriodRange(preset, startMonth);
    if (range.from && range.from === from && range.to === to) return preset;
  }
  return "custom";
}

const LINKS = [
  { to: "/finance/reports", label: "Financial reports", icon: IconFile },
  { to: "/finance/partners", label: "Capital partners", icon: IconUsers },
  { to: "/finance/loans", label: "Loans", icon: IconWallet },
  { to: "/finance/staff-cash", label: "Cash with staff", icon: IconWallet },
  { to: "/finance/accounts", label: "Manage accounts", icon: IconBuilding },
  { to: "/finance/settings", label: "Tax to FBR", icon: IconFolder },
  { to: "/finance/settings", label: "Settings", icon: IconSettings },
  { to: "/finance/commissions-rebates", label: "Commissions & rebates", icon: IconTarget },
];

const EMPTY: Record<HomeView, string> = {
  revenue: "No revenue for the selected filters.",
  totalExpenses: "No costs for the selected filters.",
  customerDeposits: "No customer deposits held for the selected filters.",
  overdue: "No overdue installments for the selected filters.",
};

const TITLES: Record<HomeView, string> = {
  revenue: "Revenue",
  totalExpenses: "Total expenses",
  customerDeposits: "Customer deposits",
  overdue: "Overdue",
};

const SOURCE: Record<string, { label: string; tone: "grey" | "green" | "orange" }> = {
  "Manual Revenue": { label: "Manual revenue", tone: "grey" },
  "Unit Sale": { label: "Unit sale", tone: "green" },
  "Cancellation Retained": { label: "Cancellation retained", tone: "orange" },
};

const today = pakistanToday;

const emptyRevenue = (): RevenueFormState => ({
  id: null, concurrencyToken: "", projectId: "", financeAccountId: "", amount: "", revenueType: "",
  revenueCategoryId: "", description: "", reference: "", date: today(), attachment: null, selectedAttachment: null, removeAttachment: false,
});
const emptyExpense = (): ExpenseFormState => ({
  id: null, concurrencyToken: "", projectId: "", financeAccountId: "", amount: "", categoryId: "", category: "",
  legacyCategory: false, description: "", vendorId: "", vendor: "", date: today(), wht: emptyWht(),
  attachment: null, selectedAttachment: null, removeAttachment: false,
});
const emptyAsset = (): AssetPurchaseFormState => ({
  id: null, projectId: "", assetAccountId: "", assetAccountName: "", financeAccountId: "", amount: "", itemName: "",
  categoryId: "", category: "", description: "", vendorId: "", vendor: "", date: today(), wht: emptyWht(),
  attachment: null, selectedAttachment: null, removeAttachment: false, concurrencyToken: "",
});

function moneyTone(amount: number, tone: "green" | "red" | "blue" | "ink" = "ink") {
  const colour = tone === "green" ? "text-success" : tone === "red" ? "text-danger" : tone === "blue" ? "text-info" : "text-ink";
  return <span className={`font-extrabold tabular-nums whitespace-nowrap ${colour}`}>{formatRs(amount)}</span>;
}

function signedCost(amount: number) {
  const text = `${amount < 0 ? "−" : "+"}${formatRs(Math.abs(amount))}`;
  return <span className={`font-extrabold tabular-nums whitespace-nowrap ${amount < 0 ? "text-success" : "text-danger"}`}>{text}</span>;
}

export default function FinanceDashboardPage({ user }: { user: User | null }) {
  const access = pageAccess(user?.role, "finance");
  if (access === "wait") return null;
  if (access === "deny") {
    return <p className="m-0 font-ui text-body font-bold text-ink">You don't have access to Finance.</p>;
  }
  return <FinanceHome />;
}

function FinanceHome() {
  const navigate = useNavigate();
  const toast = useToast();
  const isPhone = useIsPhone();
  const { projects } = useProjects();
  const { startMonth, failed: yearFailed } = useFinancialYearStartMonth(true);
  const idempotency = useIdempotencyKeys();

  const [accounts, setAccounts] = useState<FinanceAccountOption[]>([]);
  const [assetAccounts, setAssetAccounts] = useState<FinanceAccountOption[]>([]);
  const [revenueCategories, setRevenueCategories] = useState<RevenueCategory[]>([]);
  const [expenseCategories, setExpenseCategories] = useState<ExpenseCategory[]>([]);
  const [vendors, setVendors] = useState<VendorOption[]>([]);
  const [categoriesLoading, setCategoriesLoading] = useState(false);
  const revenueLoaded = useRef(false);
  const assetLoaded = useRef(false);
  const whtLoaded = useRef(false);

  const [projectId, setProjectId] = useState("");
  const [accountFilter, setAccountFilter] = useState("");
  const [fromDate, setFromDate] = useState("");
  const [toDate, setToDate] = useState("");
  const [sheetOpen, setSheetOpen] = useState(false);
  const [moreOpen, setMoreOpen] = useState(false);
  const [draft, setDraft] = useState({ projectId: "", account: "", from: "", to: "" });

  const [summary, setSummary] = useState<FinancialSummary | null>(null);
  const [summaryLoading, setSummaryLoading] = useState(true);
  const [summaryError, setSummaryError] = useState<string | null>(null);
  const [owed, setOwed] = useState<number | null>(null);
  const [view, setView] = useState<HomeView>("revenue");
  const rangeError = financeRangeError(fromDate, toDate);
  const draftRangeError = financeRangeError(draft.from, draft.to);

  const rows = useFinanceRows<HomeRow>(VIEW_PARAM[view], projectId, fromDate, toDate, accountFilter, !rangeError);

  const [revenueForm, setRevenueForm] = useState<RevenueFormState | null>(null);
  const [expenseForm, setExpenseForm] = useState<ExpenseFormState | null>(null);
  const [assetForm, setAssetForm] = useState<AssetPurchaseFormState | null>(null);
  const [saving, setSaving] = useState(false);
  const savingRef = useRef(false);
  const [formError, setFormError] = useState<string | null>(null);
  const [taxBlocksSave, setTaxBlocksSave] = useState(false);
  const [opening, setOpening] = useState<string | null>(null);
  const [pendingDelete, setPendingDelete] = useState<
    | { kind: "revenue"; id: number; token: string }
    | { kind: "expense"; id: number; token: string }
    | { kind: "asset"; id: number; token: string }
    | null
  >(null);
  const [deleting, setDeleting] = useState(false);

  const accountSelected = accountFilter !== "";
  const filtersOn = Boolean(projectId || accountFilter || fromDate || toDate);
  const period = useMemo(() => {
    if (!fromDate && !toDate) return "all" as FinancePeriodPreset;
    for (const preset of PERIODS) {
      if (preset === "all") continue;
      const range = buildPeriodRange(preset, startMonth);
      if (range.from && range.from === fromDate && range.to === toDate) return preset;
    }
    return "custom" as FinancePeriodPreset;
  }, [fromDate, toDate, startMonth]);

  const summaryRequest = useRef(0);
  const loadSummary = useCallback(async (signal?: AbortSignal) => {
    const ticket = ++summaryRequest.current;
    if (rangeError) {
      setSummary(null);
      setSummaryLoading(false);
      setSummaryError(rangeError);
      return;
    }
    setSummaryLoading(true);
    try {
      const loaded = await fetchFinanceDashboard<FinancialSummary>(
        { projectId, from: fromDate, to: toDate, account: accountFilter },
        signal,
      );
      if (ticket !== summaryRequest.current) return;
      setSummary(loaded.summary);
      setSummaryError(null);
    } catch {
      if (ticket !== summaryRequest.current) return;
      setSummary(null);
      setSummaryError("The finance totals could not be loaded, so the figures below are unavailable.");
    } finally {
      if (ticket === summaryRequest.current) setSummaryLoading(false);
    }
  }, [projectId, fromDate, toDate, accountFilter, rangeError]);

  const refreshAll = useCallback(async () => {
    const summaryRefresh = loadSummary();
    rows.reload();
    await summaryRefresh;
  }, [loadSummary, rows]);

  useEffect(() => {
    void (async () => {
      try {
        const [regular, staff] = await Promise.all([
          api("/api/finance/accounts/options?includeInactive=true&cashLikeOnly=true"),
          api("/api/finance/accounts/options?includeInactive=true&type=10"),
        ]);
        const regularRows = regular.ok ? await regular.json() as FinanceAccountOption[] : [];
        const staffRows = staff.ok ? await staff.json() as FinanceAccountOption[] : [];
        const merged = [...regularRows, ...staffRows];
        setAccounts(merged.filter((row, index) => merged.findIndex((item) => item.id === row.id) === index));
      } catch { /* the form keeps its own validation if accounts cannot be loaded */ }
    })();
  }, []);

  useEffect(() => {
    let live = true;
    payableSummary()
      .then((result) => { if (live) setOwed(result.outstandingPayable); })
      .catch(() => { if (live) setOwed(null); });
    return () => { live = false; };
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    void loadSummary(controller.signal);
    return () => controller.abort();
  }, [loadSummary]);

  useEffect(() => {
    if (accountSelected && (view === "customerDeposits" || view === "overdue")) setView("revenue");
  }, [accountSelected, view]);

  const loadRevenueCategories = useCallback(async () => {
    if (revenueLoaded.current) return;
    setCategoriesLoading(true);
    try {
      const response = await api("/api/finance/revenue-categories?includeInactive=true");
      if (response.ok) {
        const data = await response.json();
        if (Array.isArray(data)) {
          setRevenueCategories(data);
          revenueLoaded.current = true;
        }
      }
    } catch { /* the form shows an empty list and cannot save without a category */ }
    finally { setCategoriesLoading(false); }
  }, []);

  const loadAssetAccounts = useCallback(async () => {
    if (assetLoaded.current) return;
    try {
      const response = await api("/api/finance/accounts/options?includeInactive=true&type=7");
      if (response.ok) {
        setAssetAccounts(await response.json());
        assetLoaded.current = true;
      }
    } catch { /* the purchase form shows its validation if these cannot be loaded */ }
  }, []);

  const loadWhtLookups = useCallback(async () => {
    if (whtLoaded.current) return;
    try {
      const [categories, vendorRows] = await Promise.all([listCategories(true), vendorOptions(true)]);
      setExpenseCategories(categories);
      setVendors(vendorRows);
      whtLoaded.current = true;
    } catch { /* the expense form cannot offer a managed head until this succeeds */ }
  }, []);

  useEffect(() => { if (revenueForm) void loadRevenueCategories(); }, [revenueForm, loadRevenueCategories]);
  useEffect(() => { if (assetForm) void loadAssetAccounts(); }, [assetForm, loadAssetAccounts]);
  useEffect(() => { if (expenseForm || assetForm) void loadWhtLookups(); }, [expenseForm, assetForm, loadWhtLookups]);

  const applyPeriod = (preset: string) => {
    if (preset === "custom") return;
    const range = buildPeriodRange(preset as FinancePeriodPreset, startMonth);
    setFromDate(range.from);
    setToDate(range.to);
  };

  const resetFilters = () => {
    setProjectId("");
    setAccountFilter("");
    setFromDate("");
    setToDate("");
    setDraft({ projectId: "", account: "", from: "", to: "" });
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

  const closeForms = () => {
    setRevenueForm(null);
    setExpenseForm(null);
    setAssetForm(null);
    setFormError(null);
    setTaxBlocksSave(false);
    finishSaving();
  };

  const submitRevenue = async () => {
    if (!revenueForm || !startSaving()) return;
    setFormError(null);
    const amount = Number(revenueForm.amount);
    if (!Number.isFinite(amount) || amount <= 0) { setFormError("Enter a valid amount greater than zero."); finishSaving(); return; }
    if (!revenueForm.revenueCategoryId) { setFormError("Choose a revenue category."); finishSaving(); return; }
    if (!revenueForm.financeAccountId) { setFormError("Select the account where this revenue was received."); finishSaving(); return; }
    try {
      const body = new FormData();
      if (revenueForm.projectId) body.append("projectId", revenueForm.projectId);
      body.append("financeAccountId", revenueForm.financeAccountId);
      body.append("amount", String(amount));
      body.append("revenueType", revenueForm.revenueType.trim());
      body.append("revenueCategoryId", revenueForm.revenueCategoryId);
      body.append("description", revenueForm.description.trim());
      body.append("reference", revenueForm.reference.trim());
      if (revenueForm.date) body.append("date", revenueForm.date);
      if (revenueForm.selectedAttachment) body.append("attachment", revenueForm.selectedAttachment);
      if (revenueForm.removeAttachment) body.append("removeAttachment", "true");
      if (revenueForm.id) body.append("concurrencyToken", revenueForm.concurrencyToken);
      const signature = `revenue:${revenueForm.financeAccountId}:${amount}:${revenueForm.date}:${revenueForm.revenueCategoryId}`;
      const res = revenueForm.id
        ? await api(`/api/Finance/revenue/${revenueForm.id}/form`, { method: "PUT", body })
        : await api("/api/Finance/revenue/form", moneyRequest(idempotency.key(signature, "revenue"), { method: "POST", body }));
      if (!res.ok) { setFormError(await financeApiError(res, "Failed to save revenue entry.")); return; }
      if (!revenueForm.id) idempotency.release(signature);
      closeForms();
      toast.success("Revenue saved");
      await refreshAll();
    } catch {
      setFormError("The revenue entry could not be saved. Check your connection and try again.");
    } finally { finishSaving(); }
  };

  const submitExpense = async () => {
    if (!expenseForm || !startSaving()) return;
    setFormError(null);
    const amount = Number(expenseForm.amount);
    if (!Number.isFinite(amount) || amount <= 0) { setFormError("Enter a valid amount greater than zero."); finishSaving(); return; }
    if (!expenseForm.categoryId && !expenseForm.legacyCategory) { setFormError("Choose an expense category."); finishSaving(); return; }
    if (!expenseForm.financeAccountId) { setFormError("Select the account this expense was paid from."); finishSaving(); return; }
    try {
      const body = new FormData();
      if (expenseForm.projectId) body.append("projectId", expenseForm.projectId);
      body.append("financeAccountId", expenseForm.financeAccountId);
      body.append("amount", String(amount));
      if (expenseForm.categoryId) body.append("categoryId", expenseForm.categoryId);
      body.append("category", expenseForm.category.trim());
      body.append("description", expenseForm.description.trim());
      if (expenseForm.vendorId) body.append("vendorId", expenseForm.vendorId);
      body.append("vendor", expenseForm.vendor.trim());
      if (expenseForm.date) body.append("date", expenseForm.date);
      if (expenseForm.categoryId) {
        if (expenseForm.wht.rate !== "") body.append("whtRate", expenseForm.wht.rate);
        if (expenseForm.wht.amount !== "") body.append("whtAmount", expenseForm.wht.amount);
        if (expenseForm.wht.overrideReason.trim()) body.append("whtOverrideReason", expenseForm.wht.overrideReason.trim());
      }
      if (expenseForm.selectedAttachment) body.append("attachment", expenseForm.selectedAttachment);
      if (expenseForm.removeAttachment) body.append("removeAttachment", "true");
      if (expenseForm.id) body.append("concurrencyToken", expenseForm.concurrencyToken);
      const signature = `expense:${expenseForm.financeAccountId}:${amount}:${expenseForm.date}:${expenseForm.categoryId}`;
      const res = expenseForm.id
        ? await api(`/api/Finance/expenses/${expenseForm.id}/form`, { method: "PUT", body })
        : await api("/api/Finance/expenses/form", moneyRequest(idempotency.key(signature, "expense"), { method: "POST", body }));
      if (!res.ok) { setFormError(await financeApiError(res, "Failed to save expense.")); return; }
      if (!expenseForm.id) idempotency.release(signature);
      closeForms();
      toast.success("Expense saved");
      await refreshAll();
    } catch {
      setFormError("The expense could not be saved. Check your connection and try again.");
    } finally { finishSaving(); }
  };

  const submitAsset = async () => {
    if (!assetForm || !startSaving()) return;
    setFormError(null);
    const amount = Number(assetForm.amount);
    if (!Number.isFinite(amount) || amount <= 0) { setFormError("Enter a valid amount greater than zero."); finishSaving(); return; }
    if (!assetForm.itemName.trim()) { setFormError("Describe what was bought, e.g. \"3 office desks\"."); finishSaving(); return; }
    if (!assetForm.assetAccountId) { setFormError("Select the asset account this purchase belongs to."); finishSaving(); return; }
    if (!assetForm.financeAccountId) { setFormError("Select the account this purchase was paid from."); finishSaving(); return; }
    if (!assetForm.categoryId) { setFormError("Choose a category."); finishSaving(); return; }
    try {
      const body = new FormData();
      if (assetForm.projectId) body.append("projectId", assetForm.projectId);
      body.append("assetAccountId", assetForm.assetAccountId);
      body.append("financeAccountId", assetForm.financeAccountId);
      body.append("amount", String(amount));
      body.append("itemName", assetForm.itemName.trim());
      body.append("categoryId", assetForm.categoryId);
      body.append("category", assetForm.category.trim());
      body.append("description", assetForm.description.trim());
      if (assetForm.vendorId) body.append("vendorId", assetForm.vendorId);
      body.append("vendor", assetForm.vendor.trim());
      if (assetForm.date) body.append("date", assetForm.date);
      if (assetForm.wht.rate !== "") body.append("whtRate", assetForm.wht.rate);
      if (assetForm.wht.amount !== "") body.append("whtAmount", assetForm.wht.amount);
      if (assetForm.wht.overrideReason.trim()) body.append("whtOverrideReason", assetForm.wht.overrideReason.trim());
      if (assetForm.selectedAttachment) body.append("attachment", assetForm.selectedAttachment);
      if (assetForm.removeAttachment) body.append("removeAttachment", "true");
      if (assetForm.id) body.append("concurrencyToken", assetForm.concurrencyToken);
      const signature = `asset:${assetForm.financeAccountId}:${assetForm.assetAccountId}:${amount}:${assetForm.date}`;
      const res = assetForm.id
        ? await api(`/api/Finance/asset-purchases/${assetForm.id}/form`, { method: "PUT", body })
        : await api("/api/Finance/asset-purchases/form", moneyRequest(idempotency.key(signature, "asset-purchase"), { method: "POST", body }));
      if (!res.ok) { setFormError(await financeApiError(res, "Failed to save the asset purchase.")); return; }
      if (!assetForm.id) idempotency.release(signature);
      closeForms();
      toast.success("Purchase saved");
      await refreshAll();
    } catch {
      setFormError("The purchase could not be saved. Check your connection and try again.");
    } finally { finishSaving(); }
  };

  const confirmDelete = async () => {
    if (!pendingDelete) return;
    setDeleting(true);
    const path = pendingDelete.kind === "revenue"
      ? `/api/Finance/revenue/${pendingDelete.id}`
      : pendingDelete.kind === "expense"
        ? `/api/Finance/expenses/${pendingDelete.id}`
        : `/api/Finance/asset-purchases/${pendingDelete.id}`;
    try {
      const res = await api(`${path}?concurrencyToken=${encodeURIComponent(pendingDelete.token)}`, { method: "DELETE" });
      if (!res.ok) {
        toast.error(await financeApiError(res, "Failed to delete."));
        setPendingDelete(null);
        return;
      }
      setPendingDelete(null);
      toast.success("Deleted");
      await refreshAll();
    } catch {
      toast.error("Failed to delete.");
      setPendingDelete(null);
    } finally { setDeleting(false); }
  };

  const loadCostRecord = async (row: CostLine): Promise<ExpenseLine | AssetPurchaseLine | null> => {
    const path = row.source === "expense"
      ? `/api/Finance/expenses/${row.sourceId}`
      : `/api/Finance/asset-purchases/${row.sourceId}`;
    setOpening(`${row.source}:${row.sourceId}`);
    try {
      const res = await api(path);
      if (!res.ok) {
        toast.error(await financeApiError(res, "This record could not be opened. Refresh and try again."));
        return null;
      }
      return (await res.json()) as ExpenseLine | AssetPurchaseLine;
    } catch {
      toast.error("This record could not be opened. Check your connection and try again.");
      return null;
    } finally { setOpening(null); }
  };

  const editExpense = (row: ExpenseLine) => {
    setRevenueForm(null);
    setAssetForm(null);
    setFormError(null);
    setExpenseForm({
      id: row.id,
      concurrencyToken: row.concurrencyToken,
      projectId: row.projectId != null ? String(row.projectId) : "",
      financeAccountId: row.financeAccountId != null ? String(row.financeAccountId) : "",
      amount: String(row.amount),
      categoryId: row.categoryId != null ? String(row.categoryId) : "",
      category: row.category,
      legacyCategory: row.categoryId == null,
      description: row.description ?? "",
      vendorId: row.vendorId != null ? String(row.vendorId) : "",
      vendor: row.reference ?? "",
      date: row.date.slice(0, 10),
      wht: {
        rate: row.categoryId != null ? String(Number(row.whtRate.toFixed(4))) : "",
        amount: row.categoryId != null ? String(row.whtAmount) : "",
        overrideReason: row.whtOverrideReason ?? "",
      },
      attachment: row.attachment,
      selectedAttachment: null,
      removeAttachment: false,
    });
  };

  const editAsset = (row: AssetPurchaseLine) => {
    setRevenueForm(null);
    setExpenseForm(null);
    setFormError(null);
    setAssetForm({
      id: row.id,
      projectId: row.projectId != null ? String(row.projectId) : "",
      assetAccountId: String(row.assetAccountId),
      assetAccountName: row.assetAccountName,
      financeAccountId: String(row.financeAccountId),
      amount: String(row.amount),
      itemName: row.itemName,
      categoryId: row.categoryId != null ? String(row.categoryId) : "",
      category: row.category,
      description: row.description ?? "",
      vendorId: row.vendorId != null ? String(row.vendorId) : "",
      vendor: row.vendor ?? "",
      date: row.date.slice(0, 10),
      wht: { rate: String(Number(row.whtRate.toFixed(4))), amount: String(row.whtAmount), overrideReason: "" },
      attachment: row.attachment,
      selectedAttachment: null,
      removeAttachment: false,
      concurrencyToken: row.concurrencyToken,
    });
  };

  const openCost = async (row: CostLine) => {
    if (row.source === "commission" || row.source === "rebate" || row.source === "customerCredit") {
      navigate("/finance/commissions-rebates");
      return;
    }
    if (row.source === "loanInterest") {
      navigate("/finance/loans");
      return;
    }
    const record = await loadCostRecord(row);
    if (!record) return;
    if (row.source === "expense") editExpense(record as ExpenseLine);
    else editAsset(record as AssetPurchaseLine);
  };

  const askDeleteCost = async (row: CostLine) => {
    const record = await loadCostRecord(row);
    if (!record) return;
    if (row.source === "expense") {
      const expense = record as ExpenseLine;
      setPendingDelete({ kind: "expense", id: expense.id, token: expense.concurrencyToken });
    } else {
      const asset = record as AssetPurchaseLine;
      setPendingDelete({ kind: "asset", id: asset.id, token: asset.concurrencyToken });
    }
  };

  const openFile = async (kind: FinanceRecordKind, id: number, attachment: FinanceAttachmentInfo, download: boolean) => {
    try {
      await openFinanceAttachment(kind, id, attachment.fileName, download);
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "The attachment could not be opened.");
    }
  };

  const projectOptions = projects.map((project) => ({ value: String(project.id), label: project.projectName }));
  const accountOptions = [
    { value: "unassigned", label: "Unassigned" },
    ...accounts.map((account) => ({
      value: String(account.id),
      label: `${accountLabel(account, isStaffFloat(account))}`,
    })),
  ];
  const periodOptions = [
    ...PERIODS.map((preset) => ({
      value: preset,
      label: financePeriodLabel(preset, startMonth),
      disabled: (preset === "year" || preset === "lastYear") && startMonth === null,
    })),
    ...((period === "custom" || draftPeriod(draft.from, draft.to, startMonth) === "custom") ? [{ value: "custom", label: "Custom", disabled: true }] : []),
  ];

  const cardValue = (amount: number | null | undefined) => {
    if (summaryLoading) return <span className="mt-1 block h-7 w-28 animate-pulse rounded bg-line" />;
    if (summaryError || amount == null) return "—";
    return formatRs(amount);
  };

  const datesSet = Boolean(fromDate || toDate);
  const singleAccount = accountSelected && accountFilter !== "unassigned";

  return (
    <div className="flex flex-col gap-4">
      <PageHeader
        title="Finance"
        actions={isPhone ? (
          <div className="flex items-center gap-2">
            <Button icon={<IconPlus size={16} />} onClick={() => { setFormError(null); setExpenseForm(emptyExpense()); }}>Add expense</Button>
            <Button iconOnly variant="outline" aria-label="More actions" icon={<IconMore size={18} />} onClick={() => setMoreOpen(true)} />
          </div>
        ) : (
          <div className="flex flex-wrap items-center gap-2">
            <Button variant="outline" icon={<IconPlus size={16} />} onClick={() => { setFormError(null); setRevenueForm(emptyRevenue()); }}>Add revenue</Button>
            <Button variant="outline" icon={<IconPlus size={16} />} onClick={() => { setFormError(null); setAssetForm(emptyAsset()); }}>Add fixed asset</Button>
            <Button icon={<IconPlus size={16} />} onClick={() => { setFormError(null); setExpenseForm(emptyExpense()); }}>Add expense</Button>
          </div>
        )}
      />

      <nav className="hidden flex-wrap gap-x-4 gap-y-2 md:flex" aria-label="Finance">
        {LINKS.map((link) => (
          <Link key={link.label} to={link.to} className="text-sm font-bold text-primary no-underline hover:underline">{link.label}</Link>
        ))}
      </nav>

      <div className="hidden flex-wrap items-center gap-2.5 md:flex">
        <Dropdown size="filter" label="Period" options={periodOptions} value={period} onChange={applyPeriod} className="w-auto max-w-[280px]" />
        <Dropdown size="filter" label="Project" options={[{ value: "", label: "All" }, ...projectOptions]} value={projectId} onChange={setProjectId} className="w-auto max-w-[240px]" />
        <Dropdown size="filter" label="Account" options={[{ value: "", label: "All" }, ...accountOptions]} value={accountFilter} onChange={setAccountFilter} className="w-auto max-w-[240px]" />
        <DatePicker size="filter" label="From" max={today()} value={fromDate} onChange={setFromDate} />
        <DatePicker size="filter" label="To" max={today()} value={toDate} onChange={setToDate} />
        {filtersOn && <Button variant="link" onClick={resetFilters}>Reset</Button>}
      </div>
      <div className="md:hidden">
        <button
          type="button"
          aria-label={filtersOn ? "Filters (applied)" : "Filters"}
          onClick={() => { setDraft({ projectId, account: accountFilter, from: fromDate, to: toDate }); setSheetOpen(true); }}
          className="relative flex h-11 items-center gap-2 rounded-field border border-line-input bg-card px-3 font-ui text-sm font-bold text-ink"
        >
          Filters
          {filtersOn && <span className="flex size-5 items-center justify-center rounded-full bg-gold text-caption font-extrabold text-primary">{[projectId, accountFilter, fromDate || toDate].filter(Boolean).length}</span>}
        </button>
      </div>
      {(rangeError || yearFailed) && (
        <div className="flex flex-col gap-2">
          {rangeError && <Notice tone="red" title={rangeError} />}
          {yearFailed && <Notice tone="gold" title="Financial year setting unavailable — use a custom From/To range." />}
        </div>
      )}

      <div className={`grid grid-cols-2 gap-2.5 md:gap-4 ${accountSelected ? "md:grid-cols-3" : "md:grid-cols-4"}`}>
        <StatCard
          label={accountSelected ? "Revenue on this account" : "Total revenue"}
          tone="green"
          value={cardValue(summary?.totalRevenue)}
          selected={view === "revenue"}
          onClick={() => setView("revenue")}
        />
        <StatCard
          label={accountSelected ? "Costs on this account" : "Total expenses"}
          tone="grey"
          value={cardValue(summary?.totalExpenses)}
          selected={view === "totalExpenses"}
          onClick={() => setView("totalExpenses")}
        />
        {accountSelected ? (
          <StatCard
            label="Account net movement"
            tone={(summary?.accountNetMovement ?? 0) < 0 ? "red" : "grey"}
            value={summaryLoading ? cardValue(null) : summaryError ? "—" : (
              <span className={(summary?.accountNetMovement ?? 0) < 0 ? "text-danger" : undefined}>{formatRs(summary?.accountNetMovement ?? 0)}</span>
            )}
          />
        ) : (
          <>
            <StatCard
              label={<span>Customer deposits<span className="mt-0.5 block text-caption font-semibold text-ink-muted">At period end</span></span>}
              tone="blue"
              value={cardValue(summary?.customerDepositsBalance)}
              selected={view === "customerDeposits"}
              onClick={() => setView("customerDeposits")}
            />
            <StatCard
              label={<span>Overdue<span className="mt-0.5 block text-caption font-semibold text-ink-muted">As of today</span></span>}
              tone="red"
              value={cardValue(summary?.overdueAmount)}
              selected={view === "overdue"}
              onClick={() => setView("overdue")}
            />
          </>
        )}
      </div>

      {summaryError && (
        <Notice tone="red" title={summaryError} action={<Button variant="outline" onClick={() => void loadSummary()}>Try again</Button>} />
      )}
      {owed != null && owed > 0 && (
        <Notice
          tone="gold"
          title={`Tax to deposit to FBR: ${formatRs(owed)}`}
          action={<Button variant="outline" onClick={() => navigate("/finance/settings")}>View</Button>}
        />
      )}
      {accountSelected && (
        <Notice
          tone="gold"
          title="Account filter on: entries on this account only. Sales move no cash, so they are on no account."
          action={<Button variant="outline" onClick={() => setAccountFilter("")}>Clear account filter</Button>}
        />
      )}
      {singleAccount && summary && !summaryLoading && !summaryError && (
        <div className="grid grid-cols-1 gap-2.5 md:grid-cols-3">
          <StatCard label={`Current balance${datesSet ? " (to period end)" : ""}`} tone={(summary.accountCurrentBalance ?? 0) < 0 ? "red" : "grey"} value={<span className={(summary.accountCurrentBalance ?? 0) < 0 ? "text-danger" : undefined}>{formatRs(summary.accountCurrentBalance ?? 0)}</span>} />
          <StatCard label="Opening balance" value={formatRs(summary.accountOpeningBalance ?? 0)} />
          <StatCard label={`Net movement${datesSet ? " (period)" : ""}`} value={formatRs(summary.accountNetMovement ?? 0)} />
        </div>
      )}

      <section className="flex flex-col gap-3">
        <h2 className="m-0 font-ui text-body font-extrabold text-ink">{TITLES[view]}</h2>
        {rows.error && (
          <Notice tone="red" title={rows.error} action={<Button variant="outline" onClick={() => rows.reload()}>Try again</Button>} />
        )}
        {rows.loading && rows.rows.length === 0 ? (
          <div className="flex flex-col gap-2" aria-hidden="true">
            {Array.from({ length: 4 }, (_, index) => <div key={index} className="h-14 animate-pulse rounded-card bg-line" />)}
          </div>
        ) : rows.rows.length === 0 ? (
          <EmptyState title={EMPTY[view]} />
        ) : (
          <HomeTable
            view={view}
            rows={rows.rows}
            isPhone={isPhone}
            opening={opening}
            onEditRevenue={(row) => {
              setExpenseForm(null);
              setAssetForm(null);
              setFormError(null);
              setRevenueForm({
                id: row.manualRevenueId,
                concurrencyToken: row.concurrencyToken ?? "",
                projectId: row.projectId != null ? String(row.projectId) : "",
                financeAccountId: row.financeAccountId != null ? String(row.financeAccountId) : "",
                amount: String(row.amount),
                revenueType: row.revenueType,
                revenueCategoryId: row.revenueCategoryId != null ? String(row.revenueCategoryId) : "",
                description: row.description ?? "",
                reference: row.reference ?? "",
                date: row.date.slice(0, 10),
                attachment: row.attachment,
                selectedAttachment: null,
                removeAttachment: false,
              });
            }}
            onDeleteRevenue={(row) => setPendingDelete({ kind: "revenue", id: row.manualRevenueId!, token: row.concurrencyToken ?? "" })}
            onOpenCost={(row) => void openCost(row)}
            onDeleteCost={(row) => void askDeleteCost(row)}
            onOpenFile={(kind, id, attachment, download) => void openFile(kind, id, attachment, download)}
          />
        )}
        {!isPhone && rows.totalCount > 0 && (
          <Pagination page={rows.page} onPageChange={rows.setPage} totalCount={rows.totalCount} pageSize={FINANCE_PAGE_SIZE} itemLabel="entries" />
        )}
        {isPhone && rows.rows.length < rows.totalCount && (
          <LoadMore shown={rows.rows.length} total={rows.totalCount} loading={rows.loadingMore} onLoadMore={rows.loadMore} />
        )}
        {isPhone && rows.totalCount > 0 && rows.rows.length >= rows.totalCount && (
          <p className="m-0 text-center text-small text-ink-muted">Showing {rows.rows.length.toLocaleString("en-PK")} of {rows.totalCount.toLocaleString("en-PK")} entries</p>
        )}
      </section>

      <BottomSheet open={moreOpen} onClose={() => setMoreOpen(false)} title="More actions" footer={null}>
        <div className="flex flex-col">
          <SheetRow icon={<IconPlus size={18} />} label="Add revenue" onClick={() => { setMoreOpen(false); setFormError(null); setRevenueForm(emptyRevenue()); }} />
          <SheetRow icon={<IconPlus size={18} />} label="Add fixed asset" onClick={() => { setMoreOpen(false); setFormError(null); setAssetForm(emptyAsset()); }} />
          <div className="my-1 h-px bg-line" />
          {LINKS.map((link) => (
            <Link key={link.label} to={link.to} onClick={() => setMoreOpen(false)} className="flex h-12 items-center gap-3 text-sm font-bold text-ink no-underline">
              <link.icon size={18} />
              {link.label}
            </Link>
          ))}
        </div>
      </BottomSheet>

      <BottomSheet
        open={sheetOpen}
        onClose={() => setSheetOpen(false)}
        title="Filters"
        onApply={() => {
          if (draftRangeError) return;
          setProjectId(draft.projectId);
          setAccountFilter(draft.account);
          setFromDate(draft.from);
          setToDate(draft.to);
          setSheetOpen(false);
        }}
        onReset={() => { resetFilters(); setSheetOpen(false); }}
      >
        <div className="flex flex-col gap-4">
          <Dropdown label="Period" options={periodOptions} value={draftPeriod(draft.from, draft.to, startMonth)} onChange={(preset) => {
            if (preset === "custom") return;
            const range = buildPeriodRange(preset as FinancePeriodPreset, startMonth);
            setDraft((current) => ({ ...current, from: range.from, to: range.to }));
          }} />
          <Dropdown label="Project" options={[{ value: "", label: "All" }, ...projectOptions]} value={draft.projectId} onChange={(value) => setDraft((current) => ({ ...current, projectId: value }))} />
          <Dropdown label="Account" options={[{ value: "", label: "All" }, ...accountOptions]} value={draft.account} onChange={(value) => setDraft((current) => ({ ...current, account: value }))} />
          <DatePicker label="From" max={today()} value={draft.from} onChange={(value) => setDraft((current) => ({ ...current, from: value }))} />
          <DatePicker label="To" max={today()} value={draft.to} onChange={(value) => setDraft((current) => ({ ...current, to: value }))} />
          {draftRangeError && <Notice tone="red" title={draftRangeError} />}
        </div>
      </BottomSheet>

      {revenueForm && (
        <RevenueDialog
          form={revenueForm}
          setForm={setRevenueForm}
          projects={projects}
          accounts={accounts}
          categories={revenueCategories}
          categoriesLoading={categoriesLoading}
          saving={saving}
          error={formError}
          onClose={closeForms}
          onSave={() => void submitRevenue()}
        />
      )}
      {expenseForm && (
        <ExpenseDialog
          form={expenseForm}
          setForm={setExpenseForm}
          projects={projects}
          accounts={accounts}
          categories={expenseCategories}
          vendors={vendors}
          saving={saving}
          taxBlocksSave={taxBlocksSave}
          onTaxBlocksSave={setTaxBlocksSave}
          error={formError}
          onClose={closeForms}
          onSave={() => void submitExpense()}
        />
      )}
      {assetForm && (
        <AssetDialog
          form={assetForm}
          setForm={setAssetForm}
          projects={projects}
          accounts={accounts}
          assetAccounts={assetAccounts}
          categories={expenseCategories}
          vendors={vendors}
          saving={saving}
          taxBlocksSave={taxBlocksSave}
          onTaxBlocksSave={setTaxBlocksSave}
          error={formError}
          onClose={closeForms}
          onSave={() => void submitAsset()}
        />
      )}

      <ConfirmDialog
        open={pendingDelete != null}
        loading={deleting}
        danger
        confirmLabel="Delete"
        title={pendingDelete?.kind === "revenue" ? "Delete this manual revenue entry?" : pendingDelete?.kind === "asset" ? "Delete this fixed asset purchase?" : "Delete this expense?"}
        message={pendingDelete?.kind === "asset" ? "The bank balance and the asset account both move back." : "This cannot be undone."}
        onClose={() => { if (!deleting) setPendingDelete(null); }}
        onConfirm={() => void confirmDelete()}
      />
    </div>
  );
}

function SheetRow({ icon, label, onClick }: { icon: ReactNode; label: string; onClick: () => void }) {
  return (
    <button type="button" onClick={onClick} className="flex h-12 w-full cursor-pointer items-center gap-3 border-0 bg-transparent p-0 text-left text-sm font-bold text-ink">
      {icon}
      {label}
    </button>
  );
}

function attachmentBits(
  kind: FinanceRecordKind,
  id: number | null,
  attachment: FinanceAttachmentInfo | null,
  isPhone: boolean,
  onOpen: (kind: FinanceRecordKind, id: number, attachment: FinanceAttachmentInfo, download: boolean) => void,
) {
  if (!attachment || id == null) return <span className="text-small text-ink-muted">None</span>;
  return (
    <span className="inline-flex items-center gap-2">
      <button type="button" onClick={(event) => { event.stopPropagation(); onOpen(kind, id, attachment, false); }} className="inline-flex h-6 items-center gap-1 rounded-full bg-steel-soft px-2.5 text-label font-bold text-steel">
        <IconPaperclip size={12} /> Attached
      </button>
      {!isPhone && (
        <Button iconOnly variant="ghost" aria-label={`Download ${attachment.fileName}`} icon={<IconDownload size={16} />} onClick={(event) => { event.stopPropagation(); onOpen(kind, id, attachment, true); }} />
      )}
    </span>
  );
}

function iconButtons(edit: () => void, remove: () => void, busy: boolean) {
  return (
    <span className="inline-flex justify-end gap-1">
      <Button iconOnly variant="ghost" aria-label="Edit" loading={busy} icon={<IconPencil size={16} />} onClick={(event) => { event.stopPropagation(); edit(); }} />
      <Button iconOnly variant="danger" aria-label="Delete" icon={<IconTrash size={16} />} onClick={(event) => { event.stopPropagation(); remove(); }} />
    </span>
  );
}

function HomeTable({
  view, rows, isPhone, opening, onEditRevenue, onDeleteRevenue, onOpenCost, onDeleteCost, onOpenFile,
}: {
  view: HomeView;
  rows: HomeRow[];
  isPhone: boolean;
  opening: string | null;
  onEditRevenue: (row: RevenueLine) => void;
  onDeleteRevenue: (row: RevenueLine) => void;
  onOpenCost: (row: CostLine) => void;
  onDeleteCost: (row: CostLine) => void;
  onOpenFile: (kind: FinanceRecordKind, id: number, attachment: FinanceAttachmentInfo, download: boolean) => void;
}) {
  if (view === "revenue") {
    const list = rows as RevenueLine[];
    const columns: DataTableColumn<RevenueLine>[] = [
      { key: "date", header: "Date", render: (row) => formatDay(row.date) },
      { key: "project", header: "Project", render: (row) => row.projectName || "General" },
      { key: "account", header: "Received in", render: (row) => <span>{row.financeAccountName ?? "Unassigned"}{row.accountHolderName && <small className="block text-ink-muted">{row.accountHolderName}</small>}</span> },
      { key: "type", header: "Revenue type", render: (row) => row.revenueType },
      { key: "amount", header: "Amount", align: "right", render: (row) => moneyTone(row.amount, "green") },
      { key: "source", header: "Source", render: (row) => <StatusBadge status={SOURCE[row.source]?.label ?? row.source} tone={SOURCE[row.source]?.tone}>{SOURCE[row.source]?.label ?? row.source}</StatusBadge> },
      { key: "file", header: "Attachment", render: (row) => attachmentBits("revenue", row.manualRevenueId, row.attachment, isPhone, onOpenFile) },
      { key: "actions", header: "", align: "right", render: (row) => row.manualRevenueId != null ? iconButtons(() => onEditRevenue(row), () => onDeleteRevenue(row), false) : null },
    ];
    return (
      <DataTable
        columns={columns}
        rows={list}
        rowKey={(row) => `${row.source}:${row.manualRevenueId ?? row.reference ?? row.description}:${row.date}:${row.amount}`}
        minWidth={980}
        phoneCard={(row) => (
          <article className="rounded-card border border-line bg-card p-3 font-ui">
            <div className="flex items-start justify-between gap-3">
              <p className="m-0 font-extrabold text-ink">{row.revenueType}</p>
              {moneyTone(row.amount, "green")}
            </div>
            <p className="m-0 mt-1 text-small text-ink-muted">{row.projectName || "General"} · {formatDay(row.date)}</p>
            <p className="m-0 text-small text-ink-2">Received in {row.financeAccountName ?? "Unassigned"}</p>
            <div className="mt-2 flex flex-wrap items-center gap-2">
              <StatusBadge status={SOURCE[row.source]?.label ?? row.source} tone={SOURCE[row.source]?.tone}>{SOURCE[row.source]?.label ?? row.source}</StatusBadge>
              {attachmentBits("revenue", row.manualRevenueId, row.attachment, true, onOpenFile)}
              {row.manualRevenueId != null && iconButtons(() => onEditRevenue(row), () => onDeleteRevenue(row), false)}
            </div>
          </article>
        )}
      />
    );
  }

  if (view === "totalExpenses") {
    const list = rows as CostLine[];
    const owned = (row: CostLine) => row.source === "expense" || row.source === "assetPurchase";
    const elsewhere = (row: CostLine) => row.source === "loanInterest" ? "Open in Loans" : "Open in Commissions & rebates";
    const columns: DataTableColumn<CostLine>[] = [
      { key: "date", header: "Date", render: (row) => formatDay(row.date) },
      { key: "project", header: "Project", render: (row) => row.projectName || "General" },
      { key: "cost", header: "Cost", render: (row) => row.label },
      { key: "amount", header: "Amount", align: "right", render: (row) => signedCost(row.amount) },
      { key: "file", header: "Attachment", render: (row) => owned(row) ? attachmentBits(row.source === "assetPurchase" ? "assetPurchase" : "expense", row.sourceId, row.attachment, isPhone, onOpenFile) : <span className="text-ink-muted">—</span> },
      { key: "actions", header: "", align: "right", render: (row) => owned(row)
        ? iconButtons(() => onOpenCost(row), () => onDeleteCost(row), opening === `${row.source}:${row.sourceId}`)
        : <Button iconOnly variant="ghost" aria-label={elsewhere(row)} icon={<span aria-hidden="true">↗</span>} onClick={(event) => { event.stopPropagation(); onOpenCost(row); }} /> },
    ];
    return (
      <DataTable
        columns={columns}
        rows={list}
        rowKey={(row) => `${row.source}:${row.sourceId}`}
        onRowClick={onOpenCost}
        rowLabel={(row) => owned(row) ? `Edit ${row.label}` : elsewhere(row)}
        minWidth={860}
        phoneCard={(row) => (
          <article className="rounded-card border border-line bg-card p-3 font-ui">
            <div className="flex items-start justify-between gap-3">
              <p className="m-0 font-extrabold text-ink">{row.label}</p>
              {signedCost(row.amount)}
            </div>
            <p className="m-0 mt-1 text-small text-ink-muted">{row.projectName || "General"} · {formatDay(row.date)}</p>
            <div className="mt-2 flex flex-wrap items-center gap-2">
              {owned(row) ? (
                <>
                  {attachmentBits(row.source === "assetPurchase" ? "assetPurchase" : "expense", row.sourceId, row.attachment, true, onOpenFile)}
                  {iconButtons(() => onOpenCost(row), () => onDeleteCost(row), opening === `${row.source}:${row.sourceId}`)}
                </>
              ) : (
                <button type="button" className="border-0 bg-transparent p-0 text-sm font-bold text-primary" onClick={() => onOpenCost(row)}>{elsewhere(row)} ↗</button>
              )}
            </div>
          </article>
        )}
      />
    );
  }

  if (view === "customerDeposits") {
    const list = rows as CustomerDepositLine[];
    const cleared = (row: CustomerDepositLine) => row.recognitionDate
      ? { word: "Possession", date: row.recognitionDate, className: "text-success" }
      : row.cancellationDate
        ? { word: "Cancelled", date: row.cancellationDate, className: "text-danger" }
        : null;
    const columns: DataTableColumn<CustomerDepositLine>[] = [
      { key: "booking", header: "Booking", render: (row) => <span>{row.bookingReference}<small className="block text-ink-muted">{row.unitNumber}</small></span> },
      { key: "customer", header: "Customer", render: (row) => <span>{row.customerName}<small className="block text-ink-muted">{row.projectName}</small></span> },
      { key: "status", header: "Current status", render: (row) => <StatusBadge status={row.bookingStatus || "—"} /> },
      { key: "sale", header: "Net sale value", align: "right", render: (row) => moneyTone(row.netSaleValue) },
      { key: "cash", header: "Cash received", align: "right", render: (row) => moneyTone(row.customerCashReceived, "green") },
      { key: "held", header: "Deposit held", align: "right", render: (row) => moneyTone(row.depositBalance, "blue") },
      { key: "cleared", header: "Cleared by", render: (row) => {
        const event = cleared(row);
        return event ? <span><span className={`font-extrabold ${event.className}`}>{event.word}</span> · {formatDay(event.date)}</span> : "—";
      } },
    ];
    return (
      <DataTable
        columns={columns}
        rows={list}
        rowKey={(row) => row.bookingId}
        minWidth={980}
        phoneCard={(row) => {
          const event = cleared(row);
          return (
            <article className="rounded-card border border-line bg-card p-3 font-ui">
              <div className="flex items-center justify-between gap-2">
                <span className="text-small text-ink-muted">{row.bookingReference}</span>
                <StatusBadge status={row.bookingStatus || "—"} />
              </div>
              <p className="m-0 mt-1 font-extrabold text-ink">{row.customerName}</p>
              <p className="m-0 text-small text-ink-muted">{row.unitNumber} · {row.projectName}</p>
              <dl className="mt-2 grid grid-cols-2 gap-2 text-small">
                <div><dt className="text-ink-muted">Net sale value</dt><dd className="m-0 font-extrabold">{formatRs(row.netSaleValue)}</dd></div>
                <div><dt className="text-ink-muted">Cash received</dt><dd className="m-0 font-extrabold text-success">{formatRs(row.customerCashReceived)}</dd></div>
                <div><dt className="text-ink-muted">Deposit held</dt><dd className="m-0 font-extrabold text-info">{formatRs(row.depositBalance)}</dd></div>
                <div><dt className="text-ink-muted">Cleared by</dt><dd className="m-0 font-extrabold">{event ? `${event.word} · ${formatDay(event.date)}` : "—"}</dd></div>
              </dl>
            </article>
          );
        }}
      />
    );
  }

  const list = rows as OverdueLine[];
  const columns: DataTableColumn<OverdueLine>[] = [
    { key: "due", header: "Due date", render: (row) => <span className="font-bold text-danger">{formatDay(row.dueDate)}</span> },
    { key: "booking", header: "Booking", render: (row) => row.bookingReference },
    { key: "customer", header: "Customer", render: (row) => row.customerName },
    { key: "project", header: "Project", render: (row) => row.projectName },
    { key: "unit", header: "Unit", render: (row) => row.unitNumber },
    { key: "installment", header: "Installment", render: (row) => `#${row.sequenceNumber} · ${row.installmentType}` },
    { key: "amount", header: "Amount", align: "right", render: (row) => moneyTone(row.amount) },
    { key: "paid", header: "Paid", align: "right", render: (row) => moneyTone(row.paidAmount, "green") },
    { key: "overdue", header: "Overdue", align: "right", render: (row) => moneyTone(row.overdueAmount, "red") },
  ];
  return (
    <DataTable
      columns={columns}
      rows={list}
      rowKey={(row) => `${row.bookingReference}:${row.sequenceNumber}:${row.dueDate}`}
      minWidth={980}
      phoneCard={(row) => (
        <article className="rounded-card border border-line bg-card p-3 font-ui">
          <div className="flex items-center justify-between gap-2">
            <span className="text-small text-ink-muted">{row.bookingReference}</span>
            <span className="text-small font-bold text-danger">Due {formatDay(row.dueDate)}</span>
          </div>
          <p className="m-0 mt-1 font-extrabold text-ink">{row.customerName}</p>
          <p className="m-0 text-small text-ink-muted">{row.unitNumber} · {row.projectName} · Installment #{row.sequenceNumber} · {row.installmentType}</p>
          <dl className="mt-2 grid grid-cols-3 gap-2 text-small">
            <div><dt className="text-ink-muted">Amount</dt><dd className="m-0 font-extrabold">{formatRs(row.amount)}</dd></div>
            <div><dt className="text-ink-muted">Paid</dt><dd className="m-0 font-extrabold text-success">{formatRs(row.paidAmount)}</dd></div>
            <div><dt className="text-ink-muted">Overdue</dt><dd className="m-0 font-extrabold text-danger">{formatRs(row.overdueAmount)}</dd></div>
          </dl>
        </article>
      )}
    />
  );
}
