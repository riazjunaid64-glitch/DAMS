import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { ReactNode } from "react";
import { Link, useNavigate } from "react-router-dom";
import { api } from "../api/api.ts";
import type { User } from "../App.tsx";
import Button from "../lib/Button.tsx";
import VirtualInfiniteTable from "../lib/VirtualInfiniteTable.tsx";
import type { Column } from "../lib/VirtualInfiniteTable.tsx";
import { usePaginatedRows } from "../lib/usePaginatedRows.ts";
import { fetchFinanceDashboard, financeRangeError, type FinanceChartData } from "../lib/financeChartData.ts";
import { buildPeriodRange, financePeriodLabel, pakistanToday } from "../lib/financePeriods.ts";
import { moneyRequest, useIdempotencyKeys } from "../lib/idempotency.ts";
import FinanceCharts from "../components/FinanceCharts.tsx";
import FinanceAttachmentField from "../components/FinanceAttachmentField.tsx";
import ExpenseWhtFields from "../components/ExpenseWhtFields.tsx";
import { listCategories, vendorOptions } from "../features/finance/whtApi.ts";
import { useFinancialYearStartMonth } from "../features/finance/useFinancialYearStartMonth.ts";
import { emptyWht, type ExpenseCategory, type VendorOption, type WhtFormValue } from "../features/finance/whtTypes.ts";
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
  type: number | string;
  accountHolderName: string;
  isActive: boolean;
}

const isStaffFloat = (account: FinanceAccountOption) =>
  account.type === 10 || account.type === "StaffFloat" || Number(account.type) === 10;

/** A fixed-asset purchase row: what was bought, where the value landed, and what paid for it. */
interface AssetPurchaseLine {
  id: number;
  date: string;
  projectId: number | null;
  projectName: string;
  assetAccountId: number;
  assetAccountName: string;
  financeAccountId: number;
  financeAccountName: string | null;
  accountHolderName: string | null;
  itemName: string;
  category: string;
  categoryId: number | null;
  description: string | null;
  vendor: string | null;
  vendorId: number | null;
  /** Gross — what the asset is carried at. Cash paid is `netPaid`. */
  amount: number;
  whtAmount: number;
  whtRate: number;
  netPaid: number;
  whtTaxSection: string | null;
  attachment: FinanceAttachmentInfo | null;
  concurrencyToken: string;
}

interface RevenueCategory {
  id: number;
  name: string;
  /** Stable identifier — the name is editable by the client, this is not. */
  code: string;
  isActive: boolean;
}

/** The one revenue head that overlaps a workflow DAMS already runs by itself. See the warning
 *  shown when it is selected. */
const CANCELLATION_REVENUE_CODE = "cancellation_forfeiture";

interface FinancialSummary {
  totalRevenue: number;
  /** Unit sales recognised at possession + amounts retained on cancellation. Not customer cash. */
  automaticRevenue: number;
  /** Customer money held but not yet earned, as at the END of the range. A balance, not income. */
  customerDepositsBalance: number;
  manualRevenue: number;
  /** Every cost of the period, including the fixed assets bought in it. */
  totalExpenses: number;
  /** The result: totalRevenue − totalExpenses, with the client's fixed-asset rule applied — buying
   *  an asset spends the money. The same figure the Profit & Loss statement reports for the period.
   *  NULL while an account filter is applied: a recognised sale belongs to no bank account, so an
   *  account-filtered subtraction would drop every possession from the revenue side and keep every
   *  cost. Net Movement is shown in its place. */
  netProfit: number | null;
  /** True when a single account (or "unassigned") is selected. Revenue and Expenses then mean
   *  "recorded against this account", not "the period's revenue and cost". */
  accountFilterApplied: boolean;
  whtWithheld: number;
  /** Fixed assets bought in the period, at cost. A breakdown of totalExpenses, not an addition to
   *  it — the cost is already inside that total and inside netProfit. */
  totalAssetPurchases: number;
  outstandingAmount: number;
  overdueAmount: number;
  accountOpeningBalance: number | null;
  accountCurrentBalance: number | null;
  accountNetMovement: number | null;
}

interface RevenueLine {
  date: string;
  projectId: number | null;
  projectName: string;
  revenueType: string;
  revenueCategoryId: number | null;
  amount: number;
  source: string;
  reference: string | null;
  description: string | null;
  manualRevenueId: number | null;
  financeAccountId: number | null;
  financeAccountName: string | null;
  accountHolderName: string | null;
  /** Base64 row version. Null on recognised sales and retained cancellations — events, not editable records. */
  concurrencyToken: string | null;
  attachment: FinanceAttachmentInfo | null;
}

interface ExpenseLine {
  id: number;
  date: string;
  projectId: number | null;
  projectName: string;
  category: string;
  categoryId: number | null;
  /** Gross — the business cost. Cash paid is `netPaid`. */
  amount: number;
  whtApplied: boolean;
  whtRate: number;
  whtAmount: number;
  netPaid: number;
  whtRateOverridden: boolean;
  whtOverrideReason: string | null;
  whtTaxSection: string | null;
  description: string | null;
  reference: string | null;
  vendorId: number | null;
  financeAccountId: number | null;
  financeAccountName: string | null;
  accountHolderName: string | null;
  /** Base64 row version — sent back on update and delete so a stale edit is refused, not applied. */
  concurrencyToken: string;
  attachment: FinanceAttachmentInfo | null;
}

/**
 * One booking's share of the Customer Deposits liability, as at the selected end date.
 * Read-only: possession and cancellation are what clear a deposit, never this screen.
 */
interface CustomerDepositLine {
  bookingId: number;
  bookingReference: string;
  customerName: string;
  projectId: number | null;
  projectName: string;
  unitNumber: string;
  bookingStatus: string;
  netSaleValue: number;
  customerCashReceived: number;
  depositBalance: number;
  recognitionDate: string | null;
  cancellationDate: string | null;
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
  /** "revenue" and "expense" together sum to netProfit. A fixed-asset purchase is an expense row. */
  kind: "revenue" | "expense";
  amount: number;
}

/** One component of the Total Expenses card. The signed amounts add up to that card exactly —
 *  the expense table alone cannot, because the card also carries commissions, rebates, customer
 *  credits, loan interest and fixed assets. */
interface CostLine {
  date: string;
  projectName: string;
  label: string;
  kind: "cost" | "reduction";
  amount: number;
  /** Present only on ordinary expense rows — the ones that can be opened and corrected. */
  expenseId: number | null;
}

type AnyRow = RevenueLine | ExpenseLine | AssetPurchaseLine | CustomerDepositLine | OutstandingLine | OverdueLine | NetProfitLine | CostLine;

type View = "revenue" | "expense" | "totalExpenses" | "assetPurchase" | "customerDeposits" | "netProfit" | "outstanding" | "overdue";

// API view query value for each card view.
const VIEW_PARAM: Record<View, string> = {
  revenue: "revenue",
  expense: "expense",
  totalExpenses: "totalExpenses",
  assetPurchase: "assetPurchase",
  customerDeposits: "customerDeposits",
  netProfit: "netProfit",
  outstanding: "outstanding",
  overdue: "overdue",
};

const VIEW_TITLES: Record<View, string> = {
  revenue: "Revenue",
  expense: "Expense Records",
  // Everything inside the Total Expenses card, not just the expense table — the card also carries
  // commissions, rebates, customer credits, loan interest and fixed assets.
  totalExpenses: "Total Expenses Breakdown",
  // Things the company keeps — and still a cost of the period the client's rule charges to profit.
  assetPurchase: "Fixed Asset Purchases",
  customerDeposits: "Customer Deposits",
  netProfit: "Net Profit Breakdown",
  outstanding: "Outstanding Balances",
  overdue: "Overdue Installments",
};

// Ancillary developer revenue (charges NOT auto-captured by booking/installment/possession
// payments). Grounded in standard Pakistani housing-society / developer charge heads. Only the
// first entry is used — as the placeholder name on a blank form until a managed category is
// chosen — so a head DAMS records automatically must not lead this list.
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
  "Rental Income",
  "Commission Income",
  "Other Income",
];

// Sentinel used by the Revenue Type <select> to switch into free-text entry.
const CUSTOM_TYPE = "__custom__";

// Expense heads now come from the managed rate table (Finance ▸ Settings), because each one
// carries the withholding rate applied to payments under it. Free text stays available for
// one-off heads — it just carries no tax.

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

// Every form on this page — revenue, expense and asset purchase — defaults its date from here,
// and the server judges all three against the Pakistani calendar. See pakistanToday.
const todayInput = pakistanToday;

