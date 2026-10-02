import { formatDay } from "../../lib/dates.ts";
import { formatPkr } from "../../utils/currency.ts";
import { PAYMENT_METHODS, referenceRequired } from "../bookings/paymentForm.ts";
import { commissionAllocationPercent, commissionBasisFor, commissionBases, rebateBasisFor, rebateBases, usesStoredBasisAmount } from "./state.ts";
import type { CommissionFormState, RebateFormState } from "./state.ts";
import type { BookingWorkspace, CalculationBasis, Commission, MoneyMovement, Rebate, RebateMethod } from "./types.ts";

/** The partner types the directory accepts (CommissionRebateService.Directory), as the server spells them. */
/** A plain "name@domain.tld" check. The server does not look at the shape of an email, so the forms do. */
export const isValidEmail = (value: string) => /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value.trim());

export const PARTNER_TYPES = ["Agency", "Broker", "Dealer", "Referral Partner", "Introducer", "Marketing Partner", "External Sales Agent", "Other"]
  .map((value) => ({ value, label: value.charAt(0) + value.slice(1).toLowerCase() }));

const BASIS_NAMES: Record<string, string> = {
  AgreedSalePrice: "Agreed sale price",
  NetSalePriceAfterDiscount: "Net sale price",
  AmountActuallyCollected: "Amount collected",
  BookingAmountReceived: "Booking amount received",
  ManuallyApprovedAmount: "Manually approved amount",
};
export const basisName = (basis: CalculationBasis) => BASIS_NAMES[basis] ?? basis;

export const basisOptions = (bases: CalculationBasis[]) => bases.map((value) => ({ value, label: basisName(value) }));

/** "Agency · 2% of net sale price", "Referral partner · Fixed amount". */
export function commissionSummary(c: Pick<Commission, "partnerType" | "calculationType" | "percentageRate" | "calculationBasis">): string {
  const how = c.calculationType === "Percentage" ? `${c.percentageRate ?? 0}% of ${basisName(c.calculationBasis).toLowerCase()}` : "Fixed amount";
  const type = c.partnerType ? c.partnerType.charAt(0) + c.partnerType.slice(1).toLowerCase() : "Partner";
  return `${type} · ${how}`;
}

/** The three ways the popup offers, in the order it lists them. Old data may also carry Credit note / Other. */
export const REBATE_WAYS: { value: RebateMethod; label: string }[] = [
  { value: "CashOrBankPayment", label: "Pay by cash or bank" },
  { value: "InstallmentAdjustment", label: "Take off installments" },
  { value: "OutstandingBalanceReduction", label: "Reduce the outstanding balance" },
];
const LEGACY_WAYS: Partial<Record<RebateMethod, string>> = { CreditNote: "Credit note", Other: "Other" };
export const rebateWayLabel = (method: RebateMethod) => REBATE_WAYS.find((w) => w.value === method)?.label ?? LEGACY_WAYS[method] ?? method;
/** The way a rebate reads on its block: how the customer gets it, in the past tense. */
export const rebateWayShort = (method: RebateMethod): string => ({
  CashOrBankPayment: "Paid by cash or bank",
  InstallmentAdjustment: "Taken off installments",
  OutstandingBalanceReduction: "Reduces the balance",
  CreditNote: "Credit note",
  Other: "Other",
})[method];

/** Every figure a preview reads off the booking: the same ones the server recalculates on save. */
export type BookingFigures = Pick<BookingWorkspace, "agreedSalePrice" | "netSalePrice" | "amountCollected">;

const round = (value: number) => (Number.isFinite(value) && value > 0 ? Math.round(value * 100) / 100 : 0);

