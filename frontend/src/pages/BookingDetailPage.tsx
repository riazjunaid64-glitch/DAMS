import { can } from "../features/access/permissions.ts";
import { useCallback, useEffect, useRef, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { api } from "../api/api.ts";
import type { User } from "../App.tsx";
import Button from "../lib/Button.tsx";
import Container from "../lib/Container.tsx";
import { Button as UiButton, Notice, Tabs, useIsPhone } from "../components/ui";
import BookingCommissionRebatePanel from "../features/commissionRebates/BookingCommissionRebatePanel.tsx";
import { TabPanel } from "../features/bookings/ui.tsx";
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
import { PaymentsTab } from "../features/bookings/PaymentsTab.tsx";
import { PlanDialog } from "../features/bookings/PlanDialog.tsx";
import { PlanTab } from "../features/bookings/PlanTab.tsx";
import { PossessionDialog } from "../features/bookings/PossessionDialog.tsx";
import { RecordPaymentDialog, type PaymentTarget } from "../features/bookings/RecordPaymentDialog.tsx";
import { TermsDialog } from "../features/bookings/TermsDialog.tsx";

type Props = { user: User | null };

type Dialog =
  | { kind: "terms" }
  | { kind: "plan" }
  | { kind: "pay"; target: PaymentTarget }
  | { kind: "possession" }
  | { kind: "complete" }
  | null;

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
  const [dialog, setDialog] = useState<Dialog>(null);

  // Which account the money lands in. Every payment must name one, so the account
  // balances and the Finance dashboard's per-account view can be trusted.
  const [financeAccounts, setFinanceAccounts] = useState<FinanceAccountOption[]>([]);

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

  const openInstallmentPay = (item: ScheduleItem) => setDialog({ kind: "pay", target: { kind: "installment", item } });
  const closeDialog = () => setDialog(null);
  const openReceipt = (payment: BookingPayment) => navigate(`/receipt/${bookingId}/${payment.id}`);

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

  // A tab's data is read when the tab is first opened, so for a moment it is neither loaded nor failed.
  // Nothing acts on the schedule unless both it and the booking it was measured against are current.
  const planFresh = scheduleIsFresh && bookingIsFresh;

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

      <TabPanel id="plan" active={activeTab} visited={visitedTabs}>
        <PlanTab
          booking={booking}
          schedule={schedule}
          scheduleError={scheduleError}
          fresh={planFresh}
          onCreate={() => setDialog({ kind: "plan" })}
          onChange={() => setDialog({ kind: "plan" })}
          onRecord={openInstallmentPay}
          onRetry={() => void reload()}
        />
      </TabPanel>

      <TabPanel id="payments" active={activeTab} visited={visitedTabs}>
        <PaymentsTab
          booking={booking}
          payments={payments}
          paymentsError={paymentsError}
          onOpenReceipt={openReceipt}
          onChanged={() => void load(false)}
          onRetry={() => void reload()}
        />
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
      {dialog?.kind === "plan" && <PlanDialog booking={booking} schedule={schedule} onClose={closeDialog} onSaved={() => load()} />}
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
