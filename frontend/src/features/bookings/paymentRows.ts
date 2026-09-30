import type { BookingPayment } from "./detailTypes.ts";

/** "Installment 4" · "Possession" · "Booking amount": what a payment was for. */
export function paymentFor(payment: Pick<BookingPayment, "type" | "installmentSequence" | "installmentType">): string {
  if (payment.type === "BookingAmount") return "Booking amount";
  if (payment.installmentType === "Possession") return "Possession";
  return payment.installmentSequence ? `Installment ${payment.installmentSequence}` : "Installment";
}
