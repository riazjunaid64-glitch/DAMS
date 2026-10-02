// @vitest-environment happy-dom
import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router-dom";
import { ToastProvider } from "../components/ui/Toast.tsx";
import { ProjectsContext } from "../contexts/projectsContextValue.ts";
import type { User } from "../App.tsx";
import type { ProjectFromApi } from "../utils/parseProject.ts";
import { formatMoney } from "../features/finance/home/format.ts";
import FinanceDashboardPage from "./FinanceDashboardPage.tsx";

const calls: string[] = [];

vi.mock("../api/api.ts", () => ({
  api: async (url: string, init?: RequestInit) => {
    calls.push(`${init?.method ?? "GET"} ${url}`);
    if (url.includes("/dashboard") && dashboardGate) await dashboardGate;
    const { status = 200, body } = respond(url, init);
    return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
  },
}));

const summary = {
  totalRevenue: 12450000,
  customerDepositsBalance: 48750000,
  totalExpenses: 3872500,
  netProfit: 1000000,
  accountFilterApplied: false,
  whtWithheld: 0,
  totalAssetPurchases: 0,
  outstandingAmount: 0,
  overdueAmount: 1240000,
  accountOpeningBalance: null,
  accountCurrentBalance: null,
  accountNetMovement: null,
};

const revenueRow = {
  date: "2026-09-29",
  projectId: 1,
  projectName: "Deen Square",
  revenueType: "Transfer charges",
  revenueCategoryId: 2,
  amount: 75000,
  source: "Manual Revenue",
  reference: null,
  description: null,
  manualRevenueId: 9,
  rowId: "Manual Revenue:9",
  financeAccountId: 3,
  financeAccountName: "HBL Current",
  accountHolderName: "Adeel Satti",
  concurrencyToken: "tok",
  attachment: null,
};

let rows: unknown[] = [revenueRow];
let payableCalls = 0;
let categoryCalls = 0;
let categoryStatus = 200;
let rowsCalls = 0;
let rowsFailAfter = Number.POSITIVE_INFINITY;
let listTotal: number | null = null;
let accountOptionCalls = 0;
let accountOptionsStatus = 200;
let dashboardGate: Promise<void> | null = null;

function respond(url: string, init?: RequestInit): { status?: number; body: unknown } {
  if (url.includes("/wht/settings")) {
    return { body: { financialYearStartMonth: 7, whtRatesConfirmedAt: null, whtRatesConfirmedByName: null, currentFinancialYear: "2026-27", concurrencyToken: "t" } };
  }
  if (url.includes("/payable-summary")) {
    payableCalls += 1;
    return { body: { outstandingPayable: payableCalls === 1 ? 184350 : 90000, withheldInPeriod: 0, depositedInPeriod: 0, openingPayable: 0, totalWithheldAllTime: 0, totalDepositedAllTime: 0, paymentCount: 0, vendorCount: 0, bySection: [] } };
  }
  if (url.includes("revenue-categories")) {
    categoryCalls += 1;
    return { status: categoryStatus, body: categoryStatus === 200 ? [] : {} };
  }
  if (/\/expenses\/\d+/.test(url) && (init?.method ?? "GET") === "GET") {
    return { body: { id: 7, concurrencyToken: "tok" } };
  }
  if (url.includes("/accounts/options")) {
    if (url.includes("type=7")) return { body: [] };
    accountOptionCalls += 1;
    if (accountOptionsStatus !== 200) return { status: accountOptionsStatus, body: {} };
    return { body: [{ id: 3, name: "Meezan Bank", type: 1, accountHolderName: "Adeel Satti", isActive: true }] };
  }
  if (url.includes("/dashboard")) {
    const account = new URL(url, "http://local").searchParams.get("account");
    const namedAccount = Boolean(account) && account !== "unassigned";
    return {
      body: {
        summary: {
          ...summary,
          accountFilterApplied: Boolean(account),
          // A named account has a cash movement. Unassigned is not an account, so the server
          // leaves the figure null — the card must not turn that into Rs 0.
          accountNetMovement: namedAccount ? 12500 : null,
          accountCurrentBalance: namedAccount ? 20000 : null,
          accountOpeningBalance: namedAccount ? 7500 : null,
        },
        trend: [],
        distribution: [],
      },
    };
  }
  if (url.includes("/rows")) {
    rowsCalls += 1;
    if (rowsCalls > rowsFailAfter) return { status: 500, body: {} };
    return { body: { items: rows, hasMore: false, totalCount: listTotal ?? rows.length } };
  }
  if (init?.method === "DELETE") return { body: { message: "Deleted" } };
  return { body: {} };
}

