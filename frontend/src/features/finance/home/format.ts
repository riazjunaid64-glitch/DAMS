import { formatPickerDate } from "../../../components/ui/pickerFormat.ts";
import { pakistanToday } from "../../../lib/financePeriods.ts";
import { emptyWht } from "../whtTypes.ts";
import type { AssetPurchaseFormState, ExpenseFormState, ExpenseLine, AssetPurchaseLine, RevenueFormState, RevenueLine } from "./types.ts";

export function formatMoney(n: number): string {
  const sign = n < 0 ? "-" : "";
  return `${sign}Rs ${Math.abs(n).toLocaleString("en-PK", { maximumFractionDigits: 2 })}`;
}

/** A cost is "+Rs …" in red; a reduction is "−Rs …" in green. */
export function formatCostAmount(amount: number): { text: string; className: string } {
  const sign = amount >= 0 ? "+" : "−";
  return {
    text: `${sign}${formatMoney(Math.abs(amount))}`,
    className: amount >= 0 ? "text-danger" : "text-success",
  };
}

export function formatDate(value: string | null | undefined): string {
  if (!value) return "—";
  return formatPickerDate(value.slice(0, 10)) || "—";
}

export function projectLabel(name: string | null | undefined): string {
  return name?.trim() ? name : "General";
}

export function emptyRevenueForm(): RevenueFormState {
  return {
    id: null,
    concurrencyToken: "",
    projectId: "",
    financeAccountId: "",
    amount: "",
    revenueType: "",
    revenueCategoryId: "",
    description: "",
    reference: "",
    date: pakistanToday(),
    attachment: null,
    selectedAttachment: null,
    removeAttachment: false,
  };
}

export function emptyExpenseForm(): ExpenseFormState {
  return {
    id: null,
    concurrencyToken: "",
    projectId: "",
    financeAccountId: "",
    amount: "",
    categoryId: "",
    category: "",
    legacyCategory: false,
    description: "",
    vendorId: "",
    vendor: "",
    date: pakistanToday(),
    wht: emptyWht(),
    attachment: null,
    selectedAttachment: null,
    removeAttachment: false,
  };
}

export function emptyAssetForm(): AssetPurchaseFormState {
  return {
    id: null,
    projectId: "",
    assetAccountId: "",
    assetAccountName: "",
    financeAccountId: "",
    amount: "",
    itemName: "",
    categoryId: "",
    category: "",
    description: "",
    vendorId: "",
    vendor: "",
    date: pakistanToday(),
    wht: emptyWht(),
    attachment: null,
    selectedAttachment: null,
    removeAttachment: false,
    concurrencyToken: "",
  };
}

export function revenueFormFrom(row: RevenueLine): RevenueFormState {
  return {
    id: row.manualRevenueId,
    concurrencyToken: row.concurrencyToken ?? "",
    projectId: row.projectId != null ? String(row.projectId) : "",
    financeAccountId: row.financeAccountId != null ? String(row.financeAccountId) : "",
    amount: String(row.amount),
    revenueType: row.revenueType,
    revenueCategoryId: row.revenueCategoryId != null ? String(row.revenueCategoryId) : "",
    description: row.description ?? "",
    reference: row.reference ?? "",
    date: row.date.slice(0, 10),
    attachment: row.attachment,
    selectedAttachment: null,
    removeAttachment: false,
  };
}

export function expenseFormFrom(row: ExpenseLine): ExpenseFormState {
  return {
    id: row.id,
    concurrencyToken: row.concurrencyToken,
    projectId: row.projectId != null ? String(row.projectId) : "",
    financeAccountId: row.financeAccountId != null ? String(row.financeAccountId) : "",
    amount: String(row.amount),
    categoryId: row.categoryId != null ? String(row.categoryId) : "",
    category: row.category,
    legacyCategory: row.categoryId == null,
    description: row.description ?? "",
    vendorId: row.vendorId != null ? String(row.vendorId) : "",
    vendor: row.reference ?? "",
    date: row.date.slice(0, 10),
    wht: {
      rate: row.categoryId != null ? String(Number(row.whtRate.toFixed(4))) : "",
      amount: row.categoryId != null ? String(row.whtAmount) : "",
      overrideReason: row.whtOverrideReason ?? "",
    },
    attachment: row.attachment,
    selectedAttachment: null,
    removeAttachment: false,
  };
}

export function assetFormFrom(row: AssetPurchaseLine): AssetPurchaseFormState {
  return {
    id: row.id,
    projectId: row.projectId != null ? String(row.projectId) : "",
    assetAccountId: String(row.assetAccountId),
    assetAccountName: row.assetAccountName,
    financeAccountId: String(row.financeAccountId),
    amount: String(row.amount),
    itemName: row.itemName,
    categoryId: row.categoryId != null ? String(row.categoryId) : "",
    category: row.category,
    description: row.description ?? "",
    vendorId: row.vendorId != null ? String(row.vendorId) : "",
    vendor: row.vendor ?? "",
    date: row.date.slice(0, 10),
    wht: {
      rate: String(Number(row.whtRate.toFixed(4))),
      amount: String(row.whtAmount),
      overrideReason: "",
    },
    attachment: row.attachment,
    selectedAttachment: null,
    removeAttachment: false,
    concurrencyToken: row.concurrencyToken,
  };
}

export function positiveAmount(value: string): boolean {
  const amount = Number(value);
  return Number.isFinite(amount) && amount > 0;
}
