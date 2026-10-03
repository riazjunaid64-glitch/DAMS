// @vitest-environment happy-dom
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { ToastProvider } from "../components/ui/Toast.tsx";
import { PHONE_QUERY } from "../components/ui/useMediaQuery.ts";
import type { User } from "../App.tsx";
import { loansApi } from "../features/finance/loans/api.ts";
import type { CashAccount, Loan, LoanAccount, LoanTransaction, Statement } from "../features/finance/loans/types.ts";
import FinanceLoansPage from "./FinanceLoansPage.tsx";

vi.mock("../features/finance/loans/api.ts", () => ({
  STATEMENT_MAX_TAKE: 200,
  loansApi: {
    loans: vi.fn(),
    loanAccounts: vi.fn(),
    cashAccounts: vi.fn(),
    statement: vi.fn(),
    saveLoan: vi.fn(),
    recordMovement: vi.fn(),
    correctMovement: vi.fn(),
    deleteMovement: vi.fn(),
    openAttachment: vi.fn(),
    allMovements: vi.fn(),
  },
}));

const api = vi.mocked(loansApi);

const loan = (over: Partial<Loan> = {}): Loan => ({
  id: 1, name: "Bank Alfalah loan", lenderName: "Bank Alfalah", financeAccountId: 30, financeAccountName: "Bank Alfalah loan", isActive: true,
  openingBalance: 0, drawnPrincipal: 5_000_000, repaidPrincipal: 2_000_000, interestPaid: 355_000, currentBalance: 3_000_000,
  transactionCount: 3, concurrencyToken: "loan-tok", ...over,
});
const alfalah = loan();
const director = loan({
  id: 2, name: "Director loan — Ayub Satti", lenderName: "Ayub Satti", financeAccountId: 31, financeAccountName: "Director loan account", isActive: false,
  drawnPrincipal: 1_000_000, repaidPrincipal: 1_000_000, interestPaid: 0, currentBalance: 0, transactionCount: 0, concurrencyToken: "dir-tok",
});

const slip = { fileName: "hbl-transfer-feb.pdf", contentType: "application/pdf", fileSize: 98_304, uploadedAt: "2026-02-05T00:00:00" };
const movement = (over: Partial<LoanTransaction> = {}): LoanTransaction => ({
  id: 10, loanId: 1, type: "Repayment", principalAmount: 0, interestAmount: 35_000, totalCashMovement: 35_000, date: "2026-09-05T00:00:00",
  financeAccountId: 8, financeAccountName: "HBL Current", reference: null, note: null, runningBalance: 3_000_000,
  createdAt: "", updatedAt: "", concurrencyToken: "row-10", attachment: null, ...over,
});
const interestOnly = movement({ note: "Principal holiday agreed with bank" });
const february = movement({ id: 11, principalAmount: 400_000, interestAmount: 60_000, totalCashMovement: 460_000, date: "2026-02-05T00:00:00", runningBalance: 4_600_000, concurrencyToken: "row-11", attachment: slip });
const drawdown = movement({
  id: 12, type: "Drawdown", principalAmount: 5_000_000, interestAmount: 0, totalCashMovement: 5_000_000, date: "2026-01-05T00:00:00",
  financeAccountId: 7, financeAccountName: "Meezan Bank", reference: "BAL-DD-01", runningBalance: 5_000_000, concurrencyToken: "row-12",
});
const statementOf = (forLoan: Loan, items: LoanTransaction[], hasMore = false): Statement => ({ loan: forLoan, items, hasMore });

const cash = (over: Partial<CashAccount>): CashAccount => ({ id: 7, name: "Meezan Bank", accountHolderName: "Seven Ventures", isActive: true, ...over });
const cashAccounts = [cash({}), cash({ id: 8, name: "HBL Current" }), cash({ id: 9, name: "Old bank", isActive: false })];
const loanAccounts: LoanAccount[] = [
  { id: 30, name: "Bank Alfalah loan", accountHolderName: "Seven Ventures", isActive: true, linkedLoanId: 1, linkedLoanName: "Bank Alfalah loan" },
  { id: 31, name: "Director loan account", accountHolderName: "Seven Ventures", isActive: true, linkedLoanId: 2, linkedLoanName: "Director loan" },
  { id: 32, name: "Car finance", accountHolderName: "Seven Ventures", isActive: true, linkedLoanId: null, linkedLoanName: null },
];