type Period = "today" | "month" | "year" | "lastYear" | "all" | "custom";

// Order only — every chip takes its wording from financePeriodLabel, so the range a chip states
// and the range it applies come from the same place.
const PERIODS: Exclude<Period, "custom">[] = ["today", "month", "year", "lastYear", "all"];

interface RevenueFormState {
  id: number | null;
  /** Base64 row version of the row being edited; "" for a new one. */
  concurrencyToken: string;
  projectId: string;
  financeAccountId: string;
  amount: string;
  revenueType: string;
  revenueCategoryId: string;
  description: string;
  reference: string;
  date: string;
  attachment: FinanceAttachmentInfo | null;
  selectedAttachment: File | null;
  removeAttachment: boolean;
}

interface ExpenseFormState {
  id: number | null;
  /** Base64 row version of the row being edited; "" for a new one. */
  concurrencyToken: string;
  projectId: string;
  financeAccountId: string;
  amount: string;
  /** Managed category id, or "" when the head is free text. */
  categoryId: string;
  category: string;
  /** True only for a row recorded before the managed list existed, which may keep its free text. */
  legacyCategory: boolean;
  description: string;
  /** Managed vendor id, or "" when the payee is free text. */
  vendorId: string;
  vendor: string;
  date: string;
  wht: WhtFormValue;
  attachment: FinanceAttachmentInfo | null;
  selectedAttachment: File | null;
  removeAttachment: boolean;
}

/**
 * Deliberately close to ExpenseFormState — the brief is that recording a purchase should feel like
 * recording an expense. The two differences are the ones that matter: `assetAccountId` (where the
 * value lands, which an expense has no equivalent of) and `itemName` (what was actually bought, as
 * distinct from the tax head it is classified under).
 */
interface AssetPurchaseFormState {
  id: number | null;
  projectId: string;
  assetAccountId: string;
  /**
   * The destination's name as recorded on the row. Needed because a purchase saved before
   * construction spend became an expense points at a work-in-progress account, and that account is
   * no longer offered as a destination — without the name here the select would silently blank it
   * and the operator would be forced to mis-file a row they only meant to correct.
   */
  assetAccountName: string;
  financeAccountId: string;
  amount: string;
  itemName: string;
  categoryId: string;
  category: string;
  description: string;
  vendorId: string;
  vendor: string;
  date: string;
  wht: WhtFormValue;
  attachment: FinanceAttachmentInfo | null;
  selectedAttachment: File | null;
  removeAttachment: boolean;
  concurrencyToken: string;
}

const emptyAssetPurchaseForm = (): AssetPurchaseFormState => ({
  id: null,
  projectId: "",
  assetAccountId: "",
  assetAccountName: "",
  financeAccountId: "",
  amount: "",
  itemName: "",
  categoryId: "",
  category: "",
  description: "",
  vendorId: "",
  vendor: "",
  date: todayInput(),
  wht: emptyWht(),
  attachment: null,
  selectedAttachment: null,
  removeAttachment: false,
  concurrencyToken: "",
});

const emptyRevenueForm = (): RevenueFormState => ({
  id: null,
  concurrencyToken: "",
  projectId: "",
  financeAccountId: "",
  amount: "",
  revenueType: REVENUE_TYPES[0],
  revenueCategoryId: "",
  description: "",
  reference: "",
  date: todayInput(),
  attachment: null,
  selectedAttachment: null,
  removeAttachment: false,
});

const emptyExpenseForm = (): ExpenseFormState => ({
  id: null,
  concurrencyToken: "",
  projectId: "",
  financeAccountId: "",
  amount: "",
  categoryId: "",
  category: "",
  legacyCategory: false,
  description: "",
  vendorId: "",
  vendor: "",
  date: todayInput(),
  wht: emptyWht(),
  attachment: null,
  selectedAttachment: null,
  removeAttachment: false,
});

