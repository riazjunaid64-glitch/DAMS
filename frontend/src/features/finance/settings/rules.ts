import type { FilterDef } from "../../../components/ui";
import { countOf, paymentsText } from "../lists.ts";
import type { RevenueCategory, SaveRevenueCategory } from "../revenueCategoryApi.ts";
import { formatRate, formatRs, type ExpenseCategory, type FilerStatus, type SaveExpenseCategory, type SaveVendor, type Vendor } from "../whtTypes.ts";

// ── The two category lists ─────────────────────────────────────────────────────

export type CategoryFilters = { search: string; status: string };

/** Both category lists open on the active ones; a status of "" is All. */
export const OPENING_FILTERS: CategoryFilters = { search: "", status: "active" };

export const STATUS_FILTER: readonly FilterDef[] = [
  { type: "select", key: "status", label: "Status", options: [{ value: "active", label: "Active" }, { value: "retired", label: "Retired" }] },
];

/**
 * The categories the Status filter keeps whose text holds the search, ignoring case. The server's
 * order (display order, then name) is kept. Every category is loaded, so nothing here asks the server.
 */
export function filterCategories<T extends { isActive: boolean }>(
  rows: readonly T[],
  filters: CategoryFilters,
  text: (row: T) => readonly (string | null)[],
): T[] {
  const term = filters.search.trim().toLowerCase();
  return rows.filter((row) =>
    (filters.status === "" || row.isActive === (filters.status === "active"))
    && (term === "" || text(row).some((value) => value?.toLowerCase().includes(term))));
}

export type Removal = "retire" | "delete";

/**
 * What a row's ⋯ menu offers. A category nothing is filed under is deleted; a used one can only be
 * retired, and once it is retired there is nothing left to offer (Edit switches it back on).
 */
export function removalOf(usage: number, isActive: boolean): Removal | null {
  if (usage === 0) return "delete";
  return isActive ? "retire" : null;
}

export const DELETE_MESSAGE = "It has never been used, so it will be removed for good.";

export const expenseSearchText = (category: ExpenseCategory) => [category.name, category.taxSection, category.description];

export const expenseRetireMessage = (usage: number) =>
  `It is used by ${paymentsText(usage)}, so it can't be deleted. A retired category can't be picked for new expenses.`;

/** "1 revenue entry", "4 revenue entries": what a revenue category's usage counts. */
export const revenueEntriesText = (count: number) => countOf(count, "revenue entry", "revenue entries");

export const revenueSearchText = (category: RevenueCategory) => [category.name, category.description];

export const revenueRetireMessage = (usage: number) =>
  `It is used by ${revenueEntriesText(usage)}, so it can't be deleted. A retired category can't be picked for new revenue.`;

// ── Expense category cells ─────────────────────────────────────────────────────

/** "153(1)(a)", "—" when tax is withheld under no section, or null for a category with no tax. */
export const sectionText = (category: Pick<ExpenseCategory, "isWhtApplicable" | "taxSection">) =>
  category.isWhtApplicable ? category.taxSection || "—" : null;

export const rateText = (category: Pick<ExpenseCategory, "isWhtApplicable">, rate: number) =>
  category.isWhtApplicable ? formatRate(rate) : "—";

/** "Rs 75,000"; "From Rs 1" for a limit of 0 (tax from the first rupee); "—" for a category with no tax. */
export function yearlyLimitText(category: Pick<ExpenseCategory, "isWhtApplicable" | "annualThreshold">): string {
  if (!category.isWhtApplicable) return "—";
  return category.annualThreshold > 0 ? formatRs(category.annualThreshold) : "From Rs 1";
}

// ── Forms ──────────────────────────────────────────────────────────────────────

/** Where a new category sorts until someone moves it: after the seeded ones. */
export const NEW_DISPLAY_ORDER = 900;

const orNull = (value: string) => value.trim() || null;

/** A typed number the server can take: not empty and not a lone ".". */
const isNumber = (value: string) => value.trim() !== "" && Number.isFinite(Number(value));

export type CategoryFields = {
  name: string;
  withholding: boolean;
  filerRate: string;
  nonFilerRate: string;
  taxSection: string;
  yearlyLimit: string;
  description: string;
  isActive: boolean;
};

/**
 * A new category withholds tax and waits for its two rates. A saved category with no tax has its
 * numbers stored as 0, which are not rates anyone chose, so switching it on starts them empty.
 */
