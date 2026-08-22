import type { KeyboardEvent } from "react";
import type { CommissionStatus, RebateStatus } from "./types";

export const commissionActions = (status:CommissionStatus) => ({
  canSubmit: status === "Draft", canApprove: status === "PendingApproval", canReject: status === "PendingApproval",
  canReturn: status === "PendingApproval", canEarn: status === "Approved", canMakePayable: status === "Earned",
  canPay: status === "Payable" || status === "PartiallyPaid",
  canCancel: ["Draft","Approved","Earned","Payable","Rejected"].includes(status),
  canReverse: status === "Paid" || status === "PartiallyPaid" || status === "ReversalRequired",
});

export const rebateActions = (status:RebateStatus) => ({
  canSubmit: status === "Draft", canApprove: status === "PendingApproval", canReject: status === "PendingApproval",
  canReturn: status === "PendingApproval", canDisburse: status === "Approved" || status === "PartiallyApplied",
  canCancel: ["Draft","Approved","Rejected"].includes(status),
  canReverse: status === "Applied" || status === "Paid" || status === "PartiallyApplied" || status === "ReversalRequired",
});

export const prettyEnum = (value:string) => value.replace(/([a-z])([A-Z])/g,"$1 $2");
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
