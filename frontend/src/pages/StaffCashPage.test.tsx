// @vitest-environment happy-dom
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { ToastProvider } from "../components/ui/Toast.tsx";
import { PHONE_QUERY } from "../components/ui/useMediaQuery.ts";
import type { User } from "../App.tsx";
import { ProjectsContext } from "../contexts/projectsContextValue.ts";
import { staffCashApi } from "../features/finance/staffCash/api.ts";
import type { HistoryItem, Holder, Overview, Statement } from "../features/finance/staffCash/types.ts";
import { calculateWht, listCategories, vendorOptions } from "../features/finance/whtApi.ts";
import type { ExpenseCategory, VendorOption, WhtCalculation } from "../features/finance/whtTypes.ts";
import type { ProjectFromApi } from "../utils/parseProject";
import StaffCashPage from "./StaffCashPage.tsx";

vi.mock("../features/finance/staffCash/api.ts", () => ({
  staffCashApi: {
    overview: vi.fn(),
    cashAccounts: vi.fn(),
    statement: vi.fn(),
    addPerson: vi.fn(),
    recordMovement: vi.fn(),
    correctMovement: vi.fn(),
    deleteMovement: vi.fn(),
    recordExpense: vi.fn(),
    openAttachment: vi.fn(),
  },
}));

vi.mock("../features/finance/whtApi.ts", () => ({
  listCategories: vi.fn(),
  vendorOptions: vi.fn(),
  calculateWht: vi.fn(),
}));

const api = vi.mocked(staffCashApi);

const person = (over: Partial<Holder>): Holder => ({
  financeAccountId: 5, personName: "Adeel Satti", accountName: "Adeel Satti — Staff float", isActive: true, openingBalance: 0,
  currentBalance: 145_000, outstandingSince: "2026-09-19T00:00:00", daysOutstanding: 12, lastActivityDate: "2026-10-01T00:00:00",
  transactionCount: 4, ...over,
});
const adeel = person({});
const salman = person({ financeAccountId: 6, personName: "Salman Khan", currentBalance: 25_000, outstandingSince: "2026-09-26T00:00:00", daysOutstanding: 5, transactionCount: 3 });
const ahmad = person({ financeAccountId: 7, personName: "Ahmad Farad", currentBalance: -40_000, outstandingSince: "2026-08-25T00:00:00", daysOutstanding: 37, transactionCount: 2 });
const kamran = person({ financeAccountId: 8, personName: "Kamran Shah", currentBalance: 0, outstandingSince: null, daysOutstanding: null, transactionCount: 14 });
const bilal = person({ financeAccountId: 9, personName: "Bilal Ahmed", isActive: false, currentBalance: 0, outstandingSince: null, daysOutstanding: null, transactionCount: 1 });

const overviewOf = (holders: Holder[]): Overview => ({
  totalHeldByStaff: 170_000, totalOwedToStaff: 40_000, netStaffBalance: 130_000, holdingCount: 2, owedCount: 1, holders,
});
const everyone = overviewOf([adeel, salman, ahmad, kamran, bilal]);

const slip = { fileName: "cash-return-slip.jpg", contentType: "image/jpeg", fileSize: 655_360, uploadedAt: "2026-09-28T00:00:00" };
const item = (over: Partial<HistoryItem>): HistoryItem => ({
  recordType: "Transfer", recordId: 21, kind: "Money received", date: "2026-09-30T00:00:00", description: "Received from Meezan Bank",
  reference: "CHQ-2240", projectName: null, amount: 60_000, grossAmount: 60_000, whtAmount: 0, runningBalance: 153_600,
  movementType: "FundsGiven", counterpartyFinanceAccountId: 7, counterpartyFinanceAccountName: "Meezan Bank", note: null,
  concurrencyToken: "row-21", attachment: null, ...over,
});
const fuel = item({
  recordType: "Expense", recordId: 21, kind: "Expense", date: "2026-10-01T00:00:00", description: "Fuel", reference: "PSO Taxila",
  projectName: "General", amount: -8_600, grossAmount: 8_600, note: "Site visits", runningBalance: 145_000, movementType: null,
  counterpartyFinanceAccountId: null, counterpartyFinanceAccountName: null, concurrencyToken: null,
});
const received = item({});
const returned = item({
  recordId: 22, kind: "Money returned", date: "2026-09-28T00:00:00", description: "Returned to Cash in office", reference: null,
  amount: -20_000, grossAmount: 20_000, runningBalance: 93_600, movementType: "FundsReturned", counterpartyFinanceAccountId: 8,
  counterpartyFinanceAccountName: "Cash in office", concurrencyToken: "row-22", attachment: slip,
});
const materials = item({
  recordType: "Expense", recordId: 32, kind: "Expense", date: "2026-09-25T00:00:00", description: "Construction materials",
  reference: "City Hardware", projectName: "Floria Heights", amount: -48_000, grossAmount: 50_000, whtAmount: 2_000,
  note: "Cement bags for block B", runningBalance: 113_600, movementType: null, counterpartyFinanceAccountId: null,
  counterpartyFinanceAccountName: null, concurrencyToken: null,
});
const statementOf = (holder: Holder, items: HistoryItem[], totalCount = items.length): Statement =>
  ({ holder, items, totalCount, hasMore: items.length < totalCount });

