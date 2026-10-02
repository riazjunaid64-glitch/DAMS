import { useCallback, useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { api } from "../api/api.ts";
import { financeApiError, openFinanceAttachment, type FinanceAttachmentInfo, type FinanceRecordKind } from "../api/financeAttachments.ts";
import type { User } from "../App.tsx";
import {
  Button,
  FilterBar,
  IconMore,
  IconPlus,
  LoadMore,
  Notice,
  PageHeader,
  Pagination,
  useIsPhone,
  useToast,
  type FilterValues,
} from "../components/ui";
import { pageAccess } from "../features/access/permissions.ts";
import { useProjects } from "../contexts/projectsContextValue.ts";
import { AssetPurchaseDialog } from "../features/finance/home/AssetPurchaseDialog.tsx";
import { FinanceDeleteDialog } from "../features/finance/home/delete.tsx";
import { ExpenseDialog } from "../features/finance/home/ExpenseDialog.tsx";
import { assetFormFrom, emptyAssetForm, emptyExpenseForm, emptyRevenueForm, expenseFormFrom, revenueFormFrom } from "../features/finance/home/format.ts";
import { FinanceAddButtons, FinanceLinkRow, FinanceMoreSheet } from "../features/finance/home/links.tsx";
import { filterAccountLabel } from "../features/finance/home/options.ts";
import { RevenueDialog } from "../features/finance/home/RevenueDialog.tsx";
import { FinanceCards, FinanceNotices } from "../features/finance/home/summary.tsx";
import { FinanceTable } from "../features/finance/home/table.tsx";
import type {
  AssetPurchaseFormState,
  AssetPurchaseLine,
  CostLine,
  ExpenseFormState,
  ExpenseLine,
  FinanceAccountOption,
  FinanceRow,
  FinanceView,
  FinancialSummary,
  PendingDelete,
  RevenueCategory,
  RevenueFormState,
} from "../features/finance/home/types.ts";
import { listCategories, payableSummary, vendorOptions } from "../features/finance/whtApi.ts";
import type { ExpenseCategory, VendorOption } from "../features/finance/whtTypes.ts";
import { useFinancialYearStartMonth } from "../features/finance/useFinancialYearStartMonth.ts";
import { fetchFinanceDashboard } from "../lib/financeChartData.ts";
import { buildPeriodRange, pakistanToday } from "../lib/financePeriods.ts";
import { usePagedList, type PagedListQuery } from "../lib/usePagedList.ts";

type Props = { user: User | null };

const VIEW_PARAM: Record<FinanceView, string> = {
  revenue: "revenue",
  totalExpenses: "totalExpenses",
  customerDeposits: "customerDeposits",
  overdue: "overdue",
};

const VIEWS: readonly FinanceView[] = ["revenue", "totalExpenses", "customerDeposits", "overdue"];

/** The list query starts with the open card. Rows from the previous card keep that card's columns. */
function viewInQueryKey(queryKey: string): FinanceView | null {
  const head = queryKey.split("|")[0] as FinanceView;
  return VIEWS.includes(head) ? head : null;
}

export default function FinanceDashboardPage({ user }: Props) {
  const access = pageAccess(user?.role, "finance");
  const allowed = access === "allow";
  const isPhone = useIsPhone();
  const navigate = useNavigate();
  const toast = useToast();
  const { projects } = useProjects();
  const { startMonth, failed: yearFailed } = useFinancialYearStartMonth(allowed);
  const yearStatus = yearFailed ? "error" : startMonth == null ? "loading" : "ready";

  const [projectId, setProjectId] = useState("");
  const [accountFilter, setAccountFilter] = useState("");
  const [fromDate, setFromDate] = useState("");
  const [toDate, setToDate] = useState("");
  const [view, setView] = useState<FinanceView>("revenue");
  const accountSelected = accountFilter !== "";
  const shownView: FinanceView = accountSelected && (view === "customerDeposits" || view === "overdue") ? "revenue" : view;
  if (shownView !== view) setView(shownView);

  const [summary, setSummary] = useState<FinancialSummary | null>(null);
  const [summaryLoading, setSummaryLoading] = useState(true);
  const [summaryError, setSummaryError] = useState<string | null>(null);
  const [payable, setPayable] = useState<number | null>(null);
  const [revenueLookupError, setRevenueLookupError] = useState<string | null>(null);
  const [lookupError, setLookupError] = useState<string | null>(null);
  const [assetLookupError, setAssetLookupError] = useState<string | null>(null);
  const [accounts, setAccounts] = useState<FinanceAccountOption[]>([]);
  const [assetAccounts, setAssetAccounts] = useState<FinanceAccountOption[]>([]);
  const [assetAccountsLoading, setAssetAccountsLoading] = useState(false);
  const [revenueCategories, setRevenueCategories] = useState<RevenueCategory[]>([]);
  const [revenueCategoriesLoading, setRevenueCategoriesLoading] = useState(false);
  const [expenseCategories, setExpenseCategories] = useState<ExpenseCategory[]>([]);
  const [vendors, setVendors] = useState<VendorOption[]>([]);
  const [lookupsLoading, setLookupsLoading] = useState(false);
  const [revenueForm, setRevenueForm] = useState<RevenueFormState | null>(null);
  const [expenseForm, setExpenseForm] = useState<ExpenseFormState | null>(null);
  const [assetForm, setAssetForm] = useState<AssetPurchaseFormState | null>(null);
  const [pendingDelete, setPendingDelete] = useState<PendingDelete | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [busyKey, setBusyKey] = useState<string | null>(null);
  const [moreOpen, setMoreOpen] = useState(false);

  const summaryRequest = useRef(0);
  const payableRequest = useRef(0);
  const assetLoaded = useRef(false);
  const assetFailed = useRef(false);
  const assetRequest = useRef<Promise<void> | null>(null);
  const revenueLoaded = useRef(false);
  const revenueFailed = useRef(false);
  const revenueRequest = useRef<Promise<void> | null>(null);
  const lookupsLoaded = useRef(false);
  const lookupsFailed = useRef(false);
  const lookupsRequest = useRef<Promise<void> | null>(null);

  const loadSummary = useCallback(async (signal?: AbortSignal) => {
    const ticket = ++summaryRequest.current;
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
      if (signal?.aborted || ticket !== summaryRequest.current) return;
      setSummary(null);
      setSummaryError("The finance totals could not be loaded, so the figures below are unavailable.");
    } finally {
      if (ticket === summaryRequest.current) setSummaryLoading(false);
    }
  }, [projectId, fromDate, toDate, accountFilter]);

  const fetchPage = useCallback(async ({ skip, take, signal }: PagedListQuery) => {
    if (!allowed) return { items: [] as FinanceRow[], hasMore: false, totalCount: 0 };
    const params = new URLSearchParams({ view: VIEW_PARAM[shownView], skip: String(skip), take: String(take) });
    if (projectId) params.set("projectId", projectId);
    if (fromDate) params.set("from", fromDate);
    if (toDate) params.set("to", toDate);
    if (accountFilter) params.set("account", accountFilter);
    const res = await api(`/api/Finance/rows?${params.toString()}`, { signal });
    if (!res.ok) throw new Error(await financeApiError(res, "Unable to load rows."));
    const json = await res.json() as { items?: FinanceRow[]; hasMore?: boolean; totalCount?: number | null };
    return { items: json.items ?? [], hasMore: json.hasMore, totalCount: json.totalCount ?? null };
  }, [allowed, shownView, projectId, fromDate, toDate, accountFilter]);

  const list = usePagedList<FinanceRow>({
    queryKey: allowed ? `${shownView}|${projectId}|${accountFilter}|${fromDate}|${toDate}` : "waiting",
    fetchPage,
  });

  const loadPayable = useCallback(async () => {
    const ticket = ++payableRequest.current;
    try {
      const loaded = await payableSummary();
      if (ticket !== payableRequest.current) return;
      setPayable(loaded.outstandingPayable);
    } catch {
      if (ticket !== payableRequest.current) return;
      setPayable(null);
    }
  }, []);

  const refreshTotals = useCallback(() => {
    void loadSummary();
    void loadPayable();
  }, [loadSummary, loadPayable]);

  const refresh = useCallback(() => {
    refreshTotals();
    list.reload();
  }, [refreshTotals, list]);

  useEffect(() => {
    if (!allowed) return;
    const controller = new AbortController();
    void loadSummary(controller.signal);
    return () => controller.abort();
  }, [allowed, loadSummary]);

  useEffect(() => {
    if (!allowed) return;
    void loadPayable();
  }, [allowed, loadPayable]);

  useEffect(() => {
    if (!allowed) return;
    let live = true;
    void (async () => {
      try {
        const [regular, staff] = await Promise.all([
          api("/api/finance/accounts/options?includeInactive=true&cashLikeOnly=true"),
          api("/api/finance/accounts/options?includeInactive=true&type=10"),
        ]);
        if (!live) return;
        const regularRows = regular.ok ? await regular.json() as FinanceAccountOption[] : [];
        const staffRows = staff.ok ? await staff.json() as FinanceAccountOption[] : [];
        const rows = [...regularRows, ...staffRows];
        setAccounts(rows.filter((row, index) => rows.findIndex((other) => other.id === row.id) === index));
      } catch {
        /* The account filter stays on All, and a form says so if it cannot be chosen. */
      }
    })();
    return () => { live = false; };
  }, [allowed]);

  const loadRevenueCategories = useCallback((force = false): Promise<void> => {
    if (!force && (revenueLoaded.current || revenueFailed.current)) return Promise.resolve();
    if (revenueRequest.current) return revenueRequest.current;
    setRevenueCategoriesLoading(true);
    setRevenueLookupError(null);
    const request = (async () => {
      try {
        const response = await api("/api/finance/revenue-categories?includeInactive=true");
        if (!response.ok) throw new Error(await financeApiError(response, "Revenue categories could not be loaded."));
        setRevenueCategories(await response.json());
        revenueLoaded.current = true;
        revenueFailed.current = false;
      } catch (caught) {
        revenueFailed.current = true;
        setRevenueLookupError(caught instanceof Error && caught.message.trim() ? caught.message : "Revenue categories could not be loaded.");
      } finally {
        revenueRequest.current = null;
        setRevenueCategoriesLoading(false);
      }
    })();
    revenueRequest.current = request;
    return request;
  }, []);

  const loadAssetAccounts = useCallback((force = false): Promise<void> => {
    if (!force && (assetLoaded.current || assetFailed.current)) return Promise.resolve();
    if (assetRequest.current) return assetRequest.current;
    setAssetAccountsLoading(true);
    setAssetLookupError(null);
    const request = (async () => {
      try {
        const response = await api("/api/finance/accounts/options?includeInactive=true&type=7");
        if (!response.ok) throw new Error(await financeApiError(response, "Fixed asset accounts could not be loaded."));
        setAssetAccounts(await response.json() as FinanceAccountOption[]);
        assetLoaded.current = true;
        assetFailed.current = false;
      } catch (caught) {
        assetFailed.current = true;
        setAssetLookupError(caught instanceof Error && caught.message.trim() ? caught.message : "Fixed asset accounts could not be loaded.");
      } finally {
        assetRequest.current = null;
        setAssetAccountsLoading(false);
      }
    })();
    assetRequest.current = request;
    return request;
  }, []);

  const loadLookups = useCallback((force = false): Promise<void> => {
    if (!force && (lookupsLoaded.current || lookupsFailed.current)) return Promise.resolve();
    if (lookupsRequest.current) return lookupsRequest.current;
    setLookupsLoading(true);
    setLookupError(null);
    const request = (async () => {
      try {
        const [categories, vendorRows] = await Promise.all([listCategories(true), vendorOptions(true)]);
        setExpenseCategories(categories);
        setVendors(vendorRows);
        lookupsLoaded.current = true;
        lookupsFailed.current = false;
      } catch (caught) {
        lookupsFailed.current = true;
        setLookupError(caught instanceof Error && caught.message.trim() ? caught.message : "Expense categories could not be loaded.");
      } finally {
        lookupsRequest.current = null;
        setLookupsLoading(false);
      }
    })();
    lookupsRequest.current = request;
    return request;
  }, []);

  const retryRevenueCategories = useCallback(() => {
    revenueFailed.current = false;
    revenueLoaded.current = false;
    void loadRevenueCategories(true);
  }, [loadRevenueCategories]);

  const retryAssetAccounts = useCallback(() => {
    assetFailed.current = false;
    assetLoaded.current = false;
    void loadAssetAccounts(true);
  }, [loadAssetAccounts]);

  const retryLookups = useCallback(() => {
    lookupsFailed.current = false;
    lookupsLoaded.current = false;
    void loadLookups(true);
  }, [loadLookups]);

  const revenueOpen = revenueForm !== null;
  const expenseOpen = expenseForm !== null;
  const assetOpen = assetForm !== null;
  useEffect(() => { if (allowed && revenueOpen) void loadRevenueCategories(); }, [allowed, revenueOpen, loadRevenueCategories]);
  useEffect(() => { if (allowed && assetOpen) void loadAssetAccounts(); }, [allowed, assetOpen, loadAssetAccounts]);
  useEffect(() => { if (allowed && (expenseOpen || assetOpen)) void loadLookups(); }, [allowed, expenseOpen, assetOpen, loadLookups]);

  const openFile = useCallback(async (kind: FinanceRecordKind, id: number, attachment: FinanceAttachmentInfo, download: boolean) => {
    try {
      await openFinanceAttachment(kind, id, attachment.fileName, download);
    } catch (caught) {
      toast.error(caught instanceof Error ? caught.message : "The attachment could not be opened.");
    }
  }, [toast]);

  const loadCostRecord = useCallback(async (row: CostLine): Promise<ExpenseLine | AssetPurchaseLine | null> => {
    const path = row.source === "expense"
      ? `/api/Finance/expenses/${row.sourceId}`
      : `/api/Finance/asset-purchases/${row.sourceId}`;
    try {
      const res = await api(path);
      if (!res.ok) {
        toast.error(await financeApiError(res, "This record could not be opened. Refresh and try again."));
        return null;
      }
      return await res.json() as ExpenseLine | AssetPurchaseLine;
    } catch {
      toast.error("This record could not be opened. Check your connection and try again.");
      return null;
    }
  }, [toast]);

  const openRevenue = () => { setExpenseForm(null); setAssetForm(null); setRevenueForm(emptyRevenueForm()); };
  const openExpense = () => { setRevenueForm(null); setAssetForm(null); setExpenseForm(emptyExpenseForm()); };
  const openAsset = () => { setRevenueForm(null); setExpenseForm(null); setAssetForm(emptyAssetForm()); };

  const openCost = async (row: CostLine) => {
    if (row.source === "commission" || row.source === "rebate" || row.source === "customerCredit") {
      navigate("/finance/commissions-rebates");
      return;
    }
    if (row.source === "loanInterest") {
      navigate("/finance/loans");
      return;
    }
    setBusyKey(`edit:${row.source}:${row.sourceId}`);
    const record = await loadCostRecord(row);
    setBusyKey(null);
    if (!record) return;
    if (row.source === "expense") {
      setRevenueForm(null);
      setAssetForm(null);
      setExpenseForm(expenseFormFrom(record as ExpenseLine));
    } else {
      setRevenueForm(null);
      setExpenseForm(null);
      setAssetForm(assetFormFrom(record as AssetPurchaseLine));
    }
  };

  const askDeleteCost = async (row: CostLine) => {
    if (row.source !== "expense" && row.source !== "assetPurchase") return;
    setBusyKey(`delete:${row.source}:${row.sourceId}`);
    const record = await loadCostRecord(row);
    setBusyKey(null);
    if (!record) return;
    setPendingDelete({ kind: row.source, id: record.id, token: record.concurrencyToken });
  };

  const confirmDelete = async () => {
    if (!pendingDelete || deleting) return;
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
      refreshTotals();
      list.afterDelete();
    } catch {
      toast.error("This record could not be deleted. Check your connection and try again.");
      setPendingDelete(null);
    } finally {
      setDeleting(false);
    }
  };

  const onFilter = (changes: FilterValues) => {
    if ("project" in changes) setProjectId(changes.project ?? "");
    if ("account" in changes) {
      setAccountFilter(changes.account ?? "");
      setSummaryLoading(true);
    }
    if ("from" in changes) setFromDate(changes.from ?? "");
    if ("to" in changes) setToDate(changes.to ?? "");
  };

  if (access === "wait") return null;
  if (access === "deny") {
    return <p className="m-0 px-4 py-8 text-body font-bold text-ink">You don&apos;t have access to Finance.</p>;
  }

  const singleAccount = accountSelected && accountFilter !== "unassigned";
  const shown = list.rows.length;
  const total = list.total;

  return (
    <div className="mx-auto flex w-full max-w-[1500px] flex-col gap-4 px-4 py-5 md:gap-5 md:px-8 md:py-7">
      {isPhone ? (
        <div className="flex items-center justify-between gap-2">
          <h1 className="m-0 text-[22px] font-extrabold text-ink">Finance</h1>
          <div className="flex items-center gap-2">
            <Button icon={<IconPlus size={16} />} onClick={openExpense}>Add expense</Button>
            <Button iconOnly variant="outline" icon={<IconMore size={18} />} aria-label="More actions" onClick={() => setMoreOpen(true)} />
          </div>
        </div>
      ) : (
        <div className="flex flex-col gap-3">
          <PageHeader title="Finance" actions={<FinanceAddButtons onRevenue={openRevenue} onAsset={openAsset} onExpense={openExpense} />} />
          <FinanceLinkRow />
        </div>
      )}

      <FilterBar
        filters={[
          {
            type: "period",
            key: "period",
            label: "Period",
            fromKey: "from",
            toKey: "to",
            rangeFor: (preset) => buildPeriodRange(preset, startMonth),
            financialYear: yearStatus,
          },
          {
            type: "select",
            key: "project",
            label: "Project",
            options: projects.map((project) => ({ value: String(project.id), label: project.projectName })),
          },
          {
            type: "select",
            key: "account",
            label: "Account",
            options: [
              ...accounts.map((account) => ({ value: String(account.id), label: filterAccountLabel(account) })),
              { value: "unassigned", label: "Unassigned" },
            ],
          },
          { type: "dateRange", fromKey: "from", toKey: "to", max: pakistanToday() },
        ]}
        values={{ project: projectId, account: accountFilter, from: fromDate, to: toDate }}
        onChange={onFilter}
        onReset={() => { setProjectId(""); setAccountFilter(""); setFromDate(""); setToDate(""); }}
      />

      {yearFailed && (
        <Notice tone="gold" title="Financial year setting unavailable — use a custom From/To range." />
      )}

      <FinanceCards
        summary={summary}
        loading={summaryLoading}
        error={!!summaryError}
        accountSelected={accountSelected}
        view={shownView}
        onView={setView}
      />

      <FinanceNotices
        summaryError={summaryError}
        onRetryTotals={() => void loadSummary()}
        payable={payable}
        accountSelected={accountSelected}
        onClearAccount={() => setAccountFilter("")}
        summary={summary}
        summaryLoading={summaryLoading}
        showBalance={singleAccount}
        datesSet={!!fromDate || !!toDate}
      />

      {list.error && (
        <Notice
          tone="red"
          role="alert"
          title={list.error}
          action={<Button variant="outline" onClick={() => list.reload()}>Try again</Button>}
        />
      )}

      <FinanceTable
        view={(list.loading && list.rows.length > 0 ? viewInQueryKey(list.rowsKey) : null) ?? shownView}
        rows={list.rows}
        loading={list.loading}
        busyKey={busyKey}
        showEmpty={!list.error}
        onEditRevenue={(row) => { setExpenseForm(null); setAssetForm(null); setRevenueForm(revenueFormFrom(row)); }}
        onDeleteRevenue={(row) => {
          if (row.manualRevenueId == null) return;
          setPendingDelete({ kind: "revenue", id: row.manualRevenueId, token: row.concurrencyToken ?? "" });
        }}
        onOpenCost={(row) => void openCost(row)}
        onDeleteCost={(row) => void askDeleteCost(row)}
        onOpenAttachment={(kind, id, attachment) => void openFile(kind, id, attachment, false)}
        onDownloadAttachment={(kind, id, attachment) => void openFile(kind, id, attachment, true)}
      />

      <div className="hidden md:block">
        <Pagination {...list.pagination} itemLabel="entries" />
      </div>
      <div className="flex flex-col items-center gap-2 md:hidden">
        {((total != null && shown < total) || (total == null && list.hasMore)) && (
          <LoadMore {...list.loadMoreBar} showCount={false} />
        )}
        {total != null && total > 0 && (
          <p className="m-0 text-small text-ink-muted">
            Showing {shown.toLocaleString("en-PK")} of {total.toLocaleString("en-PK")} entries
          </p>
        )}
      </div>

      <FinanceMoreSheet open={moreOpen} onClose={() => setMoreOpen(false)} onAddRevenue={openRevenue} onAddAsset={openAsset} />

      {revenueForm && (
        <RevenueDialog
          form={revenueForm}
          projects={projects}
          accounts={accounts}
          categories={revenueCategories}
          categoriesLoading={revenueCategoriesLoading}
          lookupError={revenueLookupError}
          onRetryLookups={retryRevenueCategories}
          onChange={setRevenueForm}
          onClose={() => setRevenueForm(null)}
          onSaved={refresh}
          onOpenAttachment={() => { if (revenueForm.id && revenueForm.attachment) void openFile("revenue", revenueForm.id, revenueForm.attachment, false); }}
          onDownloadAttachment={() => { if (revenueForm.id && revenueForm.attachment) void openFile("revenue", revenueForm.id, revenueForm.attachment, true); }}
        />
      )}
      {expenseForm && (
        <ExpenseDialog
          form={expenseForm}
          projects={projects}
          accounts={accounts}
          categories={expenseCategories}
          vendors={vendors}
          lookupsLoading={lookupsLoading}
          lookupError={lookupError}
          onRetryLookups={retryLookups}
          onChange={setExpenseForm}
          onClose={() => setExpenseForm(null)}
          onSaved={refresh}
          onOpenAttachment={() => { if (expenseForm.id && expenseForm.attachment) void openFile("expense", expenseForm.id, expenseForm.attachment, false); }}
          onDownloadAttachment={() => { if (expenseForm.id && expenseForm.attachment) void openFile("expense", expenseForm.id, expenseForm.attachment, true); }}
        />
      )}
      {assetForm && (
        <AssetPurchaseDialog
          form={assetForm}
          projects={projects}
          accounts={accounts}
          assetAccounts={assetAccounts}
          assetAccountsLoading={assetAccountsLoading}
          categories={expenseCategories}
          vendors={vendors}
          lookupsLoading={lookupsLoading}
          lookupError={lookupError}
          onRetryLookups={retryLookups}
          assetLookupError={assetLookupError}
          onRetryAssetAccounts={retryAssetAccounts}
          onChange={setAssetForm}
          onClose={() => setAssetForm(null)}
          onSaved={refresh}
          onOpenAttachment={() => { if (assetForm.id && assetForm.attachment) void openFile("assetPurchase", assetForm.id, assetForm.attachment, false); }}
          onDownloadAttachment={() => { if (assetForm.id && assetForm.attachment) void openFile("assetPurchase", assetForm.id, assetForm.attachment, true); }}
        />
      )}
      <FinanceDeleteDialog
        pending={pendingDelete}
        deleting={deleting}
        onClose={() => { if (!deleting) setPendingDelete(null); }}
        onConfirm={() => void confirmDelete()}
      />
    </div>
  );
}
