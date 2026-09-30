import { useId, useState, type FormEvent } from "react";
import { Link } from "react-router-dom";
import { Modal, Notice, NumberField, useToast } from "../../components/ui";
import { useIdempotencyKeys } from "../../lib/idempotency.ts";
import { pakistanToday } from "../../lib/financePeriods.ts";
import { formatPkr } from "../../utils/currency.ts";
import type { FinanceAccountOption } from "../bookings/detailTypes.ts";
import { DialogTitle } from "../bookings/DialogTitle.tsx";
import { useProofUpload } from "../proof/useProofUpload.ts";
import { commissionRebateApi } from "./api.ts";
import { PaymentOutFields } from "./PaymentOutFields.tsx";
import { isMissingBankDetails, newMovementId, paymentOutErrors, type FormErrors, type PaymentFields } from "./forms.ts";
import type { RunMutation } from "./runMutation.ts";
import type { Commission } from "./types.ts";

type Props = {
  bookingId: number;
  commission: Commission;
  financeAccounts: FinanceAccountOption[];
  /** Set when the account list could not be loaded, so the account field can say why it is empty. */
  accountsError: string | null;
  run: RunMutation;
  onClose: () => void;
};

/** Pay commission: a payment to the partner, with optional proof. Bank transfer needs the partner's bank details. */
export function PayCommissionDialog({ bookingId, commission, financeAccounts, accountsError, run, onClose }: Props) {
  const toast = useToast();
  const keys = useIdempotencyKeys();
  const proof = useProofUpload();
  const formId = useId();
  const [fields, setFields] = useState<PaymentFields>({
    amount: String(commission.outstandingAmount),
    method: "BankTransfer",
    accountId: financeAccounts.length === 1 ? String(financeAccounts[0]!.id) : "",
    reference: "",
    date: pakistanToday(),
  });
  const [shown, setShown] = useState<FormErrors<PaymentFields>>({});
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const set = (change: Partial<PaymentFields>) => {
    setFields((current) => ({ ...current, ...change }));
    setShown((current) => {
      const next = { ...current };
      for (const key of Object.keys(change)) delete next[key as keyof PaymentFields];
      // Cash needs no reference, so a missing-reference message must not outlive the method that asked for it.
      if (change.method !== undefined) delete next.reference;
      return next;
    });
  };

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (saving) return;
    const problems = paymentOutErrors(fields, commission.outstandingAmount);
    setShown(problems);
    if (Object.keys(problems).length > 0) return;

    setSaving(true);
    setError(null);
    // The same intent keeps the same key, so pressing the button again after a dropped connection is
    // recognised as the retry it is rather than paying the partner twice.
    const signature = `payout:${commission.id}:${JSON.stringify(fields)}`;
    try {
      const saved = await run(() => commissionRebateApi.payout(bookingId, commission.id, {
        financeAccountId: Number(fields.accountId),
        amount: Number(fields.amount),
        paymentDate: fields.date,
        paymentMethod: fields.method,
        paymentReference: fields.reference.trim() || null,
        notes: null,
        idempotencyKey: keys.key(signature, "commission-payout"),
        commissionConcurrencyToken: commission.concurrencyToken,
      }), false);
      keys.release(signature);
      toast.success("Payment recorded.");
      // A failed proof never undoes the payment: the popup closes with a warning and the file is
      // attached later from the payment row.
      const payoutId = newMovementId(commission.payouts, saved.commissions.find((c) => c.id === commission.id)?.payouts ?? []);
      if (proof.hasFile && !(payoutId !== null && await proof.upload("CommissionPayout", payoutId))) {
        toast.error("The payment was saved, but its proof did not upload. Attach it from the payment.");
      }
      onClose();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "The payment could not be recorded.");
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
      title={<DialogTitle title="Pay commission" subtitle={`${commission.partnerName} · ${formatPkr(commission.outstandingAmount)} remaining`} />}
      primaryAction={{ label: "Pay commission", form: formId, loading: saving }}
    >
      <form id={formId} noValidate onSubmit={(event) => void submit(event)} className="flex flex-col gap-4">
        {error && (
          <Notice
            tone="red"
            role="alert"
            title={error}
            action={isMissingBankDetails(error)
              ? <Link to="/finance/commissions-rebates" className="text-sm font-bold text-gold-text underline-offset-4 hover:underline">Open the partner page</Link>
              : undefined}
          />
        )}
        {accountsError && <Notice tone="orange" role="alert" title={accountsError} />}
        <NumberField
          label="Amount"
          required
          prefix="Rs"
          disabled={saving}
          helper={`Up to ${formatPkr(commission.outstandingAmount)}`}
          error={shown.amount}
          value={fields.amount}
          onChange={(amount) => set({ amount })}
        />
        <PaymentOutFields fields={fields} errors={shown} onChange={set} financeAccounts={financeAccounts} proof={proof.fieldProps} disabled={saving} />
      </form>
    </Modal>
  );
}