const admin: User = { userId: "1", role: "Admin", email: "a@b.c" };

function Where() {
  return <p data-testid="where">{useLocation().pathname}</p>;
}

function show(path = "/finance/loans", user: User | null = admin) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <ToastProvider>
        <Routes>
          <Route path="/finance/loans" element={<FinanceLoansPage user={user} />} />
          <Route path="/finance/loans/:loanId" element={<FinanceLoansPage user={user} />} />
          <Route path="/" element={<p>Home page</p>} />
        </Routes>
        <Where />
      </ToastProvider>
    </MemoryRouter>,
  );
}

let phoneWidth = false;
const mediaListeners = new Set<() => void>();

function stubMedia(phone: boolean) {
  phoneWidth = phone;
  mediaListeners.clear();
  vi.stubGlobal("matchMedia", (query: string) => ({
    get matches() { return phoneWidth && query === PHONE_QUERY; },
    media: query,
    addEventListener: (_: string, listener: () => void) => mediaListeners.add(listener),
    removeEventListener: (_: string, listener: () => void) => mediaListeners.delete(listener),
  }));
}

/** The window is resized across the phone width while the page is open. */
function resize(phone: boolean) {
  act(() => {
    phoneWidth = phone;
    mediaListeners.forEach((listener) => listener());
  });
}

/** `count` distinct movements starting at `skip`, newest first, as the server pages them. */
const movementsFrom = (skip: number, count: number) => Array.from({ length: count }, (_, index) => movement({
  id: 1000 + skip + index,
  reference: `REF-${skip + index + 1}`,
  date: `2026-09-${String(28 - ((skip + index) % 28)).padStart(2, "0")}T00:00:00`,
}));

const dialog = () => within(screen.getByRole("dialog"));
const button = (name: string | RegExp, scope: { getByRole: typeof screen.getByRole } = screen) => scope.getByRole("button", { name }) as HTMLButtonElement;
const footerButton = (name: string) => within(screen.getByRole("dialog").querySelector("footer") as HTMLElement).getByRole("button", { name }) as HTMLButtonElement;
const type = (input: HTMLElement, value: string) => fireEvent.change(input, { target: { value } });
const pick = (name: RegExp | string, option: string) => {
  fireEvent.click(dialog().getByRole("combobox", { name }));
  fireEvent.click(screen.getByRole("option", { name: option }));
};
const tableRow = (text: string) => within(screen.getAllByRole("row").find((candidate) => within(candidate).queryByText(text)) as HTMLElement);
const where = () => screen.getByTestId("where").textContent;
const sent = (body: FormData) => Object.fromEntries([...body.entries()].map(([key, value]) => [key, typeof value === "string" ? value : (value as File).name]));
const loaded = async () => { await screen.findByText("Sep 5, 2026"); };

beforeEach(() => {
  vi.resetAllMocks();
  stubMedia(false);
  // 2026-10-01 12:00 in Pakistan.
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date("2026-10-01T07:00:00Z"));
  Element.prototype.scrollIntoView = () => {};
  api.loans.mockResolvedValue([alfalah, director]);
  api.loanAccounts.mockResolvedValue(loanAccounts);
  api.cashAccounts.mockResolvedValue(cashAccounts);
  api.statement.mockImplementation(async (id) => (id === 1 ? statementOf(alfalah, [interestOnly, february, drawdown]) : statementOf(director, [])));
  api.saveLoan.mockImplementation(async (body, id) => loan({ ...body, id: id ?? 3, lenderName: body.lenderName, financeAccountName: "Car finance" }));
  api.recordMovement.mockResolvedValue(undefined);
  api.correctMovement.mockResolvedValue(undefined);
  api.deleteMovement.mockResolvedValue(undefined);
  api.openAttachment.mockResolvedValue(undefined);
  api.allMovements.mockResolvedValue([interestOnly, february, drawdown]);
});

