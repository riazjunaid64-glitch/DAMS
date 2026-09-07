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

// The bases the booking screen can state in one line. The enum carries five; these forms offer
// three and two. Anything else — ManuallyApprovedAmount above all — is shown read-only and carried
// back untouched, because substituting a form basis for it CHANGES THE AMOUNT: a 10% commission on
// a manually approved Rs 500,000 became 10% of a Rs 10m net sale price.
export const commissionBases: CalculationBasis[] = ["NetSalePriceAfterDiscount", "AgreedSalePrice", "AmountActuallyCollected"];
export const rebateBases: CalculationBasis[] = ["AgreedSalePrice", "NetSalePriceAfterDiscount"];

/** Whether this record's stored basis is one the form can offer at all. */
export const isEditableBasis = (basis: CalculationBasis, supported: CalculationBasis[]) => supported.includes(basis);

/**
 * Whether the server will keep this record's STORED basis amount instead of re-deriving it from
 * today's booking — its own rule (CommissionRebasableBases / RebateRebasableBases), which is these
 * same two lists.
 *
 * The preview has to ask this or it shows a number the save will not produce. A rebate agreed on
 * AmountActuallyCollected is the case that bites: the form cannot offer that basis, so the server
 * freezes the amount collected when it was agreed, while a preview reading the live figure promised
 * 10% of everything collected since. Preview Rs 25,000, save Rs 10,000, no warning either way.
 */
export const usesStoredBasisAmount = (
  basis: CalculationBasis,
  existingBasis: CalculationBasis | null | undefined,
  supported: CalculationBasis[],
) => !!existingBasis && basis === existingBasis && !supported.includes(basis);

/**
 * The basis the server will calculate on for this save — the single answer used by both the request
 * body and the on-screen preview, so the operator cannot be shown one and have the other saved.
 * A new record takes the form's; an edit keeps its own unless the operator could genuinely change
 * it (percentage, and a basis this form lists) and did.
 */
export function commissionBasisFor(form: CommissionFormState, existing: Commission | null): CalculationBasis {
  const percentage = form.calculationType === "Percentage";
  if (!existing) return percentage ? form.calculationBasis : "NetSalePriceAfterDiscount";
  return percentage && isEditableBasis(existing.calculationBasis, commissionBases)
    ? form.calculationBasis
    : existing.calculationBasis;
}

export function rebateBasisFor(form: RebateFormState, existing: Rebate | null): CalculationBasis {
  const percentage = form.calculationType === "Percentage";
  if (!existing) return percentage ? form.calculationBasis : "AgreedSalePrice";
  return percentage && isEditableBasis(existing.calculationBasis, rebateBases)
    ? form.calculationBasis
    : existing.calculationBasis;
}

/**
 * The allocation share the server will apply. An attribution belongs to ONE partner, so it survives
 * an edit only while the partner is unchanged — resubmitting it after an explicit partner change is
 * what the server's ownership check correctly rejects. Without an attribution the commission is
 * calculated in full, which is what a newly chosen partner is owed.
 */
export const commissionAttributionFor = (form: CommissionFormState, existing: Commission | null) =>
  existing && Number(form.partnerId) === existing.partnerId ? existing.attributionId ?? null : null;

export const commissionAllocationPercent = (form: CommissionFormState, existing: Commission | null) =>
  commissionAttributionFor(form, existing) === null ? 100 : existing!.allocationPercent;

/**
 * The rule the server should calculate on. A rule belongs to the partner it was picked for, so it
 * survives an edit only while the partner is unchanged — exactly like the attribution above.
 * Resubmitting the previous partner's rule after a partner change either fails the server's own
 * "does not apply to this booking and partner" check, or, where the rule is broad enough to match
 * both, quietly pays the new partner on the old one's terms and skips the ranking that would have
 * chosen the right rule for them. Sending null is what asks for that ranking.
 */
export const commissionRuleFor = (form: CommissionFormState, existing: Commission | null) =>
  existing && Number(form.partnerId) === existing.partnerId ? existing.ruleId ?? null : null;

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
  const basis = commissionBasisFor(form, existing);
  return {
    partnerId: Number(form.partnerId),
    attributionId: commissionAttributionFor(form, existing),
    ruleId: commissionRuleFor(form, existing),
    isManual: existing ? existing.isManual : true,
    manualReason: ruleDriven ? null : (form.notes.trim() || null),
    manualCalculationType: ruleDriven ? null : form.calculationType,
    // A fixed amount still records a basis so the saved commission reads back against the booking it
    // was agreed on. It never changes the amount.
    manualCalculationBasis: ruleDriven ? null : basis,
    manualPercentageRate: ruleDriven || !percentage ? null : Number(form.percentageRate),
    manualFixedAmount: ruleDriven || percentage ? null : Number(form.fixedAmount),
    // ManuallyApprovedAmount has no booking figure behind it — the amount IS the basis — so it has
    // to travel with the request or the server has nothing to calculate on.
    manualBasisAmount: !ruleDriven && basis === "ManuallyApprovedAmount" ? existing?.basisAmount ?? null : null,
    manualEarningCondition: "ManualMilestone",
    minimumCollectionPercent: null,
    adjustmentAmount: existing?.adjustmentAmount ?? 0,
    adjustmentReason: existing?.adjustmentReason ?? null,
    ...(existing ? { concurrencyToken: existing.concurrencyToken, changeReason: form.changeReason } : {}),
  };
}

export function rebateRequestBody(form: RebateFormState, existing: Rebate | null) {
  const percentage = form.calculationType === "Percentage";
  const basis = rebateBasisFor(form, existing);
  return {
    calculationType: form.calculationType,
    calculationBasis: basis,
    percentageRate: percentage ? Number(form.percentageRate) : null,
    fixedAmount: percentage ? null : Number(form.fixedAmount),
    manualBasisAmount: basis === "ManuallyApprovedAmount" ? existing?.basisAmount ?? null : null,
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

/**
 * What the commission form says about an adjustment it is carrying but not editing.
 *
 * The final amount is only predictable where this form holds every input the server uses. For a
 * MANUAL commission it does — rate, basis and allocation are all on screen, and the server adds the
 * adjustment to exactly what they produce. For a RULE-DRIVEN one it does not: the server picks the
 * rule, applies it, and clamps the result to the rule's minimum and maximum before the adjustment
 * goes on. Predicting from the visible rate anyway promised Rs 11,000 on a commission a Rs 25,000
 * rule minimum settles at Rs 26,000 — and did it directly beneath a readout correctly saying the
 * amount would be recalculated on save. Say what is known instead of guessing what is not.
 */
export const commissionAdjustmentNote = (
  existing: { adjustmentAmount: number; adjustmentReason: string | null } | null,
  ruleDriven: boolean,
  calculatedAmount: number,
): string | null => {
  const adjustment = existing?.adjustmentAmount ?? 0;
  if (!existing || adjustment === 0) return null;
  const because = existing.adjustmentReason ? ` (${existing.adjustmentReason})` : "";
  const opening = `An agreed adjustment of ${money(adjustment)} is kept on this commission${because}`;
  if (ruleDriven) return `${opening}, and is added to whatever the rule works out to.`;
  const final = Math.max(0, Math.round((calculatedAmount + adjustment) * 100) / 100);
  return `${opening}, so the final amount will be ${money(final)}.`;
};

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
