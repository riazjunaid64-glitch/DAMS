import type { FinanceAttachmentInfo } from "../../../api/financeAttachments.ts";
import type { WhtFormValue } from "../whtTypes.ts";

export interface FinanceAccountOption {
  id: number;
  name: string;
  type: number | string;
  accountHolderName: string;
  isActive: boolean;
}

export const isStaffFloat = (account: FinanceAccountOption) =>
  account.type === 10 || account.type === "StaffFloat" || Number(account.type) === 10;

export interface AssetPurchaseLine {
  id: number;
  date: string;
  projectId: number | null;
  projectName: string;
  assetAccountId: number;
  assetAccountName: string;
  financeAccountId: number;
  financeAccountName: string | null;
  accountHolderName: string | null;
  itemName: string;
  category: string;
  categoryId: number | null;
  description: string | null;
  vendor: string | null;
  vendorId: number | null;
  amount: number;
  whtAmount: number;
  whtRate: number;
  netPaid: number;
  whtTaxSection: string | null;
  attachment: FinanceAttachmentInfo | null;
  concurrencyToken: string;
}

export interface RevenueCategory {
  id: number;
  name: string;
  code: string;
  isActive: boolean;
}

export const CANCELLATION_REVENUE_CODE = "cancellation_forfeiture";

export interface FinancialSummary {
  totalRevenue: number;
  customerDepositsBalance: number;
  totalExpenses: number;
  netProfit: number | null;
  accountFilterApplied: boolean;
  overdueAmount: number;
  accountOpeningBalance: number | null;
  accountCurrentBalance: number | null;
  accountNetMovement: number | null;
}

export interface RevenueLine {
  date: string;
  projectId: number | null;
  projectName: string;
  revenueType: string;
  revenueCategoryId: number | null;
  amount: number;
  source: string;
  reference: string | null;
  description: string | null;
  manualRevenueId: number | null;
  financeAccountId: number | null;
  financeAccountName: string | null;
  accountHolderName: string | null;
  concurrencyToken: string | null;
  attachment: FinanceAttachmentInfo | null;
}

export interface ExpenseLine {
  id: number;
  date: string;
  projectId: number | null;
  projectName: string;
  category: string;
  categoryId: number | null;
  amount: number;
  whtApplied: boolean;
  whtRate: number;
  whtAmount: number;
  netPaid: number;
  whtRateOverridden: boolean;
  whtOverrideReason: string | null;
  whtTaxSection: string | null;
  description: string | null;
  reference: string | null;
  vendorId: number | null;
  financeAccountId: number | null;
  financeAccountName: string | null;
  accountHolderName: string | null;
  concurrencyToken: string;
  attachment: FinanceAttachmentInfo | null;
}

export interface CustomerDepositLine {
  bookingId: number;
  bookingReference: string;
  customerName: string;
  projectId: number | null;
  projectName: string;
  unitNumber: string;
  bookingStatus: string;
  netSaleValue: number;
  customerCashReceived: number;
  depositBalance: number;
  recognitionDate: string | null;
  cancellationDate: string | null;
}

export interface OverdueLine {
  bookingReference: string;
  customerName: string;
  projectId: number | null;
  projectName: string;
  unitNumber: string;
  sequenceNumber: number;
  installmentType: string;
  dueDate: string;
  amount: number;
  paidAmount: number;
  overdueAmount: number;
}

export type CostSource = "expense" | "assetPurchase" | "commission" | "rebate" | "customerCredit" | "loanInterest";

export interface CostLine {
  date: string;
  projectName: string;
  label: string;
  kind: "cost" | "reduction";
  amount: number;
  expenseId: number | null;
  source: CostSource;
  sourceId: number;
  attachment: FinanceAttachmentInfo | null;
}

export type HomeView = "revenue" | "totalExpenses" | "customerDeposits" | "overdue";

export type HomeRow = RevenueLine | CostLine | CustomerDepositLine | OverdueLine;

export interface RevenueFormState {
  id: number | null;
  concurrencyToken: string;
  projectId: string;
  financeAccountId: string;
  amount: string;
  revenueType: string;
  revenueCategoryId: string;
  description: string;
  reference: string;
  date: string;
  attachment: FinanceAttachmentInfo | null;
  selectedAttachment: File | null;
  removeAttachment: boolean;
}

export interface ExpenseFormState {
  id: number | null;
  concurrencyToken: string;
  projectId: string;
  financeAccountId: string;
  amount: string;
  categoryId: string;
  category: string;
  legacyCategory: boolean;
  description: string;
  vendorId: string;
  vendor: string;
  date: string;
  wht: WhtFormValue;
  attachment: FinanceAttachmentInfo | null;
  selectedAttachment: File | null;
  removeAttachment: boolean;
}

export interface AssetPurchaseFormState {
  id: number | null;
  projectId: string;
  assetAccountId: string;
  assetAccountName: string;
  financeAccountId: string;
  amount: string;
  itemName: string;
  categoryId: string;
  category: string;
  description: string;
  vendorId: string;
  vendor: string;
  date: string;
  wht: WhtFormValue;
  attachment: FinanceAttachmentInfo | null;
  selectedAttachment: File | null;
  removeAttachment: boolean;
  concurrencyToken: string;
}
