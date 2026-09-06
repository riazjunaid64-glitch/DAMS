import AppSelect from "../lib/AppSelect.tsx";
import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { api } from "../api/api.ts";
import type { User } from "../App.tsx";
import Button from "../lib/Button.tsx";
import Container from "../lib/Container.tsx";
import Field from "../lib/Field.tsx";
import BookingCommissionRebatePanel from "../features/commissionRebates/BookingCommissionRebatePanel.tsx";
import { BookingTabs, DetailRow, EmptyState, PanelCard, StatCard, TabPanel } from "../features/bookings/ui.tsx";
import { Icons, th } from "../features/bookings/tokens.tsx";
import BookingCancellationPanel from "../features/bookingCancellation/BookingCancellationPanel.tsx";
import CancellationDialog from "../features/bookingCancellation/CancellationDialog.tsx";
import type { CancellationSettlement } from "../features/bookingCancellation/types.ts";
import { pakistanToday } from "../lib/financePeriods.ts";
import { moneyRequest, useIdempotencyKeys } from "../lib/idempotency.ts";

type Props = { user: User | null };

interface BookingDetail {
  id: number;
  bookingReference: string;
  customerName: string;
  customerPhone: string;
  projectName: string;
  unitNumber: string;
  status: string;
  agreedSalePrice: number;
  discountAmount: number;
  discountPercent: number;
  bookingAmountRequired: number;
  bookingAmountReceived: number;
  bookingAmountRemaining: number;
  // Net non-cash rebate credits on this booking. Served by the booking endpoint itself, so it is
  // known whenever the page has a booking at all, and it is the same figure the payment and
  // schedule services measure their own limits against.
  rebateCredits: number;
  totalInstallmentAmount: number;
  installmentPlanStartDate?: string | null;
  concurrencyToken: string;
  cancellationSettlement?: CancellationSettlement | null;
}

interface ScheduleItem {
  id: number;
  sequenceNumber: number;
  type: string;
  dueDate: string;
  amount: number;
  status: string;
  amountPaid: number;
  remainingBalance: number;
  isOverdue: boolean;
  paidAt?: string | null;
  notes?: string | null;
}

interface BookingPayment {
  id: number;
  bookingId: number;
  installmentId?: number | null;
  type: string;
  amount: number;
  paymentMethod: string;
  paymentReference?: string | null;
  receiptNumber?: string | null;
  notes?: string | null;
  paidAt: string;
}

interface InstallmentSchedule {
  bookingId: number;
  bookingReference: string;
  bookingStatus: string;
  agreedSalePrice: number;
  discountAmount: number;
  discountPercent: number;
  bookingAmountReceived: number;
  possessionAmount: number;
  installmentPool: number;
  frequency?: string | null;
  numberOfInstallments?: number | null;
  installmentStartDate?: string | null;
  possessionDueDate?: string | null;
  generatedAt?: string | null;
  hasSchedule: boolean;
  canGenerate: boolean;
  canRegenerate: boolean;
  scheduleTotal: number;
  schedulePaid: number;
  scheduleRemaining: number;
  // What the customer owes that this plan does not demand — normally zero, positive only after a
  // credit the plan was built smaller by is reversed. The payment service refuses a receipt while
  // it stands, so the plan has to be regenerated first.
  unscheduledBalance?: number;
  items: ScheduleItem[];
}

const FREQUENCIES = [
  { value: "Monthly", label: "Monthly" },
  { value: "Quarterly", label: "Quarterly" },
  { value: "HalfYearly", label: "Half-Yearly" },
  { value: "Yearly", label: "Yearly" },
];

const PAYMENT_METHODS = [
  { value: "Cash", label: "Cash" },
  { value: "BankTransfer", label: "Bank Transfer" },
  { value: "Cheque", label: "Cheque" },
  { value: "Online", label: "Online" },
];

// Booking amount is set as a percentage of the agreed sale price.
const BOOKING_PERCENTS = ["5", "10", "15", "20", "25", "30", "custom"];

function statusBadgeClass(status: string) {
  switch (status) {
    case "Paid":
      return "border-emerald-500/30 bg-emerald-500/10 text-emerald-400";
    case "PartiallyPaid":
      return "border-amber-500/30 bg-amber-500/10 text-amber-300";
    case "Overdue":
      return "border-rose-500/30 bg-rose-500/10 text-rose-400";
    default:
      return "border-[var(--border)] text-[var(--text-muted)]";
  }
}

function prettyStatus(status: string) {
  return status.replace(/([A-Z])/g, " $1").trim();
}

// Matches the booking list, so a booking wears the same colour wherever it is seen: the live stages
// in the house gold, and a colour each for the three states that end or advance the journey.
function bookingStatusClass(status: string) {
  switch (status) {
    case "PossessionGiven":
      return "border-sky-400/30 bg-sky-500/10 text-sky-300";
    case "SaleCompleted":
      return "border-emerald-400/30 bg-emerald-500/10 text-emerald-300";
    case "Cancelled":
      return "border-rose-400/30 bg-rose-500/10 text-rose-300";
    default:
      return "border-[var(--border-hover)] bg-[var(--accent-glow)] text-[var(--accent-light)]";
  }
}

function formatMoney(n: number) {
  return n.toLocaleString(undefined, { minimumFractionDigits: 0, maximumFractionDigits: 2 });
}

