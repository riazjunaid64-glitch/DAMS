// @vitest-environment happy-dom
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { ToastProvider } from "../components/ui/Toast.tsx";
import { PHONE_QUERY } from "../components/ui/useMediaQuery.ts";
import type { User } from "../App.tsx";
import { readFinanceAccountsPage } from "../features/finance/accountPageReads.ts";
import type { Account, Overview, Transaction } from "../features/finance/accounts/accountGroups.ts";
import { accountsApi } from "../features/finance/accounts/api.ts";
import FinanceAccountsPage from "./FinanceAccountsPage.tsx";

vi.mock("../features/finance/accountPageReads.ts", () => ({ readFinanceAccountsPage: vi.fn() }));
vi.mock("../features/finance/accounts/api.ts", () => ({
  accountsApi: { details: vi.fn(), transactions: vi.fn(), save: vi.fn(), setActive: vi.fn() },
}));

const read = vi.mocked(readFinanceAccountsPage);
const api = vi.mocked(accountsApi);

const account = (over: Partial<Account>): Account => ({
  id: 1, name: "Cash in office", type: 1, accountHolderName: "Riaz Junaid", openingBalance: 500_000, ledgerCode: "1001", displayOrder: 3,
  systemRole: 0, isSystemAccount: false, bankOrWalletName: null, description: null, isActive: true, revenueReceived: 2_100_000,
  expensesPaid: 1_350_000, whtWithheld: 0, whtDeposited: 0, netMovement: 750_000, currentBalance: 1_250_000, transactionCount: 2, concurrencyToken: "tok-1", ...over,
});

const cash = account({});
const meezan = account({
  id: 2, name: "Meezan Bank", type: "Bank", accountHolderName: "Seven Ventures", openingBalance: 12_000_000, ledgerCode: "1002", bankOrWalletName: "Meezan Bank",
  revenueReceived: 7_282_660, expensesPaid: 882_660, currentBalance: 18_400_000, whtWithheld: 59_500, whtDeposited: 0, displayOrder: 7, concurrencyToken: "tok-2",
});
const alfalah = account({
  id: 3, name: "Alfalah Saving", type: 2, accountHolderName: "Seven Ventures", openingBalance: 0, ledgerCode: "1004", isActive: false, revenueReceived: 0, expensesPaid: 0,
  currentBalance: 0, concurrencyToken: "tok-3",
});
const tax = account({
  id: 4, name: "Tax payable (FBR)", type: 5, accountHolderName: "Seven Ventures", openingBalance: 0, ledgerCode: "2102", systemRole: 1, isSystemAccount: true,
  revenueReceived: 184_350, expensesPaid: 0, currentBalance: 184_350, concurrencyToken: "tok-4",
});
const deposits = account({
  id: 5, name: "Customer deposits", type: 5, accountHolderName: "Seven Ventures", openingBalance: 0, ledgerCode: "2101", systemRole: "CustomerDeposits", isSystemAccount: true,
  revenueReceived: 18_750_000, expensesPaid: 0, currentBalance: 18_750_000, concurrencyToken: "tok-5",
});
const loan = account({
  id: 6, name: "Bank Alfalah loan", type: 5, accountHolderName: "Seven Ventures", openingBalance: 3_000_000, ledgerCode: "2201", revenueReceived: 0, expensesPaid: 0,
  currentBalance: 3_000_000, concurrencyToken: "tok-6",
});
const capital = account({
  id: 7, name: "Capital — Riaz Junaid", type: 6, accountHolderName: "Riaz Junaid", openingBalance: 10_000_000, ledgerCode: "3001", revenueReceived: 4_000_000, expensesPaid: 0,
  currentBalance: 14_000_000, concurrencyToken: "tok-7",
});
const staff = account({
  id: 8, name: "Adeel Satti — Staff float", type: 10, accountHolderName: "Adeel Satti", openingBalance: 0, ledgerCode: "1010", revenueReceived: 300_000, expensesPaid: 155_000,
  currentBalance: 145_000, concurrencyToken: "tok-8",
});
const everyAccount = [cash, meezan, alfalah, staff, tax, deposits, loan, capital];

const overview = (over: Partial<Overview> = {}): Overview => ({
  activeAccounts: 7, inactiveAccounts: 1, totalBalance: 19_650_000, holderBalances: [],
  openingDebitTotal: 12_500_000, openingCreditTotal: 13_000_000, goLiveDate: "2026-08-15T00:00:00",
  holderNames: ["Adeel Satti", "Riaz Junaid", "Seven Ventures"], ...over,
});

const transaction = (over: Partial<Transaction>): Transaction => ({
  kind: "Revenue", recordId: 1, date: "2026-09-02T00:00:00", label: "Late payment surcharge", reference: "MR-0031", projectName: "Mall of Faisal Hills",
  amount: 30_000, grossAmount: 30_000, whtAmount: 0, ...over,
});

const admin: User = { userId: "1", role: "Admin", email: "a@b.c" };
const accountant: User = { userId: "2", role: "Accountant", email: "c@b.c" };
const salesManager: User = { userId: "3", role: "SalesManager", email: "s@b.c" };

function show(user: User | null = admin) {
  return render(
    <MemoryRouter initialEntries={["/finance/accounts"]}>
      <ToastProvider>
        <Routes>
          <Route path="/finance/accounts" element={<FinanceAccountsPage user={user} />} />
          <Route path="/finance/settings" element={<p>Finance settings page</p>} />
          <Route path="/" element={<p>Home page</p>} />
        </Routes>
      </ToastProvider>
    </MemoryRouter>,
  );
}

function stubMedia(phone: boolean) {
  vi.stubGlobal("matchMedia", (query: string) => ({
    matches: phone && query === PHONE_QUERY,
    media: query,
    addEventListener: () => {},
    removeEventListener: () => {},
  }));
}

const table = () => within(screen.getByRole("table"));
const dialogs = () => screen.getAllByRole("dialog");
const dialog = (index = 0) => within(dialogs()[index]!);
const button = (name: string | RegExp, scope: { getByRole: typeof screen.getByRole } = screen) => scope.getByRole("button", { name }) as HTMLButtonElement;
const rowOf = (name: string) => within(table().getAllByRole("row").find((candidate) => within(candidate).queryByText(name)) as HTMLElement);
const field = (name: string | RegExp) => dialog().getByRole("textbox", { name }) as HTMLInputElement;
const type = (input: HTMLElement, value: string) => fireEvent.change(input, { target: { value } });
const pick = (name: RegExp | string, option: string) => {
  fireEvent.click(dialog().getByRole("combobox", { name }));
  fireEvent.click(screen.getByRole("option", { name: option }));
};
const footerButton = (name: string, index = 0) => within(dialogs()[index]!.querySelector("footer") as HTMLElement).getByRole("button", { name }) as HTMLButtonElement;
const lastBody = () => api.save.mock.calls.at(-1)![0];
const loaded = async () => { await screen.findAllByText("Meezan Bank"); };

