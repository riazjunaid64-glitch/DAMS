import { formatPkr } from "../../utils/currency.ts";
import type { BookingDetail, InstallmentSchedule, ScheduleItem } from "./detailTypes.ts";

export const PLAN_FREQUENCIES = [
  { value: "Monthly", label: "Monthly" },
  { value: "Quarterly", label: "Quarterly" },
  { value: "HalfYearly", label: "Half-yearly" },
  { value: "Yearly", label: "Yearly" },
];

/** The most regular installments the server accepts. */
export const MAX_INSTALLMENTS = 600;

const toNumber = (text: string): number => (text.trim() === "" ? 0 : Number(text));

export interface PlanFields {
  agreedSalePrice: string;
  discountPercent: string;
  installments: string;
  frequency: string;
  /** The due date of installment 1. */
  firstDue: string;
  possessionAmount: string;
  possessionDue: string;
}

/** Create starts from the booking's terms; Change starts from the plan that is there. */
export function planFields(booking: BookingDetail, schedule: InstallmentSchedule | null): PlanFields {
  const date = (value?: string | null) => value?.slice(0, 10) ?? "";
  if (schedule?.hasSchedule) {
    return {
      agreedSalePrice: String(schedule.agreedSalePrice),
      discountPercent: String(schedule.discountPercent ?? 0),
      installments: String(schedule.numberOfInstallments ?? 12),
      frequency: schedule.frequency ?? "Monthly",
      firstDue: date(schedule.installmentStartDate),
      possessionAmount: schedule.possessionAmount > 0 ? String(schedule.possessionAmount) : "",
      possessionDue: date(schedule.possessionDueDate),
    };
  }
  return {
    agreedSalePrice: String(booking.agreedSalePrice),
    discountPercent: String(booking.discountPercent ?? 0),
    installments: "12",
    frequency: "Monthly",
    firstDue: "",
    possessionAmount: "",
    possessionDue: "",
  };
}

export interface PlanFigures {
  agreed: number;
  discountPercent: number;
  /** Discount in rupees, rounded to paisa like the server does. */
  discount: number;
  net: number;
  installments: number;
  possession: number;
  /** Net price − booking amount received − rebate credits. */
  toSchedule: number;
  /** What is left for the regular installments once the possession amount is taken out. */
  pool: number;
  /** Each regular installment; the last one takes the rounding difference. */
  each: number;
}

/** The figures the form's summary strip shows, from what has been typed. Mirrors the server's build. */
export function planFigures(fields: PlanFields, received: number, credits: number): PlanFigures {
  const agreed = toNumber(fields.agreedSalePrice);
  const discountPercent = toNumber(fields.discountPercent);
  const discount = Math.round(agreed * discountPercent) / 100;
  const net = agreed - discount;
  const installments = Math.trunc(toNumber(fields.installments));
  const possession = toNumber(fields.possessionAmount);
  const toSchedule = net - received - credits;
  const pool = toSchedule - possession;
  // Rounded DOWN to the paisa, like the server, so the last installment carries the remainder.
  const each = installments > 0 && pool > 0 ? Math.floor((pool / installments) * 100) / 100 : 0;
  return { agreed, discountPercent, discount, net, installments, possession, toSchedule, pool, each };
}

export type PlanErrors = Partial<Record<"agreed" | "discount" | "installments" | "firstDue" | "possession" | "possessionDue", string>>;

/** What is wrong with the form, field by field. */
export function planErrors(fields: PlanFields, figures: PlanFigures): PlanErrors {
  const errors: PlanErrors = {};
  if (!(figures.agreed > 0)) errors.agreed = "Enter the agreed sale price.";
  if (figures.discountPercent < 0 || figures.discountPercent > 100) errors.discount = "Enter a discount between 0 and 100.";
  const count = toNumber(fields.installments);
  if (!Number.isInteger(count) || count < 1 || count > MAX_INSTALLMENTS) {
    errors.installments = `Enter a whole number from 1 to ${MAX_INSTALLMENTS}.`;
  }
  if (!fields.firstDue) errors.firstDue = "Choose when installment 1 is due.";
  if (figures.possession < 0) errors.possession = "Can't be negative.";
  else if (figures.possession > 0 && !fields.possessionDue) errors.possessionDue = "Choose when the possession amount is due.";

  if (!errors.agreed && !errors.discount && !errors.possession) {
    if (figures.toSchedule <= 0) errors.agreed = "Nothing is left to schedule after the booking amount and rebate credits.";
    else if (figures.pool <= 0) errors.possession = `Must be less than the ${formatPkr(figures.toSchedule)} to schedule.`;
    else if (!errors.installments && figures.each <= 0) errors.installments = "Too many installments for this amount.";
  }
  return errors;
}

export const canSavePlan = (errors: PlanErrors): boolean => Object.keys(errors).length === 0;

/** "12 monthly installments", "1 yearly installment". */
export function frequencyPhrase(frequency: string | null | undefined, count: number): string {
  const word = { Monthly: "monthly", Quarterly: "quarterly", HalfYearly: "half-yearly", Yearly: "yearly" }[frequency ?? ""] ?? "";
  return `${count} ${word ? `${word} ` : ""}${count === 1 ? "installment" : "installments"}`;
}

const isPaid = (item: ScheduleItem) => item.status === "Paid";

/** Due date order; on the same day the possession payment comes after the regular installment. */
export function orderInstallments(items: readonly ScheduleItem[]): ScheduleItem[] {
  return [...items].sort((a, b) =>
    a.dueDate.localeCompare(b.dueDate)
    || Number(a.type === "Possession") - Number(b.type === "Possession")
    || a.sequenceNumber - b.sequenceNumber);
}

/** "Installment 4" / "Possession". */
export const installmentLabel = (item: Pick<ScheduleItem, "type" | "sequenceNumber">): string =>
  item.type === "Possession" ? "Possession" : `Installment ${item.sequenceNumber}`;

export interface PlanFiguresOnScreen {
  total: number;
  paid: number;
  remaining: number;
  paidCount: number;
  count: number;
  overdue: number;
  overdueCount: number;
  next: ScheduleItem | null;
}

/** The stat cards' numbers, all read off the schedule the server sent. */
export function planStats(schedule: InstallmentSchedule): PlanFiguresOnScreen {
  const items = orderInstallments(schedule.items);
  const overdue = items.filter((item) => item.isOverdue && !isPaid(item));
  return {
    total: schedule.scheduleTotal,
    paid: schedule.schedulePaid,
    remaining: schedule.scheduleRemaining,
    paidCount: items.filter(isPaid).length,
    count: items.length,
    overdue: overdue.reduce((sum, item) => sum + item.remainingBalance, 0),
    overdueCount: overdue.length,
    next: items.find((item) => !isPaid(item)) ?? null,
  };
}

/** The phone's short list: the last two paid, then the next unpaid ones with an overdue one first. */
export function phoneInstallments(items: readonly ScheduleItem[], paidShown = 2, unpaidShown = 3): ScheduleItem[] {
  const ordered = orderInstallments(items);
  const paid = ordered.filter(isPaid).slice(-paidShown);
  // Unpaid rows are already in due-date order, which puts an overdue one ahead of any that is not.
  const unpaid = ordered.filter((item) => !isPaid(item)).slice(0, unpaidShown);
  return [...paid, ...unpaid];
}