const cashAccounts = [
  { id: 7, name: "Meezan Bank", accountHolderName: "Seven Ventures", isActive: true },
  { id: 8, name: "Cash in office", accountHolderName: "Riaz Junaid", isActive: true },
  { id: 9, name: "Old bank", accountHolderName: "Seven Ventures", isActive: false },
];

const category = (over: Partial<ExpenseCategory>): ExpenseCategory => ({
  id: 4, name: "Construction materials", code: "materials", description: null, isWhtApplicable: true, filerRate: 4, nonFilerRate: 8,
  annualThreshold: 0, taxSection: "153(1)(a)", displayOrder: 1, isActive: true, usageCount: 0, createdAt: "", updatedAt: null,
  concurrencyToken: "c", ...over,
});
const categories = [category({}), category({ id: 5, name: "Fuel", code: "fuel", isWhtApplicable: false, taxSection: null })];
const vendors: VendorOption[] = [
  { id: 11, name: "City Hardware", filerStatus: "NonFiler", ntn: null, isActive: true },
  { id: 12, name: "Al-Noor Steel", filerStatus: "Filer", ntn: null, isActive: true },
];
const projects = [{ id: 3, projectName: "Floria Heights" }] as ProjectFromApi[];

/** 4% for a filer, 8% for anyone else, on a category that withholds. */
const wht = async ({ categoryId, vendorId, grossAmount }: { categoryId: number | null; vendorId: number | null; grossAmount: number }): Promise<WhtCalculation> => {
  const applicable = categoryId === 4;
  const rate = vendorId === 12 ? 4 : 8;
  const tax = applicable ? (grossAmount * rate) / 100 : 0;
  return {
    isWhtApplicable: applicable, rate: applicable ? rate : 0, whtAmount: tax, netPaid: grossAmount - tax, whtApplied: applicable,
    filerStatus: vendorId === 12 ? "Filer" : "NonFiler", taxSection: applicable ? "153(1)(a)" : null, belowThreshold: false,
    annualThreshold: 0, yearToDateTotal: 0, financialYear: "2026-27", notice: null,
  };
};

const admin: User = { userId: "1", role: "Admin", email: "a@b.c" };

function Where() {
  return <p data-testid="where">{useLocation().pathname}</p>;
}

function show(path = "/finance/staff-cash", user: User | null = admin) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <ProjectsContext.Provider value={{ projects, loading: false, error: null, reload: async () => {} }}>
        <ToastProvider>
          <Routes>
            <Route path="/finance/staff-cash" element={<StaffCashPage user={user} />} />
            <Route path="/finance/staff-cash/:accountId" element={<StaffCashPage user={user} />} />
            <Route path="/" element={<p>Home page</p>} />
          </Routes>
          <Where />
        </ToastProvider>
      </ProjectsContext.Provider>
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

const dialog = () => within(screen.getByRole("dialog"));
const button = (name: string | RegExp, scope: { getByRole: typeof screen.getByRole } = screen) => scope.getByRole("button", { name }) as HTMLButtonElement;
const footerButton = (name: string) => within(screen.getByRole("dialog").querySelector("footer") as HTMLElement).getByRole("button", { name }) as HTMLButtonElement;
const type = (input: HTMLElement, value: string) => fireEvent.change(input, { target: { value } });
const pick = (name: RegExp | string, option: string) => {
  fireEvent.click(dialog().getByRole("combobox", { name }));
  fireEvent.click(screen.getByRole("option", { name: option }));
};
const options = (name: RegExp | string) => {
  fireEvent.click(dialog().getByRole("combobox", { name }));
  const labels = screen.getAllByRole("option").map((option) => option.textContent);
  fireEvent.click(dialog().getByRole("combobox", { name }));
  return labels;
};
const tableRow = (text: string) => within(screen.getAllByRole("row").find((candidate) => within(candidate).queryByText(text)) as HTMLElement);
const where = () => screen.getByTestId("where").textContent;
const sent = (body: FormData) => Object.fromEntries([...body.entries()].map(([key, value]) => [key, typeof value === "string" ? value : (value as File).name]));
const personCard = (name: string) => button(new RegExp(`^${name}`));
const loaded = async () => { await screen.findAllByText("Site visits"); };

