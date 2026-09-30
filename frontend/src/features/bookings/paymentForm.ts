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

/**
 * The payment that a save just created: the one on the booking now that was not there before, with
 * the amount that was sent. Payments carry no id in the save's response, and the proof has to be
 * attached to a saved payment, so it is found by comparing the list before and after.
 */
export function newPaymentId(before: readonly number[], after: readonly { id: number; amount: number }[], amount: number): number | null {
  const known = new Set(before);
  const created = after.filter((payment) => !known.has(payment.id));
  const match = created.filter((payment) => payment.amount === amount);
  return (match.length ? match : created).reduce<number | null>((latest, payment) => (latest === null || payment.id > latest ? payment.id : latest), null);
}
