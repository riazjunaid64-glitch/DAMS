import type { BookingDetail } from "./detailTypes.ts";
import { termsNotSet } from "./statusNames.ts";

export type MainActionKind = "recordPayment" | "givePossession" | "completeSale" | "payRefund";

export interface HeaderActions {
  /** Cancel booking: only while Awaiting booking amount or Payment plan, which is the server's rule. */
  cancel: boolean;
  /** The one main button of the current status, or none. */
  main: { kind: MainActionKind; label: string; disabled: boolean } | null;
}

type ActionInput = Pick<
  BookingDetail,
  "status" | "bookingAmountRequired" | "bookingAmountRemaining" | "hasInstallmentSchedule" | "outstanding" | "installmentsPaid" | "installmentsTotal"
> & { cancellationSettlement?: { refundStatus: string } | null };

/**
 * Which buttons the header shows for a booking. Print application form is on every status, so it
 * is not part of this: only what varies is decided here, and the same answer drives desktop and phone.
 */
export function headerActions(booking: ActionInput): HeaderActions {
  const cancel = booking.status === "AwaitingBookingAmount" || booking.status === "PaymentPlanActive";

  if (booking.status === "AwaitingBookingAmount") {
    // Terms not set: the Set terms button lives in the card, and there is nothing to record yet.
    // Withheld once the amount is fully covered (a rebate credit can do that): there is nothing to record.
    return { cancel, main: termsNotSet(booking) ? null : { kind: "recordPayment", label: "Record payment", disabled: booking.bookingAmountRemaining <= 0 } };
  }
  if (booking.status === "PaymentPlanActive") {
    // Possession follows the plan: it shows once a plan exists, or when nothing is left to pay.
    const ready = booking.hasInstallmentSchedule || booking.outstanding <= 0;
    return { cancel, main: ready ? { kind: "givePossession", label: "Give possession", disabled: false } : null };
  }
  if (booking.status === "PossessionGiven") {
    // Completing can only fail while money or an installment is still open, so it is withheld then.
    const settled = booking.outstanding <= 0 && booking.installmentsPaid >= booking.installmentsTotal;
    return { cancel, main: { kind: "completeSale", label: "Complete sale", disabled: !settled } };
  }
  if (booking.status === "Cancelled") {
    // Only while a refund is still owed: once it is paid, or none was due, there is nothing to do.
    return { cancel, main: booking.cancellationSettlement?.refundStatus === "Pending" ? { kind: "payRefund", label: "Pay refund", disabled: false } : null };
  }
  return { cancel, main: null };
}

export type ExtraAction = "print" | "cancel";

export interface PhoneActions {
  /** The extras that are not the main button, in menu order. */
  extras: ExtraAction[];
  /** "menu": the ⋯ button opening a sheet (two or more extras). "printer": a printer icon (only Print is left). */
  more: "menu" | "printer";
}

/** The phone rule: one main button, then ⋯ only when two or more extras remain; when only Print is left, a printer icon. */
export function phoneActions(actions: HeaderActions): PhoneActions {
  const extras: ExtraAction[] = actions.cancel ? ["print", "cancel"] : ["print"];
  return { extras, more: extras.length >= 2 ? "menu" : "printer" };
}
