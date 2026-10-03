import type { Option } from "../../../components/ui";
import { formatRs } from "../whtTypes.ts";
import { showDay } from "../capitalPartners/rules.ts";

export type Account = {
  id: number;
  name: string;
  type: number | string;
  accountHolderName: string;
  openingBalance: number;
  ledgerCode: string | null;
  displayOrder: number;
  systemRole: number | string;
  isSystemAccount: boolean;
  bankOrWalletName: string | null;
  description: string | null;
  isActive: boolean;
  revenueReceived: number;
  expensesPaid: number;
  whtWithheld: number;
  whtDeposited: number;
  netMovement: number;
  currentBalance: number;
  transactionCount: number;
  concurrencyToken: string;
};

/** `amount` is the cash effect (net of tax withheld, for expenses); `grossAmount` is the invoice total. */
export type Transaction = {
  kind: string;
  recordId: number;
  date: string;
  label: string;
  reference: string | null;
  projectName: string;
  amount: number;
  grossAmount: number;
  whtAmount: number;
};

export type Overview = {
  activeAccounts: number;
  inactiveAccounts: number;
  totalBalance: number;
  holderBalances: { accountHolderName: string; accountCount: number; currentBalance: number }[];
  openingDebitTotal: number;
  openingCreditTotal: number;
  /** The go-live date from Finance settings, or null until one is saved. */
  goLiveDate: string | null;
  holderNames: string[];
};

// The server's account types: 1 Cash, 2 Bank, 3 Mobile wallet, 4 Other, 5 Liability, 6 Capital,
// 7 Fixed asset, 8 Receivable, 9 Work in progress, 10 Staff float. It may send the number or the name.
const TYPE_NAMES: Record<number, string> = {
  1: "Cash", 2: "Bank", 3: "Mobile wallet", 4: "Other", 5: "Liability", 6: "Capital",
  7: "Fixed asset", 8: "Receivable", 9: "Work in progress", 10: "Staff float",
};
const TYPE_BY_NAME: Record<string, number> = {
  Cash: 1, Bank: 2, MobileWallet: 3, Other: 4, Liability: 5, Capital: 6, FixedAsset: 7, Receivable: 8, WorkInProgress: 9, StaffFloat: 10,
};

/** The type list in the board's order, the ten values the server knows. */
export const TYPE_OPTIONS: Option[] = [1, 2, 3, 4, 10, 7, 8, 9, 5, 6].map((value) => ({ value: String(value), label: TYPE_NAMES[value]! }));

export const typeValue = (value: number | string): number => (typeof value === "number" ? value : (TYPE_BY_NAME[value] ?? Number(value)));
export const typeName = (value: number | string): string => TYPE_NAMES[typeValue(value)] ?? "—";
export const isCashLike = (value: number | string): boolean => typeValue(value) <= 4;

// 3 Customer deposits, 4 Customer receivables, 5 Commission payable: worked out per booking and per
// commission, so they take no typed opening balance. Customer refunds payable (2) does.
const BOOKING_DRIVEN: Record<string, number> = { CustomerDeposits: 3, CustomerReceivables: 4, CommissionPayable: 5 };
export function takesNoTypedOpening(role: number | string): boolean {
  const value = typeof role === "number" ? role : (BOOKING_DRIVEN[role] ?? Number(role));
  return value >= 3 && value <= 5;
}

type GroupDef = { label: string; types: number[] };
const GROUPS: GroupDef[] = [
  { label: "Cash & bank", types: [1, 2, 3, 4] },
  { label: "Cash held by staff", types: [10] },
  { label: "Fixed assets", types: [7] },
  { label: "Work in progress", types: [9] },
  { label: "Receivables", types: [8] },
  { label: "Liabilities", types: [5] },
  { label: "Capital", types: [6] },
];

export type Totals = Pick<Account, "openingBalance" | "revenueReceived" | "expensesPaid" | "currentBalance">;

export function sumAccounts(accounts: readonly Account[]): Totals {
  return accounts.reduce<Totals>((total, account) => ({
    openingBalance: total.openingBalance + account.openingBalance,
    revenueReceived: total.revenueReceived + account.revenueReceived,
    expensesPaid: total.expensesPaid + account.expensesPaid,
    currentBalance: total.currentBalance + account.currentBalance,
  }), { openingBalance: 0, revenueReceived: 0, expensesPaid: 0, currentBalance: 0 });
}

