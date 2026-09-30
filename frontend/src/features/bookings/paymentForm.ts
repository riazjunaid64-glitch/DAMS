import { formatPkr } from "../../utils/currency.ts";

export const PAYMENT_METHODS = [
  { value: "Cash", label: "Cash" },
  { value: "BankTransfer", label: "Bank transfer" },
  { value: "Cheque", label: "Cheque" },
  { value: "Online", label: "Online" },
];

/** A reference (cheque / transfer number) is optional for Cash and required for every other method. */
export const referenceRequired = (method: string): boolean => method !== "Cash";

export interface PaymentFields {
  amount: string;
  method: string;
  accountId: string;
  reference: string;
  paidAt: string;
}

export type PaymentErrors = Partial<Record<keyof PaymentFields, string>>;

/** What is wrong with the form, field by field. `limit` is the most this payment may be. */
export function paymentErrors(fields: PaymentFields, limit: number): PaymentErrors {
  const errors: PaymentErrors = {};
  const amount = Number(fields.amount);
  if (!fields.amount || !(amount > 0)) errors.amount = "Enter the amount received.";
  else if (amount > limit) errors.amount = `Can't be more than ${formatPkr(limit)}`;
  if (!fields.accountId) errors.accountId = "Choose the account it was received in.";
  if (referenceRequired(fields.method) && !fields.reference.trim()) errors.reference = "Enter the cheque or transfer number.";
  if (!fields.paidAt) errors.paidAt = "Choose the payment date.";
  return errors;
}
