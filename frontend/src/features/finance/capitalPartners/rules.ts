import { formatPickerDate } from "../../../components/ui/pickerFormat.ts";
import type { AccountOption, Partner, Transaction } from "./types.ts";

/** The server's tolerance for the active shares adding up to 100 (`CapitalPartnerService.ShareTolerance`). */
export const SHARE_TOLERANCE = 0.01;

/** A typed share as a number; an empty or half-typed box counts as 0, as the old table did. */
export const toNumber = (value: string): number => {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : 0;
};

/** Rounded to the 4 decimals a share is stored with, so float dust never shows. */
const roundShare = (value: number) => Math.round(value * 10_000) / 10_000;

/** Adds up the shares of the partners whose switch is on. */
export function activeShareTotal(rows: readonly { isActive: boolean; share: number }[]): number {
  return roundShare(rows.filter((row) => row.isActive).reduce((sum, row) => sum + row.share, 0));
}

/**
 * True when the total is within the server's tolerance of 100. Compared in whole units of the 4th
 * decimal: the server uses decimals, so 99.99 is inside the tolerance, while in floats it is
 * 0.0100000000000051 away.
 */
export const sharesAddUp = (total: number): boolean => Math.round(Math.abs(total - 100) * 10_000) <= SHARE_TOLERANCE * 10_000;

/** A share with up to 4 decimals and a % sign: "40%", "14.9999%". */
export const formatShare = (value: number): string =>
  `${Number(value).toLocaleString("en-PK", { maximumFractionDigits: 4 })}%`;

/** Debit types: they take the balance down (Withdrawal and Loss share), the rest bring it up. */
export const isDebit = (type: string): boolean => type === "Withdrawal" || type === "LossShare";

const TYPE_LABELS: Record<string, string> = {
  OpeningBalance: "Opening balance",
  Contribution: "Contribution",
  Withdrawal: "Withdrawal",
  ProfitShare: "Profit share",
  LossShare: "Loss share",
};

export const typeLabel = (type: string): string => TYPE_LABELS[type] ?? type.replace(/([a-z])([A-Z])/g, "$1 $2");

/**
 * The calendar day out of the text the server sent ("2026-09-30T00:00:00" → "2026-09-30"). Every
 * finance date is a Pakistani business date; parsing it with `new Date(value)` would show the day
 * before to anyone west of Pakistan.
 */
export function serverDay(value: string | null | undefined): string | null {
  const match = value ? /^(\d{4}-\d{2}-\d{2})/.exec(value) : null;
  return match ? match[1]! : null;
}

/** "Sep 30, 2026" for a server date, or "" when there is none. */
export const showDay = (value: string | null | undefined): string => formatPickerDate(serverDay(value));

export type StatementLine = Transaction & {
  /** The movement's effect on the balance: negative for Withdrawal and Loss share. */
  signed: number;
  /** The balance after this movement. */
  balance: number;
};

/**
 * The running balance, built from the opening figure with the server's sign rule (Withdrawal and
 * Loss share subtract) and rounded to 2 decimals after every step, so the last line equals the
 * closing balance the server reported.
 */
export function runningBalance(opening: number, transactions: readonly Transaction[]): StatementLine[] {
  let running = opening;
  return transactions.map((row) => {
    const signed = isDebit(row.type) ? -row.amount : row.amount;
    running = Math.round((running + signed) * 100) / 100;
    return { ...row, signed, balance: running };
  });
}

/** The line under a movement's type: "Meezan Bank · CH-1001", leaving out what is empty. */
export function accountAndReference(row: Transaction, accountNames: ReadonlyMap<number, string>): string {
  const account = row.financeAccountId != null ? accountNames.get(row.financeAccountId) : undefined;
  return [account, row.reference?.trim()].filter(Boolean).join(" · ");
}

const compact = (text: string) => text.toLowerCase().replace(/\s+/g, "");

/** The note, unless it only repeats the type name ("Contribution" on a Contribution, "ProfitShare" on a Profit share). */
export function noteToShow(row: Transaction): string | null {
  const note = row.note?.trim();
  if (!note) return null;
  return compact(note) === compact(typeLabel(row.type)) || compact(note) === compact(row.type) ? null : note;
}

/** "From" later than "To" is refused, before and after the server. Either side may be empty. */
export function statementRangeError(from: string, to: string): string | null {
  return from && to && from > to ? "The From date cannot be after the To date." : null;
}

/** "Name · Holder", as the cash and bank accounts are listed. */
export const cashAccountLabel = (account: AccountOption): string => `${account.name} · ${account.accountHolderName}`;

/** The accounts of type Capital: the server sends the type as its name or as the number 6. */
export const isCapitalAccount = (account: AccountOption): boolean => account.type === "Capital" || Number(account.type) === 6;

/**
 * The Capital accounts a partner can be linked to: active ones not linked to another partner, plus
 * the partner's own account, whatever its state ("Name (Inactive)" when it is inactive).
 */
export function capitalAccountChoices(
  accounts: readonly AccountOption[],
  partners: readonly Partner[],
  editing: Partner | null,
): { value: string; label: string }[] {
  const takenByOthers = new Set(partners.filter((row) => row.id !== editing?.id && row.financeAccountId !== null).map((row) => row.financeAccountId));
  return accounts
    .filter(isCapitalAccount)
    .filter((account) => account.id === editing?.financeAccountId || (account.isActive && !takenByOthers.has(account.id)))
    .map((account) => ({ value: String(account.id), label: `${account.name}${account.isActive ? "" : " (Inactive)"}` }));
}

/** What the statement's header line says: "40% share · Capital — Riaz Junaid", the share alone on a phone. */
export function statementSubtitle(partner: Pick<Partner, "profitSharePercent" | "financeAccountName">, phone: boolean): string {
  const share = `${formatShare(partner.profitSharePercent)} share`;
  return !phone && partner.financeAccountName ? `${share} · ${partner.financeAccountName}` : share;
}

/** A single share must be between 0 and 100: the server refuses anything else, whatever the total. */
export const shareInRange = (share: number): boolean => share >= 0 && share <= 100;
