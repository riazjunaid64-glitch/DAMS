import { can } from "../features/access/permissions.ts";
import AppSelect from "../lib/AppSelect.tsx";
import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { api } from "../api/api.ts";
import type { User } from "../App.tsx";
import Button from "../lib/Button.tsx";
import Container from "../lib/Container.tsx";
import Field from "../lib/Field.tsx";
import { Button as UiButton, DatePicker, Notice, Tabs, useIsPhone } from "../components/ui";
import BookingCommissionRebatePanel from "../features/commissionRebates/BookingCommissionRebatePanel.tsx";
import { EmptyState, PanelCard, StatCard, TabPanel } from "../features/bookings/ui.tsx";
import { Icons, th } from "../features/bookings/tokens.tsx";
import { missingReadMessages, readBookingDetail, type WantedReads } from "../features/bookings/detailReads.ts";
import { createPanelRefreshSignal, type PanelRefreshSignal } from "../features/bookings/refreshCoordination.ts";
import BookingCancellationPanel from "../features/bookingCancellation/BookingCancellationPanel.tsx";
import CancellationDialog, { type CancellationDialogHandle } from "../features/bookingCancellation/CancellationDialog.tsx";
import { BookingHeader } from "../features/bookings/BookingHeader.tsx";
import { BookingSummary } from "../features/bookings/BookingSummary.tsx";
import { CompleteSaleDialog } from "../features/bookings/CompleteSaleDialog.tsx";
import type {
  BookingDetail, BookingPayment, FinanceAccountOption, InstallmentSchedule, ScheduleItem,
} from "../features/bookings/detailTypes.ts";
import type { MainActionKind } from "../features/bookings/headerActions.ts";
import { PossessionDialog } from "../features/bookings/PossessionDialog.tsx";
import { RecordPaymentDialog, type PaymentTarget } from "../features/bookings/RecordPaymentDialog.tsx";
import { TermsDialog } from "../features/bookings/TermsDialog.tsx";
import { pakistanToday } from "../lib/financePeriods.ts";

type Props = { user: User | null };

type Dialog =
  | { kind: "terms" }
  | { kind: "pay"; target: PaymentTarget }
  | { kind: "possession" }
  | { kind: "complete" }
  | null;

const FREQUENCIES = [
  { value: "Monthly", label: "Monthly" },
  { value: "Quarterly", label: "Quarterly" },
  { value: "HalfYearly", label: "Half-Yearly" },
  { value: "Yearly", label: "Yearly" },
];

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

function formatMoney(n: number) {
  return n.toLocaleString(undefined, { minimumFractionDigits: 0, maximumFractionDigits: 2 });
}

function formatDate(iso: string) {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleDateString();
}

function toDateInput(iso?: string | null) {
  if (!iso) return "";
  return iso.slice(0, 10);
}

