// @vitest-environment happy-dom
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { ToastProvider } from "../components/ui/Toast.tsx";
import { PHONE_QUERY } from "../components/ui/useMediaQuery.ts";
import { ProjectsContext } from "../contexts/projectsContextValue.ts";
import type { User } from "../App.tsx";
import type { ProjectFromApi } from "../utils/parseProject.ts";
import FinanceReportsPage from "./FinanceReportsPage.tsx";

const calls: string[] = [];

vi.mock("../api/api.ts", () => ({
  api: async (url: string) => {
    calls.push(url);
    const { status = 200, body, blob } = respond(url);
    if (blob) return new Response(blob, { status });
    return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
  },
}));

const pnl = {
  periodStart: "2026-07-01",
  periodEnd: "2027-06-30",
  periodLabel: "2026-27",
  projectName: null,
  incomeLines: [{ categoryId: 1, name: "Unit sales", amount: 1, priorAmount: null, transactionCount: 1 }],
  totalIncome: 10125000,
  expenseLines: [],
  totalExpenses: 9179350,
  netProfit: 945650,
  priorTotalIncome: 0,
  priorTotalExpenses: 0,
  priorNetProfit: 0,
};

const trial = {
  columnDates: ["2026-09-29"],
  rows: [
    { accountId: 1, accountKey: "acct:1", ledgerCode: "1002", accountName: "Meezan Bank", debitBalances: [18400000], creditBalances: [0] },
    { accountId: 2, accountKey: "acct:2", ledgerCode: null, accountName: "Customer deposits", debitBalances: [0], creditBalances: [18400000] },
  ],
  columnDebitTotals: [18400000],
  columnCreditTotals: [18400000],
  columnBalanced: [true],
};

const details = {
  accountName: "Meezan Bank",
  ledgerCode: "1002",
  from: "2026-09-01",
  to: "2026-09-29",
  openingBalance: 12000000,
  openingBalanceType: "Debit",
  closingBalance: 18400000,
  closingBalanceType: "Debit",
  totalDebit: 7282660,
  totalCredit: 882660,
  rows: [
    { id: 1, date: "2026-09-02", description: "Late payment surcharge", reference: "MR-0031", debit: 30000, credit: 0, runningBalance: 12030000, runningBalanceType: "Debit" },
    { id: 2, date: "2026-09-27", description: "Fixed asset purchase", reference: null, debit: 0, credit: 92160, runningBalance: 11937840, runningBalanceType: "Debit" },
  ],
};

const balance = {
  asAt: "2026-09-29",
  assetGroups: [{ name: "CURRENT ASSETS", lines: [{ accountId: 1, ledgerCode: "1001", name: "Cash in office", amount: 1250000 }], total: 1250000 }],
  totalAssets: 1250000,
  liabilityGroups: [{ name: "LIABILITIES", lines: [{ accountId: 5, ledgerCode: "2101", name: "Customer deposits", amount: 750000 }], total: 750000 }],
  totalLiabilities: 750000,
  capitalLines: [{ accountId: 7, ledgerCode: "3001", name: "Capital — Riaz Junaid", amount: 400000 }],
  retainedProfit: 100000,
  unpostedFixedAssetCharge: 96000,
  retainedProfitStart: null as string | null,
  totalCapital: 500000,
  totalLiabilitiesAndCapital: 1250000,
  isBalanced: true,
  imbalance: 0,
  unbalancedAccounts: [] as string[],
};

let trialData: typeof trial = trial;
let balanceData: typeof balance = balance;
let pnlStatus = 200;
let detailsStatuses: number[] = [];
let exportStatus = 200;
let settingsStatus = 200;

