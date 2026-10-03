import { movementDate, rupees } from "../loans/rules.ts";
import type { Holder, HistoryItem, MovementType } from "./types.ts";

/** "1 movement", "6 movements". */
const counted = (count: number, one: string, many: string) => `${count.toLocaleString("en-PK")} ${count === 1 ? one : many}`;

/** "2 people", "1 person": the note under a stat card. */
export const peopleNote = (count: number) => counted(count, "person", "people");

/** Green while the person holds company cash, red while the company owes them, grey when settled. */
export function balanceClass(balance: number): string {
  if (balance > 0) return "text-success";
  if (balance < 0) return "text-danger";
  return "text-ink-muted";
}

/** The coloured line under a name. The list speaks of "them"; the open person's panel of "this person". */
export function holderStatus(holder: Pick<Holder, "currentBalance">, place: "list" | "panel" = "list"): string {
  if (holder.currentBalance > 0) return "Holding company cash";
  if (holder.currentBalance < 0) return place === "list" ? "Company owes them" : "Company owes this person";
  return "Settled";
}

/** "12 days open · 6 movements"; a settled person has no days open, so only "14 movements". */
export function holderActivity(holder: Pick<Holder, "daysOutstanding" | "transactionCount">): string {
  const movements = counted(holder.transactionCount, "movement", "movements");
  return holder.daysOutstanding == null ? movements : `${counted(holder.daysOutstanding, "day", "days")} open · ${movements}`;
}

/** "Open since Sep 19, 2026 · 12 days", or "Fully settled". */
export function openSince(holder: Pick<Holder, "outstandingSince" | "daysOutstanding">): string {
  if (!holder.outstandingSince || holder.daysOutstanding == null) return "Fully settled";
  return `Open since ${movementDate(holder.outstandingSince)} · ${counted(holder.daysOutstanding, "day", "days")}`;
}

/** "+Rs 60,000" or "−Rs 48,000". */
export const signedRupees = (amount: number) => `${amount < 0 ? "−" : "+"}${rupees(Math.abs(amount))}`;

/** An expense is named by its category (sent as the description); a money move by its kind. */
export const movementName = (row: HistoryItem) => (row.recordType === "Expense" ? row.description : row.kind);

/** "Received from Meezan Bank" for a money move; the project, or "General", for an expense. */
export const movementPlace = (row: HistoryItem) => (row.recordType === "Expense" ? row.projectName || "General" : row.description);

/** A money move's reference, or an expense's payee. */
export const movementRef = (row: HistoryItem) => (row.reference ? `Ref: ${row.reference}` : null);

/** "Gross Rs 50,000 · tax withheld Rs 2,000", only when tax was withheld. */
export const taxLine = (row: HistoryItem) =>
  row.whtAmount > 0 ? `Gross ${rupees(row.grossAmount)} · tax withheld ${rupees(row.whtAmount)}` : null;

/** Only money moves are corrected or deleted here; an expense is changed in the Finance home table. */
export const canChangeMovement = (holder: Pick<Holder, "isActive">, row: HistoryItem) => holder.isActive && row.recordType === "Transfer";

/** What opened the movement popup. It decides the title and the amount filled in. */
export type MovementIntent = "give" | "settle" | "return";

/**
 * The second button: "Give money", or "Settle amount owed" with what is owed filled in once the
 * person has spent more than they were given.
 */
export function giveAction(holder: Pick<Holder, "currentBalance">): { intent: MovementIntent; label: string; amount: string } {
  return holder.currentBalance < 0
    ? { intent: "settle", label: "Settle amount owed", amount: String(Math.abs(holder.currentBalance)) }
    : { intent: "give", label: "Give money", amount: "" };
}

/** Cash can only come back while the person holds some; the amount they hold is filled in. */
export const canReturnCash = (holder: Pick<Holder, "isActive" | "currentBalance">) => holder.isActive && holder.currentBalance > 0;
export const returnAmount = (holder: Pick<Holder, "currentBalance">) => String(Math.max(0, holder.currentBalance));

export const MOVEMENT_TYPES: { value: MovementType; label: string }[] = [
  { value: "FundsGiven", label: "Money given (account → staff)" },
  { value: "FundsReturned", label: "Cash returned (staff → account)" },
];

/**
 * What the Movement dropdown offers. Cash can only be returned while the person holds some, so a
 * dialog opened to give money or settle what is owed cannot be switched into a return; a correction
 * of a return keeps the option it already has.
 */
export function movementChoices(holder: Pick<Holder, "isActive" | "currentBalance">, correcting: MovementType | null) {
  return canReturnCash(holder) || correcting === "FundsReturned"
    ? MOVEMENT_TYPES
    : MOVEMENT_TYPES.filter((choice) => choice.value === "FundsGiven");
}

export const accountLabel = (type: MovementType) => (type === "FundsGiven" ? "Paid from account" : "Returned to account");

/** The popup's name follows the movement chosen; a settle keeps its name while the movement is still money given. */
export function movementTitle(type: MovementType, intent: MovementIntent, correcting: boolean): string {
  if (correcting) return "Correct movement";
  if (type === "FundsReturned") return "Record cash returned";
  return intent === "settle" ? "Settle amount owed" : "Give money";
}

export { movementDate, rupees };
