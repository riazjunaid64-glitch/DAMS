import type { KeyboardEvent } from "react";
import type { CalculationBasis, CalculationType, Commission, CommissionStatus, Rebate, RebateStatus } from "./types";

// A commission or rebate is Pending from entry until the money is fully paid or applied, whether or
// not part of it has already gone out — how much is paid and how much is left comes from the payment
// rows, so the caller passes those in. Reversing is what unwinds anything already sent.
//
// Editing and cancelling are DIFFERENT rules, and both mirror the server exactly:
//   • Edit needs a clean sheet. The server refuses once ANY payout row exists, reversed or not,
//     because that payout's own figures were struck against the amount being rewritten — hence
//     movementCount, not just the net.
//   • Cancel is not gated on history at all. The server accepts it whenever the record is pending
//     with nothing net paid, which a fully reversed payout satisfies. Applying the edit rule to
//     Cancel as well left a commission that had been paid and then fully reversed with no way to
//     close it: no Edit, no Cancel, and no payout left to reverse.
export const commissionActions = (status:CommissionStatus, paidAmount = 0, movementCount = 0) => ({
  canPay: status === "Pending",
  canEdit: status === "Pending" && paidAmount === 0 && movementCount === 0,
  canCancel: status === "Pending" && paidAmount === 0,
  canReverse: paidAmount > 0 || status === "Paid" || status === "ReversalRequired",
});

export const rebateActions = (status:RebateStatus, givenAmount = 0, movementCount = 0) => ({
  canDisburse: status === "Pending",
  canEdit: status === "Pending" && givenAmount === 0 && movementCount === 0,
  canCancel: status === "Pending" && givenAmount === 0,
  canReverse: givenAmount > 0 || status === "Applied" || status === "Paid" || status === "ReversalRequired",
});

export interface CommissionFormState {
  partnerId: string;
  calculationType: CalculationType;
  calculationBasis: CalculationBasis;
  percentageRate: string;
  fixedAmount: string;
  notes: string;
  changeReason: string;
}

export interface RebateFormState {
  calculationType: CalculationType;
  calculationBasis: CalculationBasis;
  percentageRate: string;
  fixedAmount: string;
  reason: string;
  changeReason: string;
}

/// Both update endpoints REPLACE the whole record, so anything the form does not show still has to
/// be sent back as it stands. Sending nulls instead re-derived the record from scratch: a commission
/// lost its attribution and with it the allocation share that made a quarter-share Rs 250 rather than
/// Rs 1,000, a rule-driven commission was rewritten as a manual one, and an agreed adjustment was
/// zeroed — all from an edit that only meant to change a note.
export function commissionRequestBody(form: CommissionFormState, existing: Commission | null) {
  const percentage = form.calculationType === "Percentage";
  // The rule owns the figures on a rule-driven commission; the server recomputes them from the rule
  // and ignores anything sent here, so the form does not offer them either.
  const ruleDriven = !!existing && !existing.isManual;
  return {
    partnerId: Number(form.partnerId),
    attributionId: existing?.attributionId ?? null,
    ruleId: existing?.ruleId ?? null,
    isManual: existing ? existing.isManual : true,
    manualReason: ruleDriven ? null : (form.notes.trim() || null),
    manualCalculationType: ruleDriven ? null : form.calculationType,
    // A fixed amount still records a basis so the saved commission reads back against the booking it
    // was agreed on. It never changes the amount.
    manualCalculationBasis: ruleDriven ? null : (percentage ? form.calculationBasis : "NetSalePriceAfterDiscount"),
    manualPercentageRate: ruleDriven || !percentage ? null : Number(form.percentageRate),
    manualFixedAmount: ruleDriven || percentage ? null : Number(form.fixedAmount),
    manualBasisAmount: null,
    manualEarningCondition: "ManualMilestone",
    minimumCollectionPercent: null,
    adjustmentAmount: existing?.adjustmentAmount ?? 0,
    adjustmentReason: existing?.adjustmentReason ?? null,
    ...(existing ? { concurrencyToken: existing.concurrencyToken, changeReason: form.changeReason } : {}),
  };
}

export function rebateRequestBody(form: RebateFormState, existing: Rebate | null) {
  const percentage = form.calculationType === "Percentage";
  return {
    calculationType: form.calculationType,
    calculationBasis: percentage ? form.calculationBasis : "AgreedSalePrice",
    percentageRate: percentage ? Number(form.percentageRate) : null,
    fixedAmount: percentage ? null : Number(form.fixedAmount),
    manualBasisAmount: null,
    adjustmentAmount: existing?.adjustmentAmount ?? 0,
    adjustmentReason: existing?.adjustmentReason ?? null,
    reason: form.reason.trim() || null,
    notes: existing?.notes ?? null,
    // How the rebate reaches the customer is chosen when it is applied, not here.
    method: existing ? existing.method : "OutstandingBalanceReduction",
    ...(existing ? { concurrencyToken: existing.concurrencyToken, changeReason: form.changeReason } : {}),
  };
}

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
