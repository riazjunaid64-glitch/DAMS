import { useId, useState, type FormEvent } from "react";
import { Modal, NumberField, Notice, TextArea, useToast } from "../../components/ui";
import { useIdempotencyKeys } from "../../lib/idempotency.ts";
import { pakistanToday } from "../../lib/financePeriods.ts";
import type { BookingDetail, FinanceAccountOption } from "../bookings/detailTypes.ts";
import { DialogTitle } from "../bookings/DialogTitle.tsx";
import { useProofUpload } from "../proof/useProofUpload.ts";
import { bookingCancellationApi } from "./api.ts";
import { RefundPayoutFields } from "./RefundPayoutFields.tsx";
import { payoutErrors, type PayoutErrors, type PayoutFields } from "./state.ts";
import type { PayCancellationRefundRequest, RefundPaymentMethod } from "./types.ts";

type Props = {
  booking: Pick<BookingDetail, "id" | "bookingReference" | "customerName">;
  /** The refund the cancellation decided on. The server pays all of it in one go, so it is shown, never edited. */
  refundAmount: number;
  financeAccounts: FinanceAccountOption[];
  /** Set when the account list could not be loaded, so the account field can say why it is empty. */
  accountsError: string | null;
  onClose: () => void;
  /** Reloads the booking once the refund is saved, before the popup closes. */
  onPaid: () => Promise<void> | void;
};

/** Pay refund: pays the refund a cancelled booking still owes. */
export function PayRefundDialog({ booking, refundAmount, financeAccounts, accountsError, onClose, onPaid }: Props) {
  const toast = useToast();
  const keys = useIdempotencyKeys();
  const proof = useProofUpload();
  const formId = useId();
  // Read once: the date sent is the date the person saw locked in the field.
  const [refundDate] = useState(pakistanToday);

  const [fields, setFields] = useState<PayoutFields>({
    method: "Cash",
    accountId: financeAccounts.length === 1 ? String(financeAccounts[0]!.id) : "",
    reference: "",
  });
  const [notes, setNotes] = useState("");
  const [shown, setShown] = useState<PayoutErrors>({});
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const set = (change: Partial<PayoutFields>) => {
    setFields((current) => ({ ...current, ...change }));
    setShown((current) => {
      const next = { ...current };
      for (const key of Object.keys(change)) delete next[key as keyof PayoutFields];
      // Cash needs no reference, so a missing-reference message must not outlive the method that asked for it.
      if (change.method !== undefined) delete next.reference;
      return next;
    });
  };

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (saving) return;

    const problems = payoutErrors(fields);
    setShown(problems);
    if (Object.keys(problems).length > 0) return;

    setSaving(true);
    setError(null);
    const intent: Omit<PayCancellationRefundRequest, "idempotencyKey"> = {
      financeAccountId: Number(fields.accountId),
      paymentMethod: fields.method as RefundPaymentMethod,
      paymentReference: fields.reference.trim() || null,
      paidAt: refundDate,
      notes: notes.trim() || null,
    };
    // The same intent keeps the same key, so pressing the button again after a dropped connection is
    // recognised as the retry it is rather than paying the refund twice.
    const signature = `refund:${booking.id}:${JSON.stringify(intent)}`;
    const body: PayCancellationRefundRequest = { ...intent, idempotencyKey: keys.key(signature, "cancel-refund") };
    try {
      const saved = await bookingCancellationApi.payRefund(booking.id, body);
      keys.release(signature);
      const refundId = saved.cancellationSettlement?.refund?.id ?? null;
      // A failed proof never undoes the refund: the popup closes with a warning and the file is
      // attached later from the saved refund.
      toast.success("Refund paid.");
      if (proof.hasFile && !(refundId !== null && await proof.upload("CancellationRefund", refundId))) {
        toast.error("The refund was saved, but its proof did not upload. Attach it from the refund.");
      }
      try {
        await onPaid();
      } finally {
        onClose();
      }
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "The refund could not be paid.");
      setSaving(false);
    }
  };

  return (
    <Modal
      open
      onClose={onClose}
      busy={saving}
      size="md"
      phoneLayout="fullscreen"
      title={<DialogTitle title="Pay refund" subtitle={`${booking.bookingReference} · ${booking.customerName}`} />}
      primaryAction={{ label: "Pay refund", form: formId, loading: saving }}
    >
      <form id={formId} noValidate onSubmit={(event) => void submit(event)} className="flex flex-col gap-4">
        {error && <Notice tone="red" role="alert" title={error} />}
        {accountsError && <Notice tone="orange" role="alert" title={accountsError} />}

        <NumberField label="Refund amount" prefix="Rs" disabled readOnly value={String(refundAmount)} onChange={() => undefined} />

        <RefundPayoutFields
          fields={fields}
          errors={shown}
          onChange={set}
          financeAccounts={financeAccounts}
          proof={proof.fieldProps}
          refundDate={refundDate}
          disabled={saving}
        />

        <TextArea label="Notes" rows={3} maxLength={2000} disabled={saving} value={notes} onChange={(event) => setNotes(event.target.value)} />
      </form>
    </Modal>
  );
}
