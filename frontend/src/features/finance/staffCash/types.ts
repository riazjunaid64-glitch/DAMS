import type { FinanceAttachmentInfo } from "../../../api/financeAttachments.ts";

/** One person's float, as the overview and the statement both send it. */
export type Holder = {
  financeAccountId: number;
  personName: string;
  accountName: string;
  isActive: boolean;
  openingBalance: number;
  /** Above zero the person holds company cash; below zero the company owes them. */
  currentBalance: number;
  outstandingSince: string | null;
  /** Null when the float is settled. */
  daysOutstanding: number | null;
  lastActivityDate: string | null;
  transactionCount: number;
};

export type Overview = {
  totalHeldByStaff: number;
  totalOwedToStaff: number;
  netStaffBalance: number;
  holdingCount: number;
  owedCount: number;
  /** Holders first, biggest balance first, then name. */
  holders: Holder[];
};

export type MovementType = "FundsGiven" | "FundsReturned";

/** A money move in or out of the float, or an expense paid from it. */
export type HistoryItem = {
  recordType: "Transfer" | "Expense";
  recordId: number;
  /** "Money received" / "Money returned" for a transfer; "Expense" for an expense. */
  kind: string;
  date: string;
  /** "Received from Meezan Bank" for a transfer; the category name for an expense. */
  description: string;
  /** A transfer's reference; an expense's payee. */
  reference: string | null;
  projectName: string | null;
  /** Signed. An expense is the net paid: the tax withheld never leaves the person's pocket. */
  amount: number;
  grossAmount: number;
  whtAmount: number;
  runningBalance: number;
  movementType: MovementType | null;
  counterpartyFinanceAccountId: number | null;
  counterpartyFinanceAccountName: string | null;
  note: string | null;
  concurrencyToken: string | null;
  attachment: FinanceAttachmentInfo | null;
};

export type Statement = { holder: Holder; items: HistoryItem[]; hasMore: boolean; totalCount: number };
