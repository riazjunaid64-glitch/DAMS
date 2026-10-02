// @vitest-environment happy-dom
import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router-dom";
import { ToastProvider } from "../components/ui/Toast.tsx";
import type { User } from "../App.tsx";
import FinanceDashboardPage from "./FinanceDashboardPage.tsx";

const calls: string[] = [];

vi.mock("../api/api.ts", () => ({
  api: async (url: string, init?: RequestInit) => {
    calls.push(`${init?.method ?? "GET"} ${url}`);
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
  financeAccountId: 3,
  financeAccountName: "HBL Current",
  accountHolderName: "Adeel Satti",
  concurrencyToken: "tok",
  attachment: null,
};

let rows: unknown[] = [revenueRow];

function respond(url: string, init?: RequestInit): { status?: number; body: unknown } {
  if (url.includes("/wht/settings")) {
    return { body: { financialYearStartMonth: 7, whtRatesConfirmedAt: null, whtRatesConfirmedByName: null, currentFinancialYear: "2026-27", concurrencyToken: "t" } };
  }
  if (url.includes("/payable-summary")) {
    return { body: { outstandingPayable: 184350, withheldInPeriod: 0, depositedInPeriod: 0, openingPayable: 0, totalWithheldAllTime: 0, totalDepositedAllTime: 0, paymentCount: 0, vendorCount: 0, bySection: [] } };
  }
  if (url.includes("/accounts/options")) {
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
  if (url.includes("/rows")) return { body: { items: rows, hasMore: false, totalCount: rows.length } };
  if (init?.method === "DELETE") return { body: { message: "Deleted" } };
  return { body: {} };
}

function show(user: User | null = { userId: "1", role: "Admin", email: "a@b.c" }) {
  return render(
    <MemoryRouter>
      <ToastProvider>
        <FinanceDashboardPage user={user} />
      </ToastProvider>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  calls.length = 0;
  rows = [revenueRow];
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
    expect(screen.getByRole("heading", { name: "Total expenses" })).toBeTruthy();
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
