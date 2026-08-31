import AppSelect from "../../lib/AppSelect.tsx";
import { useState, type FormEvent } from "react";
import Button from "../../lib/Button";
import Field from "../../lib/Field";
import { bookingCancellationApi } from "./api";
import { idempotencyKey, money, pakistanToday, refundDecisionLabel, refundStatusLabel, trapDialogKeys } from "./state";
import type { CancellationSettlement, PayCancellationRefundRequest, RefundPaymentMethod } from "./types";

interface FinanceAccountOption {
  id: number;
  name: string;
  accountHolderName: string;
}

interface Props {
  bookingId: number;
  status: string;
  settlement: CancellationSettlement | null | undefined;
  financeAccounts: FinanceAccountOption[];
  onChanged: () => void | Promise<void>;
}

const PAYMENT_METHODS: { value: RefundPaymentMethod; label: string }[] = [
  { value: "Cash", label: "Cash" },
  { value: "BankTransfer", label: "Bank Transfer" },
  { value: "Cheque", label: "Cheque" },
  { value: "Online", label: "Online" },
];

export default function BookingCancellationPanel({ bookingId, status, settlement, financeAccounts, onChanged }: Props) {
  const [showPay, setShowPay] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [key, setKey] = useState("");
  const [financeAccountId, setFinanceAccountId] = useState("");
  const [paymentMethod, setPaymentMethod] = useState<RefundPaymentMethod>("Cash");
  const [paymentReference, setPaymentReference] = useState("");
  const [paidAt, setPaidAt] = useState(pakistanToday());
  const [notes, setNotes] = useState("");

  if (status !== "Cancelled") return null;

  const openPay = () => {
    setError(null);
    setKey(idempotencyKey("cancel-refund"));
    setFinanceAccountId(financeAccounts.length === 1 ? String(financeAccounts[0].id) : "");
    setPaymentMethod("Cash");
    setPaymentReference("");
    setPaidAt(pakistanToday());
    setNotes("");
    setShowPay(true);
  };

  const close = () => { if (!submitting) setShowPay(false); };

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (!financeAccountId) { setError("Select the account the refund is paid from."); return; }
    setSubmitting(true);
    setError(null);
    try {
      const body: PayCancellationRefundRequest = {
        financeAccountId: Number(financeAccountId),
        paymentMethod,
        paymentReference: paymentReference.trim() || null,
        paidAt: new Date(paidAt).toISOString(),
        notes: notes.trim() || null,
        idempotencyKey: key,
      };
      await bookingCancellationApi.payRefund(bookingId, body);
      setShowPay(false);
      await onChanged();
    } catch (x) {
      setError(x instanceof Error ? x.message : "Failed to pay the refund.");
    } finally {
      setSubmitting(false);
    }
  };

  if (!settlement) {
    return (
      <section className="mt-8 rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] p-5 sm:p-6">
        <h2 className="text-lg font-semibold text-[var(--text-heading)]">Cancellation Settlement</h2>
        <p className="mt-2 text-sm text-[var(--text-muted)]">
          Legacy cancellation — structured settlement details were not recorded for this booking.
        </p>
      </section>
    );
  }

  const refund = settlement.refund;

  return (
    <section className="mt-8 rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] p-5 sm:p-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="text-lg font-semibold text-[var(--text-heading)]">Cancellation Settlement</h2>
          <p className="mt-1 text-sm text-[var(--text-muted)]">
            Cancelled {new Date(settlement.cancelledAt).toLocaleDateString()} by {settlement.cancelledByName}
          </p>
          <p className="mt-1 text-sm text-[var(--text-secondary)]">{settlement.reason}</p>
          {settlement.notes && <p className="mt-1 text-sm text-[var(--text-muted)]">Notes: {settlement.notes}</p>}
        </div>
      </div>

      {error && <p role="alert" className="mt-4 rounded-xl bg-rose-500/10 p-3 text-sm text-rose-300">{error}</p>}

      <div className="mt-5 grid gap-3 sm:grid-cols-4">
        <Metric label="Paid to date" value={money(settlement.customerCashReceivedSnapshot)} />
        <Metric label="Refund" value={money(settlement.refundAmount)} />
        <Metric label="Retained" value={money(settlement.retainedAmount)} />
        <Metric label="Refund status" value={refundStatusLabel(settlement.refundStatus)} />
      </div>
      <p className="mt-3 text-xs text-[var(--text-muted)]">
        Original decision: {refundDecisionLabel(settlement.refundDecision)}
      </p>

      {refund ? (
        <div className="mt-4 grid gap-3 sm:grid-cols-4 rounded-xl border border-[var(--border)] bg-[var(--surface-glass-hover)] p-4">
          <Metric label="Paid from" value={refund.financeAccountName} />
          <Metric label="Method" value={refund.paymentMethod} />
          <Metric label="Reference" value={refund.paymentReference ?? "—"} />
          <Metric label="Paid on" value={new Date(refund.paidAt).toLocaleDateString()} />
        </div>
      ) : settlement.refundStatus === "Pending" ? (
        <div className="mt-4">
          <Button size="sm" onClick={openPay}>Pay Refund</Button>
        </div>
      ) : null}

      {showPay && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
          <button aria-label="Close dialog" className="absolute inset-0 bg-black/70" onClick={close} />
          <section
            role="dialog" aria-modal="true" aria-labelledby="pay-refund-title"
            autoFocus tabIndex={-1} onKeyDown={(e) => trapDialogKeys(e, close)}
            className="relative w-full max-w-md rounded-2xl border border-[var(--border)] bg-[var(--modal-bg)] p-6 shadow-xl"
          >
            <div className="mb-1 flex items-start justify-between gap-3">
              <h3 id="pay-refund-title" className="text-lg font-semibold text-[var(--text-heading)]">Pay Refund</h3>
              <button aria-label="Close" onClick={close} className="text-[var(--text-muted)]">✕</button>
            </div>
            <p className="text-sm text-[var(--text-muted)]">Amount is fixed by the cancellation settlement and cannot be changed here.</p>

            <form onSubmit={submit} className="mt-4 grid gap-4">
              <Metric label="Refund amount" value={money(settlement.refundAmount)} />
              <label className="flex flex-col gap-1.5 text-sm font-medium text-[var(--text-secondary)]">
                <span>Refund From Account</span>
                <AppSelect required value={financeAccountId} onChange={(e) => setFinanceAccountId(e.target.value)}
                  className="w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-4 py-3 text-sm text-[var(--text-primary)]">
                  <option value="">Select an account…</option>
                  {financeAccounts.map((a) => <option key={a.id} value={a.id}>{a.name} — {a.accountHolderName}</option>)}
                </AppSelect>
              </label>
              <label className="flex flex-col gap-1.5 text-sm font-medium text-[var(--text-secondary)]">
                <span>Payment Method</span>
                <AppSelect value={paymentMethod} onChange={(e) => setPaymentMethod(e.target.value as RefundPaymentMethod)}
                  className="w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-4 py-3 text-sm text-[var(--text-primary)]">
                  {PAYMENT_METHODS.map((m) => <option key={m.value} value={m.value}>{m.label}</option>)}
                </AppSelect>
              </label>
              <Field label="Payment Reference" required={paymentMethod !== "Cash"} value={paymentReference}
                onChange={(e) => setPaymentReference(e.target.value)} />
              <Field label="Refund Date" type="date" required value={paidAt} onChange={(e) => setPaidAt(e.target.value)} />
              <Field as="textarea" label="Notes (optional)" value={notes} onChange={(e) => setNotes(e.target.value)} />

              <div className="flex gap-2">
                <Button type="submit" disabled={submitting}>{submitting ? "Paying…" : "Confirm Payment"}</Button>
                <Button type="button" variant="ghost" onClick={close} disabled={submitting}>Cancel</Button>
              </div>
            </form>
          </section>
        </div>
      )}
    </section>
  );
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-xl border border-[var(--border)] bg-[var(--surface)] p-3">
      <p className="text-[10px] uppercase tracking-wide text-[var(--text-muted)]">{label}</p>
      <p className="mt-1 font-semibold text-[var(--text-heading)]">{value}</p>
    </div>
  );
}
