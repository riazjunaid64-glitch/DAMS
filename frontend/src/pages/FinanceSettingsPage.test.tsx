// @vitest-environment happy-dom
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import type { User } from "../App.tsx";
import { ToastProvider } from "../components/ui/Toast.tsx";
import { PHONE_QUERY } from "../components/ui/useMediaQuery.ts";
import * as whtApi from "../features/finance/whtApi.ts";
import * as revenueApi from "../features/finance/revenueCategoryApi.ts";
import type { RevenueCategory } from "../features/finance/revenueCategoryApi.ts";
import type { ExpenseCategory, FinanceSettings, Vendor } from "../features/finance/whtTypes.ts";
import FinanceSettingsPage from "./FinanceSettingsPage.tsx";

vi.mock("../features/finance/whtApi.ts", () => ({
  getSettings: vi.fn(),
  saveSettings: vi.fn(),
  listCategories: vi.fn(),
  saveCategory: vi.fn(),
  deleteCategory: vi.fn(),
  listVendors: vi.fn(),
  saveVendor: vi.fn(),
}));
vi.mock("../features/finance/revenueCategoryApi.ts", () => ({
  listRevenueCategories: vi.fn(),
  saveRevenueCategory: vi.fn(),
  deleteRevenueCategory: vi.fn(),
}));

const wht = vi.mocked(whtApi);
const rev = vi.mocked(revenueApi);

const admin: User = { userId: "1", role: "Admin", email: "a@b.c" };
const accountant: User = { userId: "2", role: "Accountant", email: "c@d.e" };
const sales: User = { userId: "3", role: "Manager", email: "e@f.g" };

const settings = (over: Partial<FinanceSettings> = {}): FinanceSettings => ({
  financialYearStartMonth: 7, whtRatesConfirmedAt: null, whtRatesConfirmedByName: null, goLiveDate: "2026-08-15T00:00:00",
  currentFinancialYear: "2026-27", concurrencyToken: "tok", ...over,
});
const checked = settings({ whtRatesConfirmedAt: "2026-08-11T06:00:00", whtRatesConfirmedByName: "admin@dams.local", concurrencyToken: "tok-2" });

const category = (over: Partial<ExpenseCategory> = {}): ExpenseCategory => ({
  id: 1, name: "Cement", code: "cement", description: null, isWhtApplicable: true, filerRate: 1, nonFilerRate: 2,
  annualThreshold: 75_000, taxSection: "153(1)(a)", displayOrder: 10, isActive: true, usageCount: 1,
  createdAt: "2026-07-01T00:00:00", updatedAt: null, concurrencyToken: "cat-1", ...over,
});
const categories = [
  category(),
  category({ id: 2, name: "Steel / iron / rebar", code: "steel", usageCount: 0, concurrencyToken: "cat-2" }),
  category({ id: 3, name: "Contractor services", code: "contractor_services", filerRate: 4, nonFilerRate: 8, annualThreshold: 30_000, taxSection: "153(1)(b)", displayOrder: 40, usageCount: 12, concurrencyToken: "cat-3" }),
  category({ id: 4, name: "Electricity", code: "electricity", isWhtApplicable: false, filerRate: 0, nonFilerRate: 0, annualThreshold: 0, taxSection: null, usageCount: 8, concurrencyToken: "cat-4" }),
  category({ id: 5, name: "Old timber", code: "old_timber", isActive: false, usageCount: 3, concurrencyToken: "cat-5" }),
  category({ id: 6, name: "Office rent", code: "office_rent", filerRate: 10, nonFilerRate: 20, annualThreshold: 0, taxSection: "155", usageCount: 6, concurrencyToken: "cat-6" }),
];
const many = (count: number) => Array.from({ length: count }, (_, index) => category({ id: index + 1, name: `Head ${String(index + 1).padStart(2, "0")}`, code: `head_${index + 1}` }));

const revenue = (over: Partial<RevenueCategory> = {}): RevenueCategory => ({
  id: 1, name: "Transfer charges", code: "transfer_charges", description: "Charged when a unit changes hands", displayOrder: 10,
  isActive: true, revenueCount: 4, createdAt: "2026-07-01T00:00:00", updatedAt: null, concurrencyToken: "rev-1", ...over,
});
const revenues = [revenue(), revenue({ id: 2, name: "Bank profit", code: "bank_profit", description: null, displayOrder: 20, revenueCount: 0, concurrencyToken: "rev-2" })];