export default function BookingDetailPage({ user }: Props) {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const isPhone = useIsPhone();
  const bookingId = Number(id);

  const [booking, setBooking] = useState<BookingDetail | null>(null);
  // NULL means "not known", never "none" — for both of these. Each is loaded by its own request and
  // either can fail on its own, so a failure must read as a failure. Defaulting a missing list to
  // empty is what let "No payments recorded yet" mean "the payments could not be fetched".
  const [schedule, setSchedule] = useState<InstallmentSchedule | null>(null);
  const [payments, setPayments] = useState<BookingPayment[] | null>(null);
  // Non-cash rebate credits reduce what the customer owes without any payment recording it. Served by
  // the booking itself, so it is known whenever the page has a booking at all.
  const [rebateCredits, setRebateCredits] = useState<number | null>(null);
  // Which of the dependent reads failed on the last load. Held per resource so the tab that owns
  // one can say so and offer a retry, instead of every tab showing the same page-level message.
  const [scheduleError, setScheduleError] = useState<string | null>(null);
  const [paymentsError, setPaymentsError] = useState<string | null>(null);
  // The booking read's OWN freshness, kept apart from `error`: that one also carries the failure of
  // every mutation on this page, so it cannot answer "are these figures current?".
  const [bookingError, setBookingError] = useState<string | null>(null);
  const [accountsError, setAccountsError] = useState<string | null>(null);
  // Reloads overlap — a payment's reload and a manual retry can be in flight together — and the
  // slower one must not put its older answers on screen after the faster one.
  const loadSequence = useRef(0);
  // ...but discarding a superseded reload's ANSWERS must not also discard its message to the panel.
  // Constructed eagerly so the ref is never null and nothing below depends on narrowing holding
  // across a closure boundary. useRef keeps only the first one; the rest are three closures a
  // render, which is not worth a lazy-init dance on a page that re-renders on typing.
  const panelRefresh = useRef<PanelRefreshSignal>(createPanelRefreshSignal());
  const cancelDialog = useRef<CancellationDialogHandle>(null);
  const [activeTab, setActiveTab] = useState("summary");
  // Every tab opened so far. A panel is rendered from its first visit and then kept mounted, so a
  // half-typed commission or a loaded audit page survives a trip to another tab — and a tab's data
  // is only read once the tab has been opened.
  const [visitedTabs, setVisitedTabs] = useState<string[]>(["summary"]);
  const selectTab = useCallback((id: string) => {
    setActiveTab(id);
    setVisitedTabs((prev) => (prev.includes(id) ? prev : [...prev, id]));
  }, []);
  const wantSchedule = visitedTabs.includes("plan") || visitedTabs.includes("commission");
  const wantPayments = visitedTabs.includes("payments");
  const wanted = useRef<WantedReads>({ schedule: false, payments: false });
  useEffect(() => {
    wanted.current = { schedule: wantSchedule, payments: wantPayments };
  }, [wantSchedule, wantPayments]);
  // Bumped on every completed reload. The Commission & Rebate panel stays mounted behind the other
  // tabs, so this is how it learns that a payment or a plan change has moved the booking under it.
  const [dataVersion, setDataVersion] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [showRegenerateConfirm, setShowRegenerateConfirm] = useState(false);
  const [dialog, setDialog] = useState<Dialog>(null);

  // Which account the money lands in. Every payment must name one, so the account
  // balances and the Finance dashboard's per-account view can be trusted.
  const [financeAccounts, setFinanceAccounts] = useState<FinanceAccountOption[]>([]);

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

  const canUseBookings = can(user?.role, "bookings");

  // Declared here, above the handlers, because a plan action has to check the same freshness the
  // button that opened it did.
  //
  // A stale schedule must not drive actions. Generating or collecting against installment ids that
  // were read before the last failed refresh is how an operator ends up recording money against a
  // row the server has since replaced, so both are withdrawn until the schedule reloads.
  const scheduleIsFresh = schedule !== null && scheduleError === null;
  // The booking carries the status, the agreed terms, the booking-amount balance and the rebate
  // credits — every prerequisite a payment is checked against. A failed reload leaves all of them
  // describing the booking as it was, so money actions are withdrawn exactly as they are for a
  // failed schedule read.
  const bookingIsFresh = bookingError === null;

  // notifyPanel=false when the Commission & Rebate panel itself asked for this reload: its mutation
  // already answered with the whole workspace, so bumping the token only makes it re-read what it
  // has. Everything else — a payment, a plan change, a status transition, an explicit retry — moved
  // the booking under a panel that has no other way to find out.
  const load = useCallback(async (notifyPanel = true) => {
    if (!bookingId) return;
    const request = ++loadSequence.current;
    // Recorded at the START, because a reload that turns out to have been superseded returns
    // without delivering anything — and the message it was carrying must not go with it. See
    // createPanelRefreshSignal for the sequence that used to lose it.
    panelRefresh.current.reloadStarted(notifyPanel);
    setLoading(true);
    setError(null);
    try {
      // Every read stands or falls on its own, INCLUDING the booking — see detailReads.
      const reads = await readBookingDetail<BookingDetail, InstallmentSchedule, BookingPayment[]>(bookingId, api, wanted.current);
      // A newer reload started while this one was in flight; its answers are the current ones, and
      // applying these on top of them would put an older generation back on screen.
      if (request !== loadSequence.current) return;

      const b = reads.booking.value;
      if (b) {
        setBooking(b);
        // Read off the booking rather than from a second endpoint, so the one figure every balance on
        // this page depends on cannot be the one that failed to arrive.
        setRebateCredits(typeof b.rebateCredits === "number" ? b.rebateCredits : null);
        setBookingError(null);
      } else {
        // The rows already on screen stay as history, but nothing derived from them is current any
        // more — including the credits, which ride on this same response.
        setBookingError(reads.booking.error);
        setRebateCredits(null);
      }

      // The previous list is left alone on failure: showing yesterday's receipts under a visible
      // warning beats replacing them with a confident "no payments". A read nobody asked for has
      // neither a value nor an error and changes nothing.
      const paymentRows = reads.payments.value;
      if (paymentRows) {
        setPayments(paymentRows);
        setPaymentsError(null);
      } else if (reads.payments.error) {
        setPaymentsError(reads.payments.error);
      }

      const s = reads.schedule.value;
      if (s) {
        setScheduleError(null);
        setSchedule(s);
        if (!s.hasSchedule) {
          // Seeded from the booking, so it is only seeded when the booking arrived with it.
          if (b) {
            setForm((prev) => ({
              ...prev,
              agreedSalePrice: String(b.agreedSalePrice),
              discountPercent: String(b.discountPercent ?? 0),
              installmentStartDate: toDateInput(b.installmentPlanStartDate) || pakistanToday(),
            }));
          }
        } else {
          setForm((prev) => ({
            ...prev,
            agreedSalePrice: String(s.agreedSalePrice),
            discountPercent: String(s.discountPercent ?? 0),
            frequency: s.frequency ?? "Monthly",
            numberOfInstallments: String(s.numberOfInstallments ?? 12),
            installmentStartDate: toDateInput(s.installmentStartDate),
            possessionAmount: String(s.possessionAmount ?? 0),
            possessionDueDate: toDateInput(s.possessionDueDate),
          }));
        }
      } else if (reads.schedule.error) {
        setScheduleError(reads.schedule.error);
      }
      // Delivers whatever is owed, which may have been recorded by an earlier reload this one
      // superseded — that reload's answers are stale, its message is not.
      if (panelRefresh.current.deliver()) setDataVersion((v) => v + 1);
    } finally {
      // Only the reload still current owns the loading flag; a superseded one leaves it alone.
      if (request === loadSequence.current) setLoading(false);
    }
  }, [bookingId]);

  // The panel's own mutations answer with the workspace, so its reload of the page must not come
  // back at it as a fresh read of the same data.
  const handlePanelChanged = useCallback(() => { void load(false); }, [load]);

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
    if (canUseBookings) {
      load();
      loadFinanceAccounts();
    }
  }, [canUseBookings, load, loadFinanceAccounts]);

  // A tab loads its data the first time it is opened: the booking is always read, the schedule and
  // the payment list wait for the tab that shows them.
  useEffect(() => {
    if (!canUseBookings) return;
    const scheduleMissing = wantSchedule && schedule === null && scheduleError === null;
    const paymentsMissing = wantPayments && payments === null && paymentsError === null;
    if (scheduleMissing || paymentsMissing) void load(false);
  }, [canUseBookings, wantSchedule, wantPayments, schedule, scheduleError, payments, paymentsError, load]);

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

  const openInstallmentPay = (item: ScheduleItem) => setDialog({ kind: "pay", target: { kind: "installment", item } });
  const closeDialog = () => setDialog(null);

  const runMainAction = (kind: MainActionKind) => {
    if (!booking) return;
    if (kind === "recordPayment") setDialog({ kind: "pay", target: { kind: "bookingAmount", limit: booking.bookingAmountRemaining } });
    else setDialog({ kind: kind === "givePossession" ? "possession" : "complete" });
  };

  if (!canUseBookings) {
    return <Container className="py-16 text-center"><p className="text-[var(--text-muted)]">You do not have access to bookings.</p></Container>;
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
        <p className="text-rose-400">{bookingError ?? error ?? "Booking not found."}</p>
        <Button className="mt-4" variant="outline" onClick={() => navigate("/confirmed-bookings")}>Back</Button>
      </Container>
    );
  }

  // The backend decides this, not the status. canGenerate already allows PossessionGiven — a
  // recognised sale still has a receivable, and an installment is the only way DAMS collects one,
  // so hiding the form here stranded it with no route to payment. It also folds in the booking
  // amount and the regenerate rules, which a status check silently skipped.
  const canShowPlanForm = scheduleIsFresh && Boolean(schedule.canGenerate);
  const planLocked = scheduleIsFresh && schedule.hasSchedule && !schedule.canRegenerate;
  // Positive only when a credit the plan was built smaller by has been reversed. The payment service
  // refuses a receipt while it stands, because that receipt would pin the plan and strand the amount.
  const unscheduledBalance = scheduleIsFresh ? schedule.unscheduledBalance ?? 0 : 0;
  // Possession means the sale is recognised as revenue, and the backend refuses to restate the
  // terms it was recognised on. Showing them as editable would only produce a rejection.
  const termsFrozen = booking.status === "PossessionGiven";
  const isCancelled = booking.status === "Cancelled";
  // A tab's data is read when the tab is first opened, so for a moment it is neither loaded nor failed.
  const scheduleLoading = schedule === null && scheduleError === null;
  const paymentsLoading = payments === null && paymentsError === null;

  const missingReads = missingReadMessages({
    bookingError,
    creditsMissing: rebateCredits === null,
    accountsError,
    paymentsError,
    scheduleError,
  });

  return (
    <div className="mx-auto flex w-full max-w-[1500px] flex-col gap-4 px-4 py-5 md:gap-5 md:px-8 md:py-7">
      <BookingHeader
        booking={booking}
        stale={!bookingIsFresh}
        onBack={() => navigate("/confirmed-bookings")}
        onPrint={() => navigate(`/application-form?bookingId=${booking.id}`)}
        onCancel={() => cancelDialog.current?.open()}
        onMain={runMainAction}
      />

      {/* Page-level, so a failure raised on one tab is not hidden by switching to another. */}
      {error && <Notice tone="red" role="alert" title={error} />}

      {/* Says which figures are missing and why, rather than letting them quietly read zero. One
          banner listing every read that failed, because they share one Retry. */}
      {!loading && missingReads.length > 0 && (
        <Notice
          tone="orange"
          role="status"
          title={`${missingReads.join(" ")} The figures that depend on ${missingReads.length > 1 ? "them" : "it"} are shown as “Unavailable” rather than as zero.`}
          action={<UiButton size="sm" variant="outline" onClick={() => void reload()}>Retry</UiButton>}
        />
      )}

      <Tabs
        aria-label="Booking sections"
        phoneDropdownFrom={99}
        value={activeTab}
        onChange={selectTab}
        items={[
          { id: "summary", label: "Summary" },
          { id: "plan", label: isPhone ? "Plan" : "Installment plan", count: !isPhone && booking.installmentsTotal > 0 ? booking.installmentsTotal : undefined },
          { id: "payments", label: "Payments", count: !isPhone && booking.payments.length > 0 ? booking.payments.length : undefined },
          { id: "commission", label: isPhone ? "Commission" : "Commission & rebate" },
        ]}
      />

      <TabPanel id="summary" active={activeTab} visited={visitedTabs}>
        <BookingSummary
          booking={booking}
          leadLink={can(user?.role, "crm")}
          onSetTerms={() => setDialog({ kind: "terms" })}
          onEditTerms={() => setDialog({ kind: "terms" })}
          settlement={
            // Cancellation settlement lives on the Summary rather than above the tabs: it is a summary
            // of what a cancelled booking owes, and the header badge already says it is cancelled.
            <BookingCancellationPanel
              bookingId={bookingId}
              status={booking.status}
              settlement={booking.cancellationSettlement}
              financeAccounts={financeAccounts}
              onChanged={load}
            />
          }
        />
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
                <DatePicker label="Installment Start Date" required
                  value={form.installmentStartDate} disabled={planLocked}
                  onChange={(installmentStartDate) => setForm({ ...form, installmentStartDate })} />
                <Field label="Possession Amount (optional)" type="number" min="0" step="0.01"
                  value={form.possessionAmount} disabled={planLocked}
                  onChange={(e) => setForm({ ...form, possessionAmount: e.target.value })} />
                {Number(form.possessionAmount) > 0 && (
                  <DatePicker label="Possession Due Date" required
                    value={form.possessionDueDate} disabled={planLocked}
                    onChange={(possessionDueDate) => setForm({ ...form, possessionDueDate })} />
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
                    // scheduleIsFresh for the same reason the plan form uses it, and it is needed
                    // HERE too: a failed refresh leaves the previous schedule on screen, so this
                    // branch still renders — rows, amounts and installment ids all as they were
                    // before whatever broke the reload. Collecting against one of those ids is how
                    // money gets recorded against a row the server has since replaced. The banner
                    // at the top of the page already says the read failed and offers a retry.
                    const isPayable = scheduleIsFresh && bookingIsFresh
                      && (booking.status === "PaymentPlanActive" || booking.status === "PossessionGiven")
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
                          <Button size="sm" variant="outline" onClick={() => openInstallmentPay(item)}>Record Payment</Button>
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
              {scheduleLoading ? (
                <p className="px-6 py-14 text-center text-sm text-[var(--text-muted)]">Loading...</p>
              ) : (
                <EmptyState
                  message="No installment schedule yet."
                  hint={canShowPlanForm
                    ? "Set the plan terms above and generate the schedule."
                    : "The booking amount must be fully received before the installment plan unlocks."}
                />
              )}
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
                value={`Rs ${formatMoney(booking.collected)}`} />
              {/* The server nets rebate credits off: they settle the balance without any cash arriving,
                  so leaving them out would report the customer owing money a rebate has already cleared. */}
              <StatCard
                tone={isCancelled ? "rose" : "sky"}
                icon={<Icons.doc />}
                label={isCancelled ? "Sale Obligation" : "Outstanding Amount"}
                value={isCancelled ? "Cancelled" : `Rs ${formatMoney(booking.outstanding)}`} />
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
              {paymentsLoading ? (
                <p className="px-6 py-14 text-center text-sm text-[var(--text-muted)]">Loading...</p>
              ) : (
                <EmptyState
                  message="No payments recorded yet."
                  hint="Receipts appear here as soon as the first payment is recorded."
                />
              )}
            </PanelCard>
          )}
        </div>
      </TabPanel>

      {/* ── Commission & Rebate ── */}
      <TabPanel id="commission" active={activeTab} visited={visitedTabs}>
        {/* A rebate credit changes the customer's balance, can settle installments and can move the
            booking's own status, so every other tab's figures go stale the moment one is applied.
            `handlePanelChanged` is stable (useCallback on load), so this cannot loop — and it
            reloads WITHOUT bumping the token, because the panel's mutation already answered with
            the workspace that a bump would only make it fetch again.
            The schedule is passed down rather than read twice: this page already holds it. */}
        {/* The panel measures its rebate disbursements against the schedule, so it waits for that read to
            settle (answer or failure) rather than opening on "not fresh" for the moment it takes. */}
        {schedule !== null || scheduleError !== null ? (
          <BookingCommissionRebatePanel
            bookingId={bookingId}
            onChanged={handlePanelChanged}
            refreshToken={dataVersion}
            installments={schedule?.items ?? []}
            installmentsFresh={scheduleIsFresh}
          />
        ) : (
          <p className="py-10 text-center text-sm text-[var(--text-muted)]">Loading...</p>
        )}
      </TabPanel>

      {dialog?.kind === "terms" && <TermsDialog booking={booking} onClose={closeDialog} onSaved={() => load()} />}
      {dialog?.kind === "pay" && (
        <RecordPaymentDialog
          booking={booking}
          target={dialog.target}
          financeAccounts={financeAccounts}
          accountsError={accountsError}
          onClose={closeDialog}
          onSaved={() => load()}
        />
      )}
      {dialog?.kind === "possession" && <PossessionDialog booking={booking} onClose={closeDialog} onSaved={() => load()} />}
      {dialog?.kind === "complete" && <CompleteSaleDialog booking={booking} onClose={closeDialog} onSaved={() => load()} />}

      <CancellationDialog
        ref={cancelDialog}
        bookingId={bookingId}
        status={booking.status}
        unitNumber={booking.unitNumber}
        financeAccounts={financeAccounts}
        onCancelled={load}
      />
    </div>
  );
}