beforeEach(() => {
  vi.resetAllMocks();
  stubMedia(false);
  Element.prototype.scrollIntoView = () => {};
  read.mockImplementation((async (_filters: unknown, includeOverview: boolean) => ({
    items: everyAccount, hasMore: false, overview: includeOverview ? overview() : undefined,
  })) as never);
  api.details.mockImplementation(async (id) => everyAccount.find((candidate) => candidate.id === id)!);
  api.transactions.mockResolvedValue({ items: [] });
  api.save.mockResolvedValue(undefined);
  api.setActive.mockResolvedValue(undefined);
});

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("Manage accounts: the page frame", () => {
  it("has the title and one Add account button, and no back link, subtitle or stat boxes", async () => {
    show();
    expect(await screen.findByRole("heading", { name: "Manage accounts" })).toBeTruthy();
    expect(button("Add account")).toBeTruthy();
    await loaded();
    expect(screen.queryByText(/Finance dashboard/)).toBeNull();
    expect(screen.queryByText(/verified chart/)).toBeNull();
    expect(screen.queryByText(/Active accounts/)).toBeNull();
    expect(screen.queryByText(/Combined balance/)).toBeNull();
    expect(screen.queryByText(/Account holders/)).toBeNull();
  });

  it("asks for 200 accounts and the overview on the first load", async () => {
    show();
    await loaded();
    expect(read).toHaveBeenCalledTimes(1);
    expect(read.mock.calls[0]![0]).toEqual({ search: "", status: "all", typeFilter: "", holderFilter: "" });
    expect(read.mock.calls[0]![1]).toBe(true);
  });

  it("waits for the signed-in user before deciding, and never sends anyone away meanwhile", () => {
    show(null);
    expect(screen.getByRole("status")).toBeTruthy();
    expect(screen.queryByText("Home page")).toBeNull();
    expect(read).not.toHaveBeenCalled();
  });

  it("sends a user without Finance access to the home page and asks for nothing", async () => {
    show(salesManager);
    expect(await screen.findByText("Home page")).toBeTruthy();
    expect(read).not.toHaveBeenCalled();
  });

  it("lets an Accountant in", async () => {
    show(accountant);
    await loaded();
    expect(screen.queryByText("Home page")).toBeNull();
  });
});

describe("Manage accounts: the table", () => {
  it("shows one header, with an empty actions column", async () => {
    show();
    await loaded();
    expect(table().getAllByRole("columnheader").map((header) => header.textContent)).toEqual([
      "Account", "Type / holder", "Opening", "Increases", "Decreases", "Current balance", "Status", "Actions",
    ]);
  });

  it("groups the accounts in board order and leaves out empty groups", async () => {
    show();
    await loaded();
    const headings = table().getAllByRole("row").map((candidate) => candidate.textContent ?? "").filter((text) => /^(Cash & bank|Cash held by staff|Fixed assets|Work in progress|Receivables|Liabilities|Capital)$/.test(text));
    expect(headings).toEqual(["Cash & bank", "Cash held by staff", "Liabilities", "Capital"]);
  });

  it("shows the ledger code and bank name under the name, and the type with the holder", async () => {
    show();
    await loaded();
    const meezanRow = rowOf("Meezan Bank");
    expect(meezanRow.getByText("GL 1002 · Meezan Bank")).toBeTruthy();
    expect(meezanRow.getByText("Bank")).toBeTruthy();
    expect(meezanRow.getByText("Seven Ventures")).toBeTruthy();
  });

  it("shows opening, increases, decreases and current balance, with a minus for a negative opening", async () => {
    read.mockImplementation((async (_filters: unknown, includeOverview: boolean) => ({
      items: [account({ id: 9, name: "Overdrawn", type: 2, openingBalance: -50_000, currentBalance: -20_000, ledgerCode: null })], hasMore: false, overview: includeOverview ? overview() : undefined,
    })) as never);
    show();
    await screen.findAllByText("Overdrawn");
    const cells = rowOf("Overdrawn").getAllByRole("cell").map((cell) => cell.textContent);
    expect(cells.slice(2, 6)).toEqual(["-Rs 50,000", "Rs 2,100,000", "Rs 1,350,000", "-Rs 20,000"]);
  });

  it("shows Active and Inactive with the shared badge", async () => {
    show();
    await loaded();
    expect(rowOf("Meezan Bank").getByText("Active")).toBeTruthy();
    expect(rowOf("Alfalah Saving").getByText("Inactive")).toBeTruthy();
  });

  it("adds a subtotal row per group, and no all-accounts total", async () => {
    show();
    await loaded();
    const subtotal = rowOf("Total cash & bank").getAllByRole("cell").map((cell) => cell.textContent);
    // Cash 500,000 + Meezan 12,000,000 + Alfalah 0; inactive accounts count.
    expect(subtotal.slice(2, 6)).toEqual(["Rs 12,500,000", "Rs 9,382,660", "Rs 2,232,660", "Rs 19,650,000"]);
    expect(table().getByText("Total cash held by staff")).toBeTruthy();
    expect(table().getByText("Total liabilities")).toBeTruthy();
    expect(table().getByText("Total capital")).toBeTruthy();
    expect(screen.queryByText(/All accounts total/i)).toBeNull();
  });

  it("has a pencil on every account and a ⋯ on every account except an active system account", async () => {
    show();
    await loaded();
    for (const name of everyAccount.map((candidate) => candidate.name)) expect(within(rowOf(name).getAllByRole("cell").at(-1) as HTMLElement).getByRole("button", { name: `Edit ${name}` })).toBeTruthy();
    expect(screen.queryByRole("button", { name: "More for Tax payable (FBR)" })).toBeNull();
    expect(screen.queryByRole("button", { name: "More for Customer deposits" })).toBeNull();
    expect(screen.getByRole("button", { name: "More for Meezan Bank" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "More for Alfalah Saving" })).toBeTruthy();
    expect(screen.queryByText("Protected")).toBeNull();
  });

  it("offers Deactivate on an active account and Reactivate on an inactive one", async () => {
    show();
    await loaded();
    fireEvent.click(screen.getByRole("button", { name: "More for Meezan Bank" }));
    expect(screen.getByRole("menuitem", { name: "Deactivate" })).toBeTruthy();
    fireEvent.keyDown(document.body, { key: "Escape" });
    cleanup();
    show();
    await loaded();
    fireEvent.click(screen.getByRole("button", { name: "More for Alfalah Saving" }));
    expect(screen.getByRole("menuitem", { name: "Reactivate" })).toBeTruthy();
  });

  it("offers Reactivate on an inactive system account", async () => {
    read.mockImplementation((async (_filters: unknown, includeOverview: boolean) => ({
      items: [account({ ...tax, isActive: false })], hasMore: false, overview: includeOverview ? overview() : undefined,
    })) as never);
    show();
    await screen.findAllByText("Tax payable (FBR)");
    fireEvent.click(screen.getByRole("button", { name: "More for Tax payable (FBR)" }));
    expect(screen.getByRole("menuitem", { name: "Reactivate" })).toBeTruthy();
  });

  it("opens the account details from the row, and not from the pencil or ⋯", async () => {
    show();
    await loaded();
    fireEvent.click(button("Edit Meezan Bank"));
    expect(await screen.findByRole("heading", { name: "Edit account" })).toBeTruthy();
    expect(api.details).not.toHaveBeenCalled();
    fireEvent.click(button("Cancel", dialog()));
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());

    fireEvent.click(screen.getByRole("button", { name: "More for Meezan Bank" }));
    expect(screen.queryByRole("dialog")).toBeNull();
    fireEvent.keyDown(document.body, { key: "Escape" });

    fireEvent.click(rowOf("Meezan Bank").getByText("Meezan Bank", { selector: "span.font-extrabold" }));
    expect(await screen.findByRole("heading", { name: /Meezan Bank/ })).toBeTruthy();
    await waitFor(() => expect(api.details).toHaveBeenCalledWith(2, expect.anything()));
  });

  it("shows the empty state when there are no accounts", async () => {
    read.mockImplementation((async () => ({ items: [], hasMore: false, overview: overview() })) as never);
    show();
    expect(await screen.findByText("No finance accounts found.")).toBeTruthy();
    expect(screen.queryByRole("table")).toBeNull();
  });

  it("shows grey rows while the first answer is on its way", async () => {
    read.mockReturnValue(new Promise(() => {}));
    show();
    expect((await screen.findAllByRole("columnheader")).length).toBeGreaterThan(0);
    expect(document.querySelectorAll(".animate-pulse").length).toBeGreaterThan(0);
    expect(screen.queryByText("No finance accounts found.")).toBeNull();
  });

  it("keeps the old rows while a reload is on its way", async () => {
    show();
    await loaded();
    let release: (value: unknown) => void = () => {};
    read.mockImplementation((() => new Promise((resolve) => { release = resolve; })) as never);
    fireEvent.click(button("Edit Meezan Bank"));
    type(field(/Account name/), "Meezan Bank — Current");
    fireEvent.click(footerButton("Save"));
    await waitFor(() => expect(api.save).toHaveBeenCalled());
    await waitFor(() => expect(read).toHaveBeenCalledTimes(2));
    expect(screen.getAllByText("Meezan Bank").length).toBeGreaterThan(0);
    expect(screen.getByRole("progressbar", { name: "Loading" })).toBeTruthy();
    release({ items: everyAccount, hasMore: false, overview: overview() });
  });

  it("shows the server's message with Try again, instead of the empty text", async () => {
    read.mockRejectedValueOnce(new Error("Accounts could not be loaded."));
    show();
    expect(await screen.findByText("Accounts could not be loaded.")).toBeTruthy();
    expect(screen.queryByText("No finance accounts found.")).toBeNull();
    fireEvent.click(button("Try again"));
    await loaded();
    expect(screen.queryByText("Accounts could not be loaded.")).toBeNull();
  });

  it("warns, in gold, when the server has more than the 200 accounts it sent", async () => {
    read.mockImplementation((async (_filters: unknown, includeOverview: boolean) => ({ items: everyAccount, hasMore: true, overview: includeOverview ? overview() : undefined })) as never);
    show();
    expect(await screen.findByText("Showing the first 200 accounts. Narrow the search to see the rest.")).toBeTruthy();
  });
});

