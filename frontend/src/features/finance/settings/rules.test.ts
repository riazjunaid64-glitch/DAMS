import { describe, expect, it } from "vitest";
import type { RevenueCategory } from "../revenueCategoryApi.ts";
import type { ExpenseCategory, Vendor } from "../whtTypes.ts";
import {
  categoryBody,
  categoryFields,
  categoryReady,
  expenseRetireMessage,
  expenseSearchText,
  filterCategories,
  OPENING_FILTERS,
  rateText,
  removalOf,
  revenueBody,
  revenueFields,
  revenueRetireMessage,
  sectionText,
  vendorBody,
  vendorFields,
  vendorTaxId,
  yearlyLimitText,
} from "./rules.ts";

const category = (over: Partial<ExpenseCategory> = {}): ExpenseCategory => ({
  id: 7, name: "Contractor services", code: "contractor_services", description: null, isWhtApplicable: true,
  filerRate: 4, nonFilerRate: 8, annualThreshold: 30_000, taxSection: "153(1)(b)", displayOrder: 40, isActive: true,
  usageCount: 12, createdAt: "2026-07-01T00:00:00", updatedAt: null, concurrencyToken: "tok-7", ...over,
});

const revenue = (over: Partial<RevenueCategory> = {}): RevenueCategory => ({
  id: 3, name: "Transfer charges", code: "transfer_charges", description: "Charged when a unit changes hands", displayOrder: 10,
  isActive: true, revenueCount: 4, createdAt: "2026-07-01T00:00:00", updatedAt: null, concurrencyToken: "rev-3", ...over,
});

const vendor = (over: Partial<Vendor> = {}): Vendor => ({
  id: 1, name: "Al-Noor Steel", filerStatus: "Filer", ntn: "4410293-1", isActive: true, cnic: null, phone: "0300 1234567",
  address: "Badami Bagh, Lahore", notes: null, filerStatusCheckedAt: "2026-08-11T06:00:00", yearToDateGross: 4_850_000,
  yearToDateWht: 48_500, paymentCount: 14, createdAt: "2026-07-01T00:00:00", updatedAt: null, concurrencyToken: "ven-1", ...over,
});

describe("the category lists", () => {
  const rows = [
    category({ id: 1, name: "Cement", taxSection: "153(1)(a)" }),
    category({ id: 2, name: "Old cement", isActive: false }),
    category({ id: 3, name: "Electricity", isWhtApplicable: false, taxSection: null, description: "Collected by the supplier" }),
  ];
  const names = (list: readonly ExpenseCategory[]) => list.map((row) => row.name);

  it("open on the active categories, in the server's order", () => {
    expect(names(filterCategories(rows, OPENING_FILTERS, expenseSearchText))).toEqual(["Cement", "Electricity"]);
  });

  it("show the retired ones under Retired and everything under All", () => {
    expect(names(filterCategories(rows, { search: "", status: "retired" }, expenseSearchText))).toEqual(["Old cement"]);
    expect(names(filterCategories(rows, { search: "", status: "" }, expenseSearchText))).toEqual(["Cement", "Old cement", "Electricity"]);
  });

  it("search the name, the tax section and the description, ignoring case and outer spaces", () => {
    expect(names(filterCategories(rows, { search: " CEMENT ", status: "" }, expenseSearchText))).toEqual(["Cement", "Old cement"]);
    expect(names(filterCategories(rows, { search: "153(1)(a)", status: "" }, expenseSearchText))).toEqual(["Cement"]);
    expect(names(filterCategories(rows, { search: "supplier", status: "active" }, expenseSearchText))).toEqual(["Electricity"]);
    expect(filterCategories(rows, { search: "nothing like it", status: "" }, expenseSearchText)).toEqual([]);
  });

  it("offer Retire for a used active category, Delete for one never used, and nothing for a used retired one", () => {
    expect(removalOf(12, true)).toBe("retire");
    expect(removalOf(0, true)).toBe("delete");
    expect(removalOf(0, false)).toBe("delete");
    expect(removalOf(3, false)).toBeNull();
  });

  it("say how many records stop a delete", () => {
    expect(expenseRetireMessage(1)).toBe("It is used by 1 payment, so it can't be deleted. A retired category can't be picked for new expenses.");
    expect(expenseRetireMessage(12)).toBe("It is used by 12 payments, so it can't be deleted. A retired category can't be picked for new expenses.");
    expect(revenueRetireMessage(1)).toBe("It is used by 1 revenue entry, so it can't be deleted. A retired category can't be picked for new revenue.");
    expect(revenueRetireMessage(4)).toBe("It is used by 4 revenue entries, so it can't be deleted. A retired category can't be picked for new revenue.");
  });
});

