import type { FinanceAttachmentInfo } from "../../../api/financeAttachments.ts";

/** One row of `GET /api/finance/partners`. */
export type Partner = {
  id: number;
  name: string;
  cnic: string | null;
  ntn: string | null;
  profitSharePercent: number;
  financeAccountId: number | null;
  financeAccountName: string | null;
  isActive: boolean;
  joinedDate: string | null;
  exitedDate: string | null;
  openingBalance: number;
  contributions: number;
  withdrawals: number;
  profitShare: number;
  lossShare: number;
  closingBalance: number;
  concurrencyToken: string;
};

/** A row of `GET /api/finance/accounts/options`. `type` is the name or the number, depending on the server's serializer. */
export type AccountOption = { id: number; name: string; accountHolderName: string; isActive: boolean; type?: string | number };

export type CapitalTransactionType = "Contribution" | "Withdrawal" | "ProfitShare" | "LossShare";

export type Transaction = {
  id: number;
  type: string;
  amount: number;
  date: string;
  financeAccountId: number | null;
  reference: string | null;
  note: string | null;
  profitSharePercentSnapshot: number | null;
  attachment: FinanceAttachmentInfo | null;
};

export type Statement = {
  partnerId: number;
  partnerName: string;
  from: string | null;
  to: string | null;
  openingBalance: number;
  transactions: Transaction[];
  closingBalance: number;
};