const vendor = (over: Partial<Vendor> = {}): Vendor => ({
  id: 1, name: "Al-Noor Steel", filerStatus: "Filer", ntn: "4410293-1", isActive: true, cnic: null, phone: "0300 1234567",
  address: "Badami Bagh, Lahore", notes: null, filerStatusCheckedAt: "2026-08-11T06:00:00", yearToDateGross: 4_850_000,
  yearToDateWht: 48_500, paymentCount: 14, createdAt: "2026-07-01T00:00:00", updatedAt: null, concurrencyToken: "ven-1", ...over,
});
const vendors = [
  vendor(),
  vendor({ id: 2, name: "City Hardware", filerStatus: "NonFiler", ntn: null, cnic: "37405-1122334-5", phone: "0321 7654321", filerStatusCheckedAt: null, paymentCount: 9, concurrencyToken: "ven-2" }),
  vendor({ id: 3, name: "Shah Electric Works", filerStatus: "Unknown", ntn: null, cnic: null, phone: null, isActive: false, filerStatusCheckedAt: null, paymentCount: 0, concurrencyToken: "ven-3" }),
];

function show(user: User | null = admin) {
  return render(
    <MemoryRouter initialEntries={["/finance/settings"]}>
      <ToastProvider>
        <Routes>
          <Route path="/finance/settings" element={<FinanceSettingsPage user={user} />} />
          <Route path="/" element={<p>Home page</p>} />
        </Routes>
      </ToastProvider>
    </MemoryRouter>,
  );
}

let phoneWidth = false;
function stubMedia(phone: boolean) {
  phoneWidth = phone;
  vi.stubGlobal("matchMedia", (query: string) => ({
    get matches() { return phoneWidth && query === PHONE_QUERY; },
    media: query,
    addEventListener: () => {},
    removeEventListener: () => {},
  }));
}

const button = (name: string | RegExp, scope: { getByRole: typeof screen.getByRole } = screen) => scope.getByRole("button", { name }) as HTMLButtonElement;
/** The desktop table: the phone cards of the same rows are in the page too, hidden by CSS. */
const table = () => within(screen.getByRole("table"));
const dialog = () => within(screen.getByRole("dialog"));
const tab = (name: RegExp) => screen.getByRole("tab", { name });
const loaded = async () => { await screen.findAllByText("Cement"); };
const pick = (field: RegExp, option: string) => {
  fireEvent.click(screen.getByRole("combobox", { name: field }));
  fireEvent.click(screen.getByRole("option", { name: option }));
};
const menu = (name: string, item: string) => {
  fireEvent.click(button(`More for ${name}`, table()));
  fireEvent.click(screen.getByRole("menuitem", { name: item }));
};
const type = (label: RegExp, value: string, scope = dialog()) => fireEvent.change(scope.getByLabelText(label), { target: { value } });
const pageText = () => document.body.textContent ?? "";

beforeEach(() => {
  vi.resetAllMocks();
  stubMedia(false);
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date("2026-10-03T07:00:00Z"));
  Element.prototype.scrollIntoView = () => {};
  wht.getSettings.mockResolvedValue(settings());
  wht.listCategories.mockResolvedValue(categories);
  wht.saveSettings.mockResolvedValue(checked);
  wht.saveCategory.mockResolvedValue(categories[0]!);
  wht.deleteCategory.mockResolvedValue({ message: "", category: null });
  wht.listVendors.mockResolvedValue({ items: vendors, hasMore: false, totalCount: 3 });
  wht.saveVendor.mockResolvedValue(vendors[0]!);
  rev.listRevenueCategories.mockResolvedValue(revenues);
  rev.saveRevenueCategory.mockResolvedValue(revenues[0]!);
  rev.deleteRevenueCategory.mockResolvedValue({ message: "", category: null });
});