export function categoryFields(category: ExpenseCategory | null): CategoryFields {
  const taxed = category?.isWhtApplicable ?? true;
  const number = (value: number | undefined) => (category && taxed ? String(value) : "");
  return {
    name: category?.name ?? "",
    withholding: taxed,
    filerRate: number(category?.filerRate),
    nonFilerRate: number(category?.nonFilerRate),
    taxSection: category?.taxSection ?? "",
    yearlyLimit: number(category?.annualThreshold),
    description: category?.description ?? "",
    isActive: category?.isActive ?? true,
  };
}

/** Save stays off until there is a name and, while tax is withheld, both rates (the limit may stay empty). */
export const categoryReady = (fields: CategoryFields) =>
  fields.name.trim() !== ""
  && (!fields.withholding || (isNumber(fields.filerRate) && isNumber(fields.nonFilerRate) && (fields.yearlyLimit.trim() === "" || isNumber(fields.yearlyLimit))));

/**
 * What Add / Edit category sends. There is no Code or Display order field: a new category sends an
 * empty code (the server makes it from the name) and order 900; an edit sends both back as saved,
 * because the server writes the order on every save. With no tax the three numbers go as 0; the
 * section is kept, since an expense records it either way.
 */
export function categoryBody(fields: CategoryFields, category: ExpenseCategory | null): SaveExpenseCategory {
  const taxed = fields.withholding;
  return {
    name: fields.name.trim(),
    code: category?.code ?? "",
    description: orNull(fields.description),
    isWhtApplicable: taxed,
    filerRate: taxed ? Number(fields.filerRate) : 0,
    nonFilerRate: taxed ? Number(fields.nonFilerRate) : 0,
    annualThreshold: taxed && fields.yearlyLimit.trim() !== "" ? Number(fields.yearlyLimit) : 0,
    taxSection: orNull(fields.taxSection),
    displayOrder: category?.displayOrder ?? NEW_DISPLAY_ORDER,
    isActive: category ? fields.isActive : true,
    concurrencyToken: category?.concurrencyToken ?? null,
  };
}

export type RevenueFields = { name: string; description: string; order: string; isActive: boolean };

export const revenueFields = (category: RevenueCategory | null): RevenueFields => ({
  name: category?.name ?? "",
  description: category?.description ?? "",
  order: String(category?.displayOrder ?? NEW_DISPLAY_ORDER),
  isActive: category?.isActive ?? true,
});

/** An emptied "Order in lists" keeps the saved order (900 for a new category). */
export function revenueBody(fields: RevenueFields, category: RevenueCategory | null): SaveRevenueCategory {
  return {
    name: fields.name.trim(),
    code: category?.code ?? "",
    description: orNull(fields.description),
    displayOrder: isNumber(fields.order) ? Number(fields.order) : category?.displayOrder ?? NEW_DISPLAY_ORDER,
    isActive: category ? fields.isActive : true,
    concurrencyToken: category?.concurrencyToken ?? null,
  };
}

export type VendorFields = {
  name: string;
  filerStatus: FilerStatus;
  checkedToday: boolean;
  ntn: string;
  cnic: string;
  phone: string;
  address: string;
  notes: string;
  isActive: boolean;
};

/** A new vendor starts as Unknown, which is taxed at the non-filer rate until someone checks. */
export const vendorFields = (vendor: Vendor | null): VendorFields => ({
  name: vendor?.name ?? "",
  filerStatus: vendor?.filerStatus ?? "Unknown",
  checkedToday: false,
  ntn: vendor?.ntn ?? "",
  cnic: vendor?.cnic ?? "",
  phone: vendor?.phone ?? "",
  address: vendor?.address ?? "",
  notes: vendor?.notes ?? "",
  isActive: vendor?.isActive ?? true,
});

/** Empty optional text goes as null, not as an empty string the server would store. */
export const vendorBody = (fields: VendorFields, vendor: Vendor | null): SaveVendor => ({
  name: fields.name.trim(),
  ntn: orNull(fields.ntn),
  cnic: orNull(fields.cnic),
  phone: orNull(fields.phone),
  address: orNull(fields.address),
  notes: orNull(fields.notes),
  filerStatus: fields.filerStatus,
  markFilerStatusChecked: fields.checkedToday,
  isActive: vendor ? fields.isActive : true,
  concurrencyToken: vendor?.concurrencyToken ?? null,
});

/** "4410293-1": the NTN, else the CNIC; null when neither is recorded. */
export const vendorTaxId = (vendor: Pick<Vendor, "ntn" | "cnic">) => vendor.ntn || vendor.cnic || null;