function respond(url: string): { status?: number; body?: unknown; blob?: Blob } {
  if (url.includes("/wht/settings")) {
    if (settingsStatus !== 200) return { status: settingsStatus, body: {} };
    return { body: { financialYearStartMonth: 7, whtRatesConfirmedAt: null, whtRatesConfirmedByName: null, currentFinancialYear: "2026-27", concurrencyToken: "t" } };
  }
  if (url.includes("/export?")) {
    return exportStatus === 200 ? { blob: new Blob(["x"]) } : { status: exportStatus, body: { message: "The export service is down." } };
  }
  if (url.includes("/profit-and-loss")) return pnlStatus === 200 ? { body: pnl } : { status: pnlStatus, body: { message: "Profit and loss is unavailable." } };
  if (url.includes("/trial-balance/details")) {
    const status = detailsStatuses.shift() ?? 200;
    return status === 200 ? { body: details } : { status, body: { message: "The ledger could not be read." } };
  }
  if (url.includes("/trial-balance")) return { body: trialData };
  if (url.includes("/balance-sheet")) return { body: balanceData };
  return { body: {} };
}

const deenSquare = { id: 1, projectName: "Deen Square" } as ProjectFromApi;
const admin: User = { userId: "1", role: "Admin", email: "a@b.c" };

function tree(user: User | null) {
  return (
    <MemoryRouter initialEntries={["/finance/reports"]}>
      <ProjectsContext.Provider value={{ projects: [deenSquare], loading: false, error: null, reload: async () => {} }}>
        <ToastProvider>
          <Routes>
            <Route path="/finance/reports" element={<FinanceReportsPage user={user} />} />
            <Route path="/" element={<p>Home page</p>} />
          </Routes>
        </ToastProvider>
      </ProjectsContext.Provider>
    </MemoryRouter>
  );
}

const show = (user: User | null = admin) => render(tree(user));

function stubMedia(phone: boolean) {
  vi.stubGlobal("matchMedia", (query: string) => ({
    matches: phone && query === PHONE_QUERY,
    media: query,
    addEventListener: () => {},
    removeEventListener: () => {},
  }));
}

const reportCalls = (endpoint: string) => calls.filter((call) => call.includes(`/api/Finance/${endpoint}?`));

beforeEach(() => {
  calls.length = 0;
  trialData = trial;
  balanceData = balance;
  pnlStatus = 200;
  detailsStatuses = [];
  exportStatus = 200;
  settingsStatus = 200;
  stubMedia(false);
  Element.prototype.scrollIntoView = () => {};
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date("2026-09-29T07:00:00Z"));
});