afterEach(() => {
  cleanup();
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe("Loans: the list and the open loan", () => {
  it("lists every loan as a card and opens the first one with its figures and activity", async () => {
    show();
    await loaded();
    const card = button(/Director loan — Ayub Satti/);
    expect(card.textContent).toContain("Lender: Ayub Satti · Account: Director loan account");
    expect(card.textContent).toContain("Closed");
    expect(card.getAttribute("aria-pressed")).toBe("false");
    expect(button(/^Bank Alfalah loan.*Active/).getAttribute("aria-pressed")).toBe("true");

    expect(screen.getByRole("heading", { name: "Bank Alfalah loan" })).toBeTruthy();
    expect(screen.getByText("Lender: Bank Alfalah · Loan account: Bank Alfalah loan")).toBeTruthy();
    const summary = within(screen.getByRole("heading", { name: "Bank Alfalah loan" }).closest("section") as HTMLElement);
    expect(summary.getByText("Rs 5,000,000")).toBeTruthy();
    expect(summary.getByText("Rs 2,000,000")).toBeTruthy();
    expect(summary.getByText("Rs 355,000")).toBeTruthy();
    expect(api.statement).toHaveBeenCalledWith(1, 0, 20, expect.any(AbortSignal));
  });

  it("signs each movement and says what it was made of", async () => {
    show();
    await loaded();
    const hol = tableRow("Principal holiday agreed with bank");
    expect(hol.getByText("Interest only")).toBeTruthy();
    expect(hol.getByText("35,000")).toBeTruthy();
    expect(hol.getByText("−35,000")).toBeTruthy();
    expect(hol.getByText("None")).toBeTruthy();

    const feb = tableRow("Feb 5, 2026");
    expect(feb.getByText("Principal + interest")).toBeTruthy();
    expect(feb.getByText("−400,000")).toBeTruthy();
    expect(feb.getByText("−460,000")).toBeTruthy();
    expect(feb.getByText("4,600,000")).toBeTruthy();
    expect(feb.getByRole("button", { name: "Attached" })).toBeTruthy();

    const jan = tableRow("Jan 5, 2026");
    expect(jan.getByText("Funds received")).toBeTruthy();
    expect(jan.getByText("Drawdown · BAL-DD-01")).toBeTruthy();
    expect(jan.getAllByText("5,000,000")).toHaveLength(3);
    expect(screen.getByText(/Showing/).textContent).toBe("Showing 1–3 of 3 entries");
  });

  it("opens and downloads a movement's attachment", async () => {
    show();
    await loaded();
    fireEvent.click(tableRow("Feb 5, 2026").getByRole("button", { name: "Attached" }));
    fireEvent.click(tableRow("Feb 5, 2026").getByRole("button", { name: "Download hbl-transfer-feb.pdf" }));
    expect(api.openAttachment).toHaveBeenNthCalledWith(1, 1, february, false);
    expect(api.openAttachment).toHaveBeenNthCalledWith(2, 1, february, true);
  });

  it("narrows the cards by search and status without changing the open loan", async () => {
    show();
    await loaded();
    fireEvent.click(screen.getByRole("combobox", { name: /Status/ }));
    fireEvent.click(screen.getByRole("option", { name: "Closed" }));
    expect(screen.queryByRole("button", { name: /^Bank Alfalah loan.*Active/ })).toBeNull();
    expect(button(/Director loan — Ayub Satti/)).toBeTruthy();
    expect(screen.getByRole("heading", { name: "Bank Alfalah loan" })).toBeTruthy();
  });

  it("opens another loan from its card, by address", async () => {
    show();
    await loaded();
    fireEvent.click(button(/Director loan — Ayub Satti/));
    expect(where()).toBe("/finance/loans/2");
    expect(await screen.findByRole("heading", { name: "Director loan — Ayub Satti" })).toBeTruthy();
    expect(await screen.findByText("No movements recorded for this loan yet.")).toBeTruthy();
    expect(button("Record repayment").disabled).toBe(true);
    expect(button("Receive loan funds").disabled).toBe(true);
    expect(button("Export").disabled).toBe(true);
    expect(screen.getByText("This loan is closed. Reopen it from Edit loan to record new movements.")).toBeTruthy();
  });

  it("ignores a slow answer for the loan that was left", async () => {
    let finishOld: (value: Statement) => void = () => {};
    api.statement.mockImplementation((id) => id === 1
      ? new Promise<Statement>((resolve) => { finishOld = resolve; })
      : Promise.resolve(statementOf(director, [])));
    show();
    await screen.findByRole("heading", { name: "Bank Alfalah loan" });
    fireEvent.click(button(/Director loan — Ayub Satti/));
    await screen.findByText("No movements recorded for this loan yet.");
    await act(async () => finishOld(statementOf(alfalah, [interestOnly])));
    expect(screen.queryByText("Principal holiday agreed with bank")).toBeNull();
  });

  it("pages through a long history, asking the server for that page", async () => {
    api.loans.mockResolvedValue([loan({ transactionCount: 45 })]);
    api.statement.mockResolvedValue(statementOf(loan({ transactionCount: 45 }), [interestOnly], true));
    show();
    await loaded();
    expect(screen.getByText(/Showing/).textContent).toBe("Showing 1–20 of 45 entries");
    fireEvent.click(button("Page 3"));
    await waitFor(() => expect(api.statement).toHaveBeenLastCalledWith(1, 40, 20, expect.any(AbortSignal)));
  });

  it("shows a loan that is not in the list as not found", async () => {
    show("/finance/loans/99");
    expect(await screen.findByText("Loan not found")).toBeTruthy();
  });

  it("exports every movement of the loan, not only the page on screen", async () => {
    const saved: Blob[] = [];
    Object.assign(URL, { createObjectURL: (blob: Blob) => { saved.push(blob); return "blob:x"; }, revokeObjectURL: () => {} });
    show();
    await loaded();
    fireEvent.click(button("Export"));
    await waitFor(() => expect(saved).toHaveLength(1));
    expect(api.allMovements).toHaveBeenCalledWith(1);
    expect(await saved[0].text()).toContain("Outstanding principal (Rs)");
  });

  it("sends anyone without finance access home", async () => {
    show("/finance/loans", { userId: "9", role: "Client", email: "x@y.z" });
    expect(await screen.findByText("Home page")).toBeTruthy();
  });
});

describe("Loans: adding and editing a loan", () => {
  it("adds a loan on a free Liability account and opens it", async () => {
    show();
    await loaded();
    fireEvent.click(button("Add loan"));
    expect(dialog().getByText("Add loan")).toBeTruthy();
    expect(footerButton("Save loan").disabled).toBe(true);
    type(dialog().getByRole("textbox", { name: /Loan name/ }), "Car finance");
    type(dialog().getByRole("textbox", { name: /Lender name/ }), "Meezan Bank");
    fireEvent.click(dialog().getByRole("combobox", { name: /Loan liability account/ }));
    expect(screen.getAllByRole("option").map((option) => option.textContent)).toEqual(["Car finance · Seven Ventures"]);
    fireEvent.click(screen.getByRole("option", { name: "Car finance · Seven Ventures" }));
    fireEvent.click(footerButton("Save loan"));
    await waitFor(() => expect(api.saveLoan).toHaveBeenCalledWith(
      { name: "Car finance", lenderName: "Meezan Bank", financeAccountId: 32, isActive: true, concurrencyToken: "" }, null));
    await waitFor(() => expect(where()).toBe("/finance/loans/3"));
    expect(api.loans).toHaveBeenCalledTimes(2);
  });

  it("keeps the edited loan open when the rename moves it down the list", async () => {
    // The server lists active loans first, then by name: renaming "Bank Alfalah loan" to "Zarai Bank
    // loan" puts "Car finance" first. The open loan must stay the one that was edited.
    const car = loan({ id: 3, name: "Car finance", lenderName: "Meezan Bank", financeAccountId: 32, financeAccountName: "Car finance", concurrencyToken: "car-tok" });
    const renamed = loan({ name: "Zarai Bank loan", concurrencyToken: "loan-tok-2" });
    api.loans.mockResolvedValueOnce([alfalah, car, director]).mockResolvedValue([car, renamed, director]);
    api.saveLoan.mockResolvedValue(renamed);
    show();
    await loaded();
    fireEvent.click(button("Edit loan"));
    expect(dialog().getByText("Edit loan")).toBeTruthy();
    const name = dialog().getByRole("textbox", { name: /Loan name/ }) as HTMLInputElement;
    expect(name.value).toBe("Bank Alfalah loan");
    type(name, "Zarai Bank loan");
    fireEvent.click(footerButton("Save loan"));
    await waitFor(() => expect(api.saveLoan).toHaveBeenCalledWith(
      { name: "Zarai Bank loan", lenderName: "Bank Alfalah", financeAccountId: 30, isActive: true, concurrencyToken: "loan-tok" }, 1), { timeout: 5000 });
    expect(await screen.findByRole("heading", { name: "Zarai Bank loan" }, { timeout: 5000 })).toBeTruthy();
    expect(where()).toBe("/finance/loans/1");
    expect(screen.queryByRole("heading", { name: "Car finance" })).toBeNull();
    expect(button(/^Car finance/).getAttribute("aria-pressed")).toBe("false");
  });

  it("offers the loan accounts that arrived even when the loans did not", async () => {
    api.loans.mockRejectedValue(new Error("Loans could not be loaded."));
    show();
    expect(await screen.findByText("Loans could not be loaded.")).toBeTruthy();
    fireEvent.click(button("Add loan"));
    fireEvent.click(dialog().getByRole("combobox", { name: /Loan liability account/ }));
    expect(screen.getByRole("option", { name: "Car finance · Seven Ventures" })).toBeTruthy();
    expect(dialog().queryByText("Add a Liability account in Manage accounts first.")).toBeNull();
  });
});

describe("Loans: movements", () => {
  it("records a repayment with the total leaving the bank, a money-request key and the file", async () => {
    show();
    await loaded();
    fireEvent.click(button("Record repayment"));
    expect(dialog().getByText("Record repayment")).toBeTruthy();
    expect(footerButton("Save").disabled).toBe(true);
    type(dialog().getByRole("textbox", { name: /Principal portion/ }), "400000");
    type(dialog().getByRole("textbox", { name: /Interest portion/ }), "35000");
    expect(dialog().getByText("Rs 435,000")).toBeTruthy();
    fireEvent.click(dialog().getByRole("combobox", { name: /Paid from account/ }));
    expect(screen.getAllByRole("option").map((option) => option.textContent)).toEqual(["Meezan Bank · Seven Ventures", "HBL Current · Seven Ventures"]);
    fireEvent.click(screen.getByRole("option", { name: "HBL Current · Seven Ventures" }));
    type(dialog().getByRole("textbox", { name: /Reference/ }), "HBL-OCT");
    const input = screen.getByRole("dialog").querySelector("input[type=file]") as HTMLInputElement;
    fireEvent.change(input, { target: { files: [new File(["x"], "slip.pdf", { type: "application/pdf" })] } });
    fireEvent.click(footerButton("Save"));

    await waitFor(() => expect(api.recordMovement).toHaveBeenCalled());
    const [loanId, body, key] = api.recordMovement.mock.calls[0];
    expect(loanId).toBe(1);
    expect(sent(body)).toEqual({
      type: "Repayment", principalAmount: "400000", interestAmount: "35000", date: "2026-10-01", financeAccountId: "8", reference: "HBL-OCT", attachment: "slip.pdf",
    });
    expect(key).toMatch(/^loan-movement-/);
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(api.loans).toHaveBeenCalledTimes(2);
    expect(api.statement).toHaveBeenCalledTimes(2);
  });

  it("keeps the same key when a failed repayment is tried again", async () => {
    api.recordMovement.mockRejectedValueOnce(new Error("Network went away."));
    show();
    await loaded();
    fireEvent.click(button("Record repayment"));
    type(dialog().getByRole("textbox", { name: /Interest portion/ }), "35000");
    pick(/Paid from account/, "HBL Current · Seven Ventures");
    fireEvent.click(footerButton("Save"));
    expect(await dialog().findByText("Network went away.")).toBeTruthy();
    fireEvent.click(footerButton("Save"));
    await waitFor(() => expect(api.recordMovement).toHaveBeenCalledTimes(2));
    expect(api.recordMovement.mock.calls[1][2]).toBe(api.recordMovement.mock.calls[0][2]);
    expect(sent(api.recordMovement.mock.calls[1][1]).principalAmount).toBe("0");
  });

  it("receives loan funds as principal only, into the chosen account", async () => {
    show();
    await loaded();
    fireEvent.click(button("Receive loan funds"));
    expect(dialog().getByText("Receive loan funds")).toBeTruthy();
    expect(dialog().queryByRole("textbox", { name: /Interest portion/ })).toBeNull();
    type(dialog().getByRole("textbox", { name: /Principal received/ }), "1000000");
    pick(/Received in account/, "Meezan Bank · Seven Ventures");
    type(dialog().getByRole("textbox", { name: /Reference/ }), "BAL-DD-02");
    fireEvent.click(footerButton("Save"));
    await waitFor(() => expect(api.recordMovement).toHaveBeenCalled());
    expect(sent(api.recordMovement.mock.calls[0][1])).toEqual({
      type: "Drawdown", principalAmount: "1000000", interestAmount: "0", date: "2026-10-01", financeAccountId: "7", reference: "BAL-DD-02",
    });
  });

  it("refuses a repayment of nothing", async () => {
    show();
    await loaded();
    fireEvent.click(button("Record repayment"));
    type(dialog().getByRole("textbox", { name: /Principal portion/ }), "0");
    pick(/Paid from account/, "HBL Current · Seven Ventures");
    fireEvent.click(footerButton("Save"));
    expect(await dialog().findByText("A repayment needs principal, interest or both.")).toBeTruthy();
    expect(api.recordMovement).not.toHaveBeenCalled();
  });

  it("corrects a repayment with its row version and can drop its attachment", async () => {
    show();
    await loaded();
    fireEvent.click(tableRow("Feb 5, 2026").getByRole("button", { name: /Actions for the repayment/ }));
    fireEvent.click(screen.getByRole("menuitem", { name: "Correct" }));
    expect(dialog().getByText("Correct repayment")).toBeTruthy();
    expect(dialog().getByText("Bank Alfalah loan · Feb 5, 2026")).toBeTruthy();
    expect((dialog().getByRole("textbox", { name: /Principal portion/ }) as HTMLInputElement).value).toBe("400,000");
    expect(dialog().getByText("Rs 460,000")).toBeTruthy();
    expect(dialog().getByText("hbl-transfer-feb.pdf")).toBeTruthy();
    fireEvent.click(dialog().getByRole("button", { name: "View hbl-transfer-feb.pdf" }));
    expect(api.openAttachment).toHaveBeenCalledWith(1, february, false);
    fireEvent.click(dialog().getByRole("button", { name: "Remove" }));
    type(dialog().getByRole("textbox", { name: /Interest portion/ }), "55000");
    fireEvent.click(footerButton("Save correction"));
    await waitFor(() => expect(api.correctMovement).toHaveBeenCalled());
    const [loanId, movementId, body] = api.correctMovement.mock.calls[0];
    expect([loanId, movementId]).toEqual([1, 11]);
    expect(sent(body)).toEqual({
      type: "Repayment", principalAmount: "400000", interestAmount: "55000", date: "2026-02-05", financeAccountId: "8", concurrencyToken: "row-11", removeAttachment: "true",
    });
    expect(api.recordMovement).not.toHaveBeenCalled();
  });

  it("asks before deleting a movement, then works the balances out again", async () => {
    show();
    await loaded();
    fireEvent.click(tableRow("Feb 5, 2026").getByRole("button", { name: /Actions for the repayment/ }));
    fireEvent.click(screen.getByRole("menuitem", { name: "Delete" }));
    expect(dialog().getByText("Delete this repayment?")).toBeTruthy();
    expect(dialog().getByText("The loan and bank balances will be worked out again.")).toBeTruthy();
    fireEvent.click(footerButton("Delete"));
    await waitFor(() => expect(api.deleteMovement).toHaveBeenCalledWith(1, february));
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(api.statement).toHaveBeenCalledTimes(2);
  });

  it("names a drawdown when it is the one being deleted", async () => {
    show();
    await loaded();
    fireEvent.click(tableRow("Jan 5, 2026").getByRole("button", { name: /Actions for the funds received/ }));
    fireEvent.click(screen.getByRole("menuitem", { name: "Delete" }));
    expect(dialog().getByText("Delete these loan funds?")).toBeTruthy();
  });
});

describe("Loans on a phone", () => {
  beforeEach(() => stubMedia(true));

  it("shows only the list, and opens a loan on its own page", async () => {
    show();
    await screen.findByText(/Director loan — Ayub Satti/);
    expect(screen.queryByRole("heading", { name: "Bank Alfalah loan" })).toBeNull();
    expect(api.statement).not.toHaveBeenCalled();
    fireEvent.click(button(/^Bank Alfalah loan.*Active/));
    expect(where()).toBe("/finance/loans/1");
    expect(await screen.findByRole("heading", { name: "Bank Alfalah loan" })).toBeTruthy();
    expect(screen.queryByRole("heading", { name: "Loans" })).toBeNull();
  });

  it("shows the activity as cards, with the less common actions in a sheet", async () => {
    show("/finance/loans/1");
    expect(await screen.findAllByText("Sep 5, 2026 · HBL Current · Interest only")).toBeTruthy();
    expect(screen.getByText(/entries$/).textContent).toBe("Showing 3 of 3 entries");
    expect(screen.queryByRole("button", { name: "Edit loan" })).toBeNull();
    fireEvent.click(button("More for Bank Alfalah loan"));
    const sheet = within(screen.getByRole("dialog"));
    expect(sheet.getByRole("button", { name: "Receive loan funds" })).toBeTruthy();
    expect(sheet.getByRole("button", { name: "Export activity" })).toBeTruthy();
    fireEvent.click(sheet.getByRole("button", { name: "Edit loan" }));
    expect(await screen.findByText("Save loan")).toBeTruthy();
  });

  it("loads the next page under the cards it already has", async () => {
    api.loans.mockResolvedValue([loan({ transactionCount: 25 })]);
    api.statement.mockImplementation(async (_id, skip) => statementOf(loan({ transactionCount: 25 }), skip === 0 ? [interestOnly] : [drawdown], skip === 0));
    show("/finance/loans/1");
    await screen.findAllByText("Principal holiday agreed with bank");
    fireEvent.click(button("Load more"));
    await screen.findAllByText(/Drawdown · BAL-DD-01/);
    expect(api.statement).toHaveBeenLastCalledWith(1, 1, 20, expect.any(AbortSignal));
    // Appended under the first card, not in place of it.
    expect(document.querySelectorAll("ul > li")).toHaveLength(2);
  });
});

describe("Loans: resizing across the phone width", () => {
  const long = loan({ transactionCount: 45 });
  beforeEach(() => {
    api.loans.mockResolvedValue([long, director]);
    api.statement.mockImplementation(async (_id, skip, take) => statementOf(long, movementsFrom(skip, Math.min(take, 45 - skip)), skip + take < 45));
  });

  it("starts the desktop at page 1 after a phone loaded more", async () => {
    stubMedia(true);
    show("/finance/loans/1");
    await screen.findAllByText(/REF-20$/);
    fireEvent.click(button("Load more"));
    await screen.findAllByText(/REF-40$/);

    resize(false);
    await waitFor(() => expect(api.statement).toHaveBeenLastCalledWith(1, 0, 20, expect.any(AbortSignal)));
    await waitFor(() => expect(screen.getByText(/Showing/).textContent).toBe("Showing 1–20 of 45 entries"));
    // The table holds exactly the first page: the heading row plus 20 movements.
    expect(within(screen.getByRole("table")).getAllByRole("row")).toHaveLength(21);
    expect(screen.queryAllByText(/REF-21$/)).toHaveLength(0);
  });

  it("starts the phone from the newest movement after the desktop was on page 3", async () => {
    show("/finance/loans/1");
    await screen.findAllByText(/REF-1$/);
    fireEvent.click(button("Page 3"));
    await screen.findAllByText(/REF-41$/);

    resize(true);
    await waitFor(() => expect(api.statement).toHaveBeenLastCalledWith(1, 0, 20, expect.any(AbortSignal)));
    await screen.findAllByText(/REF-1$/);
    expect(screen.queryAllByText(/REF-41$/)).toHaveLength(0);
    fireEvent.click(button("Load more"));
    await waitFor(() => expect(api.statement).toHaveBeenLastCalledWith(1, 20, 20, expect.any(AbortSignal)));
    await screen.findAllByText(/REF-21$/);
    expect(document.querySelectorAll("ul > li")).toHaveLength(40);
  });
});
