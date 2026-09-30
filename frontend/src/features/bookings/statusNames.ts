import { statusLabel } from "../../components/ui/statusTone.ts";

/*
 * The one place the Bookings area turns what the server sends into words and badge colours: the
 * booking, its installments, commission, rebate and refund. Each function returns the status the
 * shared StatusBadge understands, so a screen writes <StatusBadge status={commissionStatus(...)} />
 * and never picks a word or a colour of its own.
 */

/** Booking statuses in the order the summary cards and the Status filter list them. */
export const BOOKING_STATUSES = ["AwaitingBookingAmount", "PaymentPlanActive", "PossessionGiven", "SaleCompleted"] as const;
export type BookingStatusValue = (typeof BOOKING_STATUSES)[number] | "Cancelled";

/** The Status filter: the five live statuses, then Cancelled. */
export const BOOKING_STATUS_OPTIONS = [...BOOKING_STATUSES, "Cancelled" as const].map((value) => ({ value, label: statusLabel(value) }));

/** "Terms not set" is not a status: it is an Awaiting booking amount booking whose booking amount is 0. */
export function termsNotSet(booking: { status: string; bookingAmountRequired: number }): boolean {
  return booking.status === "AwaitingBookingAmount" && booking.bookingAmountRequired === 0;
}

export function installmentStatus(status: string): string {
  return status === "PartiallyPaid" ? "Partly paid" : status;
}

/** A commission with something paid but not settled reads "Partly paid"; nothing paid yet stays "Pending". */
export function commissionStatus(status: string, paidAmount: number): string {
  if (status === "Pending" && paidAmount > 0) return "Partly paid";
  if (status === "ReversalRequired") return "Reversal required";
  return status;
}

/** A rebate with some given but not all reads "Partly given"; "Paid" (money out) reads "Given"; "Applied" (credited) stays. */
export function rebateStatus(status: string, givenAmount: number): string {
  if (status === "Pending" && givenAmount > 0) return "Partly given";
  if (status === "Paid") return "Given";
  if (status === "ReversalRequired") return "Reversal required";
  return status;
}

export function refundStatus(status: string): string {
  switch (status) {
    case "Pending": return "Refund to pay";
    case "Paid": return "Refund paid";
    case "NotRequired": return "No refund";
    default: return status;
  }
}