describe("expense category cells", () => {
  it("show the section, a dash when tax has no section, and nothing (No tax) without tax", () => {
    expect(sectionText(category())).toBe("153(1)(b)");
    expect(sectionText(category({ taxSection: null }))).toBe("—");
    expect(sectionText(category({ isWhtApplicable: false }))).toBeNull();
  });

  it("show rates as percentages, and a dash without tax", () => {
    expect(rateText(category(), 4)).toBe("4%");
    expect(rateText(category(), 7.5)).toBe("7.5%");
    expect(rateText(category({ isWhtApplicable: false }), 0)).toBe("—");
  });

  it("show the yearly limit in rupees, 'From Rs 1' for no allowance, and a dash without tax", () => {
    expect(yearlyLimitText(category())).toBe("Rs 30,000");
    expect(yearlyLimitText(category({ annualThreshold: 0 }))).toBe("From Rs 1");
    expect(yearlyLimitText(category({ isWhtApplicable: false, annualThreshold: 0 }))).toBe("—");
  });
});

describe("the expense category form", () => {
  it("starts a new category with tax on and empty rates, and keeps Save off until both rates are typed", () => {
    const fields = categoryFields(null);
    expect(fields).toEqual({ name: "", withholding: true, filerRate: "", nonFilerRate: "", taxSection: "", yearlyLimit: "", description: "", isActive: true });
    expect(categoryReady({ ...fields, name: "Cement" })).toBe(false);
    expect(categoryReady({ ...fields, name: "Cement", filerRate: "1" })).toBe(false);
    expect(categoryReady({ ...fields, name: "Cement", filerRate: "1", nonFilerRate: "2" })).toBe(true);
    expect(categoryReady({ ...fields, name: "  ", filerRate: "1", nonFilerRate: "2" })).toBe(false);
    // A lone point is not a rate yet.
    expect(categoryReady({ ...fields, name: "Cement", filerRate: ".", nonFilerRate: "2" })).toBe(false);
    expect(categoryReady({ ...fields, name: "Cement", filerRate: "1", nonFilerRate: "2", yearlyLimit: "." })).toBe(false);
  });

  it("needs only a name once the tax is switched off", () => {
    expect(categoryReady({ ...categoryFields(null), name: "Salaries", withholding: false })).toBe(true);
  });

  it("sends a new category with an empty code, order 900 and Active", () => {
    const fields = { ...categoryFields(null), name: " Cement ", filerRate: "1", nonFilerRate: "2", taxSection: "153(1)(a)", yearlyLimit: "75000" };
    expect(categoryBody(fields, null)).toEqual({
      name: "Cement", code: "", description: null, isWhtApplicable: true, filerRate: 1, nonFilerRate: 2, annualThreshold: 75_000,
      taxSection: "153(1)(a)", displayOrder: 900, isActive: true, concurrencyToken: null,
    });
  });

  it("takes an empty yearly limit as tax from the first rupee", () => {
    const fields = { ...categoryFields(null), name: "Office rent", filerRate: "10", nonFilerRate: "20" };
    expect(categoryBody(fields, null).annualThreshold).toBe(0);
  });

  it("sends an edit with the saved code, order and version", () => {
    const saved = category();
    const fields = categoryFields(saved);
    expect(fields).toMatchObject({ name: "Contractor services", filerRate: "4", nonFilerRate: "8", yearlyLimit: "30000", taxSection: "153(1)(b)" });
    expect(categoryBody({ ...fields, isActive: false }, saved)).toEqual({
      name: "Contractor services", code: "contractor_services", description: null, isWhtApplicable: true, filerRate: 4, nonFilerRate: 8,
      annualThreshold: 30_000, taxSection: "153(1)(b)", displayOrder: 40, isActive: false, concurrencyToken: "tok-7",
    });
  });

  it("sends the three numbers as 0 with the tax off, and keeps the section", () => {
    const saved = category();
    expect(categoryBody({ ...categoryFields(saved), withholding: false }, saved)).toMatchObject({
      isWhtApplicable: false, filerRate: 0, nonFilerRate: 0, annualThreshold: 0, taxSection: "153(1)(b)",
    });
  });

  it("starts the rates empty when a category with no tax is switched on", () => {
    const saved = category({ isWhtApplicable: false, filerRate: 0, nonFilerRate: 0, annualThreshold: 0 });
    const fields = { ...categoryFields(saved), withholding: true };
    expect(fields).toMatchObject({ filerRate: "", nonFilerRate: "", yearlyLimit: "" });
    expect(categoryReady(fields)).toBe(false);
  });
});

