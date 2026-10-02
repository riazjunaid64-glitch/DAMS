import type { ReactNode } from "react";
import { Button, Notice, StatCard } from "../../../components/ui";
import { formatMoney } from "./format.ts";
import { TAX_TO_FBR_PATH } from "./paths.ts";
import type { FinanceView, FinancialSummary } from "./types.ts";

/** A missing movement is not zero. Unassigned has no account to sum, so the server leaves it null. */
function movementFigure(value: number | null | undefined): ReactNode {
  if (value == null) return "—";
  return <span className={value < 0 ? "text-danger" : undefined}>{formatMoney(value)}</span>;
}

export function FinanceCards({
  summary,
  loading,
  error,
  accountSelected,
  view,
  onView,
}: {
  summary: FinancialSummary | null;
  loading: boolean;
  error: boolean;
  accountSelected: boolean;
  view: FinanceView;
  onView: (view: FinanceView) => void;
}) {
  const state = loading ? "loading" : error || !summary ? "error" : "ready";
  const money = (value: number | null | undefined) => formatMoney(value ?? 0);
  const cards = accountSelected
    ? [
        { key: "revenue" as const, label: "Revenue on this account", value: money(summary?.totalRevenue), tone: "green" as const, note: undefined, clickable: true },
        { key: "totalExpenses" as const, label: "Costs on this account", value: money(summary?.totalExpenses), tone: "grey" as const, note: undefined, clickable: true },
        { key: null, label: "Account net movement", value: movementFigure(summary?.accountNetMovement), tone: "grey" as const, note: undefined, clickable: false },
      ]
    : [
        { key: "revenue" as const, label: "Total revenue", value: money(summary?.totalRevenue), tone: "green" as const, note: undefined, clickable: true },
        { key: "totalExpenses" as const, label: "Total expenses", value: money(summary?.totalExpenses), tone: "grey" as const, note: undefined, clickable: true },
        { key: "customerDeposits" as const, label: "Customer deposits", value: money(summary?.customerDepositsBalance), tone: "blue" as const, note: "At period end", clickable: true },
        { key: "overdue" as const, label: "Overdue", value: money(summary?.overdueAmount), tone: "red" as const, note: "As of today", clickable: true },
      ];

  return (
    <div className={`grid grid-cols-2 gap-2.5 md:gap-4 ${accountSelected ? "md:grid-cols-3" : "md:grid-cols-4"}`}>
      {cards.map((card) => (
        <StatCard
          key={card.label}
          label={card.label}
          value={card.value}
          tone={card.tone}
          note={card.note}
          state={state}
          selected={card.key != null && view === card.key}
          onClick={card.clickable && card.key ? () => onView(card.key!) : undefined}
        />
      ))}
    </div>
  );
}

export function FinanceNotices({
  summaryError,
  onRetryTotals,
  payable,
  payableError = null,
  onRetryPayable,
  accountSelected,
  onClearAccount,
  summary,
  summaryLoading = false,
  showBalance,
  datesSet,
}: {
  summaryError: string | null;
  onRetryTotals: () => void;
  payable: number | null;
  /** Set when the payable summary failed. A successful zero stays null here and hides the FBR notice. */
  payableError?: string | null;
  onRetryPayable?: () => void;
  accountSelected: boolean;
  onClearAccount: () => void;
  summary: FinancialSummary | null;
  /** While a new account's summary is loading, the balance line must not keep the previous account. */
  summaryLoading?: boolean;
  showBalance: boolean;
  datesSet: boolean;
}) {
  const balance = summary?.accountCurrentBalance;
  const balanceVisible = showBalance && (summaryLoading || (summary != null && balance != null));
  const balanceState = summaryLoading ? "loading" : "ready";
  return (
    <div className="flex flex-col gap-3">
      {summaryError && (
        <Notice
          tone="red"
          role="alert"
          title={summaryError}
          action={<Button variant="outline" onClick={onRetryTotals}>Try again</Button>}
        />
      )}
      {accountSelected && (
        <Notice
          tone="gold"
          title="Account filter on: entries on this account only. Sales move no cash, so they are on no account."
          action={<Button variant="outline" onClick={onClearAccount}>Clear account filter</Button>}
        />
      )}
      {balanceVisible && (
        <div className="grid grid-cols-2 gap-2.5 md:grid-cols-3 md:gap-4">
          <StatCard
            label={datesSet ? "Current balance (to period end)" : "Current balance"}
            state={balanceState}
            value={balance == null ? "—" : <span className={balance < 0 ? "text-danger" : undefined}>{formatMoney(balance)}</span>}
          />
          <StatCard label="Opening balance" state={balanceState} value={formatMoney(summary?.accountOpeningBalance ?? 0)} />
          <StatCard
            label={datesSet ? "Net movement (period)" : "Net movement"}
            state={balanceState}
            value={movementFigure(summary?.accountNetMovement)}
          />
        </div>
      )}
      {payableError && (
        <Notice
          tone="red"
          role="alert"
          title={payableError}
          action={onRetryPayable ? <Button variant="outline" onClick={onRetryPayable}>Try again</Button> : undefined}
        />
      )}
      {payable != null && payable > 0 && (
        <Notice
          tone="gold"
          title={`Tax to deposit to FBR: ${formatMoney(payable)}`}
          action={<Button variant="outline" to={TAX_TO_FBR_PATH}>View</Button>}
        />
      )}
    </div>
  );
}
