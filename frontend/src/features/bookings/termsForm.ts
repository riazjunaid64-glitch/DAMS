import { formatPkr } from "../../utils/currency.ts";

/** The booking-amount chips: a share of the agreed price, or "custom" for any amount. */
export const BOOKING_PERCENTS = [5, 10, 15, 20, 25, 30] as const;
export const CUSTOM_CHIP = "custom";
export const BOOKING_CHIPS = [
  ...BOOKING_PERCENTS.map((percent) => ({ value: String(percent), label: `${percent}%` })),
  { value: CUSTOM_CHIP, label: "Custom" },
];

const toNumber = (text: string): number => (text.trim() === "" ? 0 : Number(text));

/** What a chip works out to: its share of the AGREED price (not the net), to the whole rupee. */
export const chipAmount = (agreed: number, percent: number): number => Math.round((agreed * percent) / 100);

/** The chip that stands for an amount already saved, or Custom when none does. */
export function chipFor(amount: number, agreed: number): string {
  const match = BOOKING_PERCENTS.find((percent) => amount > 0 && chipAmount(agreed, percent) === amount);
  return match ? String(match) : CUSTOM_CHIP;
}

export interface TermsFigures {
  agreed: number;
  discountPercent: number;
  /** Discount in rupees, rounded to paisa like the server does. */
  discount: number;
  net: number;
  bookingAmount: number;
  leftForInstallments: number;
}

export interface TermsFields {
  agreedSalePrice: string;
  discountPercent: string;
  chip: string;
  /** The amount box: locked to the chip's amount, free to type in on Custom. */
  customAmount: string;
}

/** The figures the form shows and checks, from what has been typed. */
export function termsFigures(fields: TermsFields): TermsFigures {
  const agreed = toNumber(fields.agreedSalePrice);
  const discountPercent = toNumber(fields.discountPercent);
  const discount = Math.round(agreed * discountPercent) / 100;
  const net = agreed - discount;
  const bookingAmount = fields.chip === CUSTOM_CHIP ? toNumber(fields.customAmount) : chipAmount(agreed, Number(fields.chip));
  return { agreed, discountPercent, discount, net, bookingAmount, leftForInstallments: net - bookingAmount };
}

export interface TermsErrors {
  discount?: string;
  amount?: string;
}

/** Messages under the fields. `received` is what has already been paid, which the amount cannot go below. */
export function termsErrors(figures: TermsFigures, received: number): TermsErrors {
  const errors: TermsErrors = {};
  if (figures.discountPercent < 0 || figures.discountPercent > 100) errors.discount = "Enter a discount between 0 and 100.";
  if (figures.bookingAmount > figures.net) errors.amount = `Can't be more than the net price, ${formatPkr(figures.net)}`;
  else if (received > 0 && figures.bookingAmount < received) errors.amount = `Can't be less than ${formatPkr(received)} already received`;
  return errors;
}

/** Save needs a price and an amount, and nothing on screen in red. */
export function canSaveTerms(figures: TermsFigures, errors: TermsErrors): boolean {
  return figures.agreed > 0 && figures.bookingAmount > 0 && !errors.amount && !errors.discount;
}
