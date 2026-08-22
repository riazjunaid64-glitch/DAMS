import { useState, type FormEvent, type ReactNode } from "react";
import Button from "../../lib/Button";
import Field from "../../lib/Field";
import { bookingCancellationApi } from "./api";
import { computeRetained, idempotencyKey, isStaleCancellationError, money, pakistanToday, trapDialogKeys, validateCancellationDecision } from "./state";
import type { CancelBookingRequest, CancellationRefundDecision, RefundPaymentMethod } from "./types";

interface FinanceAccountOption {
  id: number;
  name: string;
  accountHolderName: string;
}

interface Props {
  bookingId: number;
  status: string;
  unitNumber: string;
  financeAccounts: FinanceAccountOption[];
  onCancelled: () => void | Promise<void>;
}

const PAYMENT_METHODS: { value: RefundPaymentMethod; label: string }[] = [
  { value: "Cash", label: "Cash" },
  { value: "BankTransfer", label: "Bank Transfer" },
  { value: "Cheque", label: "Cheque" },
  { value: "Online", label: "Online" },
];

export default function CancellationDialog({ bookingId, status, unitNumber, financeAccounts, onCancelled }: Props) {
  const [open, setOpen] = useState(false);
  const [preparing, setPreparing] = useState(false);
  // True only after a fresh booking fetch has actually SUCCEEDED for this dialog-open. The
  // financial form must never render against a stale or never-loaded snapshot — a failed refresh
  // has to block the form, not silently fall back to whatever cashReceived/concurrencyToken were
  // left over from a previous open (or their initial zero/empty defaults on a first-ever open).
  const [prepared, setPrepared] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [stale, setStale] = useState(false);

  // Captured once when the dialog opens and held for its lifetime, per the concurrency/staleness
  // rules: the Admin's decision is made against this snapshot, and the server independently
  // re-checks both before committing anything.
  const [concurrencyToken, setConcurrencyToken] = useState("");
  const [cashReceived, setCashReceived] = useState(0);
  const [key, setKey] = useState("");

  const [reason, setReason] = useState("");
  const [refundAmount, setRefundAmount] = useState("");
  const [decision, setDecision] = useState<CancellationRefundDecision | "">("");
  const [refundFinanceAccountId, setRefundFinanceAccountId] = useState("");
  const [refundPaymentMethod, setRefundPaymentMethod] = useState<RefundPaymentMethod>("Cash");
  const [refundPaymentReference, setRefundPaymentReference] = useState("");
  const [refundPaidAt, setRefundPaidAt] = useState(pakistanToday());
  const [refundNotes, setRefundNotes] = useState("");

  const canCancel = status !== "Cancelled" && status !== "PossessionGiven" && status !== "SaleCompleted";
  if (!canCancel) return null;

  const openDialog = () => {
    setError(null);
    setStale(false);
    setPrepared(false);
    // Invalidate any snapshot left over from a previous open (or the initial zero/empty defaults
    // on a first-ever open) — the fresh fetch below is the only thing allowed to make the form
    // usable again.
    setCashReceived(0);
    setConcurrencyToken("");
    setOpen(true);
    // One key per cancellation attempt — kept across retries of THIS operation, only replaced
    // when the dialog is (re)opened for a fresh attempt.
    setKey(idempotencyKey("cancel"));
    setReason("");
    setRefundAmount("");
    setDecision("");
    setRefundFinanceAccountId(financeAccounts.length === 1 ? String(financeAccounts[0].id) : "");
    setRefundPaymentMethod("Cash");
    setRefundPaymentReference("");
    setRefundPaidAt(pakistanToday());
    setRefundNotes("");
    void loadFreshBooking();
  };

  const loadFreshBooking = async () => {
    setError(null);
    setPreparing(true);
    try {
      const fresh = await bookingCancellationApi.current(bookingId);
      setConcurrencyToken(fresh.concurrencyToken);
      setCashReceived(fresh.payments.reduce((sum, p) => sum + p.amount, 0));
      setPrepared(true);
    } catch (x) {
      setError(x instanceof Error ? x.message : "Could not load the current booking.");
      setPrepared(false);
    } finally {
      setPreparing(false);
    }
  };

  const close = () => { if (!submitting) setOpen(false); };

  // The Admin must pick one of the three options explicitly — picking "No refund" is the only
  // thing allowed to zero the amount out; typing/switching away from it clears a stale "0" so a
  // refund can't be submitted while still showing the no-refund amount.
  const chooseDecision = (next: CancellationRefundDecision) => {
    setDecision(next);
    if (next === "None") setRefundAmount("0");
    else if (refundAmount.trim() === "" || refundAmount === "0") setRefundAmount("");
  };

  const refundValue = Number(refundAmount) || 0;
  const retained = computeRetained(cashReceived, refundValue);
  const validationError = validateCancellationDecision({
    cashReceived, refundAmount: refundValue, decision,
    refundFinanceAccountId: refundFinanceAccountId ? Number(refundFinanceAccountId) : null,
    refundPaymentMethod: decision === "PayNow" ? refundPaymentMethod : null,
    refundPaymentReference,
  });
  const canSubmit = !preparing && !submitting && reason.trim().length > 0 && !validationError
    && (decision !== "PayNow" || (refundPaidAt.trim().length > 0));

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (!canSubmit) return;
    setSubmitting(true);
    setError(null);
    setStale(false);
    try {
      const payNow = decision === "PayNow";
      const body: CancelBookingRequest = {
        reason: reason.trim(),
        expectedCustomerCashReceived: cashReceived,
        refundAmount: refundValue,
        refundDecision: refundValue === 0 ? "None" : (decision as CancellationRefundDecision),
        idempotencyKey: key,
        concurrencyToken,
        refundFinanceAccountId: payNow ? Number(refundFinanceAccountId) : null,
        refundPaymentMethod: payNow ? refundPaymentMethod : null,
        refundPaymentReference: payNow ? refundPaymentReference.trim() || null : null,
        refundPaidAt: payNow ? new Date(refundPaidAt).toISOString() : null,
        // Sent regardless of decision — this is a general cancellation note, not a refund-payout
        // note, so it must not be silently dropped just because there's no payout to attach it to.
        refundNotes: refundNotes.trim() || null,
      };
      await bookingCancellationApi.cancel(bookingId, body);
      setOpen(false);
      await onCancelled();
    } catch (x) {
      const message = x instanceof Error ? x.message : "Failed to cancel booking.";
      setError(message);
      if (isStaleCancellationError(message)) setStale(true);
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <>
      <Button variant="danger" size="sm" onClick={() => void openDialog()}>Cancel Booking</Button>
      {open && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
          <button aria-label="Close dialog" className="absolute inset-0 bg-black/70" onClick={close} />
          <section
            role="dialog" aria-modal="true" aria-labelledby="cancel-booking-title"
            autoFocus tabIndex={-1} onKeyDown={(e) => trapDialogKeys(e, close)}
            className="relative max-h-[90dvh] w-full max-w-lg overflow-y-auto rounded-2xl border border-[var(--border)] bg-[var(--modal-bg)] p-6 shadow-xl"
          >
            <div className="mb-1 flex items-start justify-between gap-3">
              <h3 id="cancel-booking-title" className="text-lg font-semibold text-[var(--text-heading)]">Cancel Booking</h3>
              <button aria-label="Close" onClick={close} className="text-[var(--text-muted)]">✕</button>
            </div>
            <p className="text-sm text-[var(--text-muted)]">
              This releases unit {unitNumber} back to the market. Cancellation cannot be undone.
            </p>

            {preparing && <p className="mt-4 text-sm text-[var(--text-muted)]">Loading current booking…</p>}

            {error && (
              <div role="alert" className="mt-4 rounded-xl border border-rose-500/20 bg-rose-500/10 px-4 py-3 text-sm text-rose-400">
                {error}
                {stale && <p className="mt-1 text-xs">Close this dialog and open Cancel Booking again to review the latest figures.</p>}
                {!preparing && !prepared && (
                  <div className="mt-3 flex gap-2">
                    <Button type="button" size="sm" onClick={() => void loadFreshBooking()}>Retry</Button>
                    <Button type="button" size="sm" variant="ghost" onClick={close}>Close</Button>
                  </div>
                )}
              </div>
            )}

            {!preparing && !stale && prepared && (
              <form onSubmit={submit} className="mt-4 grid gap-4">
                <Metric label="Customer cash received" value={money(cashReceived)} />

                {cashReceived <= 0 ? (
                  <>
                    <Field label="Cancellation Reason" required value={reason} onChange={(e) => setReason(e.target.value)} />
                    <Field as="textarea" label="Notes (optional)" value={refundNotes} onChange={(e) => setRefundNotes(e.target.value)} />
                  </>
                ) : (
                  <>
                    <fieldset className="grid gap-2">
                      <legend className="text-sm font-medium text-[var(--text-secondary)]">Refund decision</legend>
                      <label className="flex items-center gap-2 text-sm text-[var(--text-secondary)]">
                        <input type="radio" name="refund-decision" checked={decision === "None"} onChange={() => chooseDecision("None")} />
                        No refund — retain the full amount
                      </label>
                      <label className="flex items-center gap-2 text-sm text-[var(--text-secondary)]">
                        <input type="radio" name="refund-decision" checked={decision === "PayNow"} onChange={() => chooseDecision("PayNow")} />
                        Refund now
                      </label>
                      <label className="flex items-center gap-2 text-sm text-[var(--text-secondary)]">
                        <input type="radio" name="refund-decision" checked={decision === "PayLater"} onChange={() => chooseDecision("PayLater")} />
                        Record refund as payable / pay later
                      </label>
                    </fieldset>

                    {(decision === "PayNow" || decision === "PayLater") && (
                      <>
                        <Field label="Refund to customer" type="number" min={0.01} max={cashReceived} step="0.01" required
                          value={refundAmount} onChange={(e) => setRefundAmount(e.target.value)} />
                        <Metric label="Company will retain" value={money(retained)} />
                      </>
                    )}

                    {decision === "None" && <Metric label="Company will retain" value={money(cashReceived)} />}

                    {refundValue > 0 && decision === "PayLater" && (
                      <p className="rounded-xl border border-[var(--border)] bg-[var(--surface-glass-hover)] px-4 py-3 text-xs text-[var(--text-muted)]">
                        The refund will be recorded as payable. Cash will not leave an account until the refund is marked as paid.
                      </p>
                    )}

                    {refundValue > 0 && decision === "PayNow" && (
                      <>
                        <label className="flex flex-col gap-1.5 text-sm font-medium text-[var(--text-secondary)]">
                          <span>Refund From Account</span>
                          <select required value={refundFinanceAccountId} onChange={(e) => setRefundFinanceAccountId(e.target.value)}
                            className="w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-4 py-3 text-sm text-[var(--text-primary)]">
                            <option value="">Select an account…</option>
                            {financeAccounts.map((a) => <option key={a.id} value={a.id}>{a.name} — {a.accountHolderName}</option>)}
                          </select>
                        </label>
                        <label className="flex flex-col gap-1.5 text-sm font-medium text-[var(--text-secondary)]">
                          <span>Payment Method</span>
                          <select value={refundPaymentMethod} onChange={(e) => setRefundPaymentMethod(e.target.value as RefundPaymentMethod)}
                            className="w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-4 py-3 text-sm text-[var(--text-primary)]">
                            {PAYMENT_METHODS.map((m) => <option key={m.value} value={m.value}>{m.label}</option>)}
                          </select>
                        </label>
                        <Field label="Payment Reference" required={refundPaymentMethod !== "Cash"} value={refundPaymentReference}
                          onChange={(e) => setRefundPaymentReference(e.target.value)} />
                        <Field label="Refund Date" type="date" required value={refundPaidAt}
                          onChange={(e) => setRefundPaidAt(e.target.value)} />
                      </>
                    )}

                    <Field label="Cancellation Reason" required value={reason} onChange={(e) => setReason(e.target.value)} />
                    <Field as="textarea" label="Notes (optional)" value={refundNotes} onChange={(e) => setRefundNotes(e.target.value)} />
                  </>
                )}

                <div className="rounded-xl border border-[var(--border)] bg-[var(--surface-glass-hover)] px-4 py-3 text-sm text-[var(--text-secondary)]">
                  <p>Customer paid <strong>{money(cashReceived)}</strong></p>
                  <p>Refund <strong>{money(refundValue)}</strong></p>
                  <p>Company retains <strong>{money(retained)}</strong></p>
                  <p>Refund timing <strong>{decision === "None" ? "No refund" : decision === "PayNow" ? "Pay now" : decision === "PayLater" ? "Pay later" : "—"}</strong></p>
                  <p>Unit released <strong>{unitNumber}</strong></p>
                </div>

                {validationError && (
                  <p role="alert" className="text-xs text-rose-400">{validationError}</p>
                )}

                <div className="flex gap-2">
                  <Button type="submit" variant="danger" disabled={!canSubmit}>
                    {submitting ? "Cancelling…" : "Cancel Booking & Save Settlement"}
                  </Button>
                  <Button type="button" variant="ghost" onClick={close} disabled={submitting}>Keep Booking</Button>
                </div>
              </form>
            )}
          </section>
        </div>
      )}
    </>
  );
}

function Metric({ label, value }: { label: string; value: ReactNode }) {
  return (
    <div className="rounded-xl border border-[var(--border)] bg-[var(--surface-glass-hover)] px-4 py-3">
      <p className="text-xs text-[var(--text-muted)]">{label}</p>
      <p className="mt-0.5 text-base font-semibold text-[var(--text-heading)]">{value}</p>
    </div>
  );
}