afterEach(() => {
  cleanup();
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe("Finance settings: who can open it", () => {
  it("decides nothing and sends nobody away while the user is not known yet", () => {
    show(null);
    expect(screen.getByText("Sign in required")).toBeTruthy();
    expect(screen.queryByText("Home page")).toBeNull();
    expect(wht.getSettings).not.toHaveBeenCalled();
  });

  it("sends a sales role home without reading anything", async () => {
    show(sales);
    expect(await screen.findByText("Home page")).toBeTruthy();
    expect(wht.getSettings).not.toHaveBeenCalled();
  });

  it("opens for an Accountant", async () => {
    show(accountant);
    await loaded();
    expect(screen.getByRole("heading", { name: "Finance settings" })).toBeTruthy();
  });
});

describe("Finance settings: frame", () => {
  it("has four tabs with the category counts, retired ones included, and opens on Expense categories", async () => {
    show();
    await loaded();
    expect(screen.getAllByRole("tab").map((item) => item.textContent)).toEqual(["Expense categories6", "Revenue categories2", "Vendors", "Financial year"]);
    expect(tab(/Expense categories/).getAttribute("aria-selected")).toBe("true");
    expect(wht.listCategories).toHaveBeenCalledWith(true, expect.anything());
    expect(rev.listRevenueCategories).toHaveBeenCalledWith(true, expect.anything());
  });

  it("changes the header button with the tab, and has none on Financial year", async () => {
    show();
    await loaded();
    expect(button("Add category")).toBeTruthy();
    fireEvent.click(tab(/Revenue categories/));
    expect(button("Add category")).toBeTruthy();
    fireEvent.click(tab(/Vendors/));
    expect(button("Add vendor")).toBeTruthy();
    fireEvent.click(tab(/Financial year/));
    expect(screen.queryByRole("button", { name: /^Add/ })).toBeNull();
  });

  it("has no back link, Accounts button or explanation paragraphs any more", async () => {
    show();
    await loaded();
    expect(screen.queryByRole("link")).toBeNull();
    expect(pageText()).not.toContain("copied onto that expense");
    expect(pageText()).not.toContain("Pakistan's tax year");
  });

  it("shows a failed read with Try again, and reads again", async () => {
    wht.getSettings.mockRejectedValueOnce(new Error("Finance settings could not be loaded."));
    show();
    expect(await screen.findByText("Finance settings could not be loaded.")).toBeTruthy();
    expect(button("Add category").disabled).toBe(true);
    fireEvent.click(button("Try again"));
    await loaded();
    expect(screen.queryByText("Finance settings could not be loaded.")).toBeNull();
  });
});

describe("Finance settings: the rates-not-checked notice", () => {
  it("shows only on Expense categories while the rates are unchecked", async () => {
    show();
    await loaded();
    expect(screen.getByText("Tax rates have not been checked by an accountant yet.")).toBeTruthy();
    fireEvent.click(tab(/Revenue categories/));
    expect(screen.queryByText("Tax rates have not been checked by an accountant yet.")).toBeNull();
  });

  it("Mark as checked sends the saved month and no go-live date, says so, and the notice goes after the reload", async () => {
    wht.getSettings.mockResolvedValueOnce(settings()).mockResolvedValue(checked);
    show();
    await loaded();
    fireEvent.click(button("Mark as checked"));
    await waitFor(() => expect(wht.saveSettings).toHaveBeenCalledTimes(1));
    expect(wht.saveSettings.mock.calls[0]![0]).toEqual({
      financialYearStartMonth: 7, goLiveDate: null, markRatesConfirmed: true, clearRatesConfirmation: false, concurrencyToken: "tok",
    });
    expect(await screen.findByText("Tax rates marked as checked.")).toBeTruthy();
    await waitFor(() => expect(screen.queryByText("Tax rates have not been checked by an accountant yet.")).toBeNull());
  });

  it("shows a refusal in a toast", async () => {
    wht.saveSettings.mockRejectedValue(new Error("Finance settings were changed by someone else. Refresh and try again."));
    show();
    await loaded();
    fireEvent.click(button("Mark as checked"));
    expect(await screen.findByText("Finance settings were changed by someone else. Refresh and try again.")).toBeTruthy();
    expect(screen.getByText("Tax rates have not been checked by an accountant yet.")).toBeTruthy();
  });

  it("is not shown once the rates are checked", async () => {
    wht.getSettings.mockResolvedValue(checked);
    show();
    await loaded();
    expect(screen.queryByText("Tax rates have not been checked by an accountant yet.")).toBeNull();
  });
});

describe("Finance settings: expense categories", () => {
  it("opens on Active, shows today's columns, and no code", async () => {
    show();
    await loaded();
    expect(table().queryByText("Old timber")).toBeNull();
    expect(table().getAllByRole("columnheader").map((cell) => cell.textContent)).toEqual(["Category", "Tax section", "Filer", "Non-filer", "Yearly limit", "Used by", "Actions"]);
    const cement = within(table().getByText("Cement").closest("tr")!);
    expect(cement.getByText("153(1)(a)")).toBeTruthy();
    expect(cement.getByText("1%")).toBeTruthy();
    expect(cement.getByText("2%")).toBeTruthy();
    expect(cement.getByText("Rs 75,000")).toBeTruthy();
    expect(cement.queryByText("cement")).toBeNull();
    const electricity = within(table().getByText("Electricity").closest("tr")!);
    expect(electricity.getByText("No tax")).toBeTruthy();
    expect(electricity.getAllByText("—")).toHaveLength(3);
    expect(within(table().getByText("Office rent").closest("tr")!).getByText("From Rs 1")).toBeTruthy();
  });

  it("shows retired categories, with a Retired badge, under Status Retired; Reset goes back to Active", async () => {
    show();
    await loaded();
    expect(screen.queryByRole("button", { name: "Reset" })).toBeNull();
    pick(/Status/, "Retired");
    const timber = within(table().getByText("Old timber").closest("tr")!);
    expect(timber.getByText("Retired")).toBeTruthy();
    expect(table().queryByText("Cement")).toBeNull();
    fireEvent.click(button("Reset"));
    expect(table().getByText("Cement")).toBeTruthy();
    expect(table().queryByText("Old timber")).toBeNull();
  });

  it("searches the name, the section and the description", async () => {
    show();
    await loaded();
    fireEvent.change(screen.getAllByPlaceholderText("Search categories")[0]!, { target: { value: "153(1)(b)" } });
    await waitFor(() => expect(table().queryByText("Cement")).toBeNull());
    expect(table().getByText("Contractor services")).toBeTruthy();
  });

  it("offers Retire for a used category, Delete for an unused one, and no ⋯ for a retired used one", async () => {
    show();
    await loaded();
    fireEvent.click(button("More for Cement", table()));
    expect(screen.getByRole("menuitem", { name: "Retire" })).toBeTruthy();
    expect(screen.queryByRole("menuitem", { name: "Delete" })).toBeNull();
    fireEvent.keyDown(document, { key: "Escape" });
    fireEvent.click(button("More for Steel / iron / rebar", table()));
    expect(screen.getByRole("menuitem", { name: "Delete" })).toBeTruthy();
    pick(/Status/, "All");
    expect(table().queryByRole("button", { name: "More for Old timber" })).toBeNull();
    expect(button("Edit Old timber", table())).toBeTruthy();
  });

  it("asks before retiring, retires on the server, says so and reads the list again", async () => {
    wht.deleteCategory.mockResolvedValue({ message: "", category: { ...categories[0]!, isActive: false } });
    show();
    await loaded();
    menu("Cement", "Retire");
    expect(dialog().getByText("Retire Cement?")).toBeTruthy();
    expect(dialog().getByText("It is used by 1 payment, so it can't be deleted. A retired category can't be picked for new expenses.")).toBeTruthy();
    fireEvent.click(button("Retire", dialog()));
    await waitFor(() => expect(wht.deleteCategory).toHaveBeenCalledWith(1));
    expect(await screen.findByText("Cement retired.")).toBeTruthy();
    await waitFor(() => expect(wht.listCategories).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
  });

  it("asks before deleting with a red button, and says what the server did", async () => {
    show();
    await loaded();
    menu("Steel / iron / rebar", "Delete");
    expect(dialog().getByText("Delete Steel / iron / rebar?")).toBeTruthy();
    expect(dialog().getByText("It has never been used, so it will be removed for good.")).toBeTruthy();
    fireEvent.click(button("Delete", dialog()));
    await waitFor(() => expect(wht.deleteCategory).toHaveBeenCalledWith(2));
    expect(await screen.findByText("Steel / iron / rebar deleted.")).toBeTruthy();
  });

  it("calls it retired when something was filed in the meantime and the server retired it", async () => {
    wht.deleteCategory.mockResolvedValue({ message: "", category: { ...categories[1]!, isActive: false, usageCount: 1 } });
    show();
    await loaded();
    menu("Steel / iron / rebar", "Delete");
    fireEvent.click(button("Delete", dialog()));
    expect(await screen.findByText("Steel / iron / rebar retired.")).toBeTruthy();
  });

  it("shows a refused delete in a toast", async () => {
    wht.deleteCategory.mockRejectedValue(new Error("Something was recorded against this category. Refresh and try again."));
    show();
    await loaded();
    menu("Steel / iron / rebar", "Delete");
    fireEvent.click(button("Delete", dialog()));
    expect(await screen.findByText("Something was recorded against this category. Refresh and try again.")).toBeTruthy();
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
  });

  it("shows twenty a page with page numbers", async () => {
    wht.listCategories.mockResolvedValue(many(25));
    show();
    await screen.findAllByText("Head 01");
    expect(table().getAllByRole("row")).toHaveLength(21);
    expect(pageText()).toContain("Showing 1–20 of 25 entries");
    fireEvent.click(button("Next"));
    expect(table().getByText("Head 21")).toBeTruthy();
    expect(table().queryByText("Head 01")).toBeNull();
    expect(pageText()).toContain("Showing 21–25 of 25 entries");
  });

  it("says when nothing matches, and when there is nothing at all", async () => {
    show();
    await loaded();
    fireEvent.change(screen.getAllByPlaceholderText("Search categories")[0]!, { target: { value: "zzz" } });
    expect(await screen.findByText("No expense categories match.")).toBeTruthy();
    cleanup();
    wht.listCategories.mockResolvedValue([]);
    show();
    expect(await screen.findByText("No expense categories yet. Add one.")).toBeTruthy();
  });
});

describe("Finance settings: the expense category popup", () => {
  it("adds a category with tax off: the four tax fields go, and the numbers go as 0 with an empty code and order 900", async () => {
    show();
    await loaded();
    fireEvent.click(button("Add category"));
    expect(dialog().getByText("Add category")).toBeTruthy();
    expect(button("Save", dialog()).disabled).toBe(true);
    type(/^Category name/, "Salaries");
    expect(button("Save", dialog()).disabled).toBe(true);
    fireEvent.click(dialog().getByRole("switch", { name: "Deduct withholding tax" }));
    for (const field of [/^Filer rate/, /^Non-filer rate/, /^Tax section/, /^Yearly limit/]) expect(dialog().queryByLabelText(field)).toBeNull();
    expect(dialog().queryByRole("switch", { name: "Active" })).toBeNull();
    fireEvent.click(button("Save", dialog()));
    await waitFor(() => expect(wht.saveCategory).toHaveBeenCalledTimes(1));
    expect(wht.saveCategory.mock.calls[0]).toEqual([null, {
      name: "Salaries", code: "", description: null, isWhtApplicable: false, filerRate: 0, nonFilerRate: 0, annualThreshold: 0,
      taxSection: null, displayOrder: 900, isActive: true, concurrencyToken: null,
    }]);
    expect(await screen.findByText("Category added.")).toBeTruthy();
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    await waitFor(() => expect(wht.listCategories).toHaveBeenCalledTimes(2));
  });

  it("keeps Save off with tax on until both rates are typed", async () => {
    show();
    await loaded();
    fireEvent.click(button("Add category"));
    type(/^Category name/, "Cement 2");
    type(/^Filer rate/, "1");
    expect(button("Save", dialog()).disabled).toBe(true);
    type(/^Non-filer rate/, "2");
    expect(button("Save", dialog()).disabled).toBe(false);
  });

  it("edits a used category: says how many payments keep their rate, and sends the saved code, order and version", async () => {
    show();
    await loaded();
    fireEvent.click(button("Edit Contractor services", table()));
    expect(dialog().getByText("Edit category")).toBeTruthy();
    expect(dialog().getByText("Used by 12 payments. They keep the rate they were entered at.")).toBeTruthy();
    expect((dialog().getByLabelText(/^Filer rate/) as HTMLInputElement).value).toBe("4");
    expect((dialog().getByLabelText(/^Yearly limit/) as HTMLInputElement).value).toBe("30,000");
    fireEvent.click(dialog().getByRole("switch", { name: "Active" }));
    fireEvent.click(button("Save", dialog()));
    await waitFor(() => expect(wht.saveCategory).toHaveBeenCalledTimes(1));
    expect(wht.saveCategory.mock.calls[0]).toEqual([3, {
      name: "Contractor services", code: "contractor_services", description: null, isWhtApplicable: true, filerRate: 4, nonFilerRate: 8,
      annualThreshold: 30_000, taxSection: "153(1)(b)", displayOrder: 40, isActive: false, concurrencyToken: "cat-3",
    }]);
    expect(await screen.findByText("Category saved.")).toBeTruthy();
  });

  it("has no subtitle for an unused category", async () => {
    show();
    await loaded();
    fireEvent.click(button("Edit Steel / iron / rebar", table()));
    expect(dialog().queryByText(/^Used by/)).toBeNull();
  });

  it("shows the server's refusal inside the popup and keeps what was typed", async () => {
    wht.saveCategory.mockRejectedValue(new Error("Filer rate cannot exceed 100%."));
    show();
    await loaded();
    fireEvent.click(button("Add category"));
    type(/^Category name/, "Too much");
    type(/^Filer rate/, "150");
    type(/^Non-filer rate/, "2");
    fireEvent.click(button("Save", dialog()));
    expect(await dialog().findByText("Filer rate cannot exceed 100%.")).toBeTruthy();
    expect((dialog().getByLabelText(/^Category name/) as HTMLInputElement).value).toBe("Too much");
    expect(button("Save", dialog()).disabled).toBe(false);
  });
});

describe("Finance settings: revenue categories", () => {
  const openRevenue = async () => {
    show();
    await loaded();
    fireEvent.click(tab(/Revenue categories/));
    await screen.findAllByText("Transfer charges");
  };

  it("shows the name, description and usage, with no Order column", async () => {
    await openRevenue();
    expect(table().getAllByRole("columnheader").map((cell) => cell.textContent)).toEqual(["Category", "Description", "Used by", "Actions"]);
    expect(table().getByText("Charged when a unit changes hands")).toBeTruthy();
    expect(within(table().getByText("Bank profit").closest("tr")!).getByText("—")).toBeTruthy();
  });

  it("asks before retiring with the revenue wording", async () => {
    await openRevenue();
    menu("Transfer charges", "Retire");
    expect(dialog().getByText("Retire Transfer charges?")).toBeTruthy();
    expect(dialog().getByText("It is used by 4 revenue entries, so it can't be deleted. A retired category can't be picked for new revenue.")).toBeTruthy();
    fireEvent.click(button("Retire", dialog()));
    await waitFor(() => expect(rev.deleteRevenueCategory).toHaveBeenCalledWith(1));
  });

  it("edits with Order in lists, sending the saved code", async () => {
    await openRevenue();
    fireEvent.click(button("Edit Transfer charges", table()));
    expect(dialog().getByText("Used by 4 revenue entries.")).toBeTruthy();
    type(/^Order in lists/, "15");
    fireEvent.click(button("Save", dialog()));
    await waitFor(() => expect(rev.saveRevenueCategory).toHaveBeenCalledTimes(1));
    expect(rev.saveRevenueCategory.mock.calls[0]).toEqual([1, {
      name: "Transfer charges", code: "transfer_charges", description: "Charged when a unit changes hands", displayOrder: 15, isActive: true, concurrencyToken: "rev-1",
    }]);
    expect(await screen.findByText("Category saved.")).toBeTruthy();
  });

  it("adds one with an empty code and order 900", async () => {
    await openRevenue();
    fireEvent.click(button("Add category"));
    expect((dialog().getByLabelText(/^Order in lists/) as HTMLInputElement).value).toBe("900");
    expect(dialog().queryByRole("switch", { name: "Active" })).toBeNull();
    type(/^Category name/, "Parking charges");
    fireEvent.click(button("Save", dialog()));
    await waitFor(() => expect(rev.saveRevenueCategory).toHaveBeenCalledTimes(1));
    expect(rev.saveRevenueCategory.mock.calls[0]).toEqual([null, {
      name: "Parking charges", code: "", description: null, displayOrder: 900, isActive: true, concurrencyToken: null,
    }]);
    expect(await screen.findByText("Category added.")).toBeTruthy();
  });
});

describe("Finance settings: vendors", () => {
  const openVendors = async () => {
    show();
    await loaded();
    fireEvent.click(tab(/Vendors/));
    await screen.findAllByText("Al-Noor Steel");
  };

  it("asks the server for twenty at a time and shows today's columns", async () => {
    await openVendors();
    expect(wht.listVendors).toHaveBeenLastCalledWith({ search: "", filerStatus: "", skip: 0, take: 20 }, expect.anything());
    expect(table().getAllByRole("columnheader").map((cell) => cell.textContent)).toEqual(["Vendor", "Filer status", "NTN / CNIC", "Paid this year", "Tax withheld", "Payments", "Actions"]);
    const noor = within(table().getByText("Al-Noor Steel").closest("tr")!);
    expect(noor.getByText("0300 1234567")).toBeTruthy();
    expect(noor.getByText("Filer")).toBeTruthy();
    expect(noor.getByText("Rs 4,850,000")).toBeTruthy();
    expect(noor.getByText("Rs 48,500")).toBeTruthy();
    expect(within(table().getByText("City Hardware").closest("tr")!).getByText("Non-filer")).toBeTruthy();
    const shah = within(table().getByText("Shah Electric Works").closest("tr")!);
    expect(shah.getByText("Unknown")).toBeTruthy();
    expect(shah.getByText("Inactive")).toBeTruthy();
    expect(shah.getByText("Not recorded")).toBeTruthy();
    expect(shah.getByText("—")).toBeTruthy();
  });

  it("filters by filer status from page one, and pages from the server's total", async () => {
    wht.listVendors.mockResolvedValue({ items: vendors, hasMore: true, totalCount: 25 });
    await openVendors();
    expect(pageText()).toContain("Showing 1–20 of 25 entries");
    fireEvent.click(button("Next"));
    await waitFor(() => expect(wht.listVendors).toHaveBeenLastCalledWith({ search: "", filerStatus: "", skip: 20, take: 20 }, expect.anything()));
    pick(/Filer status/, "Non-filer");
    await waitFor(() => expect(wht.listVendors).toHaveBeenLastCalledWith({ search: "", filerStatus: "NonFiler", skip: 0, take: 20 }, expect.anything()));
  });

  it("says when no vendor is found", async () => {
    wht.listVendors.mockResolvedValue({ items: [], hasMore: false, totalCount: 0 });
    show();
    await loaded();
    fireEvent.click(tab(/Vendors/));
    expect(await screen.findByText("No vendors yet. Add the suppliers you pay regularly.")).toBeTruthy();
  });

  it("adds a vendor as Unknown, with the FBR check and no Active switch", async () => {
    await openVendors();
    fireEvent.click(button("Add vendor"));
    expect(dialog().getByText("Add vendor")).toBeTruthy();
    expect(dialog().getByRole("combobox", { name: /Filer status/ }).textContent).toContain("Unknown (taxed as non-filer)");
    expect(dialog().queryByRole("switch", { name: "Active" })).toBeNull();
    expect(button("Save", dialog()).disabled).toBe(true);
    type(/^Vendor name/, "Interwood");
    type(/^NTN/, "1022331-8");
    fireEvent.click(dialog().getByRole("switch", { name: "Checked on the FBR taxpayer list today" }));
    fireEvent.click(button("Save", dialog()));
    await waitFor(() => expect(wht.saveVendor).toHaveBeenCalledTimes(1));
    expect(wht.saveVendor.mock.calls[0]).toEqual([null, {
      name: "Interwood", ntn: "1022331-8", cnic: null, phone: null, address: null, notes: null, filerStatus: "Unknown",
      markFilerStatusChecked: true, isActive: true, concurrencyToken: null,
    }]);
    expect(await screen.findByText("Vendor added.")).toBeTruthy();
    await waitFor(() => expect(wht.listVendors).toHaveBeenCalledTimes(2));
  });

  it("edits a vendor: payments used, when it was last checked, and Active", async () => {
    await openVendors();
    fireEvent.click(button("Edit Al-Noor Steel", table()));
    expect(dialog().getByText("Used by 14 payments")).toBeTruthy();
    expect(dialog().getByText("Last checked Aug 11, 2026")).toBeTruthy();
    fireEvent.click(dialog().getByRole("switch", { name: "Active" }));
    fireEvent.click(button("Save", dialog()));
    await waitFor(() => expect(wht.saveVendor).toHaveBeenCalledTimes(1));
    expect(wht.saveVendor.mock.calls[0]![0]).toBe(1);
    expect(wht.saveVendor.mock.calls[0]![1]).toMatchObject({ isActive: false, markFilerStatusChecked: false, filerStatus: "Filer", concurrencyToken: "ven-1" });
    expect(await screen.findByText("Vendor saved.")).toBeTruthy();
  });

  it("shows a failed list with Try again", async () => {
    wht.listVendors.mockRejectedValueOnce(new Error("Vendors could not be loaded."));
    show();
    await loaded();
    fireEvent.click(tab(/Vendors/));
    expect(await screen.findByText("Vendors could not be loaded.")).toBeTruthy();
    fireEvent.click(button("Try again"));
    expect(await screen.findAllByText("Al-Noor Steel")).toBeTruthy();
  });
});

describe("Finance settings: financial year", () => {
  it("shows the year card and, once checked, who checked the rates and when, with Clear check", async () => {
    wht.getSettings.mockResolvedValue(checked);
    wht.saveSettings.mockResolvedValue(settings({ concurrencyToken: "tok-3" }));
    show();
    await loaded();
    fireEvent.click(tab(/Financial year/));
    expect(screen.getByText("Current year: 2026-27")).toBeTruthy();
    expect(screen.getByText("Checked by accountant")).toBeTruthy();
    expect(screen.getByText("Aug 11, 2026 by admin@dams.local")).toBeTruthy();
    fireEvent.click(button("Clear check"));
    await waitFor(() => expect(wht.saveSettings).toHaveBeenCalledTimes(1));
    expect(wht.saveSettings.mock.calls[0]![0]).toEqual({
      financialYearStartMonth: 7, goLiveDate: null, markRatesConfirmed: false, clearRatesConfirmation: true, concurrencyToken: "tok-2",
    });
    expect(await screen.findByText("Check cleared.")).toBeTruthy();
  });

  it("leaves out the 'by' part when no name was kept", async () => {
    wht.getSettings.mockResolvedValue(settings({ whtRatesConfirmedAt: "2026-08-11T06:00:00" }));
    show();
    await loaded();
    fireEvent.click(tab(/Financial year/));
    expect(screen.getByText("Aug 11, 2026")).toBeTruthy();
  });

  it("Mark as checked sends the saved month, not one picked but not saved in the year card", async () => {
    show();
    await loaded();
    fireEvent.click(tab(/Financial year/));
    expect(screen.getByText("Not checked yet")).toBeTruthy();
    pick(/Year starts in/, "January");
    fireEvent.click(button("Mark as checked"));
    await waitFor(() => expect(wht.saveSettings).toHaveBeenCalledTimes(1));
    expect(wht.saveSettings.mock.calls[0]![0]).toMatchObject({ financialYearStartMonth: 7, markRatesConfirmed: true, goLiveDate: null });
    expect(await screen.findByText("Tax rates marked as checked.")).toBeTruthy();
  });

  it("keeps a month picked but not saved when Mark as checked brings a new version", async () => {
    wht.getSettings.mockResolvedValueOnce(settings()).mockResolvedValue(checked);
    show();
    await loaded();
    fireEvent.click(tab(/Financial year/));
    pick(/Year starts in/, "January");
    fireEvent.click(button("Mark as checked"));
    await waitFor(() => expect(wht.saveSettings).toHaveBeenCalledTimes(1));
    expect(wht.saveSettings.mock.calls[0]![0]).toMatchObject({ financialYearStartMonth: 7, markRatesConfirmed: true, concurrencyToken: "tok" });
    // The reload answers with version tok-2: the card is the same card, still holding January.
    expect(await screen.findByText("Checked by accountant")).toBeTruthy();
    expect(screen.getByRole("combobox", { name: /Year starts in/ }).textContent).toContain("January");
  });

  it("keeps a month picked but not saved when Clear check brings a new version", async () => {
    wht.getSettings.mockResolvedValueOnce(checked).mockResolvedValue(settings({ concurrencyToken: "tok-3" }));
    wht.saveSettings.mockResolvedValue(settings({ concurrencyToken: "tok-3" }));
    show();
    await loaded();
    fireEvent.click(tab(/Financial year/));
    pick(/Year starts in/, "January");
    fireEvent.click(button("Clear check"));
    await waitFor(() => expect(wht.saveSettings).toHaveBeenCalledTimes(1));
    expect(wht.saveSettings.mock.calls[0]![0]).toMatchObject({ financialYearStartMonth: 7, clearRatesConfirmation: true, concurrencyToken: "tok-2" });
    expect(await screen.findByText("Not checked yet")).toBeTruthy();
    expect(screen.getByRole("combobox", { name: /Year starts in/ }).textContent).toContain("January");
    // Saving the year now sends the new version, not the one the page opened with.
    fireEvent.click(button("Save"));
    await waitFor(() => expect(wht.saveSettings).toHaveBeenCalledTimes(2));
    expect(wht.saveSettings.mock.calls[1]![0]).toMatchObject({ financialYearStartMonth: 1, concurrencyToken: "tok-3" });
    expect(await screen.findByText("Finance settings saved.")).toBeTruthy();
  });

  it("keeps the chosen tab after a save", async () => {
    show();
    await loaded();
    fireEvent.click(tab(/Financial year/));
    pick(/Year starts in/, "January");
    fireEvent.click(button("Save"));
    expect(await screen.findByText("Finance settings saved.")).toBeTruthy();
    await waitFor(() => expect(wht.getSettings).toHaveBeenCalledTimes(2));
    expect(tab(/Financial year/).getAttribute("aria-selected")).toBe("true");
  });
});

describe("Finance settings: on a phone", () => {
  it("turns the tabs into one dropdown, shortens the button to Add, and loads twenty more at a time", async () => {
    stubMedia(true);
    wht.listCategories.mockResolvedValue(many(25));
    show();
    await screen.findAllByText("Head 01");
    expect(screen.queryAllByRole("tab")).toHaveLength(0);
    expect(screen.getByRole("combobox", { name: /Section/ }).textContent).toContain("Expense categories");
    expect(button("Add")).toBeTruthy();
    expect(pageText()).toContain("Showing 20 of 25 entries");
    fireEvent.click(button("Load more"));
    expect(pageText()).toContain("Showing 25 of 25 entries");
    expect(screen.queryByRole("button", { name: "Load more" })).toBeNull();
  });
});
