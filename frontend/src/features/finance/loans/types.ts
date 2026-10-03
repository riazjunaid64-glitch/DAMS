import type { FinanceAttachmentInfo } from "../../../api/financeAttachments.ts";

export type Loan = {
  id: number;
  name: string;
  lenderName: string | null;
  financeAccountId: number;
  financeAccountName: string;
  isActive: boolean;
  /** Principal brought forward as the liability account's opening balance; it sits outside "Total borrowed". */
  openingBalance: number;
  drawnPrincipal: number;
  repaidPrincipal: number;
  interestPaid: number;
  currentBalance: number;
  transactionCount: number;
  concurrencyToken: string;
};

/** A Liability account a loan can be linked to, and the loan already using it. */
export type LoanAccount = { id: number; name: string; accountHolderName: string; isActive: boolean; linkedLoanId: number | null; linkedLoanName: string | null };

/** A cash or bank account money moves through. */
export type CashAccount = { id: number; name: string; accountHolderName: string; isActive: boolean };

export type MovementType = "Drawdown" | "Repayment";

export type LoanTransaction = {
  id: number;
  loanId: number;
  type: MovementType;
  /** Unsigned: the type says which way it moved. */
  principalAmount: number;
  interestAmount: number;
  totalCashMovement: number;
  date: string;
  financeAccountId: number;
  financeAccountName: string;
  reference: string | null;
  note: string | null;
  /** Outstanding principal straight after this movement. */
  runningBalance: number;
  createdAt: string;
  updatedAt: string;
  concurrencyToken: string;
  attachment: FinanceAttachmentInfo | null;
};

export type Statement = { loan: Loan; items: LoanTransaction[]; hasMore: boolean };
