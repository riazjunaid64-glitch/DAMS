import { AttachProof, DatePicker, Dropdown, TextField, type AttachProofProps } from "../../components/ui";
import type { FinanceAccountOption } from "../bookings/detailTypes.ts";
import { PAYMENT_METHODS, referenceRequired } from "../bookings/paymentForm.ts";
import type { PayoutErrors, PayoutFields } from "./state.ts";

type Props = {
  fields: PayoutFields;
  errors: PayoutErrors;
  onChange: (change: Partial<PayoutFields>) => void;
  financeAccounts: FinanceAccountOption[];
  proof: Pick<AttachProofProps, "file" | "onPick" | "onRemove" | "progress" | "error">;
  /** Today in Pakistan, "YYYY-MM-DD": the only date the server takes for a refund. */
  refundDate: string;
  disabled: boolean;
};

/**
 * Payment method, account, reference, the refund date and the optional proof — the part the Cancel
 * popup (Pay now) and the Pay refund popup have in common. The date is locked to today because the
 * server only accepts today for a refund.
 */
export function RefundPayoutFields({ fields, errors, onChange, financeAccounts, proof, refundDate, disabled }: Props) {
  return (
    <>
      <div className="grid gap-4 md:grid-cols-2">
        <Dropdown
          label="Payment method"
          required
          disabled={disabled}
          options={PAYMENT_METHODS}
          value={fields.method}
          onChange={(method) => onChange({ method })}
        />
        <Dropdown
          label="Pay from account"
          required
          disabled={disabled}
          placeholder="Select an account"
          error={errors.accountId}
          options={financeAccounts.map((account) => ({ value: String(account.id), label: `${account.name} — ${account.accountHolderName}` }))}
          value={fields.accountId}
          onChange={(accountId) => onChange({ accountId })}
        />
      </div>
      <div className="grid gap-4 md:grid-cols-2">
        <TextField
          label="Reference"
          required={referenceRequired(fields.method)}
          disabled={disabled}
          placeholder="Cheque / transfer no."
          maxLength={200}
          error={errors.reference}
          value={fields.reference}
          onChange={(event) => onChange({ reference: event.target.value })}
        />
        <DatePicker label="Refund date" locked value={refundDate} onChange={() => undefined} />
      </div>
      <AttachProof label="Proof" disabled={disabled} {...proof} />
    </>
  );
}