const deenSquare = {
  id: 1,
  projectName: "Deen Square",
  location: "",
  startingDate: "",
  status: 1,
  createdAt: "",
  totalUnits: 0,
  availableUnits: 0,
  bookedUnits: 0,
  soldUnits: 0,
  floorCount: 0,
} as ProjectFromApi;

function show(user: User | null = { userId: "1", role: "Admin", email: "a@b.c" }, projects: ProjectFromApi[] = []) {
  return render(
    <MemoryRouter>
      <ProjectsContext.Provider value={{ projects, loading: false, error: null, reload: async () => {} }}>
        <ToastProvider>
          <FinanceDashboardPage user={user} />
        </ToastProvider>
      </ProjectsContext.Provider>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  calls.length = 0;
  rows = [revenueRow];
  payableCalls = 0;
  categoryCalls = 0;
  categoryStatus = 200;
  rowsCalls = 0;
  rowsFailAfter = Number.POSITIVE_INFINITY;
  listTotal = null;
  accountOptionCalls = 0;
  accountOptionsStatus = 200;
  dashboardGate = null;
  vi.stubGlobal("matchMedia", (query: string) => ({
    matches: false,
    media: query,
    addEventListener: () => {},
    removeEventListener: () => {},
  }));
  Element.prototype.scrollIntoView = () => {};
});

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("Finance home", () => {
  it("switches the table when a card is chosen", async () => {
    show();
    expect(await screen.findByRole("heading", { name: "Revenue" })).toBeTruthy();
    fireEvent.click(await screen.findByRole("button", { name: /Total expenses/ }));
    expect(await screen.findByRole("heading", { name: "Total expenses" })).toBeTruthy();
  });

  it("hides customer deposits and overdue when an account is chosen", async () => {
    show();
    await screen.findByRole("button", { name: /Customer deposits/ });
    fireEvent.click(screen.getByRole("combobox", { name: /Account/ }));
    fireEvent.click(await screen.findByRole("option", { name: "Meezan Bank" }));
    expect(await screen.findByRole("button", { name: /Revenue on this account/ })).toBeTruthy();
    expect(screen.getByText("Account net movement")).toBeTruthy();
    expect(screen.queryByRole("button", { name: /Customer deposits/ })).toBeNull();
    expect(screen.queryByText("At period end")).toBeNull();
  });

  it("does not turn a missing Unassigned net movement into Rs 0", async () => {
    show();
    await screen.findByRole("button", { name: /Customer deposits/ });
    fireEvent.click(screen.getByRole("combobox", { name: /Account/ }));
    fireEvent.click(await screen.findByRole("option", { name: "Unassigned" }));
    expect(await screen.findByRole("button", { name: /Revenue on this account/ })).toBeTruthy();
    const unassigned = screen.getByText("Account net movement").closest("div");
    expect(unassigned?.textContent).toContain("—");
    expect(unassigned?.textContent).not.toContain("Rs 0");

    fireEvent.click(screen.getByRole("combobox", { name: /Account/ }));
    fireEvent.click(await screen.findByRole("option", { name: "Meezan Bank" }));
    expect(await screen.findByRole("button", { name: /Revenue on this account/ })).toBeTruthy();
    expect(screen.getByText("Account net movement").closest("div")?.textContent).toContain("Rs 12,500");
  });

  it("reloads the FBR notice after an expense that can change withholding tax is deleted", async () => {
    rows = [{
      date: "2026-09-26",
      projectName: "Floria Heights",
      label: "Salaries",
      kind: "cost",
      amount: 1240000,
      expenseId: 7,
      source: "expense",
      sourceId: 7,
      attachment: null,
    }];
    show();
    expect(await screen.findByText(`Tax to deposit to FBR: ${formatMoney(184350)}`)).toBeTruthy();
    fireEvent.click(await screen.findByRole("button", { name: /Total expenses/ }));
    expect(await screen.findByRole("heading", { name: "Total expenses" })).toBeTruthy();
    fireEvent.click(screen.getAllByRole("button", { name: "Delete" })[0]);
    const dialog = await screen.findByRole("dialog", { name: "Delete this expense?" });
    fireEvent.click(within(dialog).getByRole("button", { name: "Delete" }));
    expect(await screen.findByText(`Tax to deposit to FBR: ${formatMoney(90000)}`)).toBeTruthy();
  });

  it("shows a lookup failure once and retries only when asked", async () => {
    categoryStatus = 500;
    show();
    fireEvent.click(await screen.findByRole("button", { name: "Add revenue" }));
    const dialog = await screen.findByRole("dialog", { name: "Add manual revenue" });
    expect(await within(dialog).findByText(/Revenue categories could not be loaded/)).toBeTruthy();
    expect(categoryCalls).toBe(1);
    fireEvent.click(within(dialog).getByRole("combobox", { name: /Received in account/ }));
    fireEvent.click(await screen.findByRole("option", { name: /Meezan Bank/ }));
    expect(categoryCalls).toBe(1);
    fireEvent.click(within(dialog).getByRole("button", { name: "Try again" }));
    await within(dialog).findByText(/Revenue categories could not be loaded/);
    expect(categoryCalls).toBe(2);
  });

  it("keeps the current rows and their columns when the next card fails to load", async () => {
    rowsFailAfter = 1;
    show();
    expect(await screen.findByRole("heading", { name: "Revenue" })).toBeTruthy();
    expect(screen.getAllByText("Deen Square").length).toBeGreaterThan(0);
    fireEvent.click(await screen.findByRole("button", { name: /Total expenses/ }));
    expect(await screen.findByText(/Unable to load rows/)).toBeTruthy();
    expect(screen.getByRole("heading", { name: "Revenue" })).toBeTruthy();
    expect(screen.queryByRole("heading", { name: "Total expenses" })).toBeNull();
    expect(screen.getAllByText("Deen Square").length).toBeGreaterThan(0);
    expect(screen.getByRole("button", { name: "Try again" })).toBeTruthy();
  });

  it("keeps page 1 pagination when the next page fails", async () => {
    listTotal = 40;
    rowsFailAfter = 1;
    show();
    const nav = await screen.findByRole("navigation", { name: "Pagination" });
    expect(nav.textContent).toContain("1–20");
    fireEvent.click(within(nav).getByRole("button", { name: "Page 2" }));
    expect(await screen.findByText(/Unable to load rows/)).toBeTruthy();
    expect(nav.textContent).toContain("1–20");
    expect(nav.textContent).not.toContain("21–40");
    expect(within(nav).getByRole("button", { name: "Page 1" }).getAttribute("aria-current")).toBe("page");
    expect(within(nav).getByRole("button", { name: "Page 2" }).getAttribute("aria-current")).toBeNull();
    expect(screen.getAllByText("Deen Square").length).toBeGreaterThan(0);
    expect(screen.getByRole("button", { name: "Try again" })).toBeTruthy();
  });

  it("shows an account lookup failure once and retries only when asked", async () => {
    accountOptionsStatus = 500;
    show();
    expect(await screen.findByText(/Accounts could not be loaded/)).toBeTruthy();
    const first = accountOptionCalls;
    expect(first).toBeGreaterThan(0);
    fireEvent.click(await screen.findByRole("button", { name: "Add revenue" }));
    const dialog = await screen.findByRole("dialog", { name: "Add manual revenue" });
    expect(within(dialog).getByText(/Accounts could not be loaded/)).toBeTruthy();
    fireEvent.change(within(dialog).getByLabelText("Description (optional)"), { target: { value: "Site note" } });
    expect(accountOptionCalls).toBe(first);
    fireEvent.click(screen.getAllByRole("button", { name: "Try again" })[0]);
    expect((await screen.findAllByText(/Accounts could not be loaded/)).length).toBeGreaterThan(0);
    expect(accountOptionCalls).toBe(first + 2);
  });

  it("shows loading totals as soon as the project, period, or reset changes", async () => {
    let releaseDashboard: (() => void) | null = null;
    const holdDashboard = () => {
      dashboardGate = new Promise((resolve) => { releaseDashboard = resolve; });
    };
    const release = async () => {
      const done = releaseDashboard;
      dashboardGate = null;
      releaseDashboard = null;
      done?.();
      expect(await screen.findByRole("button", { name: /Total revenue/ })).toBeTruthy();
    };
    const expectTotalsLoading = () => {
      const card = screen.getByText("Total revenue").closest("[aria-busy='true']");
      expect(card).toBeTruthy();
      expect(card?.textContent ?? "").not.toMatch(/12,?450,?000|1,?24,?50,?000/);
    };

    show({ userId: "1", role: "Admin", email: "a@b.c" }, [deenSquare]);
    expect(await screen.findByRole("button", { name: /Total revenue/ })).toBeTruthy();

    holdDashboard();
    fireEvent.click(screen.getByRole("combobox", { name: /Project/ }));
    fireEvent.click(await screen.findByRole("option", { name: "Deen Square" }));
    expectTotalsLoading();
    await release();

    holdDashboard();
    fireEvent.click(screen.getByRole("combobox", { name: /Period/ }));
    fireEvent.click(await screen.findByRole("option", { name: "Last month" }));
    expectTotalsLoading();
    await release();

    holdDashboard();
    fireEvent.click(screen.getByRole("button", { name: "Reset" }));
    expectTotalsLoading();
    await release();
  });

  it("shows loading totals as soon as the account filter is cleared", async () => {
    show();
    await screen.findByRole("button", { name: /Customer deposits/ });
    fireEvent.click(screen.getByRole("combobox", { name: /Account/ }));
    fireEvent.click(await screen.findByRole("option", { name: "Meezan Bank" }));
    expect(await screen.findByRole("button", { name: /Revenue on this account/ })).toBeTruthy();

    dashboardGate = new Promise(() => {});
    fireEvent.click(screen.getByRole("button", { name: "Clear account filter" }));
    const card = screen.getByText("Total revenue").closest("[aria-busy='true']");
    expect(card).toBeTruthy();
    expect(card?.textContent ?? "").not.toMatch(/12,?450,?000|1,?24,?50,?000/);
    expect(screen.queryByText("Revenue on this account")).toBeNull();
  });

  it("shows the empty state for the selected filters", async () => {
    rows = [];
    show();
    expect(await screen.findByText("No revenue for the selected filters.")).toBeTruthy();
  });

  it("asks before deleting a manual revenue entry", async () => {
    show();
    await screen.findByRole("heading", { name: "Revenue" });
    fireEvent.click(screen.getAllByRole("button", { name: "Delete" })[0]);
    const dialog = await screen.findByRole("dialog", { name: "Delete this manual revenue entry?" });
    fireEvent.click(within(dialog).getByRole("button", { name: "Delete" }));
    expect(await screen.findByText("Deleted")).toBeTruthy();
    expect(calls.some((call) => call.startsWith("DELETE /api/Finance/revenue/9"))).toBe(true);
  });

  it("opens the phone more sheet with the finance links", async () => {
    vi.stubGlobal("matchMedia", (query: string) => ({
      matches: true,
      media: query,
      addEventListener: () => {},
      removeEventListener: () => {},
    }));
    show();
    fireEvent.click(await screen.findByRole("button", { name: "More actions" }));
    const sheet = await screen.findByRole("dialog", { name: "More actions" });
    expect(within(sheet).getByRole("button", { name: "Add revenue" })).toBeTruthy();
    expect(within(sheet).getByRole("link", { name: "Financial reports" })).toBeTruthy();
    expect(within(sheet).getByRole("link", { name: "Tax to FBR" })).toBeTruthy();
  });

  it("waits for the signed-in user and tells someone without access", () => {
    show(null);
    expect(screen.queryByRole("heading", { name: "Finance" })).toBeNull();
    expect(calls).toEqual([]);
    cleanup();
    show({ userId: "2", role: "Employee", email: "e@b.c" });
    expect(screen.getByText("You don't have access to Finance.")).toBeTruthy();
    expect(calls).toEqual([]);
  });
});