beforeEach(() => {
  vi.resetAllMocks();
  stubMedia(false);
  // 2026-10-01 12:00 in Pakistan.
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date("2026-10-01T07:00:00Z"));
  Element.prototype.scrollIntoView = () => {};
  api.overview.mockResolvedValue(everyone);
  api.cashAccounts.mockResolvedValue(cashAccounts);
  api.statement.mockImplementation(async (id) => {
    if (id === 5) return statementOf(adeel, [fuel, received, returned, materials]);
    if (id === 7) return statementOf(ahmad, [item({ recordId: 40, concurrencyToken: "row-40" })]);
    const holder = everyone.holders.find((candidate) => candidate.financeAccountId === id) as Holder;
    return statementOf(holder, []);
  });
  api.addPerson.mockImplementation(async (personName) => person({ financeAccountId: 10, personName, currentBalance: 0, outstandingSince: null, daysOutstanding: null, transactionCount: 0 }));
  api.recordMovement.mockResolvedValue(undefined);
  api.correctMovement.mockResolvedValue(undefined);
  api.deleteMovement.mockResolvedValue(undefined);
  api.recordExpense.mockResolvedValue(undefined);
  api.openAttachment.mockResolvedValue(undefined);
  vi.mocked(listCategories).mockResolvedValue(categories);
  vi.mocked(vendorOptions).mockResolvedValue(vendors);
  vi.mocked(calculateWht).mockImplementation(wht);
});