export default function FinanceDashboardPage({ user }: Props) {
  const navigate = useNavigate();
  const isAdmin = user?.role === "Admin";

  const [projects, setProjects] = useState<ProjectOption[]>([]);
  const [financeAccounts, setFinanceAccounts] = useState<FinanceAccountOption[]>([]);
  const [assetAccounts, setAssetAccounts] = useState<FinanceAccountOption[]>([]);
  const [revenueCategories, setRevenueCategories] = useState<RevenueCategory[]>([]);
  const [expenseCategories, setExpenseCategories] = useState<ExpenseCategory[]>([]);
  const [vendors, setVendors] = useState<VendorOption[]>([]);
  const [projectId, setProjectId] = useState<string>("");
  const [accountFilter, setAccountFilter] = useState<string>("");
  const [fromDate, setFromDate] = useState<string>("");
  const [toDate, setToDate] = useState<string>("");
  // null until the client's configured year start is read back. Nothing that depends on the
  // financial year may be stated or applied before then — see useFinancialYearStartMonth.
  const { startMonth: financialYearStartMonth, failed: financialYearFailed } =
    useFinancialYearStartMonth(isAdmin);

  const [summary, setSummary] = useState<FinancialSummary | null>(null);
  const [summaryLoading, setSummaryLoading] = useState(true);
  const [summaryError, setSummaryError] = useState<string | null>(null);
  const [view, setView] = useState<View>("revenue");

  // Cards and charts arrive together from one request, so they cannot end up describing different
  // periods — and there is no longer a partial-failure state where one loaded and the other did not.
  const [chartData, setChartData] = useState<FinanceChartData | null>(null);
  // Why a custom From/To cannot be used yet. Blocks the request rather than sending half a range.
  const rangeError = financeRangeError(fromDate, toDate);

  // Paged rows for the active view (infinite scroll). Switching view or filters resets it.
  const { rows, loading, loadingMore, hasMore, error, loadMore, reload } =
    usePaginatedRows<AnyRow>(VIEW_PARAM[view], projectId, fromDate, toDate, accountFilter, !rangeError);

  const [revenueForm, setRevenueForm] = useState<RevenueFormState | null>(null);
  const [expenseForm, setExpenseForm] = useState<ExpenseFormState | null>(null);
  const [assetForm, setAssetForm] = useState<AssetPurchaseFormState | null>(null);
  const [saving, setSaving] = useState(false);
  const savingRef = useRef(false);
  // savingRef stops a double click; this stops the retry after a lost response — the one the
  // operator cannot tell from a genuine failure. See lib/idempotency.
  const idempotency = useIdempotencyKeys();
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
      const [regular, staff] = await Promise.all([
        api("/api/finance/accounts/options?includeInactive=true&cashLikeOnly=true"),
        api("/api/finance/accounts/options?includeInactive=true&type=10"),
      ]);
      if (regular.ok || staff.ok) {
        const regularRows = regular.ok ? await regular.json() as FinanceAccountOption[] : [];
        const staffRows = staff.ok ? await staff.json() as FinanceAccountOption[] : [];
        const rows = [...regularRows, ...staffRows];
        setFinanceAccounts(rows.filter((row, index) => rows.findIndex((x) => x.id === row.id) === index));
      }
    } catch {
      /* The form will retain its validation message if accounts cannot be loaded. */
    }
  }, []);

  // The purchase destinations: fixed-asset accounts (type 7) only, asked for explicitly rather than
  // by "not cash-like", which would also offer liabilities and capital as somewhere to put a desk.
  //
  // Work-in-progress accounts (type 9) are deliberately NOT offered. Construction and site work is
  // a cost on the day it is paid, so it belongs on the Expense form under its construction head;
  // the server refuses a work-in-progress destination and this list simply agrees with it. The WIP
  // accounts still exist and still carry their inherited ERP balances.
  const loadAssetAccounts = useCallback(async () => {
    try {
      const response = await api("/api/finance/accounts/options?includeInactive=true&type=7");
      if (response.ok) setAssetAccounts(await response.json() as FinanceAccountOption[]);
    } catch {
      /* The purchase form shows its validation message if these cannot be loaded. */
    }
  }, []);

  // Inactive entries are included so editing an old expense still shows the head or payee it was
  // booked against, rather than silently blanking it.
  const loadWhtLookups = useCallback(async () => {
    try {
      const [categories, vendorRows] = await Promise.all([listCategories(true), vendorOptions(true)]);
      setExpenseCategories(categories);
      setVendors(vendorRows);
    } catch {
      /* The expense form falls back to free-text entry if these cannot be loaded. */
    }
  }, []);

  const loadRevenueCategories = useCallback(async () => {
    try {
      const response = await api("/api/finance/revenue-categories?includeInactive=true");
      if (response.ok) setRevenueCategories(await response.json());
    } catch {
      /* The revenue form shows an empty managed list and cannot save an unclassified entry. */
    }
  }, []);

  // The signal matters as much as the request. These seven cards are Revenue, Expenses, Net Profit,
  // Deposits and Outstanding: a failed load that leaves them showing Rs 0 reads as "the business did
  // nothing", and one that leaves the PREVIOUS filter's figures on screen reads as an answer to a
  // question nobody asked. Both are worse than saying nothing, so a failure clears the figures and
  // says so, and only the newest request may write.
  //
  // The ticket is what enforces that last part, not the abort signal: refreshAll calls this after a
  // save with nothing to abort it, so two loads can genuinely be in flight and the slower one must
  // lose regardless of which of them was cancelled.
  const summaryRequest = useRef(0);
  const loadSummary = useCallback(async (signal?: AbortSignal) => {
    const ticket = ++summaryRequest.current;
    if (rangeError) {
      setSummary(null);
      setChartData(null);
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
      setChartData(loaded.charts);
      setSummaryError(null);
    } catch {
      if (ticket !== summaryRequest.current) return;
      setSummary(null);
      setChartData(null);
      setSummaryError("The finance totals could not be loaded, so the figures below are unavailable.");
    } finally {
      if (ticket === summaryRequest.current) setSummaryLoading(false);
    }
  }, [projectId, fromDate, toDate, accountFilter, rangeError]);

  // After a create/edit/delete, refresh the totals, the charts and the visible rows.
  const refreshAll = useCallback(async () => {
    await loadSummary();
    reload();
  }, [loadSummary, reload]);

  useEffect(() => {
    if (!isAdmin) {
      navigate("/");
      return;
    }
    loadProjects();
    loadFinanceAccounts();
    void loadAssetAccounts();
    void loadWhtLookups();
    void loadRevenueCategories();
  }, [isAdmin, navigate, loadProjects, loadFinanceAccounts, loadAssetAccounts, loadWhtLookups, loadRevenueCategories]);

  useEffect(() => {
    if (!isAdmin) return;
    const controller = new AbortController();
    void loadSummary(controller.signal);
    return () => controller.abort();
  }, [isAdmin, loadSummary]);

  // Which quick-period chip (if any) matches the current from/to selection.
  const activePeriod = useMemo<Period>(() => {
    if (!fromDate && !toDate) return "all";
    for (const preset of PERIODS) {
      if (preset === "all") continue;
      const r = buildPeriodRange(preset, financialYearStartMonth);
      if (r.from === fromDate && r.to === toDate) return preset;
    }
    return "custom";
  }, [fromDate, toDate, financialYearStartMonth]);

  const applyPeriod = useCallback((period: Period) => {
    const r = buildPeriodRange(period, financialYearStartMonth);
    setFromDate(r.from);
    setToDate(r.to);
  }, [financialYearStartMonth]);

  // An account filter changes what the money cards MEAN, so it changes what they are called. A
  // recognised sale moves no cash and belongs to no bank, so "Total Revenue" under a bank filter is
  // that bank's revenue entries, not the period's revenue — and Net Profit cannot be stated at all.
  const accountSelected = !!accountFilter;

  // The views whose cards disappear under an account filter must not stay open behind them: a Net
  // Profit table for a bank is the same untruth as a Net Profit card for one, and deposits,
  // outstanding and overdue belong to bookings rather than accounts and would show empty.
  useEffect(() => {
    if (!accountSelected) return;
    setView((current) => (current === "netProfit" || current === "customerDeposits"
      || current === "outstanding" || current === "overdue") ? "revenue" : current);
  }, [accountSelected]);

  const summaryCards = useMemo(() => {
    const s = summary;
    const periodCards = [
      {
        label: accountSelected ? "Revenue on This Account" : "Total Revenue",
        value: s?.totalRevenue ?? 0, valueColor: "text-[var(--app-text)]", underline: "#34d399", view: "revenue" as View,
      },
      // Opens the BREAKDOWN, not the expense table. This card is ordinary expenses plus commissions,
      // rebates, customer credits, loan interest and fixed assets — a table holding only the first of
      // those could not account for the figure the operator just clicked.
      {
        label: accountSelected ? "Costs on This Account" : "Total Expenses",
        value: s?.totalExpenses ?? 0, valueColor: "text-[var(--app-text)]", underline: "#fb7185", view: "totalExpenses" as View,
      },
      // Sits next to Total Expenses because it is part of it: the same spending, broken out so the
      // reader can see how much of the period's cost went on things the company still owns.
      { label: "Fixed Assets Bought", value: s?.totalAssetPurchases ?? 0, valueColor: "text-[var(--app-text)]", underline: "#38bdf8", view: "assetPurchase" as View },
    ];
    // A balance as at the END of the selected period, not a period total, and deliberately next to
    // Revenue: this is money customers have handed over that the company has NOT yet earned. It
    // belongs to a booking rather than to an account, so an account filter suppresses it.
    const depositCard = {
      label: "Customer Deposits (at period end)",
      value: s?.customerDepositsBalance ?? 0, valueColor: "text-[var(--app-text)]", underline: "#a78bfa", view: "customerDeposits" as View,
    };
    // One profit figure, and the same one every other screen reports — but only when the question
    // is one an answer exists for. With an account selected the screen shows what that account can
    // actually say about itself: how much cash moved through it.
    const profitCard = accountSelected
      ? {
        label: "Account Net Movement",
        value: s?.accountNetMovement ?? 0,
        valueColor: (s?.accountNetMovement ?? 0) >= 0 ? "text-indigo-400" : "text-rose-400",
        underline: "#818cf8", view: null,
      }
      : {
        label: "Net Profit", value: s?.netProfit ?? 0,
        valueColor: (s?.netProfit ?? 0) >= 0 ? "text-[var(--gold-bright)]" : "text-rose-400",
        underline: "#cba95c", view: "netProfit" as View,
      };
    // Named "Current" because they are, and because the cards beside them are not: these two are
    // balances as they stand today and ignore the date filter entirely. Under a January range the
    // row otherwise read as "January revenue, January profit, January overdue", and only the last
    // of those was false.
    const snapshotCards = [
      { label: "Current Outstanding", value: s?.outstandingAmount ?? 0, valueColor: "text-[var(--app-text)]", underline: "#60a5fa", view: "outstanding" as View },
      { label: "Current Overdue", value: s?.overdueAmount ?? 0, valueColor: "text-[var(--app-text-muted)]", underline: "#6b7280", view: "overdue" as View },
    ];
    return accountSelected
      ? [...periodCards, profitCard]
      : [...periodCards, depositCard, profitCard, ...snapshotCards];
  }, [summary, accountSelected]);

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
    setAssetForm(null);
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
    if (!revenueForm.revenueCategoryId) {
      setFormError("Choose a revenue category. Manage the list under Finance settings.");
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
      body.append("revenueCategoryId", revenueForm.revenueCategoryId);
      body.append("description", revenueForm.description.trim());
      body.append("reference", revenueForm.reference.trim());
      if (revenueForm.date) body.append("date", revenueForm.date);
      if (revenueForm.selectedAttachment) body.append("attachment", revenueForm.selectedAttachment);
      if (revenueForm.removeAttachment) body.append("removeAttachment", "true");
      // The version the form was opened with. Without it a second admin's save would overwrite
      // the first's silently; with it the server refuses and the operator is told to reload.
      if (revenueForm.id) body.append("concurrencyToken", revenueForm.concurrencyToken);
      // Creates carry an idempotency key so a retry after a lost response is recognised instead of
      // recording the income twice; the key is tied to what is being saved, so correcting the amount
      // and saving again is treated as the new entry it is.
      const signature = `revenue:${revenueForm.financeAccountId}:${amount}:${revenueForm.date}:${revenueForm.revenueCategoryId}`;
      const res = revenueForm.id
        ? await api(`/api/Finance/revenue/${revenueForm.id}/form`, { method: "PUT", body })
        : await api("/api/Finance/revenue/form",
            moneyRequest(idempotency.key(signature, "revenue"), { method: "POST", body }));
      if (!res.ok) {
        setFormError(await financeApiError(res, "Failed to save revenue entry."));
        return;
      }
      if (!revenueForm.id) idempotency.release(signature);
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
    if (!expenseForm.categoryId && !expenseForm.legacyCategory) {
      setFormError("Choose an expense category. Add a new head under Finance ▸ Settings if the one you need is missing.");
      return;
    }
    if (!expenseForm.categoryId && !expenseForm.category.trim()) {
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
      // A category id wins server-side; the text is still sent so free-text heads keep working.
      if (expenseForm.categoryId) body.append("categoryId", expenseForm.categoryId);
      body.append("category", expenseForm.category.trim());
      body.append("description", expenseForm.description.trim());
      if (expenseForm.vendorId) body.append("vendorId", expenseForm.vendorId);
      body.append("vendor", expenseForm.vendor.trim());
      if (expenseForm.date) body.append("date", expenseForm.date);
      // Only sent when the head is a managed one — the server recalculates and rejects a figure
      // that does not belong, so a stale value cannot slip through.
      if (expenseForm.categoryId) {
        if (expenseForm.wht.rate !== "") body.append("whtRate", expenseForm.wht.rate);
        if (expenseForm.wht.amount !== "") body.append("whtAmount", expenseForm.wht.amount);
        if (expenseForm.wht.overrideReason.trim())
          body.append("whtOverrideReason", expenseForm.wht.overrideReason.trim());
      }
      if (expenseForm.selectedAttachment) body.append("attachment", expenseForm.selectedAttachment);
      if (expenseForm.removeAttachment) body.append("removeAttachment", "true");
      if (expenseForm.id) body.append("concurrencyToken", expenseForm.concurrencyToken);
      const signature = `expense:${expenseForm.financeAccountId}:${amount}:${expenseForm.date}:${expenseForm.categoryId}`;
      const res = expenseForm.id
        ? await api(`/api/Finance/expenses/${expenseForm.id}/form`, { method: "PUT", body })
        : await api("/api/Finance/expenses/form",
            moneyRequest(idempotency.key(signature, "expense"), { method: "POST", body }));
      if (!res.ok) {
        setFormError(await financeApiError(res, "Failed to save expense."));
        return;
      }
      if (!expenseForm.id) idempotency.release(signature);
      resetForms();
      await refreshAll();
    } catch {
      setFormError("The expense could not be saved. Check your connection and try again.");
    } finally {
      finishSaving();
    }
  };

  const submitAssetPurchase = async () => {
    if (!assetForm) return;
    setFormError(null);
    const amount = Number(assetForm.amount);
    if (!Number.isFinite(amount) || amount <= 0) {
      setFormError("Enter a valid amount greater than zero.");
      return;
    }
    if (!assetForm.itemName.trim()) {
      setFormError("Describe what was bought, e.g. “3 office desks”.");
      return;
    }
    if (!assetForm.assetAccountId) {
      setFormError("Select the asset account this purchase belongs to.");
      return;
    }
    if (!assetForm.financeAccountId) {
      setFormError("Select the account this purchase was paid from.");
      return;
    }
    if (!assetForm.categoryId) {
      setFormError("Choose a category. Add a new head under Finance ▸ Settings if the one you need is missing.");
      return;
    }
    if (!startSaving()) return;
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
      // The server recalculates and rejects a figure that does not belong, so a stale value
      // cannot slip through here any more than it can on the expense form.
      if (assetForm.wht.rate !== "") body.append("whtRate", assetForm.wht.rate);
      if (assetForm.wht.amount !== "") body.append("whtAmount", assetForm.wht.amount);
      if (assetForm.wht.overrideReason.trim())
        body.append("whtOverrideReason", assetForm.wht.overrideReason.trim());
      if (assetForm.selectedAttachment) body.append("attachment", assetForm.selectedAttachment);
      if (assetForm.removeAttachment) body.append("removeAttachment", "true");
      if (assetForm.id) body.append("concurrencyToken", assetForm.concurrencyToken);
      const signature = `asset:${assetForm.financeAccountId}:${assetForm.assetAccountId}:${amount}:${assetForm.date}`;
      const res = assetForm.id
        ? await api(`/api/Finance/asset-purchases/${assetForm.id}/form`, { method: "PUT", body })
        : await api("/api/Finance/asset-purchases/form",
            moneyRequest(idempotency.key(signature, "asset-purchase"), { method: "POST", body }));
      if (!res.ok) {
        setFormError(await financeApiError(res, "Failed to save the asset purchase."));
        return;
      }
      if (!assetForm.id) idempotency.release(signature);
      resetForms();
      await refreshAll();
    } catch {
      setFormError("The purchase could not be saved. Check your connection and try again.");
    } finally {
      finishSaving();
    }
  };

  // Deletes carry the row version too, and surface the server's message rather than a generic
  // failure — "changed by someone else" is the one delete error an operator can actually act on.
  const deleteRevenue = async (row: RevenueLine) => {
    if (row.manualRevenueId == null) return;
    if (!window.confirm("Delete this manual revenue entry?")) return;
    const res = await api(
      `/api/Finance/revenue/${row.manualRevenueId}?concurrencyToken=${encodeURIComponent(row.concurrencyToken ?? "")}`,
      { method: "DELETE" },
    );
    if (res.ok) await refreshAll();
    else alert(await financeApiError(res, "Failed to delete revenue entry."));
  };

  const deleteExpense = async (row: ExpenseLine) => {
    if (!window.confirm("Delete this expense?")) return;
    const res = await api(
      `/api/Finance/expenses/${row.id}?concurrencyToken=${encodeURIComponent(row.concurrencyToken)}`,
      { method: "DELETE" },
    );
    if (res.ok) await refreshAll();
    else alert(await financeApiError(res, "Failed to delete expense."));
  };

  const deleteAssetPurchase = async (row: AssetPurchaseLine) => {
    if (!window.confirm("Delete this asset purchase? The bank balance and the asset account both move back.")) return;
    const res = await api(
      `/api/Finance/asset-purchases/${row.id}?concurrencyToken=${encodeURIComponent(row.concurrencyToken)}`,
      { method: "DELETE" },
    );
    if (res.ok) await refreshAll();
    else alert(await financeApiError(res, "Failed to delete the asset purchase."));
  };

  const editAssetPurchase = (row: AssetPurchaseLine) => {
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
      // What was actually withheld, not what today's rate table would produce — reopening a
      // purchase must not restate a figure that has already been filed.
      wht: {
        rate: String(Number(row.whtRate.toFixed(4))),
        amount: String(row.whtAmount),
        overrideReason: "",
      },
      attachment: row.attachment,
      selectedAttachment: null,
      removeAttachment: false,
      concurrencyToken: row.concurrencyToken,
    });
  };

  const editRevenue = (row: RevenueLine) => {
    if (row.manualRevenueId == null) return;
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
      // Seeded from what was actually withheld, so reopening an expense shows its own figures
      // rather than what today's rate table would produce.
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
                  <button type="button" onClick={() => deleteRevenue(row)} className="fin-act fin-act--del" aria-label="Delete" title="Delete"><IconTrash /></button>
                </span>
              ) : null;
            } },
          ],
        };
      case "expense":
        return {
          minWidth: 1320,
          emptyText: "No expenses for the selected filters.",
          columns: [
            { key: "date", header: "Date", width: "130px", render: (r) => <span className="text-[var(--text-secondary)]">{formatDate((r as ExpenseLine).date)}</span> },
            { key: "project", header: "Project", width: "minmax(120px,1fr)", render: (r) => <span className="text-[var(--text-primary)]">{(r as ExpenseLine).projectName}</span> },
            { key: "account", header: "Paid From", width: "minmax(150px,1fr)", render: (r) => { const x=r as ExpenseLine; return <span>{x.financeAccountName ?? "Unassigned"}<small className="block text-[var(--text-muted)]">{x.accountHolderName}</small></span>; } },
            { key: "category", header: "Category", width: "minmax(140px,1fr)", render: (r) => { const x = r as ExpenseLine; return <span className="text-[var(--text-primary)]">{x.category}{x.whtTaxSection && <small className="block text-[var(--text-muted)]">s.{x.whtTaxSection}</small>}</span>; } },
            { key: "amount", header: "Gross", width: "120px", align: "right", render: (r) => money((r as ExpenseLine).amount, "text-rose-400") },
            // Gross, tax and net are shown side by side: the expense total and the bank movement
            // are different numbers now, and hiding either invites a reconciliation dispute.
            { key: "wht", header: "WHT", width: "120px", align: "right", render: (r) => {
              const x = r as ExpenseLine;
              if (!x.whtApplied) return <span className="text-xs text-[var(--text-muted)]">—</span>;
              return (
                <span className="whitespace-nowrap">
                  {money(x.whtAmount, "text-amber-400")}
                  <small className="block text-[var(--text-muted)]">
                    {Number(x.whtRate.toFixed(4))}%{x.whtRateOverridden ? " ⚑" : ""}
                  </small>
                </span>
              );
            } },
            { key: "net", header: "Net Paid", width: "120px", align: "right", render: (r) => money((r as ExpenseLine).netPaid) },
            { key: "description", header: "Description", width: "minmax(160px,1fr)", render: (r) => <span className="text-[var(--text-secondary)]">{(r as ExpenseLine).description || "—"}</span> },
            { key: "reference", header: "Vendor", width: "minmax(140px,1fr)", render: (r) => <span className="text-[var(--text-secondary)]">{(r as ExpenseLine).reference || "—"}</span> },
            { key: "attachment", header: "Attachment", width: "130px", render: (r) => { const row = r as ExpenseLine; return attachmentCell("expense", row.id, row.attachment); } },
            { key: "actions", header: "", width: "120px", align: "right", render: (r) => {
              const row = r as ExpenseLine;
              return (
                <span className="inline-flex justify-end gap-2">
                  <button type="button" onClick={() => editExpense(row)} className="fin-act" aria-label="Edit" title="Edit"><IconPencil /></button>
                  <button type="button" onClick={() => deleteExpense(row)} className="fin-act fin-act--del" aria-label="Delete" title="Delete"><IconTrash /></button>
                </span>
              );
            } },
          ],
        };
      case "assetPurchase":
        return {
          minWidth: 1360,
          emptyText: "No fixed asset purchases for the selected filters.",
          columns: [
            { key: "date", header: "Date", width: "130px", render: (r) => <span className="text-[var(--text-secondary)]">{formatDate((r as AssetPurchaseLine).date)}</span> },
            { key: "item", header: "Item", width: "minmax(160px,1fr)", render: (r) => { const x = r as AssetPurchaseLine; return <span className="text-[var(--text-primary)]">{x.itemName}{x.description && <small className="block text-[var(--text-muted)]">{x.description}</small>}</span>; } },
            // The destination account is the point of the whole record, so it is a first-class
            // column rather than something to be inferred from the category.
            { key: "assetAccount", header: "Asset Account", width: "minmax(160px,1fr)", render: (r) => <span className="text-sky-300">{(r as AssetPurchaseLine).assetAccountName}</span> },
            { key: "account", header: "Paid From", width: "minmax(150px,1fr)", render: (r) => { const x = r as AssetPurchaseLine; return <span>{x.financeAccountName ?? "—"}<small className="block text-[var(--text-muted)]">{x.accountHolderName}</small></span>; } },
            { key: "category", header: "Category", width: "minmax(140px,1fr)", render: (r) => { const x = r as AssetPurchaseLine; return <span className="text-[var(--text-primary)]">{x.category}{x.whtTaxSection && <small className="block text-[var(--text-muted)]">s.{x.whtTaxSection}</small>}</span>; } },
            // Cost, not "gross expense": this figure is what the asset is carried at.
            { key: "amount", header: "Cost", width: "120px", align: "right", render: (r) => money((r as AssetPurchaseLine).amount, "text-sky-300") },
            { key: "wht", header: "WHT", width: "120px", align: "right", render: (r) => {
              const x = r as AssetPurchaseLine;
              // Positive-test rather than `<= 0`: a missing amount must fall to the dash, and
              // `undefined <= 0` is false, which used to let it through to the rate below.
              if (!(x.whtAmount > 0)) return <span className="text-xs text-[var(--text-muted)]">—</span>;
              return (
                <span className="whitespace-nowrap">
                  {money(x.whtAmount, "text-amber-400")}
                  <small className="block text-[var(--text-muted)]">{Number((x.whtRate ?? 0).toFixed(4))}%</small>
                </span>
              );
            } },
            { key: "net", header: "Net Paid", width: "120px", align: "right", render: (r) => money((r as AssetPurchaseLine).netPaid) },
            { key: "project", header: "Project", width: "minmax(120px,1fr)", render: (r) => <span className="text-[var(--text-primary)]">{(r as AssetPurchaseLine).projectName}</span> },
            { key: "vendor", header: "Supplier", width: "minmax(140px,1fr)", render: (r) => <span className="text-[var(--text-secondary)]">{(r as AssetPurchaseLine).vendor || "—"}</span> },
            { key: "attachment", header: "Attachment", width: "130px", render: (r) => { const row = r as AssetPurchaseLine; return attachmentCell("assetPurchase", row.id, row.attachment); } },
            { key: "actions", header: "", width: "120px", align: "right", render: (r) => {
              const row = r as AssetPurchaseLine;
              return (
                <span className="inline-flex justify-end gap-2">
                  <button type="button" onClick={() => editAssetPurchase(row)} className="fin-act" aria-label="Edit" title="Edit"><IconPencil /></button>
                  <button type="button" onClick={() => deleteAssetPurchase(row)} className="fin-act fin-act--del" aria-label="Delete" title="Delete"><IconTrash /></button>
                </span>
              );
            } },
          ],
        };
      // Read-only by design: a deposit is cleared by giving possession or by cancelling the
      // booking, never by editing this list. There is nothing here to edit or delete.
      case "customerDeposits":
        return {
          minWidth: 1180,
          emptyText: "No customer deposits held for the selected filters.",
          columns: [
            { key: "booking", header: "Booking", width: "140px", render: (r) => <span className="text-[var(--text-primary)] whitespace-nowrap">{(r as CustomerDepositLine).bookingReference}</span> },
            { key: "customer", header: "Customer", width: "minmax(140px,1fr)", render: (r) => <span className="text-[var(--text-primary)]">{(r as CustomerDepositLine).customerName}</span> },
            { key: "project", header: "Project", width: "minmax(120px,1fr)", render: (r) => <span className="text-[var(--text-secondary)]">{(r as CustomerDepositLine).projectName}</span> },
            { key: "unit", header: "Unit", width: "110px", render: (r) => <span className="text-[var(--text-secondary)]">{(r as CustomerDepositLine).unitNumber}</span> },
            { key: "status", header: "Current Status", width: "150px", render: (r) => {
              // Labelled "Current" on purpose: the money on this row is historical (as at the
              // selected date) but the status is today's, so a booking can legitimately show a
              // deposit balance next to a status that has since moved past it.
              const row = r as CustomerDepositLine;
              return (
                <span className="inline-flex rounded-full border border-violet-500/20 bg-violet-500/10 px-2.5 py-1 text-[10px] font-semibold text-violet-300">
                  {row.bookingStatus || "—"}
                </span>
              );
            } },
            { key: "netSale", header: "Net Sale Value", width: "140px", align: "right", render: (r) => money((r as CustomerDepositLine).netSaleValue) },
            { key: "cash", header: "Cash Received", width: "140px", align: "right", render: (r) => money((r as CustomerDepositLine).customerCashReceived, "text-emerald-400") },
            { key: "balance", header: "Deposit Held", width: "140px", align: "right", render: (r) => money((r as CustomerDepositLine).depositBalance, "text-violet-300") },
            { key: "recognised", header: "Cleared By", width: "170px", render: (r) => {
              // Two different events can clear a deposit and they must not be conflated: possession
              // turns it into revenue, cancellation turns it into a refund payable. Showing a
              // cancellation date under a "Possession" heading would assert a handover that never
              // happened, so the event is named alongside its date.
              const row = r as CustomerDepositLine;
              const cleared = row.recognitionDate
                ? { label: "Possession", date: row.recognitionDate, tone: "text-emerald-400" }
                : row.cancellationDate
                  ? { label: "Cancelled", date: row.cancellationDate, tone: "text-rose-400" }
                  : null;
              if (!cleared) return <span className="text-[var(--text-secondary)]">—</span>;
              return (
                <span className="text-[var(--text-secondary)]">
                  <span className={`font-semibold ${cleared.tone}`}>{cleared.label}</span>
                  {" · "}{formatDate(cleared.date)}
                </span>
              );
            } },
          ],
        };
      case "totalExpenses":
        return {
          minWidth: 800,
          emptyText: "No costs for the selected filters.",
          columns: [
            { key: "date", header: "Date", width: "130px", render: (r) => <span className="text-[var(--text-secondary)]">{formatDate((r as CostLine).date)}</span> },
            { key: "project", header: "Project", width: "minmax(120px,1fr)", render: (r) => <span className="text-[var(--text-primary)]">{(r as CostLine).projectName}</span> },
            { key: "item", header: "Cost", width: "minmax(180px,1fr)", render: (r) => <span className="text-[var(--text-primary)]">{(r as CostLine).label}</span> },
            // Two kinds. Cost and Reduction, and the signed amounts add up to the Total Expenses card.
            { key: "kind", header: "Type", width: "150px", render: (r) => {
              const row = r as CostLine;
              const style = row.kind === "cost"
                ? "text-rose-400 bg-rose-500/10 border-rose-500/20"
                : "text-emerald-400 bg-emerald-500/10 border-emerald-500/20";
              return <span className={`inline-flex rounded-full border px-2.5 py-1 text-[10px] font-semibold ${style}`}>{row.kind === "cost" ? "Cost" : "Reduction"}</span>;
            } },
            { key: "amount", header: "Amount", width: "140px", align: "right", render: (r) => {
              const row = r as CostLine;
              return <span className={`font-semibold whitespace-nowrap ${row.amount >= 0 ? "text-rose-400" : "text-emerald-400"}`}>{row.amount >= 0 ? "+" : "−"}{formatMoney(Math.abs(row.amount))}</span>;
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
            // Two kinds. Revenue and Expense, and the signed amounts add up to the Net Profit card.
            { key: "kind", header: "Type", width: "150px", render: (r) => {
              const row = r as NetProfitLine;
              const style = row.kind === "revenue"
                ? "text-emerald-400 bg-emerald-500/10 border-emerald-500/20"
                : "text-rose-400 bg-rose-500/10 border-rose-500/20";
              return <span className={`inline-flex rounded-full border px-2.5 py-1 text-[10px] font-semibold ${style}`}>{row.kind === "revenue" ? "Revenue" : "Expense"}</span>;
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
            { key: "received", header: "Received / credited", width: "155px", align: "right", render: (r) => money((r as OutstandingLine).receivedAmount, "text-emerald-400") },
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
      case "totalExpenses":
        return `cost-${index}`;
      case "assetPurchase":
        return `ast-${(row as AssetPurchaseLine).id}`;
      case "customerDeposits":
        return `dep-${(row as CustomerDepositLine).bookingId}`;
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
              {PERIODS.map((preset) => {
                // A financial-year chip is offered only once the client's year start is known.
                // Until then it could neither name nor filter the right months.
                const needsFinancialYear = preset === "year" || preset === "lastYear";
                const unavailable = needsFinancialYear && financialYearStartMonth === null;
                return (
                  <button
                    key={preset}
                    type="button"
                    disabled={unavailable}
                    title={unavailable
                      ? (financialYearFailed
                        ? "The financial year setting could not be loaded, so this range cannot be applied."
                        : "Loading the configured financial year…")
                      : undefined}
                    onClick={() => applyPeriod(preset)}
                    className={`fin-pill ${activePeriod === preset ? "fin-pill--active" : ""} ${unavailable ? "opacity-50" : ""}`}
                  >
                    {financePeriodLabel(preset, financialYearStartMonth)}
                  </button>
                );
              })}
            </div>
            {financialYearFailed && (
              <p role="alert" className="w-full text-xs text-amber-300">
                The financial year setting could not be loaded, so the year filters are unavailable.
                Everything else still works, and a custom From/To range is unaffected.
              </p>
            )}

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

            {/* A half-open or backwards range is refused rather than interpreted. It used to be
                accepted by the totals and re-read as "all time" (or as the financial year) by the
                charts, so the screen answered two questions at once without saying so. */}
            {rangeError && (
              <p role="alert" className="mt-3 text-xs text-amber-300">
                {rangeError} Nothing below is filtered until both dates are set.
              </p>
            )}
          </div>

          {/* Summary cards */}
          <div className="mt-7 grid grid-cols-2 gap-4 md:grid-cols-3 xl:grid-cols-6">
            {summaryCards.map((card) => {
              const active = card.view != null && view === card.view;
              // Account Net Movement has no drill-down of its own — it is cash movement, not a list
              // of records — so that card is not a button pretending to open something.
              return (
                <button
                  key={card.label}
                  type="button"
                  disabled={card.view == null}
                  onClick={() => { if (card.view != null) focusView(card.view); }}
                  style={{ borderBottomColor: card.underline }}
                  className={`group relative overflow-hidden rounded-2xl border border-[var(--border)] border-b-[3px] bg-[var(--bg-card)] px-5 pb-5 pt-4 text-left transition-all ${
                    card.view == null ? "cursor-default" : "cursor-pointer hover:-translate-y-0.5 hover:border-[var(--border-hover)]"
                  } ${active ? "ring-2 ring-[var(--accent)]" : ""}`}
                >
                  <p className="text-[11px] font-semibold uppercase tracking-wider text-[var(--text-muted)]">
                    {card.label}
                  </p>
                  <p className={`mt-3 text-2xl font-bold leading-tight sm:text-[1.7rem] ${
                    summaryError ? "text-[var(--text-muted)]" : card.valueColor}`}>
                    {summaryLoading ? "…" : summaryError ? "—" : formatMoney(card.value)}
                  </p>
                </button>
              );
            })}
          </div>

          {summaryError && !summaryLoading && (
            <div className="mt-4 rounded-2xl border border-rose-500/30 bg-rose-500/[0.08] px-5 py-4 text-sm text-rose-200">
              {summaryError}{" "}
              <button className="underline" onClick={() => void loadSummary()}>Retry</button>
            </div>
          )}

          {summary && !accountSelected && (
            <p className="mt-3 text-xs text-[var(--text-muted)]">
              Revenue breakdown: {formatMoney(summary.automaticRevenue)} recognised sales &amp; retained cancellations
              {" + "}
              {formatMoney(summary.manualRevenue)} manual.
              {" "}
              Customer payments before possession are deposits, not revenue.
            </p>
          )}

          {/* The whole reason the cards above are renamed. Selecting a bank does not narrow the
              business to that bank: a sale is recognised at possession and moves no cash, so it
              belongs to no account and simply is not here. Saying so is the difference between a
              filtered list and a wrong total. */}
          {summary && accountSelected && (
            <p className="mt-3 text-xs text-amber-300/90">
              These are the entries recorded against the selected account — not the period's revenue,
              cost or profit. Sales recognised at possession move no cash, so they belong to no
              account and are not counted here, and Net Profit is therefore not reported while an
              account is selected. Clear the account filter to see the period's profitability.
            </p>
          )}

          {/* Said plainly rather than left to be inferred from the card names. Three different
              questions sit in one row of cards, and a reader who assumes they all answer the same
              one misreads four of the seven. */}
          {summary && !accountSelected && (
            <p className="mt-2 text-xs text-[var(--text-muted)]">
              Revenue, Expenses, Fixed Assets and Net Profit are totals for the selected period.
              Customer Deposits is a balance as it stood at the end of that period. Current
              Outstanding and Current Overdue are balances as they stand today — the From/To filter
              does not apply to them.
            </p>
          )}

          {/* Spelled out rather than left to be inferred: the same amount appears in two cards, and a
              reader who assumes those are separate totals will double-count the period's spending. */}
          {summary && !accountSelected && summary.totalAssetPurchases > 0 && (
            <p className="mt-2 text-xs text-sky-300/90">
              Total Expenses and Net Profit already include {formatMoney(summary.totalAssetPurchases)} of
              fixed assets bought in this period, at cost — buying an asset spends the money. The
              Profit &amp; Loss statement deducts the same amount, so the two agree. The assets
              themselves stay on the Balance Sheet at cost.{" "}
              <Link to="/finance/reports" className="underline hover:text-sky-200">See Reports</Link>
            </p>
          )}

          {summary && summary.whtWithheld > 0 && (
            <p className="mt-2 text-xs text-amber-300/90">
              Expenses are shown gross. {formatMoney(summary.whtWithheld)} of that was withheld as tax
              and has not left the bank — it is owed to FBR.{" "}
              <Link to="/finance/settings" className="underline hover:text-amber-200">View WHT payable</Link>
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
              {/* Cash movement, not profit: expenses count at what actually left the account and
                  FBR deposits count too. The two diverge as soon as any tax is withheld. */}
              <div>
                <p className="text-[10px] font-semibold uppercase tracking-wider text-[var(--text-muted)]">Net Movement{(fromDate || toDate) ? " (period)" : ""}</p>
                <p className={`text-sm font-semibold ${(summary.accountNetMovement ?? 0) >= 0 ? "text-indigo-400" : "text-rose-400"}`}>{formatMoney(summary.accountNetMovement ?? 0)}</p>
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
            {/* The Total Expenses card is more than the expense table, so its drill-down is more
                than the expense table. The editable records are one click away rather than
                unreachable, and the relationship between the two lists is stated. */}
            {view === "totalExpenses" && (
              <p className="mt-1 text-xs text-[var(--text-muted)]">
                Every cost inside the card: expenses, commissions, rebates, customer credits, loan
                interest and fixed assets, each net of its reversals. Added up, these rows are the
                card.{" "}
                <button type="button" className="underline hover:text-[var(--text-primary)]" onClick={() => setView("expense")}>
                  Open the expense records to edit them
                </button>
              </p>
            )}
            {view === "expense" && (
              <p className="mt-1 text-xs text-[var(--text-muted)]">
                Expense records only — this is part of Total Expenses, not all of it.{" "}
                <button type="button" className="underline hover:text-[var(--text-primary)]" onClick={() => setView("totalExpenses")}>
                  Show everything in that card
                </button>
              </p>
            )}
            {(view === "outstanding" || view === "overdue") && (
              <p className="mt-1 text-xs text-amber-300/90">
                A current balance, as it stands today. The From/To filter does not apply to it.
              </p>
            )}
          </div>
          <div className="flex flex-wrap gap-2.5">
            <Link to="/finance/reports"><Button variant="outline">Financial Reports</Button></Link>
            <Link to="/finance/partners"><Button variant="outline">Capital Partners</Button></Link>
            <Link to="/finance/loans"><Button variant="outline">Loans</Button></Link>
            <Link to="/finance/staff-cash"><Button variant="outline">Cash with Staff</Button></Link>
            <Link to="/finance/accounts"><Button variant="outline">⚙ Manage Accounts</Button></Link>
            <Link to="/finance/settings"><Button variant="outline">Tax &amp; Categories</Button></Link>
            <Link to="/finance/commissions-rebates"><Button variant="outline">Commissions &amp; Rebates</Button></Link>
            <Button variant="outline" onClick={() => { setExpenseForm(null); setAssetForm(null); setFormError(null); setRevenueForm(emptyRevenueForm()); }}>
              + Add Revenue
            </Button>
            <Button variant="outline" onClick={() => { setRevenueForm(null); setExpenseForm(null); setFormError(null); setAssetForm(emptyAssetPurchaseForm()); }}>
              ◆ Add Fixed Asset
            </Button>
            <Button onClick={() => { setRevenueForm(null); setAssetForm(null); setFormError(null); setExpenseForm(emptyExpenseForm()); }}>
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
        {/* The bars cover exactly the period the cards do, in windows the server chose so that every
            day of the range falls inside one of them. They are aggregated when the range is long,
            never sampled — a chart that drew every third quarter under a card covering all of them
            was showing roughly a third of the money and saying nothing about it. */}
        {chartData && chartData.series.length > 0 && (
          <p className="mt-6 text-xs text-[var(--text-muted)]">
            {chartData.series.length} period{chartData.series.length === 1 ? "" : "s"}, covering
            {" "}{formatDate(chartData.series[0].from)} – {formatDate(chartData.series[chartData.series.length - 1].to)}
            {" "}with no gaps.
            {/* The bars are the cards over narrower windows, so an account filter narrows them the
                same way — including dropping the recognised sales that belong to no account. The
                cards say so above; the chart has to say so too, or it reads as the whole business. */}
            {accountSelected && " Scoped to the selected account, exactly as the figures above are."}
          </p>
        )}
        <FinanceCharts data={chartData} loading={summaryLoading} formatMoney={formatMoney} />
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
                {financeAccounts.filter((a) => !isStaffFloat(a) && (a.isActive || String(a.id) === revenueForm.financeAccountId)).map((a) => <option key={a.id} value={a.id}>{a.name} — {a.accountHolderName}{a.isActive ? "" : " (Inactive)"}</option>)}
              </FormSelect>
              <FormSelect
                label="Revenue Category"
                value={revenueForm.revenueCategoryId}
                onChange={(v) => {
                  const category = revenueCategories.find((item) => String(item.id) === v);
                  setRevenueForm({ ...revenueForm, revenueCategoryId: v, revenueType: category?.name ?? revenueForm.revenueType });
                }}
              >
                <option value="">Select a category</option>
                {revenueCategories.filter((item) => item.isActive || String(item.id) === revenueForm.revenueCategoryId)
                  .map((item) => <option key={item.id} value={item.id}>{item.name}{item.isActive ? "" : " (Retired)"}</option>)}
              </FormSelect>
              {/* Cancelling a DAMS booking already recognises the retained amount as income, on the
                  cancellation date, out of the customer's deposit. Typing it in here as well counts
                  the same forfeiture twice, and because manual revenue is an independent record
                  nothing downstream can detect it. The head stays for forfeitures that predate
                  go-live or never were a DAMS booking — said here, where the choice is made. */}
              {revenueCategories.some((item) =>
                String(item.id) === revenueForm.revenueCategoryId && item.code === CANCELLATION_REVENUE_CODE) && (
                <p role="alert" className="text-xs text-amber-300 md:col-span-2">
                  Only for a forfeiture from outside DAMS — before go-live, or on something that was
                  never a booking here. Cancelling a booking in DAMS records the retained amount as
                  income by itself, so entering it again here would count it twice.
                </p>
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

      {/* Fixed-asset purchase modal. Field order mirrors the expense form so the two feel like the
          same task, with the asset account added as the one thing an expense has no equivalent of. */}
      {assetForm && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
          <div className="absolute inset-0 bg-black/60 backdrop-blur-sm animate-fade-in" onClick={() => { if (!saving) resetForms(); }} />
          <div className="relative z-10 max-h-[calc(100dvh-2rem)] w-[460px] max-w-[92vw] overflow-y-auto animate-scale-in rounded-2xl border border-[var(--border)] bg-[var(--modal-bg)] shadow-2xl">
            <div className="border-b border-[var(--border)] px-6 py-4">
              <h3 className="text-lg font-semibold text-[var(--text-heading)]">
                {assetForm.id ? "Edit Fixed Asset Purchase" : "Record Fixed Asset Purchase"}
              </h3>
              {/* Both halves of the truth, on the form that creates it: the money is gone from
                  profit and the company still owns the thing it bought. It used to say the formal
                  P&L did not deduct the purchase; it does, and leaving that sentence up would have
                  had an operator record a purchase expecting the statement not to move. */}
              <p className="mt-1 text-xs text-[var(--text-muted)]">
                Net Profit falls by the full purchase price — on this dashboard and on the Profit
                &amp; Loss statement alike — and the asset still appears on the Balance Sheet at
                cost. For construction, site work or materials being consumed, use Expenses instead.
              </p>
            </div>
            <div className="space-y-4 p-6">
              {formError && <p className="rounded-lg bg-rose-500/10 px-3 py-2 text-sm text-rose-300">{formError}</p>}
              <FormInput label="What was bought" value={assetForm.itemName} onChange={(v) => setAssetForm({ ...assetForm, itemName: v })} />
              {/* Fixed-asset accounts only. The row's own destination is re-added below when it is
                  not in the list — an old work-in-progress purchase must stay correctable in place
                  rather than being pushed onto the wrong account by a blank select. */}
              <FormSelect label="Asset Account" value={assetForm.assetAccountId} onChange={(v) => setAssetForm({ ...assetForm, assetAccountId: v })}>
                <option value="">Select a fixed asset account</option>
                {assetAccounts.filter((a) => a.isActive || String(a.id) === assetForm.assetAccountId).map((a) => (
                  <option key={a.id} value={a.id}>{a.name}{a.isActive ? "" : " (Inactive)"}</option>
                ))}
                {assetForm.assetAccountId
                  && !assetAccounts.some((a) => String(a.id) === assetForm.assetAccountId)
                  && <option value={assetForm.assetAccountId}>{assetForm.assetAccountName} (as recorded)</option>}
              </FormSelect>
              <FormSelect label="Paid From Account" value={assetForm.financeAccountId} onChange={(v) => setAssetForm({ ...assetForm, financeAccountId: v })}>
                <option value="">Select account</option>
                {financeAccounts.filter((a) => !isStaffFloat(a) && (a.isActive || String(a.id) === assetForm.financeAccountId)).map((a) => (
                  <option key={a.id} value={a.id}>{a.name} — {a.accountHolderName}{a.isActive ? "" : " (Inactive)"}</option>
                ))}
              </FormSelect>
              <FormSelect label="Project" value={assetForm.projectId} onChange={(v) => setAssetForm({ ...assetForm, projectId: v })}>
                <option value="">General (no specific project)</option>
                {projects.map((p) => <option key={p.id} value={p.id}>{p.projectName}</option>)}
              </FormSelect>
              {/* The same managed heads as expenses: the annual withholding allowance is one
                  aggregate per supplier per section, covering capital and revenue purchases alike. */}
              <FormSelect
                label="Category (for tax)"
                value={assetForm.categoryId}
                onChange={(v) => setAssetForm({
                  ...assetForm,
                  categoryId: v,
                  category: expenseCategories.find((c) => String(c.id) === v)?.name ?? "",
                  wht: v ? assetForm.wht : emptyWht(),
                })}
              >
                <option value="">Select a category</option>
                {expenseCategories
                  .filter((c) => c.isActive || String(c.id) === assetForm.categoryId)
                  .map((c) => (
                    <option key={c.id} value={c.id}>
                      {c.name}{c.isActive ? "" : " (Retired)"}
                      {c.isWhtApplicable && c.taxSection ? ` — s.${c.taxSection}` : ""}
                    </option>
                  ))}
              </FormSelect>
              <FormSelect
                label="Supplier"
                value={assetForm.vendorId || CUSTOM_TYPE}
                onChange={(v) => setAssetForm({
                  ...assetForm,
                  vendorId: v === CUSTOM_TYPE ? "" : v,
                  vendor: v === CUSTOM_TYPE ? "" : (vendors.find((x) => String(x.id) === v)?.name ?? ""),
                })}
              >
                <option value={CUSTOM_TYPE}>One-off supplier (enter manually)…</option>
                {vendors
                  .filter((v) => v.isActive || String(v.id) === assetForm.vendorId)
                  .map((v) => (
                    <option key={v.id} value={v.id}>
                      {v.name} — {v.filerStatus === "NonFiler" ? "Non-filer" : v.filerStatus}
                      {v.isActive ? "" : " (Inactive)"}
                    </option>
                  ))}
              </FormSelect>
              {!assetForm.vendorId && (
                <FormInput
                  label="Supplier / Reference (optional)"
                  value={assetForm.vendor}
                  onChange={(v) => setAssetForm({ ...assetForm, vendor: v })}
                />
              )}
              <FormInput label="Cost (Rs)" type="number" value={assetForm.amount} onChange={(v) => setAssetForm({ ...assetForm, amount: v })} />
              <FormInput label="Date" type="date" value={assetForm.date} onChange={(v) => setAssetForm({ ...assetForm, date: v })} />

              <ExpenseWhtFields
                categoryId={assetForm.categoryId}
                vendorId={assetForm.vendorId}
                grossAmount={assetForm.amount}
                date={assetForm.date}
                excludeExpenseId={null}
                excludeAssetPurchaseId={assetForm.id}
                capitalised
                value={assetForm.wht}
                disabled={saving}
                onChange={(wht) => setAssetForm((current) => current ? { ...current, wht } : current)}
              />

              <FormInput label="Notes (optional)" value={assetForm.description} onChange={(v) => setAssetForm({ ...assetForm, description: v })} />
              <FinanceAttachmentField
                existing={assetForm.attachment}
                selected={assetForm.selectedAttachment}
                removeExisting={assetForm.removeAttachment}
                disabled={saving}
                onSelected={(file) => setAssetForm((current) => current ? { ...current, selectedAttachment: file } : current)}
                onRemoveExisting={(remove) => setAssetForm((current) => current ? { ...current, removeAttachment: remove } : current)}
                onViewExisting={() => { if (assetForm.id && assetForm.attachment) void accessAttachment("assetPurchase", assetForm.id, assetForm.attachment, false); }}
                onDownloadExisting={() => { if (assetForm.id && assetForm.attachment) void accessAttachment("assetPurchase", assetForm.id, assetForm.attachment, true); }}
              />
            </div>
            <div className="flex justify-end gap-3 border-t border-[var(--border)] px-6 py-4">
              <Button variant="ghost" onClick={resetForms} disabled={saving}>Cancel</Button>
              <Button onClick={submitAssetPurchase} disabled={saving}>{saving ? "Saving…" : "Save"}</Button>
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
              {/* Says where construction belongs, because this is the form it belongs on. The pair of
                  notes here and on the fixed-asset form is what stops the same spend being recorded
                  twice through two different screens. */}
              <p className="mt-1 text-xs text-[var(--text-muted)]">
                Reduces profit on the date it is paid. Construction, materials, labour, contractors
                and site work all belong here. Use Add Fixed Asset only for something the company
                keeps, such as equipment or furniture.
              </p>
            </div>
            <div className="space-y-4 p-6">
              {formError && <p className="rounded-lg bg-rose-500/10 px-3 py-2 text-sm text-rose-300">{formError}</p>}
              <FormSelect label="Project" value={expenseForm.projectId} onChange={(v) => setExpenseForm({ ...expenseForm, projectId: v })}>
                <option value="">General (no specific project)</option>
                {projects.map((p) => <option key={p.id} value={p.id}>{p.projectName}</option>)}
              </FormSelect>
              <FormSelect label="Paid From Account" value={expenseForm.financeAccountId} onChange={(v) => setExpenseForm({ ...expenseForm, financeAccountId: v })}>
                <option value="">Select account</option>
                {financeAccounts.filter((a) => a.isActive || String(a.id) === expenseForm.financeAccountId).map((a) => <option key={a.id} value={a.id}>{a.name} — {a.accountHolderName}{isStaffFloat(a) ? " · Staff float" : ""}{a.isActive ? "" : " (Inactive)"}</option>)}
              </FormSelect>
              <FormSelect
                label="Category"
                value={expenseForm.categoryId || CUSTOM_TYPE}
                onChange={(v) => setExpenseForm({
                  ...expenseForm,
                  categoryId: v === CUSTOM_TYPE ? "" : v,
                  // Leaving the managed list means leaving the rate table behind, so the tax
                  // fields reset rather than carrying a rate that no longer has a source.
                  category: v === CUSTOM_TYPE ? "" : (expenseCategories.find((c) => String(c.id) === v)?.name ?? ""),
                  wht: v === CUSTOM_TYPE ? emptyWht() : expenseForm.wht,
                })}
              >
                {/* Free text is only offered to a row that already has it — an expense recorded
                    before the managed list existed. Offering it on a new expense would be a
                    one-click way past the rate table. */}
                {expenseForm.legacyCategory ? (
                  <option value={CUSTOM_TYPE}>Keep the original text — “{expenseForm.category}”</option>
                ) : (
                  <option value={CUSTOM_TYPE}>Select a category</option>
                )}
                {expenseCategories
                  .filter((c) => c.isActive || String(c.id) === expenseForm.categoryId)
                  .map((c) => (
                    <option key={c.id} value={c.id}>
                      {c.name}{c.isActive ? "" : " (Retired)"}
                      {c.isWhtApplicable && c.taxSection ? ` — s.${c.taxSection}` : ""}
                    </option>
                  ))}
              </FormSelect>
              {expenseForm.legacyCategory && !expenseForm.categoryId && (
                <FormInput
                  label="Original Category Text"
                  value={expenseForm.category}
                  onChange={(v) => setExpenseForm({ ...expenseForm, category: v })}
                />
              )}
              <FormSelect
                label="Vendor"
                value={expenseForm.vendorId || CUSTOM_TYPE}
                onChange={(v) => setExpenseForm({
                  ...expenseForm,
                  vendorId: v === CUSTOM_TYPE ? "" : v,
                  vendor: v === CUSTOM_TYPE ? "" : (vendors.find((x) => String(x.id) === v)?.name ?? ""),
                })}
              >
                <option value={CUSTOM_TYPE}>One-off payee (enter manually)…</option>
                {vendors
                  .filter((v) => v.isActive || String(v.id) === expenseForm.vendorId)
                  .map((v) => (
                    <option key={v.id} value={v.id}>
                      {v.name} — {v.filerStatus === "NonFiler" ? "Non-filer" : v.filerStatus}
                      {v.isActive ? "" : " (Inactive)"}
                    </option>
                  ))}
              </FormSelect>
              {!expenseForm.vendorId && (
                <FormInput
                  label="Vendor / Reference (optional)"
                  value={expenseForm.vendor}
                  onChange={(v) => setExpenseForm({ ...expenseForm, vendor: v })}
                />
              )}
              <FormInput label="Gross Amount (Rs)" type="number" value={expenseForm.amount} onChange={(v) => setExpenseForm({ ...expenseForm, amount: v })} />
              <FormInput label="Date" type="date" value={expenseForm.date} onChange={(v) => setExpenseForm({ ...expenseForm, date: v })} />

              <ExpenseWhtFields
                categoryId={expenseForm.categoryId}
                vendorId={expenseForm.vendorId}
                grossAmount={expenseForm.amount}
                date={expenseForm.date}
                excludeExpenseId={expenseForm.id}
                value={expenseForm.wht}
                disabled={saving}
                onChange={(wht) => setExpenseForm((current) => current ? { ...current, wht } : current)}
              />

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
