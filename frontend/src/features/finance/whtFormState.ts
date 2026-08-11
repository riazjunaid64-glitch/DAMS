import type { WhtCalculation, WhtFormValue } from "./whtTypes.ts";

/**
 * The decision logic behind the withholding block on the expense form, kept free of React so the
 * rules that protect a tax figure can be tested directly.
 *
 * Two of these rules exist specifically to stop a recorded figure being lost:
 * `seededKey` (an edit must not be overwritten by a recalculation of the same inputs) and
 * `effectiveTax` (a blank field means "use the calculated figure", not "withhold nothing").
 */

export interface WhtInputs {
  categoryId: string;
  vendorId: string;
  grossAmount: string;
  date: string;
}

/** Identifies the inputs a calculation belongs to. When this changes, the figures are stale. */
export function calculationKey({ categoryId, vendorId, grossAmount, date }: WhtInputs): string {
  const gross = Number(grossAmount);
  return `${categoryId}|${vendorId}|${Number.isFinite(gross) ? gross : 0}|${date}`;
}

/**
 * The key a form is already showing saved figures for, or null when it is empty.
 *
 * Opening an existing expense seeds the tax that was actually withheld — possibly a deliberate
 * override with a reason attached. Treating those figures as already applied stops the first
 * preview from replacing them, which would silently discard the override and its audit trail.
 */
export function seededKey(value: WhtFormValue, key: string): string | null {
  return value.amount.trim() !== "" || value.rate.trim() !== "" ? key : null;
}

/**
 * The tax that will actually be recorded. A blank field is not zero — it means the operator has
 * not overridden anything, so the server's calculated figure stands.
 */
export function effectiveTax(value: WhtFormValue, preview: WhtCalculation | null): number {
  if (value.amount.trim() !== "") {
    const entered = Number(value.amount);
    if (Number.isFinite(entered)) return entered;
  }
  return preview?.isWhtApplicable ? preview.whtAmount : 0;
}

/** True only when the figure genuinely differs from what the rate table produces. */
export function isOverridden(value: WhtFormValue, preview: WhtCalculation | null): boolean {
  if (preview == null || !preview.isWhtApplicable) return false;
  return effectiveTax(value, preview) !== preview.whtAmount;
}

/** Gross minus tax — always derived, never entered, so the three can never disagree. */
export function netPaid(grossAmount: string, value: WhtFormValue, preview: WhtCalculation | null): number {
  const gross = Number(grossAmount);
  if (!Number.isFinite(gross)) return 0;
  return gross - effectiveTax(value, preview);
}

/** Editing the rate drives the amount. Rounded half-up to paisa, matching the server. */
export function fromRate(value: WhtFormValue, rate: string, grossAmount: string): WhtFormValue {
  if (rate.trim() === "") return { ...value, rate };
  const parsed = Number(rate);
  const gross = Number(grossAmount);
  // A blank gross parses as 0, so guard on the value rather than on isFinite: writing "0" into
  // the tax field would read back as a deliberate "withhold nothing" override.
  if (!Number.isFinite(parsed) || !Number.isFinite(gross) || gross <= 0) return { ...value, rate };
  return { ...value, rate, amount: String(Math.round(gross * parsed) / 100) };
}

/** Editing the amount back-computes the rate, so a figure copied off a vendor invoice still
 *  leaves a percentage behind for the s.165 statement. */
export function fromAmount(value: WhtFormValue, amount: string, grossAmount: string): WhtFormValue {
  if (amount.trim() === "") return { ...value, amount };
  const parsed = Number(amount);
  const gross = Number(grossAmount);
  if (!Number.isFinite(parsed) || !Number.isFinite(gross) || gross <= 0) return { ...value, amount };
  return { ...value, amount, rate: String(Number(((parsed * 100) / gross).toFixed(4))) };
}

/** Figures adopted from a fresh calculation. The override reason is cleared because the basis
 *  it was written against has changed. */
export function fromPreview(preview: WhtCalculation): WhtFormValue {
  return {
    rate: preview.isWhtApplicable ? String(Number(preview.rate.toFixed(4))) : "",
    amount: preview.isWhtApplicable ? String(preview.whtAmount) : "",
    overrideReason: "",
  };
}
