import type { ExpenseCategory, VendorOption } from "../whtTypes.ts";
import { isStaffFloat, type FinanceAccountOption } from "./types.ts";

const inactive = (active: boolean) => (active ? "" : " (Inactive)");

export function filterAccountLabel(account: FinanceAccountOption): string {
  return `${account.name}${inactive(account.isActive)}`;
}

/** "Meezan Bank — Adeel Satti", with staff float and inactive only when they apply. */
export function formAccountLabel(account: FinanceAccountOption, staff = false): string {
  const float = staff && isStaffFloat(account) ? " · Staff float" : "";
  return `${account.name} — ${account.accountHolderName}${float}${inactive(account.isActive)}`;
}

export function categoryLabel(category: ExpenseCategory): string {
  const retired = category.isActive ? "" : " (Retired)";
  const section = category.isWhtApplicable && category.taxSection ? ` — s.${category.taxSection}` : "";
  return `${category.name}${retired}${section}`;
}

export function vendorLabel(vendor: VendorOption): string {
  const status = vendor.filerStatus === "NonFiler" ? "Non-filer" : vendor.filerStatus;
  return `${vendor.name} — ${status}${inactive(vendor.isActive)}`;
}

export function usableAccounts(accounts: readonly FinanceAccountOption[], selectedId: string, staff: boolean) {
  return accounts.filter((account) =>
    (staff || !isStaffFloat(account)) && (account.isActive || String(account.id) === selectedId));
}
