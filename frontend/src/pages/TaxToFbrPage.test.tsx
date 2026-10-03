// @vitest-environment happy-dom
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import type { User } from "../App.tsx";
import { ToastProvider } from "../components/ui/Toast.tsx";
import { PHONE_QUERY } from "../components/ui/useMediaQuery.ts";
import { loansApi } from "../features/finance/loans/api.ts";
import { byVendor, deleteDeposit, downloadWhtStatement, listDeposits, payableSummary, saveDeposit } from "../features/finance/whtApi.ts";
import type { WhtDeposit, WhtPayableSummary, WhtVendorLine } from "../features/finance/whtTypes.ts";
import TaxToFbrPage from "./TaxToFbrPage.tsx";

vi.mock("../features/finance/whtApi.ts", () => ({
  payableSummary: vi.fn(),
  listDeposits: vi.fn(),
  byVendor: vi.fn(),
  saveDeposit: vi.fn(),
  deleteDeposit: vi.fn(),
  downloadWhtStatement: vi.fn(),
}));
vi.mock("../features/finance/loans/api.ts", () => ({ loansApi: { cashAccounts: vi.fn() } }));

const summaryApi = vi.mocked(payableSummary);
const depositsApi = vi.mocked(listDeposits);
const suppliersApi = vi.mocked(byVendor);

const summary: WhtPayableSummary = {
  openingPayable: 0, totalWithheldAllTime: 1_245_850, totalDepositedAllTime: 1_061_500, outstandingPayable: 184_350,
  withheldInPeriod: 412_300, depositedInPeriod: 412_000, paymentCount: 18, vendorCount: 6, bySection: [],
};

const deposit = (over: Partial<WhtDeposit> = {}): WhtDeposit => ({
  id: 1, financeAccountId: 3, financeAccountName: "Meezan Bank", amount: 412_000, depositDate: "2026-09-15T00:00:00",
  challanNumber: "CPR-0923-118", periodFrom: "2026-08-01T00:00:00", periodTo: "2026-08-31T00:00:00", notes: null,
  createdAt: "2026-09-15T08:00:00", concurrencyToken: "token-1", ...over,
});
const deposits = [
  deposit(),
  deposit({ id: 2, amount: 386_500, depositDate: "2026-08-14T00:00:00", challanNumber: "CPR-0814-077", periodFrom: "2026-07-01T00:00:00", periodTo: "2026-07-31T00:00:00", concurrencyToken: "token-2" }),
  deposit({ id: 3, amount: 263_000, depositDate: "2026-07-15T00:00:00", challanNumber: "CPR-0715-031", financeAccountName: "HBL Current", periodFrom: "2026-06-01T00:00:00", periodTo: "2026-06-30T00:00:00", concurrencyToken: "token-3" }),
];
const manyDeposits = (count: number) => Array.from({ length: count }, (_, index) => deposit({ id: index + 1, challanNumber: `CPR-${String(index + 1).padStart(4, "0")}`, concurrencyToken: `token-${index + 1}` }));

const line = (over: Partial<WhtVendorLine>): WhtVendorLine => ({
  vendorId: 1, vendorName: "Al-Noor Steel", ntn: "4410293-1", cnic: null, filerStatus: "Filer", taxSection: "153(1)(a)",
  grossAmount: 4_850_000, whtAmount: 48_500, netPaid: 4_801_500, paymentCount: 3, ...over,
});
const suppliers = [
  line({}),
  line({ vendorId: 2, vendorName: "Khan Builders", ntn: "7712044-3", taxSection: "153(1)(b)", grossAmount: 2_480_000, whtAmount: 99_200, netPaid: 2_380_800 }),
  line({ vendorId: 3, vendorName: "City Hardware", ntn: null, cnic: "37405-1122334-5", filerStatus: "NonFiler", grossAmount: 410_000, whtAmount: 32_800, netPaid: 377_200 }),
  line({ vendorId: null, vendorName: "Unnamed vendor", ntn: null, filerStatus: "Unknown", grossAmount: 75_000, whtAmount: 3_850, netPaid: 71_150 }),
];