afterEach(() => {
  cleanup();
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe("Cash with staff: the people list", () => {
  it("shows the three totals and every person with their state, and opens no one", async () => {
    show();
    await screen.findByText("Salman Khan");
    expect(screen.getByText("Rs 170,000")).toBeTruthy();
    expect(screen.getByText("Rs 40,000")).toBeTruthy();
    expect(screen.getByText("Rs 130,000")).toBeTruthy();
    expect(screen.getByText("2 people")).toBeTruthy();
    expect(screen.getByText("1 person")).toBeTruthy();
    expect(screen.getByText("Held less owed")).toBeTruthy();
    expect(screen.queryByText("Cash / bank")).toBeNull();

    expect(personCard("Adeel Satti").textContent).toContain("Holding company cash");
    expect(personCard("Adeel Satti").textContent).toContain("12 days open · 4 movements");
    expect(personCard("Ahmad Farad").textContent).toContain("−Rs 40,000");
    expect(personCard("Ahmad Farad").textContent).toContain("Company owes them");
    expect(personCard("Kamran Shah").textContent).toContain("Settled");
    expect(personCard("Kamran Shah").textContent).toContain("14 movements");
    expect(personCard("Kamran Shah").textContent).not.toContain("open");
    expect(personCard("Bilal Ahmed").textContent).toContain("Inactive");
    expect(personCard("Bilal Ahmed").textContent).toContain("1 movement");

    expect(screen.getByText("Select a person to see their movements.")).toBeTruthy();
    expect(api.statement).not.toHaveBeenCalled();
  });

  it("says when there is nobody yet", async () => {
    api.overview.mockResolvedValue({ ...overviewOf([]), totalHeldByStaff: 0, totalOwedToStaff: 0, netStaffBalance: 0, holdingCount: 0, owedCount: 0 });
    show();
    expect(await screen.findByText("No staff floats yet")).toBeTruthy();
    expect(screen.getByText("Add a person, then record the money handed to them.")).toBeTruthy();
  });

  it("offers Try again when the overview cannot be loaded", async () => {
    api.overview.mockRejectedValueOnce(new Error("Could not load staff cash."));
    show();
    expect(await screen.findByText("Could not load staff cash.")).toBeTruthy();
    fireEvent.click(button("Try again"));
    expect(await screen.findByText("Salman Khan")).toBeTruthy();
    expect(screen.queryByText("Could not load staff cash.")).toBeNull();
  });

  it("waits for the signed-in user before deciding, and sends anyone without finance access home", async () => {
    const { unmount } = show("/finance/staff-cash", null);
    expect(where()).toBe("/finance/staff-cash");
    expect(api.overview).not.toHaveBeenCalled();
    unmount();
    show("/finance/staff-cash", { userId: "9", role: "Employee", email: "x@y.z" });
    expect(await screen.findByText("Home page")).toBeTruthy();
  });
});

describe("Cash with staff: the open person", () => {
  it("opens a person by address with their panel and movements, newest first", async () => {
    show();
    await screen.findByText("Salman Khan");
    fireEvent.click(personCard("Adeel Satti"));
    expect(where()).toBe("/finance/staff-cash/5");
    await loaded();
    expect(personCard("Adeel Satti").getAttribute("aria-pressed")).toBe("true");
    expect(screen.getByRole("heading", { name: "Adeel Satti" })).toBeTruthy();
    expect(screen.getByText("Open since Sep 19, 2026 · 12 days")).toBeTruthy();
    expect(api.statement).toHaveBeenCalledWith(5, 0, 20, expect.any(AbortSignal));

    const rows = screen.getAllByRole("row").slice(1).map((row) => row.textContent ?? "");
    expect(rows).toHaveLength(4);
    expect(rows[0]).toContain("Fuel");

    const fuelRow = tableRow("Site visits");
    expect(fuelRow.getByText("General")).toBeTruthy();
    expect(fuelRow.getByText("Ref: PSO Taxila")).toBeTruthy();
    expect(fuelRow.getByText("−Rs 8,600")).toBeTruthy();
    expect(fuelRow.queryByRole("button", { name: /Actions for/ })).toBeNull();

    const cementRow = tableRow("Cement bags for block B");
    expect(cementRow.getByText("Floria Heights")).toBeTruthy();
    expect(cementRow.getByText("Gross Rs 50,000 · tax withheld Rs 2,000")).toBeTruthy();
    expect(cementRow.getByText("−Rs 48,000")).toBeTruthy();

    const receivedRow = tableRow("Received from Meezan Bank");
    expect(receivedRow.getByText("+Rs 60,000")).toBeTruthy();
    expect(receivedRow.getByText("Ref: CHQ-2240")).toBeTruthy();
    expect(receivedRow.getByText("None")).toBeTruthy();
    expect(receivedRow.getByRole("button", { name: /Actions for the money received/ })).toBeTruthy();
    expect(screen.getByText(/Showing/).textContent).toBe("Showing 1–4 of 4 entries");
  });

  it("opens and downloads an expense's receipt and a money move's slip", async () => {
    show("/finance/staff-cash/5");
    await loaded();
    fireEvent.click(tableRow("Returned to Cash in office").getByRole("button", { name: "Attached" }));
    fireEvent.click(tableRow("Returned to Cash in office").getByRole("button", { name: "Download cash-return-slip.jpg" }));
    expect(api.openAttachment).toHaveBeenNthCalledWith(1, 5, returned, false);
    expect(api.openAttachment).toHaveBeenNthCalledWith(2, 5, returned, true);
  });

  it("shows a failed attachment in a toast", async () => {
    api.openAttachment.mockRejectedValue(new Error("The attachment file is missing from storage."));
    show("/finance/staff-cash/5");
    await loaded();
    fireEvent.click(tableRow("Returned to Cash in office").getByRole("button", { name: "Attached" }));
    expect(await screen.findByText("The attachment file is missing from storage.")).toBeTruthy();
  });

  it("offers Settle amount owed for a person the company owes, with what is owed filled in", async () => {
    show("/finance/staff-cash/7");
    await screen.findByText("Open since Aug 25, 2026 · 37 days");
    expect(screen.getByText("Company owes this person")).toBeTruthy();
    expect(button("Record cash returned").disabled).toBe(true);
    fireEvent.click(button("Settle amount owed"));
    expect(dialog().getByText("Settle amount owed")).toBeTruthy();
    expect((dialog().getByRole("textbox", { name: /Amount/ }) as HTMLInputElement).value).toBe("40,000");
  });

  it("fills in what the person holds when cash comes back, and turns money moves off for an inactive person", async () => {
    show("/finance/staff-cash/5");
    await loaded();
    fireEvent.click(button("Record cash returned"));
    expect(dialog().getByText("Record cash returned")).toBeTruthy();
    expect((dialog().getByRole("textbox", { name: /Amount/ }) as HTMLInputElement).value).toBe("145,000");
    expect(dialog().getByRole("combobox", { name: /Returned to account/ })).toBeTruthy();
    fireEvent.click(footerButton("Cancel"));

    fireEvent.click(personCard("Bilal Ahmed"));
    expect(await screen.findByText("Fully settled")).toBeTruthy();
    expect(button("Record an expense").disabled).toBe(true);
    expect(button("Give money").disabled).toBe(true);
    expect(button("Record cash returned").disabled).toBe(true);
  });

  it("does not offer Cash returned in a settle dialog for a person the company owes", async () => {
    show("/finance/staff-cash/7");
    await screen.findByText("Open since Aug 25, 2026 · 37 days");
    fireEvent.click(button("Settle amount owed"));
    expect(options(/Movement/)).toEqual(["Money given (account → staff)"]);
  });

  it("ignores a slow answer for the person that was left", async () => {
    let finishOld: (value: Statement) => void = () => {};
    api.statement.mockImplementation((id) => id === 5
      ? new Promise<Statement>((resolve) => { finishOld = resolve; })
      : Promise.resolve(statementOf(salman, [])));
    show("/finance/staff-cash/5");
    await screen.findByRole("heading", { name: "Adeel Satti" });
    fireEvent.click(personCard("Salman Khan"));
    await screen.findByText("No movements yet. Record money given to begin this float.");
    await act(async () => finishOld(statementOf(adeel, [fuel])));
    expect(screen.queryByText("Site visits")).toBeNull();
  });

  it("pages through a long history, twenty at a time", async () => {
    api.statement.mockResolvedValue(statementOf(adeel, [fuel], 45));
    show("/finance/staff-cash/5");
    await loaded();
    expect(screen.getByText(/Showing/).textContent).toBe("Showing 1–20 of 45 entries");
    fireEvent.click(button("Page 3"));
    await waitFor(() => expect(api.statement).toHaveBeenLastCalledWith(5, 40, 20, expect.any(AbortSignal)));
  });

  it("offers Try again when the history cannot be loaded", async () => {
    api.statement.mockRejectedValueOnce(new Error("Could not load this person's history."));
    show("/finance/staff-cash/5");
    expect(await screen.findByText("Could not load this person's history.")).toBeTruthy();
    fireEvent.click(button("Try again"));
    await loaded();
  });

  it("shows a person who is not in the list as not found", async () => {
    show("/finance/staff-cash/99");
    expect(await screen.findByText("Person not found")).toBeTruthy();
    fireEvent.click(button("Back to Cash with staff"));
    expect(where()).toBe("/finance/staff-cash");
  });
});

describe("Cash with staff: adding a person", () => {
  it("keeps Add person off until a name is typed, and shows the server's refusal inside the popup", async () => {
    api.addPerson.mockRejectedValueOnce(new Error("This person already has a staff float."));
    show();
    await screen.findByText("Salman Khan");
    fireEvent.click(button("Add person"));
    expect(footerButton("Add person").disabled).toBe(true);
    type(dialog().getByRole("textbox", { name: /Person name/ }), "  Adeel Satti ");
    fireEvent.click(footerButton("Add person"));
    expect(await dialog().findByText("This person already has a staff float.")).toBeTruthy();
    expect(api.addPerson).toHaveBeenCalledWith("Adeel Satti");
    expect((dialog().getByRole("textbox", { name: /Person name/ }) as HTMLInputElement).value).toBe("  Adeel Satti ");
  });

  it("adds a person, says so and opens them", async () => {
    show();
    await screen.findByText("Salman Khan");
    const added = person({ financeAccountId: 10, personName: "Zafar Iqbal", currentBalance: 0, outstandingSince: null, daysOutstanding: null, transactionCount: 0 });
    api.overview.mockResolvedValue(overviewOf([adeel, salman, ahmad, kamran, bilal, added]));
    fireEvent.click(button("Add person"));
    type(dialog().getByRole("textbox", { name: /Person name/ }), "Zafar Iqbal");
    fireEvent.click(footerButton("Add person"));
    expect(await screen.findByText("Added Zafar Iqbal.")).toBeTruthy();
    await waitFor(() => expect(where()).toBe("/finance/staff-cash/10"));
    expect(await screen.findByRole("heading", { name: "Zafar Iqbal" })).toBeTruthy();
    expect(screen.queryByText("Person not found")).toBeNull();
  });
});

describe("Cash with staff: money moves", () => {
  it("gives money from an active account with a money-request key and the slip", async () => {
    show("/finance/staff-cash/5");
    await loaded();
    fireEvent.click(button("Give money"));
    expect(dialog().getByText("Give money")).toBeTruthy();
    expect(dialog().getByText("Adeel Satti")).toBeTruthy();
    expect(footerButton("Save").disabled).toBe(true);
    type(dialog().getByRole("textbox", { name: /Amount/ }), "60000");
    expect(footerButton("Save").disabled).toBe(true);
    expect(options(/Paid from account/)).toEqual(["Meezan Bank · Seven Ventures", "Cash in office · Riaz Junaid"]);
    pick(/Paid from account/, "Meezan Bank · Seven Ventures");
    type(dialog().getByRole("textbox", { name: /Reference/ }), "CHQ-2240");
    const input = screen.getByRole("dialog").querySelector("input[type=file]") as HTMLInputElement;
    fireEvent.change(input, { target: { files: [new File(["x"], "slip.pdf", { type: "application/pdf" })] } });
    fireEvent.click(footerButton("Save"));

    await waitFor(() => expect(api.recordMovement).toHaveBeenCalled());
    const [personId, body, key] = api.recordMovement.mock.calls[0];
    expect(personId).toBe(5);
    expect(sent(body)).toEqual({
      type: "FundsGiven", amount: "60000", date: "2026-10-01", counterpartyFinanceAccountId: "7", reference: "CHQ-2240", attachment: "slip.pdf",
    });
    expect(key).toMatch(/^staff-cash-transfer-/);
    expect(await screen.findByText("Movement recorded.")).toBeTruthy();
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(api.overview).toHaveBeenCalledTimes(2);
    expect(api.statement).toHaveBeenCalledTimes(2);
  });

  it("keeps what was typed and the same key when a save fails and is tried again", async () => {
    api.recordMovement.mockRejectedValueOnce(new Error("Transfer date cannot be before the go-live date."));
    show("/finance/staff-cash/5");
    await loaded();
    fireEvent.click(button("Give money"));
    type(dialog().getByRole("textbox", { name: /Amount/ }), "5000");
    pick(/Paid from account/, "Cash in office · Riaz Junaid");
    fireEvent.click(footerButton("Save"));
    expect(await dialog().findByText("Transfer date cannot be before the go-live date.")).toBeTruthy();
    expect((dialog().getByRole("textbox", { name: /Amount/ }) as HTMLInputElement).value).toBe("5,000");
    fireEvent.click(footerButton("Save"));
    await waitFor(() => expect(api.recordMovement).toHaveBeenCalledTimes(2));
    expect(api.recordMovement.mock.calls[1][2]).toBe(api.recordMovement.mock.calls[0][2]);
  });

  it("corrects a money move with its row version, offering the saved account even when inactive", async () => {
    api.statement.mockResolvedValue(statementOf(adeel, [fuel, item({ counterpartyFinanceAccountId: 9, description: "Received from Old bank" })]));
    show("/finance/staff-cash/5");
    await loaded();
    fireEvent.click(tableRow("Received from Old bank").getByRole("button", { name: /Actions for the money received/ }));
    fireEvent.click(screen.getByRole("menuitem", { name: "Correct" }));
    expect(dialog().getByText("Correct movement")).toBeTruthy();
    expect(dialog().getByText("Adeel Satti · Sep 30, 2026")).toBeTruthy();
    expect(options(/Paid from account/)).toEqual(["Meezan Bank · Seven Ventures", "Cash in office · Riaz Junaid", "Old bank · Seven Ventures (Inactive)"]);
    type(dialog().getByRole("textbox", { name: /Amount/ }), "65000");
    fireEvent.click(footerButton("Save correction"));
    await waitFor(() => expect(api.correctMovement).toHaveBeenCalled());
    const [personId, transferId, body] = api.correctMovement.mock.calls[0];
    expect([personId, transferId]).toEqual([5, 21]);
    expect(sent(body)).toEqual({
      type: "FundsGiven", amount: "65000", date: "2026-09-30", counterpartyFinanceAccountId: "9", reference: "CHQ-2240", concurrencyToken: "row-21",
    });
    expect(await screen.findByText("Movement corrected.")).toBeTruthy();
    expect(api.recordMovement).not.toHaveBeenCalled();
  });

  it("removes a saved slip, or replaces it, but never sends both", async () => {
    show("/finance/staff-cash/5");
    await loaded();
    fireEvent.click(tableRow("Returned to Cash in office").getByRole("button", { name: /Actions for the money returned/ }));
    fireEvent.click(screen.getByRole("menuitem", { name: "Correct" }));
    expect(dialog().getByText("cash-return-slip.jpg")).toBeTruthy();
    expect(dialog().getByRole("combobox", { name: /Returned to account/ })).toBeTruthy();
    fireEvent.click(dialog().getByRole("button", { name: "View cash-return-slip.jpg" }));
    expect(api.openAttachment).toHaveBeenCalledWith(5, returned, false);

    fireEvent.click(dialog().getByRole("button", { name: "Remove" }));
    const input = screen.getByRole("dialog").querySelector("input[type=file]") as HTMLInputElement;
    fireEvent.change(input, { target: { files: [new File(["x"], "new-slip.pdf", { type: "application/pdf" })] } });
    fireEvent.click(footerButton("Save correction"));
    await waitFor(() => expect(api.correctMovement).toHaveBeenCalled());
    const body = sent(api.correctMovement.mock.calls[0][2]);
    expect(body.attachment).toBe("new-slip.pdf");
    expect(body.removeAttachment).toBeUndefined();
  });

  it("removes a saved slip on its own", async () => {
    show("/finance/staff-cash/5");
    await loaded();
    fireEvent.click(tableRow("Returned to Cash in office").getByRole("button", { name: /Actions for the money returned/ }));
    fireEvent.click(screen.getByRole("menuitem", { name: "Correct" }));
    fireEvent.click(dialog().getByRole("button", { name: "Remove" }));
    fireEvent.click(footerButton("Save correction"));
    await waitFor(() => expect(api.correctMovement).toHaveBeenCalled());
    expect(sent(api.correctMovement.mock.calls[0][2])).toMatchObject({ type: "FundsReturned", removeAttachment: "true", concurrencyToken: "row-22" });
  });

  it("asks before deleting a money move, then works the balances out again", async () => {
    show("/finance/staff-cash/5");
    await loaded();
    fireEvent.click(tableRow("Received from Meezan Bank").getByRole("button", { name: /Actions for the money received/ }));
    fireEvent.click(screen.getByRole("menuitem", { name: "Delete" }));
    expect(dialog().getByText("Delete this movement?")).toBeTruthy();
    expect(dialog().getByText("The account balances will be worked out again.")).toBeTruthy();
    fireEvent.click(footerButton("Delete"));
    await waitFor(() => expect(api.deleteMovement).toHaveBeenCalledWith(5, received));
    expect(await screen.findByText("Movement deleted.")).toBeTruthy();
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(api.statement).toHaveBeenCalledTimes(2);
    expect(api.overview).toHaveBeenCalledTimes(2);
  });

  it("shows a refused delete in a toast", async () => {
    api.deleteMovement.mockRejectedValueOnce(new Error("Staff cash data changed. Refresh and try again."));
    show("/finance/staff-cash/5");
    await loaded();
    fireEvent.click(tableRow("Received from Meezan Bank").getByRole("button", { name: /Actions for the money received/ }));
    fireEvent.click(screen.getByRole("menuitem", { name: "Delete" }));
    fireEvent.click(footerButton("Delete"));
    expect(await screen.findByText("Staff cash data changed. Refresh and try again.")).toBeTruthy();
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
  });

  it("steps back a page when a delete empties the last one", async () => {
    api.statement.mockImplementation(async (_id, skip) => statementOf(adeel, skip >= 20 ? [received] : [fuel], 21));
    show("/finance/staff-cash/5");
    await loaded();
    fireEvent.click(button("Page 2"));
    await screen.findAllByText("Received from Meezan Bank");
    api.statement.mockImplementation(async (_id, skip) => statementOf(adeel, skip >= 20 ? [] : [fuel], 20));
    fireEvent.click(tableRow("Received from Meezan Bank").getByRole("button", { name: /Actions for the money received/ }));
    fireEvent.click(screen.getByRole("menuitem", { name: "Delete" }));
    fireEvent.click(footerButton("Delete"));
    await waitFor(() => expect(api.statement).toHaveBeenLastCalledWith(5, 0, 20, expect.any(AbortSignal)));
    expect(await screen.findAllByText("Site visits")).toBeTruthy();
  });
});

describe("Cash with staff: recording an expense", () => {
  it("shows the payee name only for a one-off payee, and clears the tax when a vendor is picked", async () => {
    show("/finance/staff-cash/5");
    await loaded();
    fireEvent.click(button("Record an expense"));
    expect(dialog().getByText("Paid from Adeel Satti's float")).toBeTruthy();
    expect(footerButton("Save").disabled).toBe(true);
    expect(options(/Category/)).toEqual(["Construction materials — s.153(1)(a)", "Fuel"]);
    expect(options(/Paid to/)).toEqual(["One-off payee", "City Hardware — Non-filer", "Al-Noor Steel — Filer"]);
    expect(options(/Project/)).toEqual(["General — no project", "Floria Heights"]);
    expect(dialog().getByRole("textbox", { name: /Payee name/ })).toBeTruthy();

    pick(/Category/, "Construction materials — s.153(1)(a)");
    type(dialog().getByRole("textbox", { name: /Gross amount/ }), "50000");
    await waitFor(() => expect((dialog().getByRole("textbox", { name: /Tax withheld/ }) as HTMLInputElement).value).toBe("4,000"));

    pick(/Paid to/, "Al-Noor Steel — Filer");
    expect(dialog().queryByRole("textbox", { name: /Payee name/ })).toBeNull();
    expect((dialog().getByRole("textbox", { name: /Tax withheld/ }) as HTMLInputElement).value).toBe("");
    await waitFor(() => expect((dialog().getByRole("textbox", { name: /Tax withheld/ }) as HTMLInputElement).value).toBe("2,000"));
  });

  it("records the expense against the float with the tax worked out and a money-request key", async () => {
    show("/finance/staff-cash/5");
    await loaded();
    fireEvent.click(button("Record an expense"));
    pick(/Category/, "Construction materials — s.153(1)(a)");
    pick(/Project/, "Floria Heights");
    pick(/Paid to/, "Al-Noor Steel — Filer");
    type(dialog().getByRole("textbox", { name: /Gross amount/ }), "50000");
    type(dialog().getByRole("textbox", { name: /What it was for/ }), "Cement bags for block B");
    await waitFor(() => expect((dialog().getByRole("textbox", { name: /Tax withheld/ }) as HTMLInputElement).value).toBe("2,000"));
    fireEvent.click(footerButton("Save"));

    await waitFor(() => expect(api.recordExpense).toHaveBeenCalled());
    const [body, key] = api.recordExpense.mock.calls[0];
    expect(sent(body)).toEqual({
      financeAccountId: "5", amount: "50000", date: "2026-10-01", categoryId: "4", projectId: "3", vendorId: "12", vendor: "Al-Noor Steel",
      whtRate: "4", whtAmount: "2000", description: "Cement bags for block B",
    });
    expect(key).toMatch(/^expense-/);
    expect(await screen.findByText("Expense recorded.")).toBeTruthy();
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(api.statement).toHaveBeenCalledTimes(2);
  });

  it("names a one-off payee and keeps Save off while a changed tax has no reason", async () => {
    api.recordExpense.mockRejectedValueOnce(new Error("Choose an expense category from the list."));
    show("/finance/staff-cash/5");
    await loaded();
    fireEvent.click(button("Record an expense"));
    pick(/Category/, "Construction materials — s.153(1)(a)");
    type(dialog().getByRole("textbox", { name: /Gross amount/ }), "10000");
    type(dialog().getByRole("textbox", { name: /Payee name/ }), "Rafiq & Sons");
    await waitFor(() => expect((dialog().getByRole("textbox", { name: /Tax withheld/ }) as HTMLInputElement).value).toBe("800"));
    type(dialog().getByRole("textbox", { name: /Tax withheld/ }), "500");
    await waitFor(() => expect(footerButton("Save").disabled).toBe(true));
    type(dialog().getByRole("textbox", { name: /reason/i }), "Supplier showed an exemption certificate");
    await waitFor(() => expect(footerButton("Save").disabled).toBe(false));
    fireEvent.click(footerButton("Save"));
    expect(await dialog().findByText("Choose an expense category from the list.")).toBeTruthy();
    expect(sent(api.recordExpense.mock.calls[0][0])).toMatchObject({ vendor: "Rafiq & Sons", whtAmount: "500", whtOverrideReason: "Supplier showed an exemption certificate" });
    expect(sent(api.recordExpense.mock.calls[0][0]).vendorId).toBeUndefined();
  });
});

describe("Cash with staff on a phone", () => {
  beforeEach(() => stubMedia(true));

  it("shows only the list, and opens a person on their own page", async () => {
    show();
    await screen.findByText("Salman Khan");
    expect(screen.queryByText("Select a person to see their movements.")).toBeNull();
    fireEvent.click(personCard("Adeel Satti"));
    expect(where()).toBe("/finance/staff-cash/5");
    expect(await screen.findByRole("heading", { name: "Adeel Satti" })).toBeTruthy();
    expect(screen.queryByRole("heading", { name: "Cash with staff" })).toBeNull();
  });

  it("turns the ⋯ button off for an inactive person", async () => {
    show("/finance/staff-cash/9");
    await screen.findByText("Fully settled");
    expect(button("Record an expense").disabled).toBe(true);
    expect(button("More for Bilal Ahmed").disabled).toBe(true);
  });

  it("does not show a movement twice when Load more finds the list shifted by a new one", async () => {
    api.statement.mockImplementation(async (_id, skip) => statementOf(adeel, skip === 0 ? [fuel] : [fuel, received], 3));
    show("/finance/staff-cash/5");
    await loaded();
    fireEvent.click(button("Load more"));
    await screen.findAllByText("Received from Meezan Bank");
    expect(screen.getAllByText("Site visits")).toHaveLength(2);
  });

  it("shows movements as cards with Load more, and the money moves in a sheet", async () => {
    api.statement.mockImplementation(async (_id, skip) => statementOf(adeel, skip === 0 ? [fuel] : [received], 2));
    show("/finance/staff-cash/5");
    expect(await screen.findAllByText("Oct 1, 2026 · General · Ref: PSO Taxila")).toBeTruthy();
    expect(screen.getByText(/entries$/).textContent).toBe("Showing 1 of 2 entries");
    fireEvent.click(button("Load more"));
    expect(await screen.findAllByText("Sep 30, 2026 · Received from Meezan Bank · Ref: CHQ-2240")).toBeTruthy();
    expect(screen.getAllByText("Site visits")).toBeTruthy();
    expect(api.statement).toHaveBeenLastCalledWith(5, 20, 20, expect.any(AbortSignal));

    expect(screen.queryByRole("button", { name: "Give money" })).toBeNull();
    fireEvent.click(button("More for Adeel Satti"));
    const sheet = within(screen.getByRole("dialog"));
    expect(button("Record cash returned", sheet).disabled).toBe(false);
    fireEvent.click(button("Give money", sheet));
    expect(await screen.findByRole("combobox", { name: /Paid from account/ })).toBeTruthy();
  });
});
