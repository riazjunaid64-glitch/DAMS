import { useId, useState, type FormEvent } from "react";
import { AttachProof, DatePicker, Dropdown, Modal, Notice, NumberField, TextArea, TextField, useToast } from "../../components/ui";
import { formatDay } from "../../lib/dates.ts";
import { pakistanToday } from "../../lib/financePeriods.ts";
import { useIdempotencyKeys } from "../../lib/idempotency.ts";
import { formatPkr } from "../../utils/currency.ts";
import { useProofUpload } from "../proof/useProofUpload.ts";
import { bookingApi } from "./bookingApi.ts";
import type { BookingDetail, FinanceAccountOption, ScheduleItem } from "./detailTypes.ts";
import { DialogTitle } from "./DialogTitle.tsx";
import { PAYMENT_METHODS, paymentErrors, referenceRequired, type PaymentErrors } from "./paymentForm.ts";

export type PaymentTarget =
  /** The booking amount, from the header of an Awaiting booking; `limit` is what is still due. */
  | { kind: "bookingAmount"; limit: number }
  /** One installment, from its row in the plan tab; the amount starts at what is left on it. */
  | { kind: "installment"; item: ScheduleItem };

type Props = {
  booking: Pick<BookingDetail, "id" | "bookingReference">;
  target: PaymentTarget;
  financeAccounts: FinanceAccountOption[];
  /** Set when the account list could not be loaded, so the account field can say why it is empty. */
  accountsError: string | null;
  onClose: () => void;
  /** Reloads the booking once the payment is saved, before the popup closes. */
  onSaved: () => Promise<void> | void;
};

/**
 * Record booking amount / Record payment. One popup for both: they take the same fields and differ
 * only in what the amount may be. The proof file is optional and goes up after the payment is saved,
 * so a failed upload never undoes the money.
 */