const admin: User = { userId: "1", role: "Admin", email: "a@b.c" };
const accountant: User = { userId: "2", role: "Accountant", email: "c@d.e" };
const sales: User = { userId: "3", role: "Employee", email: "e@f.g" };

function show(user: User | null = admin) {
  return render(
    <MemoryRouter initialEntries={["/finance/tax"]}>
      <ToastProvider>
        <Routes>
          <Route path="/finance/tax" element={<TaxToFbrPage user={user} />} />
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
const tab = (name: string) => screen.getByRole("tab", { name });
const pickDay = (field: RegExp, day: string) => {
  fireEvent.click(button(field));
  fireEvent.click(button(day));
};
const setRange = async () => {
  pickDay(/^From/, "October 1, 2026");
  pickDay(/^To/, "October 15, 2026");
  await waitFor(() => expect(summaryApi).toHaveBeenLastCalledWith("2026-10-01", "2026-10-15", expect.anything()));
};
const loaded = async () => { await screen.findAllByText("CPR-0923-118"); };

beforeEach(() => {
  vi.resetAllMocks();
  stubMedia(false);
  // 2026-10-01 12:00 in Pakistan.
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date("2026-10-01T07:00:00Z"));
  Element.prototype.scrollIntoView = () => {};
  summaryApi.mockResolvedValue(summary);
  depositsApi.mockResolvedValue(deposits);
  suppliersApi.mockResolvedValue(suppliers);
  vi.mocked(saveDeposit).mockResolvedValue(deposits[0]!);
  vi.mocked(deleteDeposit).mockResolvedValue({ message: "WHT deposit deleted." });
  vi.mocked(downloadWhtStatement).mockResolvedValue(undefined);
  vi.mocked(loansApi.cashAccounts).mockResolvedValue([{ id: 3, name: "Meezan Bank", accountHolderName: "Seven Ventures", isActive: true }]);
});

afterEach(() => {
  cleanup();
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe("Tax to FBR: who can open it", () => {
  it("opens for Admin and Accountant", async () => {
    show(admin);
    await loaded();
    cleanup();
    show(accountant);
    await loaded();
  });

  it("sends anyone without the finance permission home and asks the server for nothing", async () => {
    show(sales);
    expect(await screen.findByText("Home page")).toBeTruthy();
    expect(summaryApi).not.toHaveBeenCalled();
    expect(depositsApi).not.toHaveBeenCalled();
  });

  it("waits for the signed-in user instead of sending anyone away", () => {
    show(null);
    expect(screen.getByRole("status").textContent).toContain("Loading");
    expect(screen.queryByText("Home page")).toBeNull();
    expect(summaryApi).not.toHaveBeenCalled();
  });
});

describe("Tax to FBR: the sum and the deposits", () => {
  it("shows the four cards from the server and the deposits tab first", async () => {
    show();
    await loaded();
    for (const text of ["Rs 1,245,850", "Rs 1,061,500", "Rs 184,350", "Tax owed before go-live"]) expect(screen.getByText(text)).toBeTruthy();
    expect(tab("Deposits to FBR").getAttribute("aria-selected")).toBe("true");
    expect(tab("Withheld by supplier").getAttribute("aria-selected")).toBe("false");
    expect(screen.queryByText(/In the selected period/)).toBeNull();
    expect(summaryApi).toHaveBeenCalledWith("", "", expect.anything());
  });

  it("lists each deposit with its date, account, period and amount", async () => {
    show();
    await loaded();
    const row = within(screen.getAllByRole("row").find((candidate) => within(candidate).queryByText("CPR-0923-118")) as HTMLElement);
    for (const text of ["Sep 15, 2026", "Meezan Bank", "Aug 1 – Aug 31, 2026", "Rs 412,000"]) expect(row.getByText(text)).toBeTruthy();
    expect(row.getByRole("button", { name: "Edit deposit CPR-0923-118" })).toBeTruthy();
    expect(screen.getByText(/Showing/).textContent).toBe("Showing 1–3 of 3 entries");
  });

  it("does not ask for the supplier list until its tab is opened", async () => {
    show();
    await loaded();
    expect(suppliersApi).not.toHaveBeenCalled();
    fireEvent.click(tab("Withheld by supplier"));
    await screen.findAllByText("Al-Noor Steel");
    expect(suppliersApi).toHaveBeenCalledTimes(1);
  });

  it("says there are no deposits, and says so differently for a period", async () => {
    depositsApi.mockResolvedValue([]);
    show();
    expect(await screen.findByText("No deposits yet.")).toBeTruthy();
    await setRange();
    expect(await screen.findByText("Nothing deposited in this period.")).toBeTruthy();
  });

  it("shows an error with Try again, and the list comes back", async () => {
    depositsApi.mockRejectedValueOnce(new Error("The withholding position could not be loaded."));
    show();
    const alert = await screen.findByRole("alert");
    expect(alert.textContent).toContain("The withholding position could not be loaded.");
    fireEvent.click(button("Try again", within(alert)));
    await loaded();
    expect(depositsApi).toHaveBeenCalledTimes(2);
  });

  it("shows dashes and a Try again for the cards when the figures fail, never Rs 0", async () => {
    summaryApi.mockRejectedValueOnce(new Error("The withholding position could not be loaded."));
    show();
    await loaded();
    await waitFor(() => expect(screen.getAllByText("—").length).toBeGreaterThanOrEqual(4));
    expect(screen.queryByText("Rs 0")).toBeNull();
    fireEvent.click(button("Try again"));
    expect(await screen.findByText("Rs 184,350")).toBeTruthy();
  });

  it("shows nothing in the cards before the figures arrive", async () => {
    summaryApi.mockReturnValue(new Promise(() => {}));
    show();
    await loaded();
    const cards = within(screen.getByRole("region", { name: "Tax owed to FBR" }));
    expect(cards.queryByText(/^Rs \d/)).toBeNull();
    expect(cards.queryByText("Tax owed before go-live")).toBeNull();
  });
});

describe("Tax to FBR: withheld by supplier", () => {
  it("lists each supplier with the status the tax was withheld under", async () => {
    show();
    await loaded();
    fireEvent.click(tab("Withheld by supplier"));
    await screen.findAllByText("Al-Noor Steel");
    const city = within(screen.getAllByRole("row").find((candidate) => within(candidate).queryByText("City Hardware")) as HTMLElement);
    for (const text of ["37405-1122334-5", "Non-filer", "153(1)(a)", "Rs 410,000", "Rs 32,800", "Rs 377,200"]) expect(city.getByText(text)).toBeTruthy();
    expect(within(screen.getAllByRole("row").find((candidate) => within(candidate).queryByText("Unnamed vendor")) as HTMLElement).getByText("Unknown")).toBeTruthy();
    expect(table().getAllByText("Filer")).toHaveLength(2);
    expect(screen.queryByRole("button", { name: /Edit/ })).toBeNull();
  });

  it("keeps the chosen tab when the dates change and asks for both lists under the range", async () => {
    show();
    await loaded();
    fireEvent.click(tab("Withheld by supplier"));
    await screen.findAllByText("Al-Noor Steel");
    await setRange();
    expect(tab("Withheld by supplier").getAttribute("aria-selected")).toBe("true");
    await waitFor(() => expect(suppliersApi).toHaveBeenLastCalledWith("2026-10-01", "2026-10-15", expect.anything()));
  });

  it("says no tax was withheld, and says so differently for a period", async () => {
    suppliersApi.mockResolvedValue([]);
    show();
    await loaded();
    fireEvent.click(tab("Withheld by supplier"));
    expect(await screen.findByText("No tax was withheld.")).toBeTruthy();
    await setRange();
    expect(await screen.findByText("No tax was withheld in this period.")).toBeTruthy();
  });
});

describe("Tax to FBR: From and To", () => {
  it("applies a range only once both dates are set, and keeps the cards on the all-time figures", async () => {
    show();
    await loaded();
    pickDay(/^From/, "October 1, 2026");
    expect(await screen.findByText("Enter both a From and a To date, or clear them both.")).toBeTruthy();
    expect(summaryApi).toHaveBeenCalledTimes(1);
    pickDay(/^To/, "October 15, 2026");
    await waitFor(() => expect(summaryApi).toHaveBeenCalledTimes(2));
    expect(depositsApi).toHaveBeenLastCalledWith("2026-10-01", "2026-10-15", expect.anything());
    expect(screen.getByText("Rs 184,350")).toBeTruthy();
  });

  it("shows the period line with the supplier rows it counts, not the card's supplier count", async () => {
    show();
    await loaded();
    await setRange();
    const line = await screen.findByText(/In the selected period/);
    expect(line.textContent).toBe("In the selected period: withheld Rs 412,300 from 18 payments to 4 suppliers · deposited Rs 412,000");
  });

  it("says '1 payment' and '1 supplier'", async () => {
    summaryApi.mockResolvedValue({ ...summary, paymentCount: 1 });
    suppliersApi.mockResolvedValue([suppliers[0]!]);
    show();
    await loaded();
    await setRange();
    expect((await screen.findByText(/In the selected period/)).textContent).toContain("from 1 payment to 1 supplier");
  });

  it("shows no period line at all when the supplier list fails, rather than a sentence without its supplier count", async () => {
    suppliersApi.mockRejectedValue(new Error("The supplier list is down."));
    show();
    await loaded();
    await setRange();
    await waitFor(() => expect(suppliersApi).toHaveBeenLastCalledWith("2026-10-01", "2026-10-15", expect.anything()));
    await waitFor(() => expect(summaryApi).toHaveBeenCalledTimes(2));
    expect(screen.queryByText(/In the selected period/)).toBeNull();
  });

  it("hides the line again on Reset and goes back to all time", async () => {
    show();
    await loaded();
    await setRange();
    await screen.findByText(/In the selected period/);
    fireEvent.click(button("Reset"));
    await waitFor(() => expect(screen.queryByText(/In the selected period/)).toBeNull());
    await waitFor(() => expect(summaryApi).toHaveBeenLastCalledWith("", "", expect.anything()));
    expect(screen.queryByRole("button", { name: "Reset" })).toBeNull();
  });

  it("sends the chosen range to Export, and tells the user when it fails", async () => {
    show();
    await loaded();
    fireEvent.click(button("Export"));
    await waitFor(() => expect(downloadWhtStatement).toHaveBeenCalledWith("", ""));
    await setRange();
    vi.mocked(downloadWhtStatement).mockRejectedValueOnce(new Error("boom"));
    fireEvent.click(button("Export"));
    await waitFor(() => expect(downloadWhtStatement).toHaveBeenLastCalledWith("2026-10-01", "2026-10-15"));
    expect(await screen.findByText("The withholding statement could not be exported.")).toBeTruthy();
  });
});

describe("Tax to FBR: pages", () => {
  it("cuts a long list into pages of twenty on a desktop", async () => {
    depositsApi.mockResolvedValue(manyDeposits(21));
    show();
    await screen.findAllByText("CPR-0001");
    expect(screen.getByText(/Showing/).textContent).toBe("Showing 1–20 of 21 entries");
    expect(screen.queryByText("CPR-0021")).toBeNull();
    fireEvent.click(button("Page 2"));
    expect(screen.getAllByText("CPR-0021").length).toBeGreaterThan(0);
    expect(screen.queryByText("CPR-0001")).toBeNull();
    expect(screen.getByText(/Showing/).textContent).toBe("Showing 21–21 of 21 entries");
  });

  it("steps back a page when a delete empties the last one", async () => {
    depositsApi.mockResolvedValue(manyDeposits(21));
    show();
    await screen.findAllByText("CPR-0001");
    fireEvent.click(button("Page 2"));
    depositsApi.mockResolvedValue(manyDeposits(20));
    fireEvent.click(table().getByRole("button", { name: "More for deposit CPR-0021" }));
    fireEvent.click(screen.getByRole("menuitem", { name: "Delete" }));
    fireEvent.click(button("Delete", within(screen.getByRole("dialog"))));
    await waitFor(() => expect(screen.getByText(/Showing/).textContent).toBe("Showing 1–20 of 20 entries"));
    expect(screen.getAllByText("CPR-0001").length).toBeGreaterThan(0);
  });

  it("remembers the step back, so the list growing again does not return to the old page", async () => {
    depositsApi.mockResolvedValue(manyDeposits(21));
    show();
    await screen.findAllByText("CPR-0001");
    fireEvent.click(button("Page 2"));
    depositsApi.mockResolvedValue(manyDeposits(20));
    fireEvent.click(table().getByRole("button", { name: "More for deposit CPR-0021" }));
    fireEvent.click(screen.getByRole("menuitem", { name: "Delete" }));
    fireEvent.click(button("Delete", within(screen.getByRole("dialog"))));
    await waitFor(() => expect(screen.getByText(/Showing/).textContent).toBe("Showing 1–20 of 20 entries"));
    // Someone records a twenty-first deposit; the list reloads and must stay on page one.
    depositsApi.mockResolvedValue(manyDeposits(21));
    fireEvent.click(button("Record deposit"));
    fireEvent.click(within(screen.getByRole("dialog")).getByRole("combobox", { name: /Paid from account/ }));
    fireEvent.click(await screen.findByRole("option", { name: "Meezan Bank · Seven Ventures" }));
    fireEvent.click(button("Save", within(screen.getByRole("dialog").querySelector("footer") as HTMLElement)));
    await waitFor(() => expect(saveDeposit).toHaveBeenCalled());
    await waitFor(() => expect(screen.getByText(/Showing/).textContent).toBe("Showing 1–20 of 21 entries"));
  });

  it("keeps the page it is on after a deposit is edited", async () => {
    depositsApi.mockResolvedValue(manyDeposits(21));
    show();
    await screen.findAllByText("CPR-0001");
    fireEvent.click(button("Page 2"));
    fireEvent.click(table().getByRole("button", { name: "Edit deposit CPR-0021" }));
    fireEvent.click(button("Save", within(screen.getByRole("dialog").querySelector("footer") as HTMLElement)));
    await waitFor(() => expect(saveDeposit).toHaveBeenCalled());
    await waitFor(() => expect(depositsApi).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(screen.getByText(/Showing/).textContent).toBe("Showing 21–21 of 21 entries");
  });

  it("loads twenty more at a time on a phone, with the count underneath", async () => {
    stubMedia(true);
    depositsApi.mockResolvedValue(manyDeposits(21));
    show();
    await screen.findAllByText("CPR-0001");
    expect(screen.getByText(/Showing/).textContent).toBe("Showing 20 of 21 entries");
    expect(screen.queryByText("CPR-0021")).toBeNull();
    expect(screen.queryByRole("navigation", { name: "Pagination" })).toBeNull();
    fireEvent.click(button("Load more"));
    expect(screen.getAllByText("CPR-0021").length).toBeGreaterThan(0);
    expect(screen.getByText(/Showing/).textContent).toBe("Showing 21 of 21 entries");
    expect(screen.queryByRole("button", { name: "Load more" })).toBeNull();
  });

  it("calls the second tab By supplier on a phone, and puts Export beside Record deposit as an icon", async () => {
    stubMedia(true);
    show();
    await screen.findAllByText("CPR-0923-118");
    expect(tab("By supplier")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Export" }).textContent).toBe("");
    expect(button("Record deposit")).toBeTruthy();
  });
});

describe("Tax to FBR: record, edit and delete", () => {
  it("opens the popup on what is owed, and a saved deposit reloads the cards and the list", async () => {
    show();
    await loaded();
    fireEvent.click(button("Record deposit"));
    const dialog = within(screen.getByRole("dialog"));
    expect(dialog.getByText("Owed now: Rs 184,350")).toBeTruthy();
    fireEvent.click(dialog.getByRole("combobox", { name: /Paid from account/ }));
    fireEvent.click(await screen.findByRole("option", { name: "Meezan Bank · Seven Ventures" }));
    fireEvent.click(button("Save", within(screen.getByRole("dialog").querySelector("footer") as HTMLElement)));
    await waitFor(() => expect(saveDeposit).toHaveBeenCalledTimes(1));
    expect(await screen.findByText("Deposit recorded.")).toBeTruthy();
    await waitFor(() => expect(summaryApi).toHaveBeenCalledTimes(2));
    expect(depositsApi).toHaveBeenCalledTimes(2);
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("holds Record deposit back until the owed figure has arrived, so the popup can open on it", async () => {
    let arrive: (value: WhtPayableSummary) => void = () => {};
    summaryApi.mockReturnValue(new Promise<WhtPayableSummary>((resolve) => { arrive = resolve; }));
    show();
    await loaded();
    expect(button("Record deposit").disabled).toBe(true);
    arrive(summary);
    await waitFor(() => expect(button("Record deposit").disabled).toBe(false));
    fireEvent.click(button("Record deposit"));
    expect((within(screen.getByRole("dialog")).getByLabelText(/^Amount/) as HTMLInputElement).value).toBe("184,350");
  });

  it("still lets a deposit be recorded when the owed figure could not be loaded", async () => {
    summaryApi.mockRejectedValue(new Error("The withholding position could not be loaded."));
    show();
    await loaded();
    await waitFor(() => expect(button("Record deposit").disabled).toBe(false));
    fireEvent.click(button("Record deposit"));
    expect((within(screen.getByRole("dialog")).getByLabelText(/^Amount/) as HTMLInputElement).value).toBe("");
  });

  it("asks before deleting, by the deposit's own challan number and amount", async () => {
    show();
    await loaded();
    fireEvent.click(table().getByRole("button", { name: "More for deposit CPR-0923-118" }));
    fireEvent.click(screen.getByRole("menuitem", { name: "Delete" }));
    const dialog = within(screen.getByRole("dialog"));
    expect(dialog.getByText("Delete deposit CPR-0923-118?")).toBeTruthy();
    expect(dialog.getByText("Rs 412,000 goes back to being owed to FBR.")).toBeTruthy();
    fireEvent.click(dialog.getByRole("button", { name: "Cancel" }));
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(deleteDeposit).not.toHaveBeenCalled();
  });

  it("deletes with the version it showed, then reloads the cards and the list", async () => {
    show();
    await loaded();
    fireEvent.click(table().getByRole("button", { name: "More for deposit CPR-0923-118" }));
    fireEvent.click(screen.getByRole("menuitem", { name: "Delete" }));
    fireEvent.click(button("Delete", within(screen.getByRole("dialog"))));
    await waitFor(() => expect(deleteDeposit).toHaveBeenCalledWith(1, "token-1"));
    expect(await screen.findByText("Deposit deleted.")).toBeTruthy();
    await waitFor(() => expect(summaryApi).toHaveBeenCalledTimes(2));
    expect(depositsApi).toHaveBeenCalledTimes(2);
  });

  it("shows the server's refusal in a toast and still refreshes", async () => {
    vi.mocked(deleteDeposit).mockRejectedValueOnce(new Error("This deposit was changed by someone else. Refresh and try again."));
    show();
    await loaded();
    fireEvent.click(table().getByRole("button", { name: "More for deposit CPR-0923-118" }));
    fireEvent.click(screen.getByRole("menuitem", { name: "Delete" }));
    fireEvent.click(button("Delete", within(screen.getByRole("dialog"))));
    expect(await screen.findByText("This deposit was changed by someone else. Refresh and try again.")).toBeTruthy();
    expect(screen.queryByRole("dialog")).toBeNull();
    await waitFor(() => expect(depositsApi).toHaveBeenCalledTimes(2));
  });

  it("asks about 'this deposit' when it has no challan number", async () => {
    depositsApi.mockResolvedValue([deposit({ challanNumber: null })]);
    show();
    await screen.findAllByText("Sep 15, 2026");
    fireEvent.click(table().getByRole("button", { name: "More for deposit of Sep 15, 2026" }));
    fireEvent.click(screen.getByRole("menuitem", { name: "Delete" }));
    expect(within(screen.getByRole("dialog")).getByText("Delete this deposit?")).toBeTruthy();
  });
});
