// @vitest-environment happy-dom
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ToastProvider } from "../components/ui/Toast.tsx";
import FinanceDashboardPage from "./FinanceDashboardPage.tsx";

let rows: unknown[] = [];
let summaryAccount = false;

vi.mock("../api/api.ts", () => ({
  api: async (url: string) => {
    if (url.includes("/wht/settings")) return json({ financialYearStartMonth: 7 });
    if (url.includes("/wht/payable-summary")) return json({ outstandingPayable: 184350 });
    if (url.includes("/accounts/options")) return json([]);
    if (url.includes("/dashboard")) {
      summaryAccount = url.includes("account=");
      return json({
        summary: {
          totalRevenue: 12450000,
          totalExpenses: 850000,
          customerDepositsBalance: summaryAccount ? 0 : 300000,
          overdueAmount: summaryAccount ? 0 : 12000,
          netProfit: summaryAccount ? null : 1,
          accountFilterApplied: summaryAccount,
          accountOpeningBalance: 10,
          accountCurrentBalance: 20,
          accountNetMovement: -5,
        },
        trend: [],
        distribution: [],
      });
    }
    if (url.includes("/rows")) return json({ items: rows, hasMore: false, totalCount: rows.length });
    if (url.includes("method=DELETE") || url.endsWith("DELETE")) return json({});
    return json({});
  },
}));

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

const admin = { userId: 1, role: "Admin", email: "a@b.c", fullName: "A" } as never;

function show(user: unknown = admin, phone = false) {
  vi.stubGlobal("matchMedia", (query: string) => ({
    matches: phone && String(query).includes("max-width"),
    media: query,
    addEventListener: () => {},
    removeEventListener: () => {},
  }));
  return render(
    <MemoryRouter>
      <ToastProvider>
        <FinanceDashboardPage user={user as never} />
      </ToastProvider>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  rows = [];
  summaryAccount = false;
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("Finance home", () => {
  it("waits for the signed-in user instead of sending them away", () => {
    show(null);
    expect(screen.queryByText("Finance")).toBeNull();
    expect(screen.queryByText("You don't have access to Finance.")).toBeNull();
  });

  it("tells someone without finance access, and does not offer the page", () => {
    show({ userId: 2, role: "Employee", email: "e@b.c", fullName: "E" });
    expect(screen.getByText("You don't have access to Finance.")).toBeTruthy();
    expect(screen.queryByText("Add expense")).toBeNull();
  });

  it("shows four cards, the tax notice and the empty revenue list", async () => {
    show();
    expect(await screen.findByText("Rs 12,450,000")).toBeTruthy();
    expect(screen.getByText("Total revenue")).toBeTruthy();
    expect(screen.getByText("Total expenses")).toBeTruthy();
    expect(screen.getByText("Customer deposits")).toBeTruthy();
    expect(screen.getByText("Overdue")).toBeTruthy();
    expect(screen.getByText(/Tax to deposit to FBR/)).toBeTruthy();
    expect(await screen.findByText("No revenue for the selected filters.")).toBeTruthy();
  });

  it("switches the table when a card is chosen", async () => {
    show();
    await screen.findByText("No revenue for the selected filters.");
    fireEvent.click(screen.getByRole("button", { name: /Total expenses/ }));
    expect(await screen.findByText("No costs for the selected filters.")).toBeTruthy();
  });

  it("asks before deleting a manual revenue row", async () => {
    rows = [{
      date: "2026-09-29", projectName: "General", revenueType: "Other", revenueCategoryId: 1,
      amount: 1000, source: "Manual Revenue", reference: null, description: null, manualRevenueId: 4,
      financeAccountId: 1, financeAccountName: "Cash", accountHolderName: "Adeel", concurrencyToken: "abc",
      attachment: null, projectId: null,
    }];
    show();
    await screen.findAllByText("Other");
    fireEvent.click(screen.getAllByRole("button", { name: "Delete" })[0]!);
    expect(await screen.findByText("Delete this manual revenue entry?")).toBeTruthy();
  });

  it("opens add revenue from the phone more sheet", async () => {
    show(admin, true);
    await screen.findByText("Total revenue");
    fireEvent.click(screen.getByRole("button", { name: "More actions" }));
    fireEvent.click(await screen.findByRole("button", { name: "Add revenue" }));
    expect(await screen.findByText("Add manual revenue")).toBeTruthy();
  });
});
