// @vitest-environment happy-dom
import { render, screen } from "@testing-library/react";
import { expect, it } from "vitest";
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