function basisAmount(basis: CalculationBasis, existing: { calculationBasis: CalculationBasis; basisAmount: number } | null, supported: CalculationBasis[], figures: BookingFigures): number {
  // For a record on a basis the form cannot re-pick, the server keeps the stored amount.
  if (usesStoredBasisAmount(basis, existing?.calculationBasis, supported)) return existing?.basisAmount ?? 0;
  if (basis === "AgreedSalePrice") return figures.agreedSalePrice;
  if (basis === "NetSalePriceAfterDiscount") return figures.netSalePrice;
  if (basis === "AmountActuallyCollected") return figures.amountCollected;
  return existing?.basisAmount ?? 0;
}

/** The commission the server will work out for this form, including the partner's allocation share. */
export function commissionPreview(form: CommissionFormState, existing: Commission | null, figures: BookingFigures): number {
  const basis = commissionBasisFor(form, existing);
  const raw = form.calculationType === "Percentage"
    ? basisAmount(basis, existing, commissionBases, figures) * Number(form.percentageRate) / 100
    : Number(form.fixedAmount);
  return round(raw * commissionAllocationPercent(form, existing) / 100);
}

/** The rebate before any adjustment the record already carries, and the final figure. */
export function rebatePreview(form: RebateFormState, existing: Rebate | null, figures: BookingFigures): { amount: number; final: number } {
  const basis = rebateBasisFor(form, existing);
  const amount = round(form.calculationType === "Percentage"
    ? basisAmount(basis, existing, rebateBases, figures) * Number(form.percentageRate) / 100
    : Number(form.fixedAmount));
  return { amount, final: Math.max(0, Math.round((amount + (existing?.adjustmentAmount ?? 0)) * 100) / 100) };
}

export type FormErrors<T> = Partial<Record<keyof T, string>>;

/** What is wrong with the commission popup. `paid` is what has already gone out: the total cannot be lower. */
export function commissionErrors(form: CommissionFormState, existing: Commission | null, figures: BookingFigures): FormErrors<CommissionFormState> {
  const errors: FormErrors<CommissionFormState> = {};
  if (!form.partnerId) errors.partnerId = "Choose the partner.";
  // A rule-driven commission takes its figures from the rule: nothing here to check.
  if (existing && !existing.isManual) return errors;
  if (form.calculationType === "Percentage") {
    const rate = Number(form.percentageRate);
    if (!form.percentageRate || !(rate > 0) || rate > 100) errors.percentageRate = "Enter a percentage between 0 and 100.";
  } else if (!form.fixedAmount || !(Number(form.fixedAmount) > 0)) {
    errors.fixedAmount = "Enter the commission amount.";
  }
  if (!errors.percentageRate && !errors.fixedAmount && existing && existing.paidAmount > 0) {
    const total = commissionPreview(form, existing, figures) + existing.adjustmentAmount;
    if (total < existing.paidAmount) {
      const message = `Can't be less than ${formatPkr(existing.paidAmount)} already paid.`;
      if (form.calculationType === "Percentage") errors.percentageRate = message; else errors.fixedAmount = message;
    }
  }
  return errors;
}

export function rebateErrors(form: RebateFormState, existing: Rebate | null, figures: BookingFigures): FormErrors<RebateFormState> {
  const errors: FormErrors<RebateFormState> = {};
  if (form.calculationType === "Percentage") {
    const rate = Number(form.percentageRate);
    if (!form.percentageRate || !(rate > 0) || rate > 100) errors.percentageRate = "Enter a percentage between 0 and 100.";
  } else if (!form.fixedAmount || !(Number(form.fixedAmount) > 0)) {
    errors.fixedAmount = "Enter the rebate amount.";
  }
  if (!errors.percentageRate && !errors.fixedAmount) {
    const { final } = rebatePreview(form, existing, figures);
    if (final > figures.netSalePrice) {
      const message = `Can't be more than the net sale price of ${formatPkr(figures.netSalePrice)}.`;
      if (form.calculationType === "Percentage") errors.percentageRate = message; else errors.fixedAmount = message;
    } else if (existing && existing.appliedOrPaidAmount > 0 && final < existing.appliedOrPaidAmount) {
      const message = `Can't be less than ${formatPkr(existing.appliedOrPaidAmount)} already given.`;
      if (form.calculationType === "Percentage") errors.percentageRate = message; else errors.fixedAmount = message;
    }
  }
  return errors;
}