describe("the revenue category form", () => {
  it("sends a new category with an empty code and order 900", () => {
    expect(revenueBody({ ...revenueFields(null), name: "Parking charges" }, null)).toEqual({
      name: "Parking charges", code: "", description: null, displayOrder: 900, isActive: true, concurrencyToken: null,
    });
  });

  it("sends an edit with the saved code and the typed order", () => {
    const saved = revenue();
    expect(revenueFields(saved)).toEqual({ name: "Transfer charges", description: "Charged when a unit changes hands", order: "10", isActive: true });
    expect(revenueBody({ ...revenueFields(saved), order: "25" }, saved)).toEqual({
      name: "Transfer charges", code: "transfer_charges", description: "Charged when a unit changes hands", displayOrder: 25, isActive: true, concurrencyToken: "rev-3",
    });
  });

  it("keeps the saved order when the field is emptied", () => {
    expect(revenueBody({ ...revenueFields(revenue()), order: "" }, revenue()).displayOrder).toBe(10);
    expect(revenueBody({ ...revenueFields(null), name: "Other income", order: "" }, null).displayOrder).toBe(900);
  });
});

describe("the vendor form", () => {
  it("starts a new vendor as Unknown, not checked and active", () => {
    expect(vendorFields(null)).toEqual({
      name: "", filerStatus: "Unknown", checkedToday: false, ntn: "", cnic: "", phone: "", address: "", notes: "", isActive: true,
    });
  });

  it("sends empty optional text as null and the check only when ticked", () => {
    const fields = { ...vendorFields(null), name: " City Hardware ", cnic: "37405-1122334-5", phone: "  " };
    expect(vendorBody(fields, null)).toEqual({
      name: "City Hardware", ntn: null, cnic: "37405-1122334-5", phone: null, address: null, notes: null, filerStatus: "Unknown",
      markFilerStatusChecked: false, isActive: true, concurrencyToken: null,
    });
    expect(vendorBody({ ...fields, checkedToday: true }, null).markFilerStatusChecked).toBe(true);
  });

  it("sends an edit with its version and the Active switch", () => {
    const saved = vendor();
    expect(vendorBody({ ...vendorFields(saved), isActive: false, filerStatus: "NonFiler" }, saved)).toMatchObject({
      name: "Al-Noor Steel", ntn: "4410293-1", filerStatus: "NonFiler", isActive: false, concurrencyToken: "ven-1",
    });
  });

  it("shows the NTN, else the CNIC", () => {
    expect(vendorTaxId(vendor())).toBe("4410293-1");
    expect(vendorTaxId(vendor({ ntn: null, cnic: "37405-1122334-5" }))).toBe("37405-1122334-5");
    expect(vendorTaxId(vendor({ ntn: null, cnic: null }))).toBeNull();
  });
});
