import { useState } from "react";
import { Button, DataTable, EmptyState, IconCheck, IconChevronRight, StatusBadge, useIsPhone, type DataTableColumn } from "../../../components/ui";
import { formatDate, formatMoney } from "../home/format.ts";
import type { AppliedTrialBalanceFilters, TrialBalanceReport as TrialBalanceData, TrialBalanceRow } from "../trialBalance.ts";
import { AccountDetailsDialog } from "./AccountDetailsDialog.tsx";
import { moneyOrDash } from "./format.ts";
import { ReportCard } from "./ReportCard.tsx";

export type LoadedTrial = { report: TrialBalanceData; filters: AppliedTrialBalanceFilters };

type Line = { kind: "row"; row: TrialBalanceRow } | { kind: "total" };

/**
 * The whole list is shown, never paged: the Total row has to add up to what is on screen. The last
 * column of the report is the one shown (month history is never asked for).
 */
export function TrialBalanceReport({ loaded, loading }: { loaded: LoadedTrial | null; loading: boolean }) {
  const isPhone = useIsPhone();
  const [selected, setSelected] = useState<TrialBalanceRow | null>(null);
  const report = loaded?.report;
  const columnIndex = Math.max(0, (report?.columnDates.length ?? 1) - 1);
  const totalDebit = report?.columnDebitTotals[columnIndex] ?? 0;
  const totalCredit = report?.columnCreditTotals[columnIndex] ?? 0;
  const difference = Math.abs(totalDebit - totalCredit);
  const balanced = report ? report.columnBalanced[columnIndex] ?? difference < 0.005 : true;
  const accounts = report?.rows ?? [];
  const debitOf = (row: TrialBalanceRow) => row.debitBalances[columnIndex] ?? 0;
  const creditOf = (row: TrialBalanceRow) => row.creditBalances[columnIndex] ?? 0;

  // On a phone the total is a summary line under the cards; on desktop it is the table's last row.
  const lines: Line[] = [
    ...accounts.map((row): Line => ({ kind: "row", row })),
    ...(accounts.length && !isPhone ? [{ kind: "total" } as Line] : []),
  ];

  const columns: DataTableColumn<Line>[] = [
    { key: "account", header: "Account name", render: (line) => (line.kind === "total" ? "Total" : <span className="font-bold">{line.row.accountName}</span>) },
    { key: "debit", header: "Debit", align: "right", className: "tabular-nums", render: (line) => (line.kind === "total" ? formatMoney(totalDebit) : moneyOrDash(debitOf(line.row))) },
    { key: "credit", header: "Credit", align: "right", className: "tabular-nums", render: (line) => (line.kind === "total" ? formatMoney(totalCredit) : moneyOrDash(creditOf(line.row))) },
    {
      key: "details",
      header: <span className="sr-only">Details</span>,
      align: "right",
      render: (line) => line.kind === "total" ? null : (
        <Button variant="outline" size="sm" aria-label={`Details for ${line.row.accountName}`} onClick={() => setSelected(line.row)}>Details</Button>
      ),
    },
  ];

  const phoneCard = (line: Line) => {
    if (line.kind === "total") return null;
    const { row } = line;
    return (
      <button
        type="button"
        onClick={() => setSelected(row)}
        className="flex min-h-14 w-full cursor-pointer items-center gap-3 rounded-card border border-line bg-card px-4 py-3 text-left font-ui focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary"
      >
        <span className="min-w-0 flex-1">
          <span className="block text-sm font-extrabold text-ink">{row.accountName}</span>
          <span className="mt-0.5 block text-small text-ink-muted">
            Debit <strong className="font-extrabold text-ink">{moneyOrDash(debitOf(row))}</strong>
            <span className="ml-3">Credit <strong className="font-extrabold text-ink">{moneyOrDash(creditOf(row))}</strong></span>
          </span>
        </span>
        <span aria-hidden="true" className="shrink-0 text-ink-faint"><IconChevronRight size={18} /></span>
      </button>
    );
  };

  return (
    <>
      <ReportCard
        title="Trial balance"
        subtitle={loaded ? `Closing balances as at ${formatDate(loaded.filters.to)}` : undefined}
        badge={report && (
          <StatusBadge status="" tone={balanced ? "green" : "red"}>
            {balanced ? <><IconCheck size={13} className="mr-1" />Balanced</> : `Out of balance by ${formatMoney(difference)}`}
          </StatusBadge>
        )}
      >
        <DataTable
          columns={columns}
          rows={lines}
          rowKey={(line) => (line.kind === "total" ? "total" : line.row.accountKey)}
          loading={loading}
          loadingCount={10}
          className="max-md:p-3"
          minWidth={560}
          caption={loaded ? `Trial balance account summary as at ${formatDate(loaded.filters.to)}` : "Trial balance account summary"}
          empty={<EmptyState title="No account balances were found for these filters." className="m-4" />}
          phoneCard={phoneCard}
        />
        {isPhone && accounts.length > 0 && (
          <dl className="m-0 grid grid-cols-2 gap-3 border-t border-line-soft bg-table-head px-4 py-3">
            <div>
              <dt className="text-label font-bold uppercase tracking-[0.4px] text-ink-muted">Total debit</dt>
              <dd className="m-0 mt-0.5 whitespace-nowrap text-body font-extrabold tabular-nums text-ink">{formatMoney(totalDebit)}</dd>
            </div>
            <div>
              <dt className="text-label font-bold uppercase tracking-[0.4px] text-ink-muted">Total credit</dt>
              <dd className="m-0 mt-0.5 whitespace-nowrap text-body font-extrabold tabular-nums text-ink">{formatMoney(totalCredit)}</dd>
            </div>
          </dl>
        )}
      </ReportCard>
      {selected && loaded && <AccountDetailsDialog account={selected} filters={loaded.filters} onClose={() => setSelected(null)} />}
    </>
  );
}