// The price breakdown always carries both decimals, so the figures line up as a column and read as
// a statement rather than as rounded headline numbers. Matches the commission panel's `money`.
function formatAmount(n: number) {
  return `Rs ${n.toLocaleString("en-PK", { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
}

function formatDate(iso: string) {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleDateString();
}

function toDateInput(iso?: string | null) {
  if (!iso) return "";
  return iso.slice(0, 10);
}

interface FinanceAccountOption {
  id: number;
  name: string;
  accountHolderName: string;
  isActive: boolean;
}

export default function BookingDetailPage({ user }: Props) {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const bookingId = Number(id);
  const idempotency = useIdempotencyKeys();

  const [booking, setBooking] = useState<BookingDetail | null>(null);
  // NULL means "not known", never "none" — for all three of these. Each is loaded by its own request
  // and any of them can fail on its own, so a failure must read as a failure. Defaulting a missing
  // list to empty is what let "No payments recorded yet" mean "the payments could not be fetched",
  // on the very screen an operator uses to decide whether a customer has paid.
  const [schedule, setSchedule] = useState<InstallmentSchedule | null>(null);
  const [payments, setPayments] = useState<BookingPayment[] | null>(null);
  // Non-cash rebate credits reduce what the customer owes without any payment recording it, so the
  // Summary's price breakdown cannot be derived from the booking alone. Read from the workspace the
  // Commission & Rebate tab already uses, in the same parallel batch.
  const [rebateCredits, setRebateCredits] = useState<number | null>(null);
  // Which of the dependent reads failed on the last load. Held per resource so the tab that owns
  // one can say so and offer a retry, instead of every tab showing the same page-level message.
  const [scheduleError, setScheduleError] = useState<string | null>(null);
  const [paymentsError, setPaymentsError] = useState<string | null>(null);
  const [accountsError, setAccountsError] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState("summary");
  // Every tab opened so far. A panel is rendered from its first visit and then kept mounted, so a
  // half-typed commission or a loaded audit page survives a trip to another tab.
  const [visitedTabs, setVisitedTabs] = useState<string[]>(["summary"]);
  const selectTab = useCallback((id: string) => {
    setActiveTab(id);
    setVisitedTabs((prev) => (prev.includes(id) ? prev : [...prev, id]));
  }, []);
  // Bumped on every completed reload. The Commission & Rebate panel stays mounted behind the other
  // tabs, so this is how it learns that a payment or a plan change has moved the booking under it.
  const [dataVersion, setDataVersion] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [showRegenerateConfirm, setShowRegenerateConfirm] = useState(false);

  const [payTarget, setPayTarget] = useState<ScheduleItem | null>(null);
  const [paySubmitting, setPaySubmitting] = useState(false);
  const [payError, setPayError] = useState<string | null>(null);
  const [payForm, setPayForm] = useState({
    amount: "",
    paymentMethod: "Cash",
    financeAccountId: "",
    paymentReference: "",
    notes: "",
    paidAt: pakistanToday(),
  });

  // Which account the money lands in. Every payment must name one, so the account
  // balances and the Finance dashboard's per-account view can be trusted.
  const [financeAccounts, setFinanceAccounts] = useState<FinanceAccountOption[]>([]);

  // Booking amount: set negotiated terms + record booking-amount payments.
  const [showFinancials, setShowFinancials] = useState(false);
  const [finSubmitting, setFinSubmitting] = useState(false);
  const [finError, setFinError] = useState<string | null>(null);
  const [finForm, setFinForm] = useState({
    agreedSalePrice: "",
    discountPercent: "0",
    discountReason: "",
    bookingPercent: "10",
    bookingAmountRequired: "",
    bookingAmountDueDate: "",
  });

  const [showBookingPay, setShowBookingPay] = useState(false);
  const [bookingPaySubmitting, setBookingPaySubmitting] = useState(false);
  const [bookingPayError, setBookingPayError] = useState<string | null>(null);
  const [bookingPayForm, setBookingPayForm] = useState({
    amount: "",
    paymentMethod: "Cash",
    financeAccountId: "",
    paymentReference: "",
    notes: "",
    paidAt: pakistanToday(),
  });

  const [transitioning, setTransitioning] = useState(false);

  const [form, setForm] = useState({
    agreedSalePrice: "",
    discountPercent: "0",
    discountReason: "",
    frequency: "Monthly",
    numberOfInstallments: "12",
    installmentStartDate: "",
    possessionAmount: "0",
    possessionDueDate: "",
  });

  const isAdmin = user?.role === "Admin";

  const load = useCallback(async () => {
    if (!bookingId) return;
    setLoading(true);
    setError(null);
    try {
      // Each dependent read is caught on its own: a network failure on one must not abort the
      // others, and must not be reported as the booking itself being unavailable.
      const [bookRes, schedRes, payRes] = await Promise.all([
        api(`/api/Booking/${bookingId}`),
        api(`/api/Booking/${bookingId}/installments`).catch(() => null),
        api(`/api/Booking/${bookingId}/payments`).catch(() => null),
      ]);
      if (!bookRes.ok) throw new Error("Booking not found");

      const b: BookingDetail = await bookRes.json();
      setBooking(b);
      // Read off the booking rather than from a second endpoint, so the one figure every balance on
      // this page depends on cannot be the one that failed to arrive.
      setRebateCredits(typeof b.rebateCredits === "number" ? b.rebateCredits : null);

      const derivedPercent =
        b.agreedSalePrice > 0 && b.bookingAmountRequired > 0
          ? Math.round((b.bookingAmountRequired / b.agreedSalePrice) * 100)
          : 0;
      const isPreset = ["5", "10", "15", "20", "25", "30"].includes(String(derivedPercent));
      setFinForm({
        agreedSalePrice: String(b.agreedSalePrice || ""),
        discountPercent: String(b.discountPercent ?? 0),
        discountReason: "",
        bookingPercent: b.bookingAmountRequired > 0 ? (isPreset ? String(derivedPercent) : "custom") : "10",
        bookingAmountRequired: b.bookingAmountRequired ? String(b.bookingAmountRequired) : "",
        bookingAmountDueDate: "",
      });

      // Parsed in its own guard for the same reason as the credits above, and the previous value is
      // left alone on failure: showing yesterday's list under a visible warning beats replacing it
      // with a confident "no payments".
      let paymentRows: BookingPayment[] | null = null;
      if (payRes?.ok) {
        try {
          paymentRows = await payRes.json() as BookingPayment[];
        } catch {
          paymentRows = null;
        }
      }
      if (paymentRows) {
        setPayments(paymentRows);
        setPaymentsError(null);
      } else {
        setPaymentsError("The payment history could not be loaded.");
      }

      let s: InstallmentSchedule | null = null;
      if (schedRes?.ok) {
        try {
          s = await schedRes.json() as InstallmentSchedule;
        } catch {
          s = null;
        }
      }
      if (!s) {
        setScheduleError("The installment schedule could not be loaded.");
      } else {
        const loaded = s;
        setScheduleError(null);
        setSchedule(loaded);
        if (!loaded.hasSchedule) {
          setForm((prev) => ({
            ...prev,
            agreedSalePrice: String(b.agreedSalePrice),
            discountPercent: String(b.discountPercent ?? 0),
            installmentStartDate: toDateInput(b.installmentPlanStartDate) || pakistanToday(),
          }));
        } else {
          setForm((prev) => ({
            ...prev,
            agreedSalePrice: String(loaded.agreedSalePrice),
            discountPercent: String(loaded.discountPercent ?? 0),
            frequency: loaded.frequency ?? "Monthly",
            numberOfInstallments: String(loaded.numberOfInstallments ?? 12),
            installmentStartDate: toDateInput(loaded.installmentStartDate),
            possessionAmount: String(loaded.possessionAmount ?? 0),
            possessionDueDate: toDateInput(loaded.possessionDueDate),
          }));
        }
      }
      setDataVersion((v) => v + 1);
    } catch {
      setError("Unable to load booking.");
    } finally {
      setLoading(false);
    }
  }, [bookingId]);

  // Swallowing this failure left every payment form with an empty, required "Received In Account"
  // selector and nothing on screen saying why — the operator could not record money and could not
  // tell whether that was a rule or a fault.
  const loadFinanceAccounts = useCallback(async () => {
    try {
      const res = await api("/api/finance/accounts/options");
      if (!res.ok) throw new Error();
      setFinanceAccounts(await res.json());
      setAccountsError(null);
    } catch {
      setAccountsError("The list of finance accounts could not be loaded, so payments cannot be recorded.");
    }
  }, []);

  // One Retry for the whole page: every one of these reads can fail on its own, and asking the
  // operator to work out which button reloads which figure is not a recovery path.
  const reload = useCallback(async () => {
    await Promise.all([load(), loadFinanceAccounts()]);
  }, [load, loadFinanceAccounts]);

  useEffect(() => {
    if (isAdmin) {
      load();
      loadFinanceAccounts();
    }
  }, [isAdmin, load, loadFinanceAccounts]);

  // Mirrors BookingCreditPolicy.RemainingInstallmentPool, credits included: the server builds the
  // schedule out of (net price − booking amount received − credits − possession), so a preview that
  // left the credits out promised installments larger than the ones about to be generated. NULL
  // while the credits are unknown — a preview that is confidently wrong is worse than none.
  const previewPool = useMemo(() => {
    if (rebateCredits === null) return null;
    const agreed = Number(form.agreedSalePrice) || 0;
    const possession = Number(form.possessionAmount) || 0;
    const received = booking?.bookingAmountReceived ?? 0;
    const discountPct = Math.min(100, Math.max(0, Number(form.discountPercent) || 0));
    const net = agreed - Math.round((agreed * discountPct) / 100 * 100) / 100;
    return Math.max(0, net - received - rebateCredits - possession);
  }, [form.agreedSalePrice, form.possessionAmount, form.discountPercent, booking?.bookingAmountReceived, rebateCredits]);

  const previewPerInstallment = useMemo(() => {
    const n = Number(form.numberOfInstallments) || 0;
    if (previewPool === null || n <= 0 || previewPool <= 0) return 0;
    return Math.round((previewPool / n) * 100) / 100;
  }, [previewPool, form.numberOfInstallments]);

  const handleGenerate = async (e: FormEvent, regenerate: boolean) => {
    e.preventDefault();
    setSubmitting(true);
    setError(null);
    try {
      const possessionAmt = Number(form.possessionAmount) || 0;
      const body = {
        agreedSalePrice: Number(form.agreedSalePrice),
        discountPercent: Number(form.discountPercent) || 0,
        discountReason: form.discountReason.trim() || null,
        frequency: form.frequency,
        numberOfInstallments: Number(form.numberOfInstallments),
        installmentStartDate: form.installmentStartDate,
        possessionAmount: possessionAmt,
        possessionDueDate: possessionAmt > 0 ? form.possessionDueDate || null : null,
        regenerate,
      };
      const res = await api(`/api/Booking/${bookingId}/installment-plan/generate`, {
        method: "POST",
        body: JSON.stringify(body),
      });
      const data = await res.json();
      if (!res.ok) throw new Error(data.message || "Failed to generate schedule");
      setSchedule(data);
      setShowRegenerateConfirm(false);
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to generate schedule.");
    } finally {
      setSubmitting(false);
    }
  };

  const openPayModal = (item: ScheduleItem) => {
    setPayError(null);
    setPayForm({
      amount: String(item.remainingBalance),
      paymentMethod: "Cash",
      financeAccountId: financeAccounts.length === 1 ? String(financeAccounts[0].id) : "",
      paymentReference: "",
      notes: "",
      paidAt: pakistanToday(),
    });
    setPayTarget(item);
  };

  const handleRecordPayment = async (e: FormEvent) => {
    e.preventDefault();
    if (!payTarget) return;
    if (!payForm.financeAccountId) {
      setPayError("Select the account this payment was received in.");
      return;
    }
    setPaySubmitting(true);
    setPayError(null);
    try {
      const body = {
        amount: Number(payForm.amount),
        paymentMethod: payForm.paymentMethod,
        financeAccountId: Number(payForm.financeAccountId),
        paymentReference: payForm.paymentReference.trim() || null,
        notes: payForm.notes.trim() || null,
        paidAt: payForm.paidAt || null,
      };
      // Same intent keeps the same key, so pressing Save again after a dropped connection is
      // recognised as the retry it is rather than collecting the installment twice.
      const signature = `installment:${payTarget.id}:${body.amount}:${body.paidAt}:${body.financeAccountId}`;
      const res = await api(`/api/Booking/${bookingId}/installments/${payTarget.id}/payment`, moneyRequest(
        idempotency.key(signature, "installment-payment"),
        { method: "POST", body: JSON.stringify(body) },
      ));
      const data = await res.json();
      if (!res.ok) throw new Error(data.message || "Failed to record payment");
      idempotency.release(signature);
      setSchedule(data);
      setPayTarget(null);
      await load();
    } catch (err) {
      setPayError(err instanceof Error ? err.message : "Failed to record payment.");
    } finally {
      setPaySubmitting(false);
    }
  };

  const handleSaveFinancials = async (e: FormEvent) => {
    e.preventDefault();
    setFinSubmitting(true);
    setFinError(null);
    try {
      const agreed = Number(finForm.agreedSalePrice) || 0;
      const requiredAmount =
        finForm.bookingPercent === "custom"
          ? Number(finForm.bookingAmountRequired)
          : Math.round(agreed * (Number(finForm.bookingPercent) / 100) * 100) / 100;
      const body = {
        agreedSalePrice: agreed,
        discountPercent: Number(finForm.discountPercent) || 0,
        discountReason: finForm.discountReason.trim() || null,
        bookingAmountRequired: requiredAmount,
        bookingAmountDueDate: finForm.bookingAmountDueDate || null,
      };
      const res = await api(`/api/Booking/${bookingId}/financials`, {
        method: "PUT",
        body: JSON.stringify(body),
      });
      const data = await res.json();
      if (!res.ok) throw new Error(data.message || "Failed to save terms");
      setShowFinancials(false);
      await load();
    } catch (err) {
      setFinError(err instanceof Error ? err.message : "Failed to save terms.");
    } finally {
      setFinSubmitting(false);
    }
  };

  const openBookingPay = () => {
    setBookingPayError(null);
    // The credit-aware figure, not the raw requirement: a rebate has already settled part of the
    // booking amount, and defaulting the form to the raw difference offered a payment the service
    // would then refuse for exceeding the remaining balance.
    const remaining = booking?.bookingAmountRemaining ?? 0;
    setBookingPayForm({
      amount: remaining > 0 ? String(remaining) : "",
      paymentMethod: "Cash",
      financeAccountId: financeAccounts.length === 1 ? String(financeAccounts[0].id) : "",
      paymentReference: "",
      notes: "",
      paidAt: pakistanToday(),
    });
    setShowBookingPay(true);
  };

  const handleRecordBookingPay = async (e: FormEvent) => {
    e.preventDefault();
    if (!bookingPayForm.financeAccountId) {
      setBookingPayError("Select the account this payment was received in.");
      return;
    }
    setBookingPaySubmitting(true);
    setBookingPayError(null);
    try {
      const body = {
        amount: Number(bookingPayForm.amount),
        paymentMethod: bookingPayForm.paymentMethod,
        financeAccountId: Number(bookingPayForm.financeAccountId),
        paymentReference: bookingPayForm.paymentReference.trim() || null,
        notes: bookingPayForm.notes.trim() || null,
        paidAt: bookingPayForm.paidAt || null,
      };
      const signature = `booking-amount:${bookingId}:${body.amount}:${body.paidAt}:${body.financeAccountId}`;
      const res = await api(`/api/Booking/${bookingId}/booking-amount-payment`, moneyRequest(
        idempotency.key(signature, "booking-amount-payment"),
        { method: "POST", body: JSON.stringify(body) },
      ));
      const data = await res.json();
      if (!res.ok) throw new Error(data.message || "Failed to record payment");
      idempotency.release(signature);
      setShowBookingPay(false);
      await load();
    } catch (err) {
      setBookingPayError(err instanceof Error ? err.message : "Failed to record payment.");
    } finally {
      setBookingPaySubmitting(false);
    }
  };

  const handleTransition = async (path: "possession" | "complete", confirmText: string) => {
    if (!window.confirm(confirmText)) return;
    setTransitioning(true);
    setError(null);
    try {
      const res = await api(`/api/Booking/${bookingId}/${path}`, {
        method: "POST",
        body: JSON.stringify({}),
      });
      const data = await res.json().catch(() => ({}));
      if (!res.ok) throw new Error(data.message || "Failed to update booking status");
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to update booking status.");
    } finally {
      setTransitioning(false);
    }
  };

  if (!isAdmin) {
    return <Container className="py-16 text-center"><p className="text-[var(--text-muted)]">Admin access required.</p></Container>;
  }

  // Only the FIRST load blanks the page. Every reload after that — recording a payment, applying a
  // rebate, retrying the credit read — keeps the screen up and swaps the figures when they arrive.
  // Replacing the whole page would unmount the tab panels, throwing away a half-typed commission
  // and re-issuing its requests, which is the very thing keeping panels mounted exists to prevent.
  if (loading && !booking) {
    return <Container className="py-16 text-center"><p className="text-[var(--text-muted)]">Loading...</p></Container>;
  }

  if (!booking) {
    return (
      <Container className="py-16 text-center">
        <p className="text-rose-400">{error ?? "Booking not found."}</p>
        <Button className="mt-4" variant="outline" onClick={() => navigate("/confirmed-bookings")}>Back</Button>
      </Container>
    );
  }

  // The backend decides this, not the status. canGenerate already allows PossessionGiven — a
  // recognised sale still has a receivable, and an installment is the only way DAMS collects one,
  // so hiding the form here stranded it with no route to payment. It also folds in the booking
  // amount and the regenerate rules, which a status check silently skipped.
  // A stale schedule must not drive actions. Generating or collecting against installment ids that
  // were read before the last failed refresh is how an operator ends up recording money against a
  // row the server has since replaced, so both are withdrawn until the schedule reloads.
  const scheduleIsFresh = schedule !== null && scheduleError === null;
  const canShowPlanForm = scheduleIsFresh && Boolean(schedule.canGenerate);
  const planLocked = scheduleIsFresh && schedule.hasSchedule && !schedule.canRegenerate;
  // Positive only when a credit the plan was built smaller by has been reversed. The payment service
  // refuses a receipt while it stands, because that receipt would pin the plan and strand the amount.
  const unscheduledBalance = scheduleIsFresh ? schedule.unscheduledBalance ?? 0 : 0;
  // Possession means the sale is recognised as revenue, and the backend refuses to restate the
  // terms it was recognised on. Showing them as editable would only produce a rejection.
  const termsFrozen = booking.status === "PossessionGiven";

  const netSalePrice = booking.agreedSalePrice - booking.discountAmount;
  // NULL when the payment list did not arrive OR did not REFRESH. A stale list is fine to keep on
  // screen as history, but its sum is not a current figure: record a payment, let the follow-up read
  // fail, and yesterday's receipts would be added to today's booking and reported as Amount
  // Collected — understating it by exactly the payment just taken.
  const amountCollected = payments === null || paymentsError !== null
    ? null
    : payments.reduce((sum, p) => sum + p.amount, 0);
  const isCancelled = booking.status === "Cancelled";
  // Cancelling voids the sale, and Finance drops the booking from the receivable the moment it does
  // (FinanceService.OutstandingBookings). Reporting the unpaid balance here anyway would have this
  // screen claim a debt the ledger says does not exist; what the parties still owe each other is
  // the cancellation settlement, which the Summary tab shows.
  //
  // NULL while either input is unknown: an Outstanding computed without the rebate credit or
  // without the receipts is wrong, and a wrong figure is worse than an absent one on a screen
  // people collect money from.
  const outstandingAmount = isCancelled
    ? 0
    : rebateCredits === null || amountCollected === null
      ? null
      : Math.max(0, netSalePrice - amountCollected - rebateCredits);

  const missingReads = [
    rebateCredits === null ? "The booking's rebate credits are missing from this response." : null,
    accountsError,
    paymentsError,
    scheduleError,
  ].filter((m): m is string => m !== null);

  return (
    <Container size="wide" className="py-8">
      {/* ── Booking header ── */}
      <nav aria-label="Breadcrumb" className="mb-4 flex items-center gap-2 text-sm">
        <button
          type="button"
          onClick={() => navigate("/confirmed-bookings")}
          className="inline-flex cursor-pointer items-center gap-2 text-[var(--text-muted)] transition-colors hover:text-[var(--accent)]"
        >
          <Icons.back className="h-4 w-4" />
          Confirmed Bookings
        </button>
        <span className="text-[var(--text-muted)]">›</span>
        <span className="text-[var(--text-secondary)]">{booking.bookingReference}</span>
      </nav>

      <div className="mb-6 flex flex-col gap-5 lg:flex-row lg:items-start lg:justify-between">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-3">
            <h1 className="text-3xl font-bold tracking-tight text-[var(--text-heading)]">{booking.bookingReference}</h1>
            <span className={`inline-flex items-center gap-1.5 rounded-full border px-3 py-1 text-xs font-semibold ${bookingStatusClass(booking.status)}`}>
              <Icons.pulse className="h-3.5 w-3.5" />
              {prettyStatus(booking.status)}
            </span>
          </div>
          {/* Who and what, in one scannable row. Wraps rather than truncating: a long customer or
              project name is exactly the case where the reader needs to see all of it. */}
          <div className="mt-3 flex flex-wrap items-center gap-x-5 gap-y-2 text-sm text-[var(--text-secondary)]">
            <span className="inline-flex items-center gap-2">
              <Icons.user className="h-4 w-4 text-[var(--text-muted)]" />
              <span className="font-medium text-[var(--text-primary)]">{booking.customerName}</span>
            </span>
            <span className="inline-flex items-center gap-2">
              <Icons.phone className="h-4 w-4 text-[var(--text-muted)]" />
              {booking.customerPhone}
            </span>
            <span className="inline-flex items-center gap-2">
              <Icons.pin className="h-4 w-4 text-[var(--text-muted)]" />
              {booking.projectName}
            </span>
            <span className="inline-flex items-center gap-2">
              <Icons.unit className="h-4 w-4 text-[var(--text-muted)]" />
              Unit {booking.unitNumber}
            </span>
          </div>
        </div>

        <div className="flex shrink-0 flex-wrap items-center gap-2.5">
          <Button variant="outline" size="sm" onClick={() => navigate(`/application-form?bookingId=${booking.id}`)}>
            <Icons.printer className="h-4 w-4" />
            Print Application Form
          </Button>
          {booking.status === "PaymentPlanActive" && (
            <Button variant="outline" size="sm" disabled={transitioning}
              onClick={() => handleTransition("possession", "Mark possession as handed over to the customer?")}>
              Give Possession
            </Button>
          )}
          {/* Possession only. Completion is the paperwork that follows a recognised sale, and the
              backend refuses it before possession — offering it earlier only produced a rejection. */}
          {booking.status === "PossessionGiven" && (
            <Button variant="outline" size="sm" disabled={transitioning}
              onClick={() => handleTransition("complete", "Complete this sale? All payments must be fully received. The unit will be marked as Sold.")}>
              Complete Sale
            </Button>
          )}
          <CancellationDialog
            bookingId={bookingId}
            status={booking.status}
            unitNumber={booking.unitNumber}
            financeAccounts={financeAccounts}
            onCancelled={load}
          />
        </div>
      </div>

      {/* Page-level, so a failure raised on one tab is not hidden by switching to another. */}
      {error && <div className="mb-6 rounded-xl border border-rose-500/20 bg-rose-500/10 px-4 py-3 text-sm text-rose-400">{error}</div>}

      {/* Says which figures are missing and why, rather than letting them quietly read zero. One
          banner listing every read that failed, because they share one Retry. */}
      {!loading && missingReads.length > 0 && (
        <div className="mb-6 flex flex-wrap items-center justify-between gap-3 rounded-xl border border-amber-500/20 bg-amber-500/10 px-4 py-3 text-sm text-amber-300">
          <span>{missingReads.join(" ")} The figures that depend on {missingReads.length > 1 ? "them" : "it"} are shown as “Unavailable” rather than as zero.</span>
          <Button size="sm" variant="outline" onClick={() => void reload()}>Retry</Button>
        </div>
      )}

      <BookingTabs
        tabs={[
          { id: "summary", label: "Summary" },
          { id: "plan", label: "Installment Plan" },
          { id: "payments", label: "Payment History" },
          { id: "commission", label: "Commission & Rebate" },
        ]}
        active={activeTab}
        onChange={selectTab}
      />

      <TabPanel id="summary" active={activeTab} visited={visitedTabs}>
        <div className="space-y-6">
          {/* Cancellation settlement lives here rather than above the tabs: it is a summary of what
              a cancelled booking owes, and the header pill already says the booking is cancelled. */}
          <BookingCancellationPanel
            bookingId={bookingId}
            status={booking.status}
            settlement={booking.cancellationSettlement}
            financeAccounts={financeAccounts}
            onChanged={load}
          />

          <PanelCard title="Booking Summary" description="Key financial information for this booking.">
            <div className="grid gap-4 px-5 pb-5 sm:grid-cols-2 sm:px-6 sm:pb-6 xl:grid-cols-4">
              <StatCard tone="gold" icon={<Icons.home />} label="Agreed Sale Price"
                value={formatMoney(booking.agreedSalePrice)} />
              <StatCard tone="emerald" icon={<Icons.wallet />} label="Booking Amount Received"
                value={`${formatMoney(booking.bookingAmountReceived)} / ${formatMoney(booking.bookingAmountRequired)}`} />
              <StatCard tone="gold" icon={<Icons.coins />} label="Booking Amount Remaining"
                value={formatMoney(booking.bookingAmountRemaining)} />
              <StatCard tone="sky" icon={<Icons.calendar />} label="Installment Pool (preview)"
                value={schedule?.installmentPool !== undefined
                  ? formatMoney(schedule.installmentPool)
                  : previewPool === null ? "Unavailable" : formatMoney(previewPool)} />
            </div>
          </PanelCard>

          <div className="grid gap-6 lg:grid-cols-2">
            <PanelCard title="Price Details">
              <div className="px-5 pb-5 sm:px-6 sm:pb-6">
                <DetailRow label="Sale Price" value={formatAmount(booking.agreedSalePrice)} />
                {/* Shown only when there is one, so an undiscounted sale is not padded with a zero row. */}
                {booking.discountAmount > 0 && (
                  <DetailRow label={`Discount (${booking.discountPercent}%)`} value={`− ${formatAmount(booking.discountAmount)}`} />
                )}
                <DetailRow label="Net Sale Price" value={formatAmount(netSalePrice)} />
                <DetailRow
                  label="Amount Collected"
                  value={amountCollected === null
                    ? <span className="font-normal text-[var(--text-muted)]">Unavailable</span>
                    : formatAmount(amountCollected)}
                />
                <DetailRow
                  label="Rebate Credits"
                  value={rebateCredits === null
                    ? <span className="font-normal text-[var(--text-muted)]">Unavailable</span>
                    : formatAmount(rebateCredits)}
                />
              </div>
            </PanelCard>

            <PanelCard title="Booking Status">
              <div className="flex items-center gap-4 px-5 pb-5 sm:px-6 sm:pb-6">
                <span className="flex h-16 w-16 shrink-0 items-center justify-center rounded-full bg-[var(--accent-glow)] text-[var(--accent)]">
                  <Icons.pulse className="h-7 w-7" />
                </span>
                <span>
                  <span className="block text-xs text-[var(--text-muted)]">Status</span>
                  <span className="mt-0.5 block text-2xl font-bold text-[var(--text-heading)]">
                    {prettyStatus(booking.status)}
                  </span>
                </span>
              </div>
            </PanelCard>
          </div>

          {/* Booking amount workflow (step before installment plan) */}
          {booking.status === "AwaitingBookingAmount" && (
            <PanelCard
              title="Booking Amount"
              description="Set the negotiated terms, then record the booking amount. Once it is fully received, the installment plan unlocks."
              action={<>
                <Button variant="outline" size="sm" onClick={() => { setFinError(null); setShowFinancials((v) => !v); }}>
                  {booking.bookingAmountRequired > 0 ? "Edit Terms" : "Set Terms"}
                </Button>
                <Button
                  size="sm"
                  disabled={booking.bookingAmountRequired <= 0 || booking.bookingAmountRemaining <= 0}
                  onClick={openBookingPay}
                >
                  Record Payment
                </Button>
              </>}
            >
            <div className="px-5 pb-5 sm:px-6 sm:pb-6">
              {booking.bookingAmountRequired <= 0 && !showFinancials && (
                <div className="mt-4 rounded-xl border border-amber-500/20 bg-amber-500/10 px-4 py-3 text-sm text-amber-300">
                  No booking amount has been set yet. Click <strong>Set Terms</strong> to enter the agreed sale price and
                  the required booking amount before recording payments.
                </div>
              )}

              {showFinancials && (
                <form onSubmit={handleSaveFinancials} className="mt-5 grid gap-4 sm:grid-cols-2">
                  {finError && (
                    <div className="sm:col-span-2 rounded-xl border border-rose-500/20 bg-rose-500/10 px-4 py-3 text-sm text-rose-400">
                      {finError}
                    </div>
                  )}
                  <Field label="Agreed Sale Price" type="number" min="0" step="0.01" required
                    value={finForm.agreedSalePrice}
                    onChange={(e) => setFinForm({ ...finForm, agreedSalePrice: e.target.value })} />
                  <label className="flex flex-col gap-1.5 text-sm font-medium text-[var(--text-secondary)]">
                    <span>Booking Amount (% of sale price)</span>
                    <AppSelect value={finForm.bookingPercent}
                      onChange={(e) => setFinForm({ ...finForm, bookingPercent: e.target.value })}
                      className="w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-4 py-3 text-sm text-[var(--text-primary)]">
                      {BOOKING_PERCENTS.map((p) => (
                        <option key={p} value={p}>{p === "custom" ? "Custom amount" : `${p}%`}</option>
                      ))}
                    </AppSelect>
                    {finForm.bookingPercent !== "custom" && (
                      <span className="text-xs text-[var(--text-muted)]">
                        = {formatMoney(Math.round((Number(finForm.agreedSalePrice) || 0) * (Number(finForm.bookingPercent) / 100) * 100) / 100)}
                      </span>
                    )}
                  </label>
                  {finForm.bookingPercent === "custom" && (
                    <Field label="Booking Amount Required" type="number" min="0" step="0.01" required
                      value={finForm.bookingAmountRequired}
                      onChange={(e) => setFinForm({ ...finForm, bookingAmountRequired: e.target.value })} />
                  )}
                  <Field label="Discount %" type="number" min="0" max="100" step="0.01"
                    value={finForm.discountPercent}
                    onChange={(e) => setFinForm({ ...finForm, discountPercent: e.target.value })} />
                  <Field label="Booking Amount Due Date (optional)" type="date"
                    value={finForm.bookingAmountDueDate}
                    onChange={(e) => setFinForm({ ...finForm, bookingAmountDueDate: e.target.value })} />
                  <div className="sm:col-span-2">
                    <Field label="Discount Reason (optional)"
                      value={finForm.discountReason}
                      onChange={(e) => setFinForm({ ...finForm, discountReason: e.target.value })} />
                  </div>
                  <div className="sm:col-span-2 flex gap-2">
                    <Button type="submit" disabled={finSubmitting}>{finSubmitting ? "Saving..." : "Save Terms"}</Button>
                    <Button type="button" variant="ghost" onClick={() => setShowFinancials(false)} disabled={finSubmitting}>Cancel</Button>
                  </div>
                </form>
              )}
            </div>
            </PanelCard>
          )}
        </div>
      </TabPanel>

      {/* ── Installment Plan ── */}
      <TabPanel id="plan" active={activeTab} visited={visitedTabs}>
        <div className="space-y-6">
          {/* Plan configuration */}
          {canShowPlanForm && (
            <PanelCard
              title={schedule?.hasSchedule ? "Regenerate Installment Plan" : "Configure Installment Plan"}
              description="Define the negotiated structure for this booking. Each booking has its own schedule."
            >
            <div className="px-5 pb-5 sm:px-6 sm:pb-6">
              {planLocked && (
                <div className="mb-4 rounded-xl border border-[var(--border)] bg-[var(--surface-glass-hover)] px-4 py-3 text-sm text-[var(--text-muted)]">
                  Schedule is locked because payments exist or installments are no longer all pending.
                </div>
              )}

              {termsFrozen && (
                <div className="mb-4 rounded-xl border border-[var(--border)] bg-[var(--surface-glass-hover)] px-4 py-3 text-sm text-[var(--text-muted)]">
                  The sale was recognised at possession, so the agreed price and discount are fixed. You
                  can still change how the remaining balance is collected — dates, frequency and the
                  number of installments.
                </div>
              )}

              <form onSubmit={(e) => {
                if (schedule?.hasSchedule && schedule.canRegenerate) {
                  e.preventDefault();
                  setShowRegenerateConfirm(true);
                } else {
                  handleGenerate(e, false);
                }
              }} className="grid gap-4 sm:grid-cols-2">
                <Field label="Agreed Sale Price" type="number" min="0" step="0.01" required
                  value={form.agreedSalePrice} disabled={planLocked || termsFrozen}
                  onChange={(e) => setForm({ ...form, agreedSalePrice: e.target.value })} />
                <Field label="Discount %" type="number" min="0" max="100" step="0.01"
                  value={form.discountPercent} disabled={planLocked || termsFrozen}
                  onChange={(e) => setForm({ ...form, discountPercent: e.target.value })} />
                <label className="flex flex-col gap-1.5 text-sm font-medium text-[var(--text-secondary)]">
                  <span>Frequency</span>
                  <AppSelect value={form.frequency} onChange={(e) => setForm({ ...form, frequency: e.target.value })}
                    disabled={planLocked}
                    className="w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-4 py-3 text-sm text-[var(--text-primary)]">
                    {FREQUENCIES.map((f) => <option key={f.value} value={f.value}>{f.label}</option>)}
                  </AppSelect>
                </label>
                <Field label="Number of Installments" type="number" min={1} max={600} required
                  value={form.numberOfInstallments} disabled={planLocked}
                  onChange={(e) => setForm({ ...form, numberOfInstallments: e.target.value })} />
                <Field label="Installment Start Date" type="date" required
                  value={form.installmentStartDate} disabled={planLocked}
                  onChange={(e) => setForm({ ...form, installmentStartDate: e.target.value })} />
                <Field label="Possession Amount (optional)" type="number" min="0" step="0.01"
                  value={form.possessionAmount} disabled={planLocked}
                  onChange={(e) => setForm({ ...form, possessionAmount: e.target.value })} />
                {Number(form.possessionAmount) > 0 && (
                  <Field label="Possession Due Date" type="date" required
                    value={form.possessionDueDate} disabled={planLocked}
                    onChange={(e) => setForm({ ...form, possessionDueDate: e.target.value })} />
                )}

                <div className="sm:col-span-2 rounded-xl border border-[var(--border)] bg-[var(--surface-glass-hover)] px-4 py-3 text-sm text-[var(--text-secondary)]">
                  {previewPool === null ? (
                    <>
                      <strong>Preview:</strong> unavailable — the rebate credits that come off the pool could not be
                      loaded, and the schedule the server builds would not match what is shown here.
                    </>
                  ) : (
                    <>
                      <strong>Preview:</strong> {form.numberOfInstallments} installments × ~{formatMoney(previewPerInstallment)}
                      {Number(form.possessionAmount) > 0 && ` + possession ${formatMoney(Number(form.possessionAmount))}`}
                      {" "}(pool {formatMoney(previewPool)})
                    </>
                  )}
                </div>

                {!planLocked && (
                  <div className="sm:col-span-2">
                    <Button type="submit" disabled={submitting}>
                      {submitting ? "Generating..." : schedule?.hasSchedule ? "Regenerate Schedule" : "Generate Schedule"}
                    </Button>
                  </div>
                )}
              </form>

              {showRegenerateConfirm && (
                <div className="mt-4 rounded-xl border border-amber-500/30 bg-amber-500/10 p-4">
                  <p className="text-sm text-amber-200">This will replace the existing pending schedule. Continue?</p>
                  <div className="mt-3 flex gap-2">
                    <Button size="sm" type="button" onClick={() => { void handleGenerate({ preventDefault: () => {} } as FormEvent, true); }} disabled={submitting}>Yes, regenerate</Button>
                    <Button size="sm" variant="ghost" onClick={() => setShowRegenerateConfirm(false)}>Cancel</Button>
                  </div>
                </div>
              )}
            </div>
            </PanelCard>
          )}

          {/* The plan no longer covers the balance — say so where the plan is, and name the repair.
              Collection is withheld until then because the next receipt would lock the shortfall in. */}
          {unscheduledBalance > 0 && (
            <div className="rounded-xl border border-amber-500/30 bg-amber-500/10 px-4 py-3 text-sm text-amber-200">
              This booking owes {formatMoney(unscheduledBalance)} more than the schedule below collects,
              because a rebate credit the plan was built without has since been reversed. Regenerate the
              installment plan for the current balance — payments are held until then, since taking one
              would lock the plan with that amount uncollectable.
            </div>
          )}

          {/* Schedule table */}
          {schedule?.hasSchedule && schedule.items.length > 0 ? (
            <div className="overflow-hidden rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)]">
              <div className="border-b border-[var(--border)] px-5 py-4 sm:px-6">
                <h2 className="text-lg font-semibold text-[var(--text-heading)]">Installment Schedule</h2>
                <p className="mt-1 text-xs text-[var(--text-muted)]">
                  Generated {schedule.generatedAt ? formatDate(schedule.generatedAt) : "—"} · Total {formatMoney(schedule.scheduleTotal)}
                  {" · "}Paid {formatMoney(schedule.schedulePaid)} · Remaining {formatMoney(schedule.scheduleRemaining)}
                </p>
              </div>
              <div className="overflow-x-auto">
              <table className="data-table w-full min-w-[900px] text-left text-sm">
                <thead className="border-b border-[var(--border)] bg-[var(--surface-glass-hover)]">
                  <tr>
                    {["#", "Type", "Due Date", "Amount", "Paid", "Remaining", "Status", "Notes", "Action"].map((h, i) => (
                      <th key={h || `col-${i}`} className={`${th} ${h === "Action" ? "text-right" : ""}`}>{h}</th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {schedule.items.map((item) => {
                    // Collection continues after possession: the unpaid balance is then an Accounts
                    // Receivable, and the backend accepts a receipt against it for exactly that reason.
                    // Only the two collectable states — a cancelled or completed booking takes neither.
                    // Withheld while the plan is short of the balance: the payment service refuses
                    // such a receipt, because taking it would pin the plan and leave the difference
                    // uncollectable. The banner above says so and points at Regenerate.
                    const isPayable = (booking.status === "PaymentPlanActive" || booking.status === "PossessionGiven")
                      && item.status !== "Paid" && item.remainingBalance > 0
                      && unscheduledBalance <= 0;
                    return (
                    <tr key={item.id} className="border-b border-[var(--border)] transition-colors last:border-0 hover:bg-[var(--surface-glass-hover)]">
                      <td className="px-5 py-3.5 text-[var(--text-secondary)]">{item.type === "Possession" ? "—" : item.sequenceNumber}</td>
                      <td className="px-5 py-3.5">
                        <span className={item.type === "Possession" ? "text-violet-400" : "text-[var(--text-secondary)]"}>
                          {item.type === "Possession" ? "Possession" : "Regular"}
                        </span>
                      </td>
                      <td className="px-5 py-3.5 text-[var(--text-secondary)]">{formatDate(item.dueDate)}</td>
                      <td className="px-5 py-3.5 font-semibold tabular-nums text-[var(--text-heading)]">{formatMoney(item.amount)}</td>
                      <td className="px-5 py-3.5 tabular-nums text-[var(--text-secondary)]">{formatMoney(item.amountPaid)}</td>
                      <td className="px-5 py-3.5 tabular-nums text-[var(--text-secondary)]">{formatMoney(item.remainingBalance)}</td>
                      <td className="px-5 py-3.5">
                        <span className={`inline-flex whitespace-nowrap rounded-full border px-3 py-1 text-xs font-medium ${statusBadgeClass(item.status)}`}>{prettyStatus(item.status)}</span>
                      </td>
                      <td className="max-w-[160px] truncate px-5 py-3.5 text-xs text-[var(--text-muted)]">{item.notes ?? "—"}</td>
                      <td className="px-5 py-3.5 text-right">
                        {isPayable && (
                          <Button size="sm" variant="outline" onClick={() => openPayModal(item)}>Record Payment</Button>
                        )}
                      </td>
                    </tr>
                    );
                  })}
                </tbody>
              </table>
              </div>
            </div>
          ) : scheduleError ? (
            // "No schedule" and "the schedule did not load" are different facts, and only one of
            // them means the next step is to generate a plan.
            <PanelCard>
              <div className="px-5 py-8 text-center sm:px-6">
                <p className="text-sm text-rose-400">{scheduleError}</p>
                <Button className="mt-4" size="sm" variant="outline" onClick={() => void reload()}>Retry</Button>
              </div>
            </PanelCard>
          ) : (
            // A tab must always say something. Which message depends on why there is no schedule: the
            // form above is the next step when it is available, and the booking amount is when it is not.
            <PanelCard>
              <EmptyState
                message="No installment schedule yet."
                hint={canShowPlanForm
                  ? "Set the plan terms above and generate the schedule."
                  : "The booking amount must be fully received before the installment plan unlocks."}
              />
            </PanelCard>
          )}
        </div>
      </TabPanel>

      {/* ── Payment History ── */}
      <TabPanel id="payments" active={activeTab} visited={visitedTabs}>
        <div className="space-y-6">
          <PanelCard
            title="Payment Summary"
            description={isCancelled
              ? "This booking is cancelled, so the sale is no longer owed. What remains between the parties is the cancellation settlement, on the Summary tab."
              : "What has been received against this booking."}
          >
            <div className="grid gap-4 px-5 pb-5 sm:grid-cols-2 sm:px-6 sm:pb-6 xl:grid-cols-3">
              <StatCard tone="emerald" icon={<Icons.wallet />} label="Booking Amount Received"
                value={`Rs ${formatMoney(booking.bookingAmountReceived)} / ${formatMoney(booking.bookingAmountRequired)}`} />
              <StatCard tone="gold" icon={<Icons.coins />} label="Amount Collected"
                value={amountCollected === null
                  ? <span className="text-[var(--text-muted)]">Unavailable</span>
                  : `Rs ${formatMoney(amountCollected)}`} />
              {/* Net of rebate credits: they settle the balance without any cash arriving, so leaving
                  them out would report the customer owing money a rebate has already cleared. */}
              <StatCard
                tone={isCancelled ? "rose" : "sky"}
                icon={<Icons.doc />}
                label={isCancelled ? "Sale Obligation" : "Outstanding Amount"}
                value={isCancelled
                  ? "Cancelled"
                  : outstandingAmount === null
                    ? <span className="text-[var(--text-muted)]">Unavailable</span>
                    : `Rs ${formatMoney(outstandingAmount)}`} />
            </div>
          </PanelCard>

          {payments !== null && payments.length > 0 ? (
            <div className="overflow-hidden rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)]">
              <div className="border-b border-[var(--border)] px-5 py-4 sm:px-6">
                <h2 className="text-lg font-semibold text-[var(--text-heading)]">Payment History</h2>
                <p className="mt-1 text-sm text-[var(--text-muted)]">All recorded payments for this booking with receipt numbers.</p>
              </div>
              <div className="overflow-x-auto">
                <table className="data-table w-full min-w-[900px] text-left text-sm">
                  <thead className="border-b border-[var(--border)] bg-[var(--surface-glass-hover)]">
                    <tr>
                      {["Receipt #", "Date", "Type", "For", "Amount", "Method", "Reference", "Actions"].map((h, i) => (
                        <th key={h || `col-${i}`} className={`${th} ${h === "Actions" ? "text-right" : ""}`}>{h}</th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    {payments.map((p) => {
                      const inst = p.installmentId ? schedule?.items.find((i) => i.id === p.installmentId) : null;
                      const forLabel = p.type === "BookingAmount"
                        ? "Booking Amount"
                        : inst
                          ? (inst.type === "Possession" ? "Possession" : `Installment ${inst.sequenceNumber}`)
                          : "Installment";
                      return (
                        <tr key={p.id} className="border-b border-[var(--border)] transition-colors last:border-0 hover:bg-[var(--surface-glass-hover)]">
                          <td className="px-5 py-3.5 font-mono text-xs font-semibold text-[var(--text-heading)]">{p.receiptNumber ?? "—"}</td>
                          <td className="px-5 py-3.5 text-[var(--text-secondary)]">{formatDate(p.paidAt)}</td>
                          <td className="px-5 py-3.5 text-[var(--text-secondary)]">{prettyStatus(p.type)}</td>
                          <td className="px-5 py-3.5 text-[var(--text-secondary)]">{forLabel}</td>
                          <td className="px-5 py-3.5 font-semibold tabular-nums text-[var(--text-heading)]">{formatMoney(p.amount)}</td>
                          <td className="px-5 py-3.5 text-[var(--text-secondary)]">{prettyStatus(p.paymentMethod)}</td>
                          <td className="max-w-[160px] truncate px-5 py-3.5 text-xs text-[var(--text-muted)]">{p.paymentReference ?? "—"}</td>
                          <td className="px-5 py-3.5 text-right">
                            <Button size="sm" variant="outline" onClick={() => window.open(`/receipt/${bookingId}/${p.id}`, "_blank")}>
                              <Icons.doc className="h-4 w-4" />
                              Receipt
                            </Button>
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            </div>
          ) : paymentsError ? (
            // "No payments" is a statement about the customer; a failed read is a statement about
            // the network. Showing the first when the second happened is how an operator ends up
            // collecting money that was already paid.
            <PanelCard>
              <div className="px-5 py-8 text-center sm:px-6">
                <p className="text-sm text-rose-400">{paymentsError}</p>
                <Button className="mt-4" size="sm" variant="outline" onClick={() => void reload()}>Retry</Button>
              </div>
            </PanelCard>
          ) : (
            <PanelCard>
              <EmptyState
                message="No payments recorded yet."
                hint="Receipts appear here as soon as the first payment is recorded."
              />
            </PanelCard>
          )}
        </div>
      </TabPanel>

      {/* ── Commission & Rebate ── */}
      <TabPanel id="commission" active={activeTab} visited={visitedTabs}>
        {/* A rebate credit changes the customer's balance, can settle installments and can move the
            booking's own status, so every other tab's figures go stale the moment one is applied.
            `load` is stable (useCallback on bookingId), so this cannot loop. */}
        <BookingCommissionRebatePanel bookingId={bookingId} onChanged={load} refreshToken={dataVersion} />
      </TabPanel>

      {/* Record installment payment modal */}
      {payTarget && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4" onClick={() => !paySubmitting && setPayTarget(null)}>
          <div className="w-full max-w-md rounded-2xl border border-[var(--border)] bg-[var(--modal-bg)] p-6 shadow-xl" onClick={(e) => e.stopPropagation()}>
            <h3 className="text-lg font-semibold text-[var(--text-heading)]">Record Installment Payment</h3>
            <p className="mt-1 text-sm text-[var(--text-muted)]">
              {payTarget.type === "Possession" ? "Possession payment" : `Installment ${payTarget.sequenceNumber}`} · Due {formatDate(payTarget.dueDate)} · Remaining {formatMoney(payTarget.remainingBalance)}
            </p>

            {payError && <div className="mt-4 rounded-xl border border-rose-500/20 bg-rose-500/10 px-4 py-3 text-sm text-rose-400">{payError}</div>}

            <form onSubmit={handleRecordPayment} className="mt-4 grid gap-4">
              <Field label="Amount" type="number" min="0.01" step="0.01" required
                value={payForm.amount}
                onChange={(e) => setPayForm({ ...payForm, amount: e.target.value })} />
              <label className="flex flex-col gap-1.5 text-sm font-medium text-[var(--text-secondary)]">
                <span>Payment Method</span>
                <AppSelect value={payForm.paymentMethod} onChange={(e) => setPayForm({ ...payForm, paymentMethod: e.target.value })}
                  className="w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-4 py-3 text-sm text-[var(--text-primary)]">
                  {PAYMENT_METHODS.map((m) => <option key={m.value} value={m.value}>{m.label}</option>)}
                </AppSelect>
              </label>
              <label className="flex flex-col gap-1.5 text-sm font-medium text-[var(--text-secondary)]">
                <span>Received In Account</span>
                <AppSelect required value={payForm.financeAccountId} onChange={(e) => setPayForm({ ...payForm, financeAccountId: e.target.value })}
                  className="w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-4 py-3 text-sm text-[var(--text-primary)]">
                  <option value="">Select an account…</option>
                  {financeAccounts.map((a) => <option key={a.id} value={a.id}>{a.name} — {a.accountHolderName}</option>)}
                </AppSelect>
              </label>
              <Field label="Reference (optional)" value={payForm.paymentReference}
                onChange={(e) => setPayForm({ ...payForm, paymentReference: e.target.value })} />
              <Field label="Payment Date" type="date" value={payForm.paidAt}
                onChange={(e) => setPayForm({ ...payForm, paidAt: e.target.value })} />
              <Field label="Notes (optional)" value={payForm.notes}
                onChange={(e) => setPayForm({ ...payForm, notes: e.target.value })} />

              <div className="flex gap-2">
                <Button type="submit" disabled={paySubmitting}>{paySubmitting ? "Recording..." : "Record Payment"}</Button>
                <Button type="button" variant="ghost" onClick={() => setPayTarget(null)} disabled={paySubmitting}>Cancel</Button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Record booking-amount payment modal */}
      {showBookingPay && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4" onClick={() => !bookingPaySubmitting && setShowBookingPay(false)}>
          <div className="w-full max-w-md rounded-2xl border border-[var(--border)] bg-[var(--modal-bg)] p-6 shadow-xl" onClick={(e) => e.stopPropagation()}>
            <h3 className="text-lg font-semibold text-[var(--text-heading)]">Record Booking Amount Payment</h3>
            <p className="mt-1 text-sm text-[var(--text-muted)]">
              Required {formatMoney(booking.bookingAmountRequired)} · Received {formatMoney(booking.bookingAmountReceived)}
              {/* Named, so "Remaining" reads as the sum it is rather than as a figure that does not
                  subtract. Without it a credit-covered booking showed Required 1,000,000, Received 0
                  and Remaining 0 with nothing on screen to explain the gap. */}
              {booking.rebateCredits > 0 && ` · Rebate credit ${formatMoney(booking.rebateCredits)}`}
              {" · "}Remaining {formatMoney(booking.bookingAmountRemaining)}
            </p>

            {bookingPayError && <div className="mt-4 rounded-xl border border-rose-500/20 bg-rose-500/10 px-4 py-3 text-sm text-rose-400">{bookingPayError}</div>}

            <form onSubmit={handleRecordBookingPay} className="mt-4 grid gap-4">
              <Field label="Amount" type="number" min="0.01" step="0.01" required
                value={bookingPayForm.amount}
                onChange={(e) => setBookingPayForm({ ...bookingPayForm, amount: e.target.value })} />
              <label className="flex flex-col gap-1.5 text-sm font-medium text-[var(--text-secondary)]">
                <span>Payment Method</span>
                <AppSelect value={bookingPayForm.paymentMethod} onChange={(e) => setBookingPayForm({ ...bookingPayForm, paymentMethod: e.target.value })}
                  className="w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-4 py-3 text-sm text-[var(--text-primary)]">
                  {PAYMENT_METHODS.map((m) => <option key={m.value} value={m.value}>{m.label}</option>)}
                </AppSelect>
              </label>
              <label className="flex flex-col gap-1.5 text-sm font-medium text-[var(--text-secondary)]">
                <span>Received In Account</span>
                <AppSelect required value={bookingPayForm.financeAccountId} onChange={(e) => setBookingPayForm({ ...bookingPayForm, financeAccountId: e.target.value })}
                  className="w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-4 py-3 text-sm text-[var(--text-primary)]">
                  <option value="">Select an account…</option>
                  {financeAccounts.map((a) => <option key={a.id} value={a.id}>{a.name} — {a.accountHolderName}</option>)}
                </AppSelect>
              </label>
              <Field label="Reference (optional)" value={bookingPayForm.paymentReference}
                onChange={(e) => setBookingPayForm({ ...bookingPayForm, paymentReference: e.target.value })} />
              <Field label="Payment Date" type="date" value={bookingPayForm.paidAt}
                onChange={(e) => setBookingPayForm({ ...bookingPayForm, paidAt: e.target.value })} />
              <Field label="Notes (optional)" value={bookingPayForm.notes}
                onChange={(e) => setBookingPayForm({ ...bookingPayForm, notes: e.target.value })} />

              <div className="flex gap-2">
                <Button type="submit" disabled={bookingPaySubmitting}>{bookingPaySubmitting ? "Recording..." : "Record Payment"}</Button>
                <Button type="button" variant="ghost" onClick={() => setShowBookingPay(false)} disabled={bookingPaySubmitting}>Cancel</Button>
              </div>
            </form>
          </div>
        </div>
      )}

    </Container>
  );
}
