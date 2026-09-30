import { formatPkr } from "../../utils/currency.ts";
import { referenceRequired } from "../bookings/paymentForm.ts";
import type { CancellationRefundDecision } from "./types";

// Display convenience only — the backend independently recomputes and enforces this.
export function computeRetained(cashReceived: number, refundAmount: number): number {
  return Math.max(0, Math.round((cashReceived - refundAmount) * 100) / 100);
}

/** What paying a refund out needs, on both the Cancel popup (Pay now) and the Pay refund popup. */
export interface PayoutFields {
  method: string;
  accountId: string;
  reference: string;
}

export type PayoutErrors = Partial<Record<keyof PayoutFields, string>>;

export function payoutErrors(fields: PayoutFields): PayoutErrors {
  const errors: PayoutErrors = {};
  if (!fields.accountId) errors.accountId = "Choose the account the refund is paid from.";
  if (referenceRequired(fields.method) && !fields.reference.trim()) errors.reference = "Enter the cheque or transfer number.";
  return errors;
}

export interface CancelFields extends PayoutFields {
  reason: string;
  /** "" until the person has chosen: a paid customer's refund never defaults to "No refund". */
  decision: CancellationRefundDecision | "";
  refundAmount: string;
}

export type CancelErrors = Partial<Record<keyof CancelFields, string>>;

/**
 * What is wrong with the Cancel popup, field by field. `cashReceived` is what the customer has paid;
 * when it is zero there is no refund to decide and only the reason is asked for. Mirrors the server's
 * order (BookingService.CancelBookingCoreAsync), which stays the judge.
 */
export function cancelErrors(fields: CancelFields, cashReceived: number): CancelErrors {
  const errors: CancelErrors = {};
  if (!fields.reason.trim()) errors.reason = "Enter the reason for cancelling.";
  if (cashReceived <= 0 || fields.decision === "None") return errors;
  if (fields.decision === "") {
    errors.decision = "Choose what happens to the money paid.";
    return errors;
  }
  const amount = Number(fields.refundAmount);
  if (!fields.refundAmount || !(amount > 0)) errors.refundAmount = "Enter the refund amount, or choose No refund.";
  else if (amount > cashReceived) errors.refundAmount = `Can't be more than ${formatPkr(cashReceived)}`;
  if (fields.decision === "PayNow") Object.assign(errors, payoutErrors(fields));
  return errors;
}

// Recognizes every "reload and try again" business error CancelBookingCoreAsync can produce for
// a stale snapshot: a changed payment total, a changed booking (RowVersion conflict), or a
// missing/invalid concurrency token. Centralized so the dialog's stale-detection can't silently
// drift out of sync with the backend's wording again, the way it did the last time this message
// changed.
export function isStaleCancellationError(message: string): boolean {
  return /payments changed|booking changed|version is (missing|invalid)|Refresh and (try again|review)/i.test(message);
}