/** A payment out to a partner, or a rebate paid by cash or bank. */
export interface PaymentFields {
  amount: string;
  method: string;
  accountId: string;
  reference: string;
  date: string;
}

export function paymentOutErrors(fields: PaymentFields, limit: number): FormErrors<PaymentFields> {
  const errors: FormErrors<PaymentFields> = {};
  const amount = Number(fields.amount);
  if (!fields.amount || !(amount > 0)) errors.amount = "Enter the amount.";
  else if (amount > limit) errors.amount = `Can't be more than ${formatPkr(limit)}`;
  if (!fields.accountId) errors.accountId = "Choose the account it is paid from.";
  if (referenceRequired(fields.method) && !fields.reference.trim()) errors.reference = "Enter the cheque or transfer number.";
  if (!fields.date) errors.date = "Choose the payment date.";
  return errors;
}

/** The fields of the Apply rebate popup that depend on the way: installment and balance need no account. */
export interface ApplyFields extends PaymentFields {
  installmentId: string;
  notes: string;
}

export function applyRebateErrors(method: RebateMethod, fields: ApplyFields, limit: number): FormErrors<ApplyFields> {
  if (method === "CashOrBankPayment") return paymentOutErrors(fields, limit);
  const errors: FormErrors<ApplyFields> = {};
  const amount = Number(fields.amount);
  if (method === "InstallmentAdjustment" && !fields.installmentId) errors.installmentId = "Choose the installment.";
  if (!fields.amount || !(amount > 0)) errors.amount = "Enter the amount.";
  else if (amount > limit) errors.amount = `Can't be more than ${formatPkr(limit)}`;
  if (!fields.date) errors.date = "Choose the date.";
  if (method === "CreditNote" && !fields.reference.trim()) errors.reference = "Enter the credit note number.";
  if (method === "Other" && !fields.notes.trim()) errors.notes = "Explain how the customer gets it.";
  return errors;
}

/**
 * The record a save just created, found by what is new: the server answers with the whole workspace,
 * and the proof has to go onto exactly the new payment rather than onto a look-alike.
 */
export function newMovementId(before: MoneyMovement[], after: MoneyMovement[]): number | null {
  const known = new Set(before.map((movement) => movement.id));
  return after.find((movement) => !known.has(movement.id))?.id ?? null;
}

/** What is left to pay or give after the movement `amount - reversedAmount` is reversed. */
export const remainingAfterReversal = (outstanding: number, movement: Pick<MoneyMovement, "amount" | "reversedAmount">) =>
  Math.round((outstanding + movement.amount - movement.reversedAmount) * 100) / 100;

/** Whether a server message is the one about a partner without bank details, so the popup can link to the partner. */
export const isMissingBankDetails = (message: string) => /bank name|bank details|account title|\biban\b/i.test(message);


/** "Bank transfer", "Cash"; a rebate taken off installments or the balance is not a payment method. */
function methodLabel(row: MoneyMovement): string {
  if (row.rebateMethod === "InstallmentAdjustment") return "Taken off installment";
  if (row.rebateMethod === "OutstandingBalanceReduction") return "Taken off balance";
  if (row.rebateMethod === "CreditNote") return "Credit note";
  return PAYMENT_METHODS.find((method) => method.value === row.paymentMethod)?.label ?? row.paymentMethod ?? "";
}

/** The first line of a row: "Aug 10, 2026 · Bank transfer · TRX-66120". */
export const movementTitle = (row: MoneyMovement) => [formatDay(row.date), methodLabel(row), row.reference].filter(Boolean).join(" · ");
/** The second line: "Meezan Bank — 0123 · by accountant". */
export const movementDetail = (row: MoneyMovement) => [row.financeAccountName, row.recordedByName && `by ${row.recordedByName}`].filter(Boolean).join(" · ");
