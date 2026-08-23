import type { KeyboardEvent } from "react";
import type { CommissionStatus, RebateStatus } from "./types";

// A commission or rebate has one open state, shown as "Pending", and reaches its end state on its
// own once the money is fully paid or applied. Draft, PendingApproval, Approved and Earned are
// approval-ladder leftovers: records created before that ladder was removed are still pending, so
// they can be paid, corrected or cancelled like any other. Mirrors IsPending on the server.
const pendingCommission = ["Draft","PendingApproval","Approved","Earned","Payable","PartiallyPaid"];
const pendingRebate = ["Draft","PendingApproval","Approved","PartiallyApplied"];

export const commissionActions = (status:CommissionStatus) => ({
  canPay: pendingCommission.includes(status),
  // Correcting or cancelling is only about the agreement, so both stop the moment a payout lands —
  // PartiallyPaid is the one pending status that always has money already out.
  canEdit: pendingCommission.includes(status) && status !== "PartiallyPaid",
  canCancel: pendingCommission.includes(status) && status !== "PartiallyPaid",
  canReverse: status === "Paid" || status === "PartiallyPaid" || status === "ReversalRequired",
});

export const rebateActions = (status:RebateStatus) => ({
  canDisburse: pendingRebate.includes(status),
  canEdit: pendingRebate.includes(status) && status !== "PartiallyApplied",
  canCancel: pendingRebate.includes(status) && status !== "PartiallyApplied",
  canReverse: status === "Applied" || status === "Paid" || status === "PartiallyApplied" || status === "ReversalRequired",
});

export const prettyEnum = (value:string) => value.replace(/([a-z])([A-Z])/g,"$1 $2");

// What the user reads on the badge. Every open status collapses to one word; the end states and the
// exceptions keep their own names, because those are the ones worth noticing.
export const statusLabel = (status:string) =>
  pendingCommission.includes(status) || pendingRebate.includes(status) ? "Pending" : prettyEnum(status);
export const isPendingStatus = (status:string) =>
  pendingCommission.includes(status) || pendingRebate.includes(status);
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