export type AccountGroup = { label: string; rows: Account[]; totals: Totals };

/** The seven groups in order, each with its accounts as the server sent them. Empty groups are left out. */
export function groupAccounts(accounts: readonly Account[]): AccountGroup[] {
  return GROUPS
    .map(({ label, types }) => {
      const rows = accounts.filter((account) => types.includes(typeValue(account.type)));
      return { label, rows, totals: sumAccounts(rows) };
    })
    .filter((group) => group.rows.length > 0);
}

export type AccountTableRow =
  | { kind: "group"; key: string; label: string; total: number }
  | { kind: "row"; key: string; account: Account }
  | { kind: "subtotal"; key: string; label: string; totals: Totals };

/**
 * The rows the table shows: a heading per group, its accounts, then (on desktop) a subtotal row.
 * On a phone the heading carries the group's Current balance instead, so no subtotal row is added.
 */
export function tableRows(groups: readonly AccountGroup[], withSubtotals: boolean): AccountTableRow[] {
  return groups.flatMap<AccountTableRow>((group) => [
    { kind: "group", key: `group-${group.label}`, label: group.label, total: group.totals.currentBalance },
    ...group.rows.map<AccountTableRow>((account) => ({ kind: "row", key: `account-${account.id}`, account })),
    ...(withSubtotals ? [{ kind: "subtotal" as const, key: `total-${group.label}`, label: `Total ${group.label.toLowerCase()}`, totals: group.totals }] : []),
  ]);
}

/** "GL 1002 · Meezan Bank": the ledger code, then the bank or wallet name, each only when set. */
export function accountSubline(account: Pick<Account, "ledgerCode" | "bankOrWalletName">): string {
  return [account.ledgerCode ? `GL ${account.ledgerCode}` : null, account.bankOrWalletName || null].filter(Boolean).join(" · ");
}

/** "Bank · Seven Ventures · GL 1002", plus " · Inactive" for an inactive account. */
export function accountSubtitle(account: Pick<Account, "type" | "accountHolderName" | "ledgerCode" | "isActive">): string {
  return [typeName(account.type), account.accountHolderName, account.ledgerCode ? `GL ${account.ledgerCode}` : null, account.isActive ? null : "Inactive"]
    .filter(Boolean).join(" · ");
}

const cents = (value: number) => Math.round(value * 100);

/** The red notice's text when opening debits and credits differ, or null when they match. */
export function openingMismatch(overview: Pick<Overview, "openingDebitTotal" | "openingCreditTotal">): string | null {
  const difference = cents(overview.openingDebitTotal) - cents(overview.openingCreditTotal);
  if (difference === 0) return null;
  const amount = formatRs(Math.abs(difference) / 100);
  return difference > 0
    ? `Opening balances don't match: debits are ${amount} more than credits.`
    : `Opening balances don't match: credits are ${amount} more than debits.`;
}

/** What the opening-balance box holds as a number: empty, "-" and "." count as 0. */
export function parseOpening(raw: string): number {
  const value = Number(raw);
  return Number.isFinite(value) ? value : 0;
}

/** The confirm's message: "Every report from the go-live date, Aug 15, 2026, will change. Meezan Bank: Rs 12,000,000 → Rs 12,500,000." */
export function openingChangeMessage(name: string, from: number, to: number, goLiveDate: string | null): string {
  const first = goLiveDate ? `Every report from the go-live date, ${showDay(goLiveDate)}, will change.` : "Every report will change.";
  return `${first} ${name}: ${formatRs(from)} → ${formatRs(to)}.`;
}

export type TransactionLine = { transaction: Transaction; balance: number };

export function withRunningBalances(transactions: readonly Transaction[], openingBalance: number): TransactionLine[] {
  let balance = openingBalance;
  return transactions.map((transaction) => {
    balance += transaction.amount;
    return { transaction, balance };
  });
}

/** Oldest first; two on the same day keep the order they were recorded in. */
export function oldestFirst(transactions: readonly Transaction[]): Transaction[] {
  return [...transactions].sort((a, b) => (a.date === b.date ? a.recordId - b.recordId : a.date < b.date ? -1 : 1));
}
