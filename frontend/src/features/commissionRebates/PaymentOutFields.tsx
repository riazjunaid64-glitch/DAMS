import { AttachProof, DatePicker, Dropdown, TextField, type AttachProofProps } from "../../components/ui";
import { pakistanToday } from "../../lib/financePeriods.ts";
import type { FinanceAccountOption } from "../bookings/detailTypes.ts";
import { PAYMENT_METHODS, referenceRequired } from "../bookings/paymentForm.ts";
import type { FormErrors, PaymentFields } from "./forms.ts";

type Props = {
  fields: PaymentFields;
  errors: FormErrors<PaymentFields>;
  onChange: (change: Partial<PaymentFields>) => void;
  financeAccounts: FinanceAccountOption[];
  /** "Payment date" for a commission payment, "Date" where a rebate is applied. */
  dateLabel?: string;
  /** Shown as the optional Proof field; left out where no money moves. */
  proof?: Pick<AttachProofProps, "file" | "onPick" | "onRemove" | "progress" | "error">;
  disabled: boolean;
};

/**
 * Payment method, the account it is paid from, the reference, the date and the optional proof: what a
 * payment out to a partner and a rebate paid by cash or bank both ask for.
 */
export function PaymentOutFields({ fields, errors, onChange, financeAccounts, dateLabel = "Payment date", proof, disabled }: Props) {
  return (
    <>
      <div className="grid gap-4 md:grid-cols-2">
        <Dropdown label="Payment method" required disabled={disabled} options={PAYMENT_METHODS} value={fields.method} onChange={(method) => onChange({ method })} />
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
        <DatePicker label={dateLabel} required disabled={disabled} max={pakistanToday()} error={errors.date} value={fields.date} onChange={(date) => onChange({ date })} />
      </div>
      {proof && <AttachProof label="Proof" disabled={disabled} {...proof} />}
    </>
  );
}
