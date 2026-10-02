// @vitest-environment happy-dom
import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, expect, it } from "vitest";
import { MemoryRouter } from "react-router-dom";
import { FinanceNotices } from "./summary.tsx";
import type { FinancialSummary } from "./types.ts";

const summary: FinancialSummary = {
  totalRevenue: 1,
  customerDepositsBalance: 0,
  totalExpenses: 1,
  netProfit: null,
  accountFilterApplied: true,
  whtWithheld: 0,
  totalAssetPurchases: 0,
  outstandingAmount: 0,
  overdueAmount: 0,
  accountOpeningBalance: 7500,
  accountCurrentBalance: 20000,
  accountNetMovement: 12500,
};

afterEach(() => cleanup());

function notices(summaryLoading: boolean) {
  return (
    <MemoryRouter>
      <FinanceNotices
        summaryError={null}
        onRetryTotals={() => {}}
        payable={null}
        accountSelected
        onClearAccount={() => {}}
        summary={summary}
        summaryLoading={summaryLoading}
        showBalance
        datesSet={false}
      />
    </MemoryRouter>
  );
}

it("does not keep the previous account's balances while the next summary loads", () => {
  const view = render(notices(true));
  expect(screen.getByText("Current balance").closest("[aria-busy='true']")).toBeTruthy();
  expect(screen.getByText("Opening balance").closest("[aria-busy='true']")).toBeTruthy();
  expect(screen.getByText("Net movement").closest("[aria-busy='true']")).toBeTruthy();
  expect(screen.queryByText(/20,?000/)).toBeNull();
  expect(screen.queryByText(/7,?500/)).toBeNull();
  expect(screen.queryByText(/12,?500/)).toBeNull();

  view.rerender(notices(false));
  expect(screen.getByText(/20,?000/)).toBeTruthy();
  expect(screen.getByText(/7,?500/)).toBeTruthy();
  expect(screen.getByText(/12,?500/)).toBeTruthy();
});

it("keeps the account filter notice when the summary is missing", () => {
  render(
    <MemoryRouter>
      <FinanceNotices
        summaryError="The finance totals could not be loaded, so the figures below are unavailable."
        onRetryTotals={() => {}}
        payable={null}
        accountSelected
        onClearAccount={() => {}}
        summary={null}
        summaryLoading={false}
        showBalance
        datesSet={false}
      />
    </MemoryRouter>,
  );
  expect(screen.getByText(/Account filter on/)).toBeTruthy();
  expect(screen.getByRole("button", { name: "Clear account filter" })).toBeTruthy();
  expect(screen.queryByText("Current balance")).toBeNull();
});
