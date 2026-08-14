/** Matches DAMS.Domain.Enums.FilerStatus. Unknown is withheld at the non-filer rate. */
export type FilerStatus = "Unknown" | "Filer" | "NonFiler";

export const FILER_STATUSES: [FilerStatus, string][] = [
  ["Filer", "Filer (on the ATL)"],
  ["NonFiler", "Non-filer"],
  ["Unknown", "Unknown — withheld as non-filer"],
];

export interface ExpenseCategory {
  id: number;
  name: string;
  code: string;
  description: string | null;
  isWhtApplicable: boolean;
  filerRate: number;
  nonFilerRate: number;
  annualThreshold: number;
  taxSection: string | null;
  displayOrder: number;
  isActive: boolean;
  usageCount: number;
  createdAt: string;
  updatedAt: string | null;
  concurrencyToken: string;
}

export interface VendorOption {
  id: number;
  name: string;
  filerStatus: FilerStatus;
  ntn: string | null;
  isActive: boolean;
}

export interface Vendor extends VendorOption {
  cnic: string | null;
  phone: string | null;
  address: string | null;
  notes: string | null;
  filerStatusCheckedAt: string | null;
  yearToDateGross: number;
  yearToDateWht: number;
  paymentCount: number;
  createdAt: string;
  updatedAt: string | null;
  concurrencyToken: string;
}

/** Live preview returned while the expense form is being filled in. */
export interface WhtCalculation {
  isWhtApplicable: boolean;
  rate: number;
  whtAmount: number;
  netPaid: number;
  whtApplied: boolean;
  filerStatus: FilerStatus;
  taxSection: string | null;
  belowThreshold: boolean;
  annualThreshold: number;
  yearToDateTotal: number;
  financialYear: string;
  notice: string | null;
}

export interface FinanceSettings {
  financialYearStartMonth: number;
  whtRatesConfirmedAt: string | null;
  whtRatesConfirmedByName: string | null;
  currentFinancialYear: string;
  concurrencyToken: string;
}

export interface WhtSectionTotal {
  taxSection: string;
  grossAmount: number;
  whtAmount: number;
  paymentCount: number;
}

export interface WhtPayableSummary {
  withheldInPeriod: number;
  depositedInPeriod: number;
  outstandingPayable: number;
  totalWithheldAllTime: number;
  totalDepositedAllTime: number;
  paymentCount: number;
  vendorCount: number;
  bySection: WhtSectionTotal[];
}

export interface WhtVendorLine {
  vendorId: number | null;
  vendorName: string;
  ntn: string | null;
  cnic: string | null;
  filerStatus: FilerStatus;
  taxSection: string | null;
  grossAmount: number;
  whtAmount: number;
  netPaid: number;
  paymentCount: number;
}

export interface WhtDeposit {
  id: number;
  financeAccountId: number;
  financeAccountName: string | null;
  amount: number;
  depositDate: string;
  challanNumber: string | null;
  periodFrom: string | null;
  periodTo: string | null;
  notes: string | null;
  createdAt: string;
  concurrencyToken: string;
}

/**
 * The editable withholding fields on the expense form. An empty string means "whatever the server
 * calculates" — distinct from "0", which is a deliberate instruction to withhold nothing.
 */
export interface WhtFormValue {
  rate: string;
  amount: string;
  overrideReason: string;
}

export const emptyWht = (): WhtFormValue => ({ rate: "", amount: "", overrideReason: "" });

export const MONTHS = [
  "January", "February", "March", "April", "May", "June",
  "July", "August", "September", "October", "November", "December",
];

export function formatRs(value: number): string {
  const sign = value < 0 ? "-" : "";
  return `${sign}Rs ${Math.abs(value).toLocaleString("en-PK", { maximumFractionDigits: 2 })}`;
}

/** Trailing zeros dropped so a table of rates reads as 1% / 7.5% rather than 1.0000%. */
export function formatRate(value: number): string {
  return `${Number(value.toFixed(4))}%`;
}

export function filerLabel(status: FilerStatus): string {
  return status === "NonFiler" ? "Non-filer" : status;
}