export function RecordPaymentDialog({ booking, target, financeAccounts, accountsError, onClose, onSaved }: Props) {
  const toast = useToast();
  const keys = useIdempotencyKeys();
  const proof = useProofUpload();
  const formId = useId();
  const installment = target.kind === "installment" ? target.item : null;
  const limit = target.kind === "installment" ? target.item.remainingBalance : target.limit;

  const [fields, setFields] = useState({
    amount: String(limit > 0 ? limit : ""),
    method: "Cash",
    accountId: financeAccounts.length === 1 ? String(financeAccounts[0]!.id) : "",
    reference: "",
    paidAt: pakistanToday(),
  });
  const [notes, setNotes] = useState("");
  const [shown, setShown] = useState<PaymentErrors>({});
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // Set once the payment is saved but its proof did not go up: the popup stays open on the proof alone
  // so the file can be sent again, and nothing else can be submitted a second time.
  const [savedPaymentId, setSavedPaymentId] = useState<number | null | undefined>(undefined);
  const proofPending = savedPaymentId !== undefined;
  const set = (change: Partial<typeof fields>) => {
    setFields((current) => ({ ...current, ...change }));
    // A field's message goes as soon as it is edited, so it never lingers beside a fixed value.
    setShown((current) => {
      const next = { ...current };
      for (const key of Object.keys(change)) delete next[key as keyof PaymentErrors];
      return next;
    });
  };

  const title = target.kind === "bookingAmount" ? "Record booking amount" : "Record payment";
  const subtitle = target.kind === "bookingAmount"
    ? `${booking.bookingReference} · ${formatPkr(limit)} due`
    : `${booking.bookingReference} · ${installment!.type === "Possession" ? "Possession installment" : `Installment ${installment!.sequenceNumber}`}`;
  const amountHelper = installment
    ? `Due ${formatDay(installment.dueDate)} · up to ${formatPkr(limit)}`
    : `Up to ${formatPkr(limit)}`;

  const sendProof = async (paymentId: number | null): Promise<boolean> =>
    paymentId !== null && await proof.upload("CustomerPayment", paymentId);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (saving) return;

    if (proofPending) {
      setSaving(true);
      if (await sendProof(savedPaymentId ?? null)) {
        toast.success("Payment recorded.");
        onClose();
      }
      setSaving(false);
      return;
    }

    const problems = paymentErrors(fields, limit);
    setShown(problems);
    if (Object.keys(problems).length > 0) return;

    setSaving(true);
    setError(null);
    const body = {
      amount: Number(fields.amount),
      paymentMethod: fields.method,
      financeAccountId: Number(fields.accountId),
      paymentReference: fields.reference.trim() || null,
      notes: notes.trim() || null,
      paidAt: fields.paidAt,
    };
    // The same intent keeps the same key, so pressing Save again after a dropped connection is
    // recognised as the retry it is rather than collecting the money twice.
    const signature = `${target.kind}:${installment?.id ?? booking.id}:${body.amount}:${body.paidAt}:${body.financeAccountId}:${body.paymentMethod}`;
    const key = keys.key(signature, target.kind === "bookingAmount" ? "booking-amount-payment" : "installment-payment");
    let paymentId: number | null;
    try {
      // The server names the payment it created, so the proof can never land on a look-alike.
      const saved = installment
        ? await bookingApi.recordInstallment(booking.id, installment.id, body, key)
        : await bookingApi.recordBookingAmount(booking.id, body, key);
      paymentId = saved.recordedPaymentId ?? null;
      keys.release(signature);
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "The payment could not be recorded.");
      setSaving(false);
      return;
    }

    // The money is in, so the page refreshes now whatever happens to the proof.
    try {
      await onSaved();
    } catch {
      // The reload reports its own failure on the page.
    }
    if (!proof.hasFile || await sendProof(paymentId)) {
      toast.success("Payment recorded.");
      onClose();
      return;
    }
    setSavedPaymentId(paymentId);
    setSaving(false);
  };

  return (
    <Modal
      open
      onClose={onClose}
      busy={saving}
      size="md"
      phoneLayout="fullscreen"
      title={<DialogTitle title={title} subtitle={subtitle} />}
      cancelLabel={proofPending ? "Close" : "Cancel"}
      primaryAction={{ label: proofPending ? "Retry upload" : "Record payment", form: formId, loading: saving }}
    >
      <form id={formId} noValidate onSubmit={(event) => void submit(event)} className="flex flex-col gap-4">
        {proofPending ? (
          <>
            <Notice tone="orange" role="alert" title="Payment recorded, but the proof did not upload." message="Try again, or close to continue without it." />
            <AttachProof label="Proof" disabled={saving} {...proof.fieldProps} />
          </>
        ) : (
          <>
            {error && <Notice tone="red" role="alert" title={error} />}
            {accountsError && <Notice tone="orange" role="alert" title={accountsError} />}

            <NumberField
              label="Amount"
              required
              prefix="Rs"
              helper={amountHelper}
              error={shown.amount}
              value={fields.amount}
              onChange={(amount) => set({ amount })}
            />

            <div className="grid gap-4 md:grid-cols-2">
              <Dropdown
                label="Payment method"
                required
                options={PAYMENT_METHODS}
                value={fields.method}
                onChange={(method) => set({ method })}
              />
              <Dropdown
                label="Received in account"
                required
                placeholder="Select an account"
                error={shown.accountId}
                options={financeAccounts.map((account) => ({ value: String(account.id), label: `${account.name} — ${account.accountHolderName}` }))}
                value={fields.accountId}
                onChange={(accountId) => set({ accountId })}
              />
            </div>

            <div className="grid gap-4 md:grid-cols-2">
              <TextField
                label="Reference"
                required={referenceRequired(fields.method)}
                placeholder="Cheque / transfer no."
                maxLength={500}
                error={shown.reference}
                value={fields.reference}
                onChange={(event) => set({ reference: event.target.value })}
              />
              <DatePicker
                label="Payment date"
                required
                max={pakistanToday()}
                error={shown.paidAt}
                value={fields.paidAt}
                onChange={(paidAt) => set({ paidAt })}
              />
            </div>

            <AttachProof label="Proof" disabled={saving} {...proof.fieldProps} />

            <TextArea label="Notes" rows={3} maxLength={1000} value={notes} onChange={(event) => setNotes(event.target.value)} />

          </>
        )}
      </form>
    </Modal>
  );
}
