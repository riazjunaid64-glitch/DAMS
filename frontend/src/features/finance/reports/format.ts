import type { BalanceType } from "../trialBalance.ts";
import { formatMoney } from "../home/format.ts";

/** A zero amount in a ledger column is a dash, not "Rs 0". */
export function moneyOrDash(value: number): string {
  return value ? formatMoney(value) : "—";
}

/** "Rs 12,030,000 Dr". A balance of zero has no side. */
export function formatBalance(value: number, type: BalanceType): string {
  const normalized = String(type ?? "").toLowerCase();
  const side = normalized.startsWith("c") ? "Cr" : normalized.startsWith("d") ? "Dr" : "";
  return side && value !== 0 ? `${formatMoney(value)} ${side}` : formatMoney(value);
}

/** The server's group names come in capitals or mixed case; the screen shows only the first letter capital. */
export function sentenceCase(name: string): string {
  const lower = name.trim().toLowerCase();
  return lower.charAt(0).toUpperCase() + lower.slice(1);
}