afterEach(() => {
  cleanup();
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

const openTab = (name: string) => fireEvent.click(screen.getByRole("tab", { name }));

describe("Financial reports: frame and access", () => {
  it("shows the title, Export to Excel and three tabs, with no back link, Capital partners or Refresh", async () => {
    show();
    expect(await screen.findByRole("heading", { name: "Financial reports" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Export to Excel" })).toBeTruthy();
    expect(screen.getAllByRole("tab").map((tab) => tab.textContent)).toEqual(["Profit & loss", "Trial balance", "Balance sheet"]);
    expect(screen.queryByText(/Capital partners/i)).toBeNull();
    expect(screen.queryByRole("button", { name: /Refresh/ })).toBeNull();
    expect(screen.queryByText(/Finance dashboard/)).toBeNull();
  });

  it("does not send anyone home, or load anything, while the signed-in user is still loading", async () => {
    const view = show(null);
    await new Promise((resolve) => setTimeout(resolve, 400));
    expect(screen.queryByText("Home page")).toBeNull();
    expect(calls).toEqual([]);
    view.rerender(tree(admin));
    expect(await screen.findByText("Profit & loss · 2026-27")).toBeTruthy();
    expect(screen.queryByText("Home page")).toBeNull();
  });

  it("sends a sales role home once the user has loaded, without loading a report", async () => {
    show({ userId: "2", role: "Manager", email: "m@b.c" });
    expect(await screen.findByText("Home page")).toBeTruthy();
    expect(calls).toEqual([]);
  });
});

describe("Financial reports: Profit & loss", () => {
  it("opens on this financial year with From and To filled and shows the three figures", async () => {
    show();
    expect(await screen.findByText("Profit & loss · 2026-27")).toBeTruthy();
    expect(screen.getByText("Jul 1, 2026 – Jun 30, 2027")).toBeTruthy();
    expect(screen.getByText("Rs 10,125,000")).toBeTruthy();
    expect(screen.getByText("Rs 9,179,350")).toBeTruthy();
    expect(screen.getByText("Rs 945,650")).toBeTruthy();
    expect(screen.queryByText("Unit sales")).toBeNull();
    await waitFor(() => expect(reportCalls("profit-and-loss").some((call) => call.includes("from=2026-07-01&to=2027-06-30"))).toBe(true));
    expect(screen.getByRole("combobox", { name: /Period/ }).textContent).toContain("This financial year");
    expect(screen.queryByRole("button", { name: "Reset" })).toBeNull();
  });

  it("fills From and To from the Period dropdown and loads that range", async () => {
    show();
    await screen.findByText("Profit & loss · 2026-27");
    fireEvent.click(screen.getByRole("combobox", { name: /Period/ }));
    fireEvent.click(screen.getByRole("option", { name: "Last financial year" }));
    await waitFor(() => expect(reportCalls("profit-and-loss").some((call) => call.includes("from=2025-07-01&to=2026-06-30"))).toBe(true));
    expect(screen.getByRole("button", { name: "Reset" })).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Reset" }));
    await waitFor(() => expect(reportCalls("profit-and-loss").at(-1)).toContain("from=2026-07-01&to=2027-06-30"));
  });

  it("sends nothing while only one of From and To is filled", async () => {
    show();
    await screen.findByText("Profit & loss · 2026-27");
    await waitFor(() => expect(reportCalls("profit-and-loss").some((call) => call.includes("from=2026-07-01"))).toBe(true));
    const before = reportCalls("profit-and-loss").length;
    fireEvent.click(screen.getByRole("button", { name: /From/ }));
    fireEvent.click(screen.getByRole("button", { name: "Clear" }));
    expect(screen.getByRole("alert").textContent).toBe("Enter both a From and a To date, or clear them both.");
    await new Promise((resolve) => setTimeout(resolve, 400));
    expect(reportCalls("profit-and-loss").length).toBe(before);
  });

  it("names the project under the period when one is picked", async () => {
    show();
    await screen.findByText("Profit & loss · 2026-27");
    fireEvent.click(screen.getByRole("combobox", { name: /Project/ }));
    fireEvent.click(screen.getByRole("option", { name: "Deen Square" }));
    await waitFor(() => expect(reportCalls("profit-and-loss").some((call) => call.includes("projectId=1"))).toBe(true));
  });

  it("shows the server's message and a Try again button when the report fails", async () => {
    pnlStatus = 500;
    show();
    const alert = await screen.findByText("Profit and loss is unavailable.");
    expect(alert).toBeTruthy();
    pnlStatus = 200;
    fireEvent.click(screen.getByRole("button", { name: "Try again" }));
    expect(await screen.findByText("Profit & loss · 2026-27")).toBeTruthy();
  });

  it("tells the user when the financial year setting cannot be read", async () => {
    settingsStatus = 500;
    show();
    expect(await screen.findByText("Financial year setting unavailable — use a custom From/To range.")).toBeTruthy();
    expect(await screen.findByText("Profit & loss · 2026-27")).toBeTruthy();
  });
});

describe("Financial reports: Trial balance", () => {
  it("lists every account with a Total row that adds up and no paging", async () => {
    show();
    await screen.findByText("Profit & loss · 2026-27");
    openTab("Trial balance");
    expect(await screen.findByText("Closing balances as at Sep 29, 2026")).toBeTruthy();
    const table = within(screen.getByRole("table"));
    expect(table.getByText("Meezan Bank")).toBeTruthy();
    expect(table.getByText("Customer deposits")).toBeTruthy();
    const total = table.getByText("Total").closest("tr")!;
    expect(total.textContent).toContain("Rs 18,400,000Rs 18,400,000");
    expect(screen.getByText("Balanced")).toBeTruthy();
    expect(screen.queryByText(/Showing/)).toBeNull();
    expect(reportCalls("trial-balance").at(-1)).toContain("asAt=2026-09-29&monthsBack=0");
  });

  it("shows a red badge with the difference when the totals do not match", async () => {
    trialData = { ...trial, columnDebitTotals: [18400100], columnBalanced: [false] };
    show();
    await screen.findByText("Profit & loss · 2026-27");
    openTab("Trial balance");
    expect(await screen.findByText("Out of balance by Rs 100")).toBeTruthy();
  });

  it("asks for From and To in Date range mode and disables Export while the pair is wrong", async () => {
    show();
    await screen.findByText("Profit & loss · 2026-27");
    openTab("Trial balance");
    await screen.findByText("Closing balances as at Sep 29, 2026");
    fireEvent.click(screen.getByRole("combobox", { name: /Show/ }));
    fireEvent.click(screen.getByRole("option", { name: "Date range" }));
    await waitFor(() => expect(reportCalls("trial-balance").at(-1)).toContain("asAt=2026-09-29"));
    expect(screen.getByRole("button", { name: /From/ })).toBeTruthy();
    expect((screen.getByRole("button", { name: "Export to Excel" }) as HTMLButtonElement).disabled).toBe(false);

    fireEvent.click(screen.getByRole("button", { name: /From/ }));
    fireEvent.click(screen.getByRole("button", { name: "September 20, 2026" }));
    fireEvent.click(screen.getByRole("button", { name: /To/ }));
    fireEvent.click(screen.getByRole("button", { name: "September 10, 2026" }));
    expect(screen.getByRole("alert").textContent).toBe("From date cannot be after To date.");
    expect((screen.getByRole("button", { name: "Export to Excel" }) as HTMLButtonElement).disabled).toBe(true);
  });

  it("opens Account details, shows the server's error with Try again, then the ledger, and closes with Esc", async () => {
    detailsStatuses = [500];
    show();
    await screen.findByText("Profit & loss · 2026-27");
    openTab("Trial balance");
    await screen.findByText("Closing balances as at Sep 29, 2026");
    fireEvent.click(screen.getByRole("button", { name: "Details for Meezan Bank" }));

    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText("Account details · Ledger 1002")).toBeTruthy();
    expect(await within(dialog).findByText("The ledger could not be read.")).toBeTruthy();
    expect(calls.at(-1)).toContain("/api/Finance/trial-balance/details?accountKey=acct%3A1&from=2026-09-01&to=2026-09-29");

    fireEvent.click(within(dialog).getByRole("button", { name: "Try again" }));
    const ledger = within(await within(dialog).findByRole("table"));
    expect(ledger.getByText("Late payment surcharge")).toBeTruthy();
    expect(ledger.getByText("Sep 2, 2026 · MR-0031")).toBeTruthy();
    expect(ledger.getByText("Rs 12,030,000 Dr")).toBeTruthy();
    expect(within(dialog).getByText("Rs 12,000,000 Dr")).toBeTruthy();
    expect(within(dialog).getByText("Rs 18,400,000 Dr")).toBeTruthy();
    const total = ledger.getByText("Total").closest("tr")!;
    expect(total.textContent).toContain("Rs 7,282,660Rs 882,660");

    fireEvent.keyDown(document, { key: "Escape" });
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
  });

  it("opens Account details when a row is tapped on a phone", async () => {
    stubMedia(true);
    show();
    await screen.findByText("Profit & loss · 2026-27");
    openTab("Trial balance");
    await screen.findByText("Closing balances as at Sep 29, 2026");
    expect(screen.getByText("Total debit")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: /^Meezan Bank\s*Debit/ }));
    const dialog = await screen.findByRole("dialog");
    // The table and the phone cards are both in the DOM here; CSS shows one of them.
    expect((await within(dialog).findAllByText("Late payment surcharge")).length).toBeGreaterThan(0);
    expect(within(dialog).getByText("Total credit")).toBeTruthy();
  });
});

describe("Financial reports: Balance sheet", () => {
  it("shows the server's groups, the retained profit line, both total bars and the fixed-asset line", async () => {
    show();
    await screen.findByText("Profit & loss · 2026-27");
    openTab("Balance sheet");
    expect(await screen.findByText("As at Sep 29, 2026")).toBeTruthy();
    expect(screen.getByText("Current assets")).toBeTruthy();
    expect(screen.getByText("Total current assets")).toBeTruthy();
    expect(screen.getByText("Retained profit (per the ledger)")).toBeTruthy();
    expect(screen.getByText("Total assets")).toBeTruthy();
    expect(screen.getByText("Total liabilities & capital")).toBeTruthy();
    expect(screen.getByText("Rs 96,000 of fixed assets bought up to this date is charged in Profit & loss, but not in retained profit here.")).toBeTruthy();
    expect(screen.getByText("Balanced")).toBeTruthy();
    expect(screen.queryByText(/Action required/)).toBeNull();
  });

  it("names the window retained profit covers when it has a start date", async () => {
    balanceData = { ...balance, retainedProfitStart: "2026-08-15" };
    show();
    await screen.findByText("Profit & loss · 2026-27");
    openTab("Balance sheet");
    expect(await screen.findByText(/bought between Aug 15, 2026 and this date is charged/)).toBeTruthy();
  });

  it("hides the fixed-asset line when nothing is uncharged", async () => {
    balanceData = { ...balance, unpostedFixedAssetCharge: 0 };
    show();
    await screen.findByText("Profit & loss · 2026-27");
    openTab("Balance sheet");
    await screen.findByText("As at Sep 29, 2026");
    expect(screen.queryByText(/of fixed assets bought/)).toBeNull();
  });

  it("shows a red badge and notice, with the accounts to review, when it is out of balance", async () => {
    balanceData = { ...balance, isBalanced: false, imbalance: 500, unbalancedAccounts: ["Cash in office", "Meezan Bank"] };
    show();
    await screen.findByText("Profit & loss · 2026-27");
    openTab("Balance sheet");
    expect(await screen.findByText("Statement is out of balance by Rs 500.")).toBeTruthy();
    expect(screen.getByText("Review: Cash in office, Meezan Bank.")).toBeTruthy();
    expect(screen.getByText("Out of balance")).toBeTruthy();
    expect(screen.queryByText(/No difference row/)).toBeNull();
  });

  it("keeps Project when the tab changes", async () => {
    show();
    await screen.findByText("Profit & loss · 2026-27");
    fireEvent.click(screen.getByRole("combobox", { name: /Project/ }));
    fireEvent.click(screen.getByRole("option", { name: "Deen Square" }));
    openTab("Balance sheet");
    await waitFor(() => expect(reportCalls("balance-sheet").some((call) => call.includes("projectId=1"))).toBe(true));
  });
});

describe("Financial reports: Export to Excel", () => {
  it("exports what is on screen with the filters in use", async () => {
    const created: string[] = [];
    vi.stubGlobal("URL", Object.assign(URL, { createObjectURL: () => { created.push("blob"); return "blob:x"; }, revokeObjectURL: () => {} }));
    show();
    await screen.findByText("Profit & loss · 2026-27");
    await waitFor(() => expect(reportCalls("profit-and-loss").some((call) => call.includes("from=2026-07-01"))).toBe(true));
    fireEvent.click(screen.getByRole("button", { name: "Export to Excel" }));
    await waitFor(() => expect(created).toEqual(["blob"]));
    expect(calls.at(-1)).toBe("/api/Finance/profit-and-loss/export?from=2026-07-01&to=2027-06-30&format=xlsx");
  });

  it("shows the server's message in a toast when the export fails", async () => {
    exportStatus = 500;
    show();
    await screen.findByText("Profit & loss · 2026-27");
    fireEvent.click(screen.getByRole("button", { name: "Export to Excel" }));
    expect(await screen.findByText("The export service is down.")).toBeTruthy();
  });
});
