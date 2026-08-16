import type { CancellationRefundDecision, RefundPaymentMethod } from "./types";

// Display convenience only — the backend independently recomputes and enforces this.
export function computeRetained(cashReceived: number, refundAmount: number): number {
  return Math.max(0, Math.round((cashReceived - refundAmount) * 100) / 100);
}

export interface CancellationDecisionInput {
  cashReceived: number;
  refundAmount: number;
  decision: CancellationRefundDecision | "";
  refundFinanceAccountId?: number | null;
  refundPaymentMethod?: RefundPaymentMethod | null;
  refundPaymentReference?: string | null;
}

// Mirrors BookingService.CancelBookingCoreAsync's validation order so the dialog can disable
// its confirm button and show the same message the server would return, before ever submitting.
export function validateCancellationDecision(input: CancellationDecisionInput): string | null {
  const { cashReceived, refundAmount, decision } = input;
  if (!Number.isFinite(refundAmount) || refundAmount < 0) return "Refund amount cannot be negative.";
  if (refundAmount > cashReceived) return "Refund amount cannot exceed the customer's paid amount.";
  if (refundAmount === 0) {
    if (decision !== "None" && decision !== "") return "Refund decision must be None when the refund amount is zero.";
    return null;
  }
  if (decision !== "PayNow" && decision !== "PayLater") return "Choose whether the refund will be paid now or paid later.";
  if (decision === "PayNow") {
    if (!input.refundFinanceAccountId) return "A refund source account is required when paying the refund now.";
    if (!input.refundPaymentMethod) return "Select a refund payment method.";
    if (input.refundPaymentMethod !== "Cash" && !input.refundPaymentReference?.trim())
      return "A payment reference is required for a non-cash refund.";
  }
  return null;
}

export function refundStatusLabel(status: string): string {
  switch (status) {
    case "NotRequired": return "No refund required";
    case "Pending": return "Pending";
    case "Paid": return "Paid";
    default: return status;
  }
}

export function refundDecisionLabel(decision: string): string {
  switch (decision) {
    case "PayNow": return "Pay now";
    case "PayLater": return "Pay later";
    default: return "None";
  }
}

// Re-exported so this feature follows the same idempotency-key / date / focus-trap conventions
// already established for financial workflow dialogs, without duplicating them.
export { idempotencyKey, money, pakistanToday, trapDialogKeys } from "../commissionRebates/state";