describe("Manage accounts: the opening balances notice", () => {
  it("says debits are more than credits", async () => {
    read.mockImplementation((async (_filters: unknown, includeOverview: boolean) => ({
      items: everyAccount, hasMore: false, overview: includeOverview ? overview({ openingDebitTotal: 920_000, openingCreditTotal: 500_000 }) : undefined,
    })) as never);
    show();
    expect(await screen.findByText("Opening balances don't match: debits are Rs 420,000 more than credits.")).toBeTruthy();
  });

  it("says credits are more than debits", async () => {
    show();
    expect(await screen.findByText("Opening balances don't match: credits are Rs 500,000 more than debits.")).toBeTruthy();
  });

  it("is hidden when they match, and has no button", async () => {
    read.mockImplementation((async (_filters: unknown, includeOverview: boolean) => ({
      items: everyAccount, hasMore: false, overview: includeOverview ? overview({ openingDebitTotal: 100, openingCreditTotal: 100 }) : undefined,
    })) as never);
    show();
    await loaded();
    expect(screen.queryByText(/Opening balances don't match/)).toBeNull();
  });

  it("is hidden while the page is loading and when the load failed", async () => {
    read.mockReturnValue(new Promise(() => {}));
    show();
    await screen.findAllByRole("columnheader");
    expect(screen.queryByText(/Opening balances don't match/)).toBeNull();
    cleanup();
    read.mockReset();
    read.mockRejectedValue(new Error("Accounts could not be loaded."));
    show();
    await screen.findByText("Accounts could not be loaded.");
    expect(screen.queryByText(/Opening balances don't match/)).toBeNull();
  });

  it("does not follow the filters: the overview is not asked for again on a filter change", async () => {
    show();
    await loaded();
    fireEvent.click(screen.getAllByRole("combobox", { name: /Status/ })[0]!);
    fireEvent.click(screen.getByRole("option", { name: "Inactive" }));
    await waitFor(() => expect(read).toHaveBeenCalledTimes(2));
    expect(read.mock.calls[1]![1]).toBe(false);
    expect(screen.getByText(/Opening balances don't match/)).toBeTruthy();
  });

  it("reloads, overview included, after a save", async () => {
    show();
    await loaded();
    fireEvent.click(button("Edit Meezan Bank"));
    type(field(/Account name/), "Meezan Bank — Current");
    fireEvent.click(footerButton("Save"));
    await waitFor(() => expect(read).toHaveBeenCalledTimes(2));
    expect(read.mock.calls[1]![1]).toBe(true);
  });
});

describe("Manage accounts: the filters", () => {
  it("has a search box with today's placeholder, and Type, Holder and Status", async () => {
    show();
    await loaded();
    expect(screen.getAllByPlaceholderText("Account, holder, bank or wallet").length).toBeGreaterThan(0);
    for (const label of ["Type", "Holder", "Status"]) expect(screen.getAllByRole("combobox", { name: new RegExp(label) }).length).toBeGreaterThan(0);
  });

  it("offers the ten types in board order", async () => {
    show();
    await loaded();
    fireEvent.click(screen.getAllByRole("combobox", { name: /Type/ })[0]!);
    expect(screen.getAllByRole("option").map((option) => option.textContent)).toEqual([
      "All", "Cash", "Bank", "Mobile wallet", "Other", "Staff float", "Fixed asset", "Receivable", "Work in progress", "Liability", "Capital",
    ]);
  });

  it("offers every holder name, from the overview, after All", async () => {
    show();
    await loaded();
    fireEvent.click(screen.getAllByRole("combobox", { name: /Holder/ })[0]!);
    expect(screen.getAllByRole("option").map((option) => option.textContent)).toEqual(["All", "Adeel Satti", "Riaz Junaid", "Seven Ventures"]);
  });

  it("sends the type, holder and status", async () => {
    show();
    await loaded();
    fireEvent.click(screen.getAllByRole("combobox", { name: /Type/ })[0]!);
    fireEvent.click(screen.getByRole("option", { name: "Bank" }));
    await waitFor(() => expect(read).toHaveBeenCalledTimes(2));
    expect(read.mock.calls[1]![0]).toEqual({ search: "", status: "all", typeFilter: "2", holderFilter: "" });
    fireEvent.click(screen.getAllByRole("combobox", { name: /Holder/ })[0]!);
    fireEvent.click(screen.getByRole("option", { name: "Riaz Junaid" }));
    await waitFor(() => expect(read).toHaveBeenCalledTimes(3));
    expect(read.mock.calls[2]![0]).toMatchObject({ typeFilter: "2", holderFilter: "Riaz Junaid" });
    fireEvent.click(screen.getAllByRole("combobox", { name: /Status/ })[0]!);
    fireEvent.click(screen.getByRole("option", { name: "Active" }));
    await waitFor(() => expect(read).toHaveBeenCalledTimes(4));
    expect(read.mock.calls[3]![0]).toMatchObject({ status: "active" });
  });

  it("applies the search after the typing pauses", async () => {
    show();
    await loaded();
    type(screen.getAllByPlaceholderText("Account, holder, bank or wallet")[0]!, "meezan");
    expect(read).toHaveBeenCalledTimes(1);
    await waitFor(() => expect(read).toHaveBeenCalledTimes(2));
    expect(read.mock.calls[1]![0]).toMatchObject({ search: "meezan" });
  });

  it("shows Reset only once something is set, and Reset clears everything", async () => {
    show();
    await loaded();
    expect(screen.queryByRole("button", { name: "Reset" })).toBeNull();
    fireEvent.click(screen.getAllByRole("combobox", { name: /Type/ })[0]!);
    fireEvent.click(screen.getByRole("option", { name: "Bank" }));
    await waitFor(() => expect(read).toHaveBeenCalledTimes(2));
    fireEvent.click(screen.getAllByRole("button", { name: "Reset" })[0]!);
    await waitFor(() => expect(read).toHaveBeenCalledTimes(3));
    expect(read.mock.calls[2]![0]).toEqual({ search: "", status: "all", typeFilter: "", holderFilter: "" });
  });

  it("drops a late answer to an earlier filter", async () => {
    show();
    await loaded();
    const slow: { resolve: (value: unknown) => void } = { resolve: () => {} };
    read.mockImplementationOnce((() => new Promise((resolve) => { slow.resolve = resolve; })) as never);
    read.mockImplementationOnce((async () => ({ items: [meezan], hasMore: false })) as never);
    fireEvent.click(screen.getAllByRole("combobox", { name: /Type/ })[0]!);
    fireEvent.click(screen.getByRole("option", { name: "Cash" }));
    fireEvent.click(screen.getAllByRole("combobox", { name: /Type/ })[0]!);
    fireEvent.click(screen.getByRole("option", { name: "Bank" }));
    await waitFor(() => expect(table().queryByText("Cash in office")).toBeNull());
    slow.resolve({ items: [cash], hasMore: false });
    await waitFor(() => expect(table().getAllByText("Meezan Bank").length).toBeGreaterThan(0));
    expect(table().queryByText("Cash in office")).toBeNull();
  });
});

describe("Manage accounts: Add account", () => {
  const open = async () => {
    show();
    await loaded();
    fireEvent.click(button("Add account"));
    return await screen.findByRole("dialog");
  };

  it("opens with the ten fields' labels, Cash selected and no Display order", async () => {
    await open();
    expect(screen.getByRole("heading", { name: "Add account" })).toBeTruthy();
    expect(dialog().getByText(/Account name/)).toBeTruthy();
    expect(dialog().getByRole("combobox", { name: /Account type/ }).textContent).toContain("Cash");
    expect(dialog().getByText(/Account holder/)).toBeTruthy();
    expect(field(/Opening balance/).value).toBe("0");
    expect(dialog().getByText("Rs")).toBeTruthy();
    expect(dialog().getByText(/Ledger code/)).toBeTruthy();
    expect(dialog().getByText(/Bank or wallet name/)).toBeTruthy();
    expect(dialog().getByText(/Description/)).toBeTruthy();
    expect(dialog().queryByText(/Display order/i)).toBeNull();
    expect(field(/Account name/).placeholder).toBe("e.g. Meezan Bank — Current");
  });

  it("keeps Save off until the name and the holder are filled, not just spaces", async () => {
    await open();
    expect(footerButton("Save").disabled).toBe(true);
    type(field(/Account name/), "   ");
    type(field(/Account holder/), "Seven Ventures");
    expect(footerButton("Save").disabled).toBe(true);
    type(field(/Account name/), "Petty cash");
    expect(footerButton("Save").disabled).toBe(false);
    type(field(/Account holder/), "  ");
    expect(footerButton("Save").disabled).toBe(true);
  });

  it("offers the ten types in board order", async () => {
    await open();
    fireEvent.click(dialog().getByRole("combobox", { name: /Account type/ }));
    expect(screen.getAllByRole("option").map((option) => option.textContent)).toEqual([
      "Cash", "Bank", "Mobile wallet", "Other", "Staff float", "Fixed asset", "Receivable", "Work in progress", "Liability", "Capital",
    ]);
  });

  it("sends trimmed text, empty optional text as null, display order 0 and an empty token, then toasts and reloads", async () => {
    await open();
    type(field(/Account name/), "  Petty cash  ");
    type(field(/Account holder/), " Riaz Junaid ");
    type(field(/Ledger code/), "  ");
    type(field(/Bank or wallet name/), "");
    fireEvent.click(footerButton("Save"));
    await waitFor(() => expect(api.save).toHaveBeenCalledTimes(1));
    expect(api.save.mock.calls[0]).toEqual([{
      name: "Petty cash", type: 1, accountHolderName: "Riaz Junaid", openingBalance: 0, ledgerCode: null, displayOrder: 0,
      bankOrWalletName: null, description: null, concurrencyToken: "",
    }, null]);
    expect(await screen.findByText("Account added.")).toBeTruthy();
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(read).toHaveBeenCalledTimes(2);
  });

  it("sends the chosen type, and an opening balance typed on the account's own side", async () => {
    await open();
    type(field(/Account name/), "Bank Alfalah loan");
    type(field(/Account holder/), "Seven Ventures");
    pick(/Account type/, "Liability");
    type(field(/Opening balance/), "3000000");
    expect(field(/Opening balance/).value).toBe("3,000,000");
    fireEvent.click(footerButton("Save"));
    // A non-zero opening on Add, with a go-live date saved, asks first.
    fireEvent.click(await screen.findByRole("button", { name: "Change" }));
    await waitFor(() => expect(api.save).toHaveBeenCalledTimes(1));
    expect(lastBody()).toMatchObject({ type: 5, openingBalance: 3_000_000 });
  });

  it("takes a negative opening balance, such as an overdraft", async () => {
    await open();
    type(field(/Account name/), "Overdraft");
    type(field(/Account holder/), "Seven Ventures");
    pick(/Account type/, "Bank");
    type(field(/Opening balance/), "-250000.5");
    expect(field(/Opening balance/).value).toBe("-250,000.5");
    fireEvent.click(footerButton("Save"));
    fireEvent.click(await screen.findByRole("button", { name: "Change" }));
    await waitFor(() => expect(api.save).toHaveBeenCalledTimes(1));
    expect(lastBody().openingBalance).toBe(-250_000.5);
  });

  it("counts an empty opening balance as 0 and saves without asking", async () => {
    await open();
    type(field(/Account name/), "Petty cash");
    type(field(/Account holder/), "Riaz Junaid");
    type(field(/Opening balance/), "");
    fireEvent.click(footerButton("Save"));
    await waitFor(() => expect(api.save).toHaveBeenCalledTimes(1));
    expect(lastBody().openingBalance).toBe(0);
    expect(screen.queryByText("Change the opening balance?")).toBeNull();
  });

  it("asks first for a non-zero opening while a go-live date is saved: from Rs 0 to the typed figure", async () => {
    await open();
    type(field(/Account name/), "Meezan Savings");
    type(field(/Account holder/), "Seven Ventures");
    pick(/Account type/, "Bank");
    type(field(/Opening balance/), "500000");
    fireEvent.click(footerButton("Save"));
    expect(await screen.findByText("Change the opening balance?")).toBeTruthy();
    expect(screen.getByText("Every report from the go-live date, Aug 15, 2026, will change. Meezan Savings: Rs 0 → Rs 500,000.")).toBeTruthy();
    expect(api.save).not.toHaveBeenCalled();
    // Cancel closes only the confirm: the form stays as it was.
    fireEvent.click(button("Cancel", within(dialogs()[1]!)));
    await waitFor(() => expect(dialogs()).toHaveLength(1));
    expect(field(/Opening balance/).value).toBe("500,000");
    expect(api.save).not.toHaveBeenCalled();
  });

  it("does not ask for a non-zero opening when no go-live date is saved yet", async () => {
    read.mockImplementation((async (_filters: unknown, includeOverview: boolean) => ({
      items: everyAccount, hasMore: false, overview: includeOverview ? overview({ goLiveDate: null }) : undefined,
    })) as never);
    await open();
    type(field(/Account name/), "Meezan Savings");
    type(field(/Account holder/), "Seven Ventures");
    type(field(/Opening balance/), "500000");
    fireEvent.click(footerButton("Save"));
    await waitFor(() => expect(api.save).toHaveBeenCalledTimes(1));
    expect(screen.queryByText("Change the opening balance?")).toBeNull();
  });

  it("on Change sends the call, then closes both popups and says Account added.", async () => {
    await open();
    type(field(/Account name/), "Meezan Savings");
    type(field(/Account holder/), "Seven Ventures");
    type(field(/Opening balance/), "500000");
    fireEvent.click(footerButton("Save"));
    fireEvent.click(await screen.findByRole("button", { name: "Change" }));
    expect(await screen.findByText("Account added.")).toBeTruthy();
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(read).toHaveBeenCalledTimes(2);
  });

  it("keeps the popup open with what was typed, and shows the server's message in a red notice", async () => {
    api.save.mockRejectedValue(new Error("A finance account with this name already exists."));
    await open();
    type(field(/Account name/), "Meezan Bank");
    type(field(/Account holder/), "Seven Ventures");
    type(field(/Description/), "Kept");
    fireEvent.click(footerButton("Save"));
    expect(await dialog().findByText("A finance account with this name already exists.")).toBeTruthy();
    expect(field(/Account name/).value).toBe("Meezan Bank");
    expect(field(/Description/).value).toBe("Kept");
    expect(footerButton("Save").disabled).toBe(false);
    expect(read).toHaveBeenCalledTimes(1);
  });

  it("closes the confirm and shows the message in the form when the confirmed save is refused", async () => {
    api.save.mockRejectedValue(new Error("Opening balance is outside the supported range."));
    await open();
    type(field(/Account name/), "Huge");
    type(field(/Account holder/), "Seven Ventures");
    type(field(/Opening balance/), "500000");
    fireEvent.click(footerButton("Save"));
    fireEvent.click(await screen.findByRole("button", { name: "Change" }));
    expect(await dialog().findByText("Opening balance is outside the supported range.")).toBeTruthy();
    await waitFor(() => expect(dialogs()).toHaveLength(1));
    expect(field(/Opening balance/).value).toBe("500,000");
  });

  it("blocks closing while the save is in flight, and shows a spinner on Save", async () => {
    let finish: () => void = () => {};
    api.save.mockImplementation(() => new Promise<void>((resolve) => { finish = resolve; }));
    await open();
    type(field(/Account name/), "Petty cash");
    type(field(/Account holder/), "Riaz Junaid");
    fireEvent.click(footerButton("Save"));
    await waitFor(() => expect(api.save).toHaveBeenCalledTimes(1));
    expect(footerButton("Save").disabled).toBe(true);
    fireEvent.keyDown(document.body, { key: "Escape" });
    expect(screen.getByRole("dialog")).toBeTruthy();
    finish();
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
  });

  it("sends one request for a double click", async () => {
    let finish: () => void = () => {};
    api.save.mockImplementation(() => new Promise<void>((resolve) => { finish = resolve; }));
    await open();
    type(field(/Account name/), "Petty cash");
    type(field(/Account holder/), "Riaz Junaid");
    fireEvent.click(footerButton("Save"));
    fireEvent.click(footerButton("Save"));
    expect(api.save).toHaveBeenCalledTimes(1);
    finish();
  });
});

describe("Manage accounts: Edit account", () => {
  const edit = async (name: string) => {
    show();
    await loaded();
    fireEvent.click(button(`Edit ${name}`));
    return await screen.findByRole("dialog");
  };

  it("opens filled in with Save on from the start", async () => {
    await edit("Meezan Bank");
    expect(screen.getByRole("heading", { name: "Edit account" })).toBeTruthy();
    expect(field(/Account name/).value).toBe("Meezan Bank");
    expect(dialog().getByRole("combobox", { name: /Account type/ }).textContent).toContain("Bank");
    expect(field(/Account holder/).value).toBe("Seven Ventures");
    expect(field(/Opening balance/).value).toBe("12,000,000");
    expect(field(/Ledger code/).value).toBe("1002");
    expect(field(/Bank or wallet name/).value).toBe("Meezan Bank");
    expect(footerButton("Save").disabled).toBe(false);
    expect(dialog().queryByText(/Display order/i)).toBeNull();
  });

  it("sends the saved display order and token, and saves directly when the opening is unchanged", async () => {
    await edit("Meezan Bank");
    type(field(/Description/), "Main operating account");
    fireEvent.click(footerButton("Save"));
    await waitFor(() => expect(api.save).toHaveBeenCalledTimes(1));
    expect(api.save.mock.calls[0]).toEqual([{
      name: "Meezan Bank", type: 2, accountHolderName: "Seven Ventures", openingBalance: 12_000_000, ledgerCode: "1002", displayOrder: 7,
      bankOrWalletName: "Meezan Bank", description: "Main operating account", concurrencyToken: "tok-2",
    }, 2]);
    expect(screen.queryByText("Change the opening balance?")).toBeNull();
    expect(await screen.findByText("Account saved.")).toBeTruthy();
  });

  it("asks first when the opening balance changed, with the from and to figures", async () => {
    await edit("Meezan Bank");
    type(field(/Opening balance/), "12500000");
    fireEvent.click(footerButton("Save"));
    expect(await screen.findByText("Change the opening balance?")).toBeTruthy();
    expect(screen.getByText("Every report from the go-live date, Aug 15, 2026, will change. Meezan Bank: Rs 12,000,000 → Rs 12,500,000.")).toBeTruthy();
    expect(api.save).not.toHaveBeenCalled();
  });

  it("starts the confirm 'Every report will change.' when no go-live date is saved", async () => {
    read.mockImplementation((async (_filters: unknown, includeOverview: boolean) => ({
      items: everyAccount, hasMore: false, overview: includeOverview ? overview({ goLiveDate: null }) : undefined,
    })) as never);
    await edit("Meezan Bank");
    type(field(/Opening balance/), "12500000");
    fireEvent.click(footerButton("Save"));
    expect(await screen.findByText("Every report will change. Meezan Bank: Rs 12,000,000 → Rs 12,500,000.")).toBeTruthy();
  });

  it("sends the call on Change, says Opening balance changed. and closes both popups", async () => {
    await edit("Meezan Bank");
    type(field(/Opening balance/), "12500000");
    fireEvent.click(footerButton("Save"));
    fireEvent.click(await screen.findByRole("button", { name: "Change" }));
    await waitFor(() => expect(api.save).toHaveBeenCalledTimes(1));
    expect(lastBody()).toMatchObject({ openingBalance: 12_500_000, concurrencyToken: "tok-2", displayOrder: 7 });
    expect(await screen.findByText("Opening balance changed.")).toBeTruthy();
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
  });

  it("shows a refusal from the server in the form and keeps what was typed", async () => {
    api.save.mockRejectedValue(new Error("This account was changed by another user. Refresh and try again."));
    await edit("Meezan Bank");
    type(field(/Description/), "Typed");
    fireEvent.click(footerButton("Save"));
    expect(await dialog().findByText("This account was changed by another user. Refresh and try again.")).toBeTruthy();
    expect(field(/Description/).value).toBe("Typed");
  });

  it("makes a system account's name, type and ledger code read-only, with a gold notice and the account name as subtitle", async () => {
    await edit("Tax payable (FBR)");
    expect(dialog().getByText("System account: the name, type and ledger code can't be changed.")).toBeTruthy();
    expect(dialog().getAllByText("Tax payable (FBR)").length).toBeGreaterThan(0);
    expect(field(/Account name/).disabled).toBe(true);
    expect((dialog().getByRole("combobox", { name: /Account type/ }) as HTMLButtonElement).disabled).toBe(true);
    expect(field(/Ledger code/).disabled).toBe(true);
    expect(field(/Account holder/).disabled).toBe(false);
    expect(field(/Opening balance/).disabled).toBe(false);
    expect(field(/Bank or wallet name/).disabled).toBe(false);
    expect(field(/Description/).disabled).toBe(false);
  });

  it("locks the opening balance of customer deposits, with the helper text", async () => {
    await edit("Customer deposits");
    expect(field(/Opening balance/).disabled).toBe(true);
    expect(field(/Opening balance/).value).toBe("0");
    expect(dialog().getByText("Worked out from bookings, not typed.")).toBeTruthy();
  });

  it.each([["CustomerReceivables"], [5]])("locks the opening balance when the role is %s", async (role) => {
    read.mockImplementation((async (_filters: unknown, includeOverview: boolean) => ({
      items: [account({ id: 12, name: "Locked", type: 5, systemRole: role, isSystemAccount: true, openingBalance: 0, ledgerCode: "2103" })], hasMore: false, overview: includeOverview ? overview() : undefined,
    })) as never);
    show();
    await screen.findAllByText("Locked");
    fireEvent.click(button("Edit Locked"));
    await screen.findByRole("dialog");
    expect(field(/Opening balance/).disabled).toBe(true);
    expect(dialog().getByText("Worked out from bookings, not typed.")).toBeTruthy();
  });

  it("keeps a legacy figure on a locked account as it was, and saves it unchanged without asking", async () => {
    read.mockImplementation((async (_filters: unknown, includeOverview: boolean) => ({
      items: [account({ ...deposits, openingBalance: 750 })], hasMore: false, overview: includeOverview ? overview() : undefined,
    })) as never);
    show();
    await screen.findAllByText("Customer deposits");
    fireEvent.click(button("Edit Customer deposits"));
    await screen.findByRole("dialog");
    expect(field(/Opening balance/).value).toBe("750");
    type(field(/Description/), "Note");
    fireEvent.click(footerButton("Save"));
    await waitFor(() => expect(api.save).toHaveBeenCalledTimes(1));
    expect(lastBody().openingBalance).toBe(750);
    expect(screen.queryByText("Change the opening balance?")).toBeNull();
  });

  it("leaves the opening of customer refunds payable and of tax payable editable", async () => {
    await edit("Tax payable (FBR)");
    expect(field(/Opening balance/).disabled).toBe(false);
  });

  it("does not show a system notice for an ordinary account", async () => {
    await edit("Meezan Bank");
    expect(dialog().queryByText(/System account/)).toBeNull();
  });
});

describe("Manage accounts: Deactivate and Reactivate", () => {
  const menu = async (name: string, item: string) => {
    show();
    await loaded();
    fireEvent.click(screen.getByRole("button", { name: `More for ${name}` }));
    fireEvent.click(screen.getByRole("menuitem", { name: item }));
    return await screen.findByRole("dialog");
  };

  it("asks with the account name and the shared wording, and deactivates in red", async () => {
    await menu("Meezan Bank", "Deactivate");
    expect(screen.getByText("Deactivate Meezan Bank?")).toBeTruthy();
    expect(screen.getByText("Past transactions are kept. It can't be picked for new entries.")).toBeTruthy();
    expect(button("Cancel", dialog())).toBeTruthy();
    expect(button("Deactivate", dialog()).className).toContain("danger");
  });

  it("sends the status and token, toasts and reloads", async () => {
    await menu("Meezan Bank", "Deactivate");
    fireEvent.click(button("Deactivate", dialog()));
    await waitFor(() => expect(api.setActive).toHaveBeenCalledWith(2, false, "tok-2"));
    expect(await screen.findByText("Meezan Bank deactivated.")).toBeTruthy();
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(read).toHaveBeenCalledTimes(2);
  });

  it("reactivates with its own wording and toast", async () => {
    await menu("Alfalah Saving", "Reactivate");
    expect(screen.getByText("Reactivate Alfalah Saving?")).toBeTruthy();
    expect(screen.getByText("It can be picked for new entries again.")).toBeTruthy();
    fireEvent.click(button("Reactivate", dialog()));
    await waitFor(() => expect(api.setActive).toHaveBeenCalledWith(3, true, "tok-3"));
    expect(await screen.findByText("Alfalah Saving reactivated.")).toBeTruthy();
  });

  it("cancel changes nothing", async () => {
    await menu("Meezan Bank", "Deactivate");
    fireEvent.click(button("Cancel", dialog()));
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(api.setActive).not.toHaveBeenCalled();
  });

  it("shows a refusal as a toast, never a browser alert, and closes the question", async () => {
    const alert = vi.fn();
    vi.stubGlobal("alert", alert);
    api.setActive.mockRejectedValue(new Error("A staff float can only be deactivated after its balance reaches zero."));
    await menu("Meezan Bank", "Deactivate");
    fireEvent.click(button("Deactivate", dialog()));
    expect(await screen.findByText("A staff float can only be deactivated after its balance reaches zero.")).toBeTruthy();
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(alert).not.toHaveBeenCalled();
  });
});

describe("Manage accounts: Account details", () => {
  const history = [
    transaction({ recordId: 11, date: "2026-09-10T00:00:00", label: "Installment received", reference: "RCP-000231", projectName: "Floria Heights", amount: 6_852_660 }),
    transaction({ recordId: 10, date: "2026-09-02T00:00:00" }),
    transaction({ recordId: 12, kind: "Expense", date: "2026-09-30T00:00:00", label: "Construction materials", reference: "EXP-0412", projectName: "Floria Heights", amount: -790_500, grossAmount: 850_000, whtAmount: 59_500 }),
  ];
  const open = async (name = "Meezan Bank", items: Transaction[] = history, count = 3) => {
    api.details.mockImplementation(async (id) => ({ ...everyAccount.find((candidate) => candidate.id === id)!, transactionCount: count }));
    api.transactions.mockResolvedValue({ items });
    show();
    await loaded();
    fireEvent.click(rowOf(name).getByText(name, { selector: "span.font-extrabold" }));
    await screen.findByRole("dialog");
  };

  it("shows the title, the subtitle and the four figures, Money in and Money out for a bank", async () => {
    await open();
    expect(screen.getByRole("heading", { name: /Meezan Bank/ })).toBeTruthy();
    expect(dialog().getByText("Bank · Seven Ventures · GL 1002")).toBeTruthy();
    for (const label of ["Opening balance", "Current balance", "Money in", "Money out"]) expect(dialog().getAllByText(label).length).toBeGreaterThan(0);
    expect(dialog().getAllByText("Rs 7,282,660").length).toBeGreaterThan(0);
    expect(dialog().getAllByText("Rs 882,660").length).toBeGreaterThan(0);
  });

  it("says Increases and Decreases for an account that is not cash-like, and Inactive in the subtitle", async () => {
    await open("Bank Alfalah loan");
    expect(dialog().getAllByText("Increases").length).toBeGreaterThan(0);
    expect(dialog().getAllByText("Decreases").length).toBeGreaterThan(0);
    expect(dialog().queryByText("Money in")).toBeNull();
    cleanup();
    await open("Alfalah Saving");
    expect(dialog().getByText("Bank · Seven Ventures · GL 1004 · Inactive")).toBeTruthy();
  });

  it("shows the tax-held notice with Record deposit, which opens Finance settings", async () => {
    await open();
    expect(dialog().getByText("Includes Rs 59,500 tax held for FBR")).toBeTruthy();
    fireEvent.click(button("Record deposit", dialog()));
    expect(await screen.findByText("Finance settings page")).toBeTruthy();
  });

  it("shows the tax-held notice only for what is still held", async () => {
    await open("Cash in office");
    expect(dialog().queryByText(/tax held for FBR/)).toBeNull();
  });

  it("lists the transactions oldest first with an Opening balance row and the running balance", async () => {
    await open();
    const table = within(dialog().getByRole("table"));
    const lines = table.getAllByRole("row").slice(1).map((candidate) => within(candidate).getAllByRole("cell").map((cell) => cell.textContent));
    expect(lines.map((cells) => cells[0])).toEqual([
      "Opening balance",
      "Late payment surchargeSep 2, 2026 · Mall of Faisal Hills · MR-0031",
      "Installment receivedSep 10, 2026 · Floria Heights · RCP-000231",
      "Construction materialsSep 30, 2026 · Floria Heights · EXP-0412Rs 850,000 invoiced · Rs 59,500 tax withheld",
    ]);
    expect(lines.map((cells) => cells[1])).toEqual(["", "+Rs 30,000", "+Rs 6,852,660", "-Rs 790,500"]);
    expect(lines.map((cells) => cells[2])).toEqual(["Rs 12,000,000", "Rs 12,030,000", "Rs 18,882,660", "Rs 18,092,160"]);
  });

  it("hides the running balance and the Opening balance row when fewer transactions came back than the account has", async () => {
    await open("Meezan Bank", history, 450);
    expect(dialog().getByText("Showing the 3 most recent transactions; running balance hidden.")).toBeTruthy();
    expect(within(dialog().getByRole("table")).queryByText("Opening balance")).toBeNull();
    expect(within(dialog().getByRole("table")).queryByText("Rs 12,030,000")).toBeNull();
  });

  it("shows the empty text when there are no transactions", async () => {
    await open("Meezan Bank", [], 0);
    expect(await dialog().findByText("No transactions assigned to this account.")).toBeTruthy();
  });

  it("shows grey blocks while loading", async () => {
    api.details.mockReturnValue(new Promise(() => {}));
    api.transactions.mockReturnValue(new Promise(() => {}));
    show();
    await loaded();
    fireEvent.click(rowOf("Meezan Bank").getByText("Meezan Bank", { selector: "span.font-extrabold" }));
    await screen.findByRole("dialog");
    expect(dialog().getByRole("status").getAttribute("aria-busy")).toBe("true");
  });

  it("shows the error with Try again, which asks again", async () => {
    api.details.mockRejectedValueOnce(new Error("The account could not be loaded."));
    api.transactions.mockResolvedValue({ items: history });
    show();
    await loaded();
    fireEvent.click(rowOf("Meezan Bank").getByText("Meezan Bank", { selector: "span.font-extrabold" }));
    expect(await dialog().findByText("The account could not be loaded.")).toBeTruthy();
    api.details.mockImplementation(async () => ({ ...meezan, transactionCount: 3 }));
    fireEvent.click(button("Try again", dialog()));
    expect(await dialog().findByRole("table")).toBeTruthy();
    expect(dialog().queryByText("The account could not be loaded.")).toBeNull();
  });

  it("has Close in the footer on desktop, and no Edit or Deactivate there", async () => {
    await open();
    expect(footerButton("Close")).toBeTruthy();
    const footer = within(dialogs()[0]!.querySelector("footer") as HTMLElement);
    expect(footer.queryByRole("button", { name: "Edit" })).toBeNull();
    expect(footer.queryByRole("button", { name: "Deactivate" })).toBeNull();
  });
});

describe("Manage accounts: on a phone", () => {
  beforeEach(() => stubMedia(true));

  const history = [
    transaction({ recordId: 10, date: "2026-09-02T00:00:00" }),
    transaction({ recordId: 12, kind: "Expense", date: "2026-09-30T00:00:00", label: "Construction materials", reference: "EXP-0412", projectName: "Floria Heights", amount: -790_500 }),
  ];

  it("shows each group's current balance on its heading, and no subtotal rows", async () => {
    show();
    await loaded();
    const list = document.querySelector("ul") as HTMLElement;
    expect(within(list).getByText("Cash & bank")).toBeTruthy();
    expect(within(list).getByText("Rs 19,650,000")).toBeTruthy();
    expect(screen.queryByText(/^Total /)).toBeNull();
  });

  it("shows a card per account: name, status, type · holder · GL · bank, opening, in and out, current balance", async () => {
    show();
    await loaded();
    const card = screen.getByRole("button", { name: "Open Meezan Bank" });
    expect(card.textContent).toContain("Meezan Bank");
    expect(card.textContent).toContain("Active");
    expect(card.textContent).toContain("Bank · Seven Ventures · GL 1002 · Meezan Bank");
    expect(card.textContent).toContain("Opening Rs 12,000,000");
    expect(card.textContent).toContain("In Rs 7,282,660 · Out Rs 882,660");
    expect(card.textContent).toContain("Rs 18,400,000");
    // No amount breaks after "Rs": each one is a single no-wrap element.
    expect(within(card).getByText("Rs 12,000,000").className).toContain("whitespace-nowrap");
  });

  it("has Add account beside the title and no pencil or ⋯ on the cards", async () => {
    show();
    await loaded();
    expect(button("Add account")).toBeTruthy();
    const list = document.querySelector("ul") as HTMLElement;
    expect(within(list).queryByRole("button", { name: /^Edit / })).toBeNull();
    expect(within(list).queryByRole("button", { name: /^More for / })).toBeNull();
  });

  it("opens the details from a card, with Edit and Deactivate in the footer", async () => {
    api.transactions.mockResolvedValue({ items: history });
    api.details.mockImplementation(async (id) => ({ ...everyAccount.find((candidate) => candidate.id === id)!, transactionCount: 2 }));
    show();
    await loaded();
    fireEvent.click(screen.getByRole("button", { name: "Open Meezan Bank" }));
    await screen.findByRole("dialog");
    expect(footerButton("Edit")).toBeTruthy();
    expect(footerButton("Deactivate")).toBeTruthy();
    expect(within(dialogs()[0]!.querySelector("footer") as HTMLElement).queryByRole("button", { name: "Close" })).toBeNull();
    // The phone list starts at the first transaction and shows the balance on the right of its second line.
    expect((await dialog().findAllByText("Late payment surcharge")).length).toBeGreaterThan(0);
    const list = within(dialogs()[0]!.querySelector("ul") as HTMLElement);
    expect(list.getAllByText("Balance")).toHaveLength(2);
    expect(list.queryByText("Opening balance")).toBeNull();
    expect(within(dialog().getByRole("table")).queryByText("Opening balance")).toBeNull();
  });

  it("Edit in the footer opens the same Edit popup", async () => {
    show();
    await loaded();
    fireEvent.click(screen.getByRole("button", { name: "Open Meezan Bank" }));
    await screen.findByRole("dialog");
    fireEvent.click(footerButton("Edit"));
    expect(await screen.findByRole("heading", { name: "Edit account" })).toBeTruthy();
    expect(field(/Account name/).value).toBe("Meezan Bank");
  });

  it("Deactivate in the footer asks the same question, and Reactivate shows for an inactive account", async () => {
    show();
    await loaded();
    fireEvent.click(screen.getByRole("button", { name: "Open Meezan Bank" }));
    await screen.findByRole("dialog");
    fireEvent.click(footerButton("Deactivate"));
    expect(await screen.findByText("Deactivate Meezan Bank?")).toBeTruthy();
    fireEvent.click(button("Cancel", dialog()));
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());

    fireEvent.click(screen.getByRole("button", { name: "Open Alfalah Saving" }));
    await screen.findByRole("dialog");
    expect(footerButton("Reactivate")).toBeTruthy();
    expect(within(dialogs()[0]!.querySelector("footer") as HTMLElement).queryByRole("button", { name: "Deactivate" })).toBeNull();
  });

  it("shows Edit only for an active system account", async () => {
    show();
    await loaded();
    fireEvent.click(screen.getByRole("button", { name: "Open Tax payable (FBR)" }));
    await screen.findByRole("dialog");
    expect(footerButton("Edit")).toBeTruthy();
    const footer = within(dialogs()[0]!.querySelector("footer") as HTMLElement);
    expect(footer.queryByRole("button", { name: "Deactivate" })).toBeNull();
    expect(footer.queryByRole("button", { name: "Reactivate" })).toBeNull();
  });

  it("opens the Add popup full screen with Cancel and Save at the bottom", async () => {
    show();
    await loaded();
    fireEvent.click(button("Add account"));
    await screen.findByRole("dialog");
    expect(footerButton("Cancel")).toBeTruthy();
    expect(footerButton("Save")).toBeTruthy();
  });
});
