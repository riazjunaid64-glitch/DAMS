import type { KeyboardEvent } from "react";
import type { CommissionStatus, RebateStatus } from "./types";

// A commission or rebate is Pending from entry until the money is fully paid or applied, whether or
// not part of it has already gone out — how much is paid and how much is left comes from the payment
// rows, so the caller passes those in. Reversing is what unwinds anything already sent.
export const commissionActions = (status:CommissionStatus, paidAmount = 0) => ({
  canPay: status === "Pending",
  // Correcting or cancelling is only about the agreement, so both stop once any money has moved.
  canEdit: status === "Pending" && paidAmount === 0,
  canCancel: status === "Pending" && paidAmount === 0,
  canReverse: paidAmount > 0 || status === "Paid" || status === "ReversalRequired",
});

export const rebateActions = (status:RebateStatus, givenAmount = 0) => ({
  canDisburse: status === "Pending",
  canEdit: status === "Pending" && givenAmount === 0,
  canCancel: status === "Pending" && givenAmount === 0,
  canReverse: givenAmount > 0 || status === "Applied" || status === "Paid" || status === "ReversalRequired",
});

export const prettyEnum = (value:string) => value.replace(/([a-z])([A-Z])/g,"$1 $2");
export const isPendingStatus = (status:string) => status === "Pending";
export const money = (value:number) => `Rs ${value.toLocaleString("en-PK",{minimumFractionDigits:2,maximumFractionDigits:2})}`;

// One implementation, in lib/idempotency, because every screen that records money needs it — not
// just commissions and rebates. Re-exported so this module keeps its existing callers.
export { newIdempotencyKey as idempotencyKey } from "../../lib/idempotency";

// Re-exported so this module's existing callers keep working. It lives in lib/financePeriods
// because every finance screen that defaults a date needs it, not just commissions and rebates.
export { pakistanToday } from "../../lib/financePeriods";

// Shared dialog keyboard handler: Escape closes; Tab is trapped so focus cycles within the modal.
// Kept in one place so both the booking panel and the settings page stay in sync.
export function trapDialogKeys(e: KeyboardEvent<HTMLElement>, close: () => void) {
  if (e.key === "Escape") { e.preventDefault(); close(); return; }
  if (e.key !== "Tab") return;
  const controls = Array.from(e.currentTarget.querySelectorAll<HTMLElement>('button:not([disabled]),input:not([disabled]),select:not([disabled]),textarea:not([disabled]),[tabindex]:not([tabindex="-1"])')).filter(x => x.offsetParent !== null);
  if (controls.length === 0) { e.preventDefault(); return; }
  const first = controls[0], last = controls[controls.length - 1];
  if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
  else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
}
