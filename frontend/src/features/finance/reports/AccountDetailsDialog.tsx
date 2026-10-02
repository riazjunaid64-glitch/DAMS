import { useEffect, useState } from "react";
import { api } from "../../../api/api.ts";
import { Button, DataTable, EmptyState, KeyValueGrid, Modal, Notice, useIsPhone, type DataTableColumn } from "../../../components/ui";
import { formatDate, formatMoney } from "../home/format.ts";
import { trialDetailsParams, type AppliedTrialBalanceFilters, type TrialBalanceDetailRow, type TrialBalanceDetails, type TrialBalanceRow } from "../trialBalance.ts";
import { formatBalance, moneyOrDash } from "./format.ts";

type Line = { kind: "row"; index: number; entry: TrialBalanceDetailRow } | { kind: "total" };

const MESSAGE = "Account details could not be loaded.";

/** The one-account ledger behind a Trial balance row, for the From–To window the row was built from. */
export function AccountDetailsDialog({
  account,
  filters,
  onClose,
}: {
  account: Pick<TrialBalanceRow, "accountKey" | "accountName" | "ledgerCode">;
  filters: AppliedTrialBalanceFilters;
  onClose: () => void;
}) {
  const isPhone = useIsPhone();
  const [details, setDetails] = useState<TrialBalanceDetails | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    setDetails(null);
    (async () => {
      try {
        const response = await api(`/api/Finance/trial-balance/details?${trialDetailsParams(account.accountKey, filters)}`, { signal: controller.signal });
        if (!response.ok) throw new Error((await response.json().catch(() => null))?.message ?? MESSAGE);
        const result = await response.json() as TrialBalanceDetails;
        if (!controller.signal.aborted) setDetails({ ...result, rows: Array.isArray(result.rows) ? result.rows : [] });
      } catch (caught) {
        if (!controller.signal.aborted) setError(caught instanceof Error && caught.message.trim() ? caught.message : MESSAGE);
      } finally {
        if (!controller.signal.aborted) setLoading(false);
      }
    })();
    return () => controller.abort();
  }, [account.accountKey, filters, attempt]);

  const ledgerCode = details?.ledgerCode ?? account.ledgerCode;
  const lines: Line[] = details
    ? [
        // Server order is ledger order. Same-day entries are not grouped: every posting stays visible.
        ...details.rows.map((entry, index): Line => ({ kind: "row", index, entry })),
        ...(details.rows.length && !isPhone ? [{ kind: "total" } as Line] : []),
      ]
    : [];

  const entryCell = (entry: TrialBalanceDetailRow) => (
    <span className="block">
      <span className="block font-bold text-ink">{entry.description || "—"}</span>
      <span className="mt-0.5 block text-small text-ink-muted">{formatDate(entry.date)}{entry.reference ? ` · ${entry.reference}` : ""}</span>
    </span>
  );

  const columns: DataTableColumn<Line>[] = [
    { key: "entry", header: "Entry", className: "whitespace-normal", render: (line) => (line.kind === "total" ? "Total" : entryCell(line.entry)) },
    { key: "debit", header: "Debit", align: "right", className: "whitespace-nowrap tabular-nums", render: (line) => (line.kind === "total" ? formatMoney(details?.totalDebit ?? 0) : moneyOrDash(line.entry.debit)) },
    { key: "credit", header: "Credit", align: "right", className: "whitespace-nowrap tabular-nums", render: (line) => (line.kind === "total" ? formatMoney(details?.totalCredit ?? 0) : moneyOrDash(line.entry.credit)) },
    {
      key: "balance",
      header: "Balance",
      align: "right",
      className: "whitespace-nowrap font-bold tabular-nums",
      render: (line) => (line.kind === "total" ? null : formatBalance(line.entry.runningBalance, line.entry.runningBalanceType)),
    },
  ];

  const phoneCard = (line: Line) => {
    if (line.kind === "total") return null;
    const { entry } = line;
    return (
      <div className="rounded-card border border-line bg-card px-4 py-3">
        {entryCell(entry)}
        <dl className="m-0 mt-2.5 grid grid-cols-2 gap-x-3 gap-y-2">
          {[
            ["Debit", moneyOrDash(entry.debit)],
            ["Credit", moneyOrDash(entry.credit)],
          ].map(([label, value]) => (
            <div key={label} className="min-w-0">
              <dt className="text-label font-bold uppercase tracking-[0.4px] text-ink-muted">{label}</dt>
              <dd className="m-0 mt-0.5 whitespace-nowrap text-sm font-extrabold tabular-nums text-ink">{value}</dd>
            </div>
          ))}
          {/* On its own row so "Rs 18,882,660 Dr" never has to break or crowd the amounts beside it. */}
          <div className="col-span-2 flex items-baseline justify-between gap-3 border-t border-line-soft pt-2">
            <dt className="text-label font-bold uppercase tracking-[0.4px] text-ink-muted">Balance</dt>
            <dd className="m-0 whitespace-nowrap text-sm font-extrabold tabular-nums text-ink">{formatBalance(entry.runningBalance, entry.runningBalanceType)}</dd>
          </div>
        </dl>
      </div>
    );
  };

  return (
    <Modal
      open
      onClose={onClose}
      size="lg"
      phoneLayout="fullscreen"
      cancelLabel="Close"
      title={(
        <>
          {account.accountName}
          <span className="mt-0.5 block text-small font-semibold text-ink-muted">Account details{ledgerCode ? ` · Ledger ${ledgerCode}` : ""}</span>
        </>
      )}
    >
      {error ? (
        <Notice tone="red" role="alert" title={error} action={<Button variant="outline" onClick={() => setAttempt((value) => value + 1)}>Try again</Button>} />
      ) : (
        <div className="flex flex-col gap-4">
          <div className="rounded-card border border-line bg-page p-4" aria-busy={loading || undefined}>
            {details ? (
              <KeyValueGrid
                items={[
                  { label: "From", value: formatDate(details.from) },
                  { label: "To", value: formatDate(details.to) },
                  { label: "Opening balance", value: formatBalance(details.openingBalance, details.openingBalanceType) },
                  { label: "Closing balance", value: formatBalance(details.closingBalance, details.closingBalanceType) },
                ]}
              />
            ) : (
              <div aria-hidden="true" className="grid grid-cols-2 gap-x-6 gap-y-3.5">
                {[0, 1, 2, 3].map((index) => <span key={index} className="h-9 animate-pulse rounded bg-track" />)}
              </div>
            )}
          </div>
          <DataTable
            columns={columns}
            rows={lines}
            rowKey={(line) => (line.kind === "total" ? "total" : `${line.entry.id}-${line.index}`)}
            loading={loading}
            loadingCount={5}
            minWidth={520}
            caption={`Ledger entries for ${account.accountName}`}
            empty={details ? <EmptyState title="No transactions were found in this period." /> : undefined}
            phoneCard={phoneCard}
          />
          {isPhone && details && details.rows.length > 0 && (
            <dl className="m-0 grid grid-cols-2 gap-3 rounded-card border border-line bg-table-head px-4 py-3">
              <div>
                <dt className="text-label font-bold uppercase tracking-[0.4px] text-ink-muted">Total debit</dt>
                <dd className="m-0 mt-0.5 whitespace-nowrap text-body font-extrabold tabular-nums text-ink">{formatMoney(details.totalDebit)}</dd>
              </div>
              <div>
                <dt className="text-label font-bold uppercase tracking-[0.4px] text-ink-muted">Total credit</dt>
                <dd className="m-0 mt-0.5 whitespace-nowrap text-body font-extrabold tabular-nums text-ink">{formatMoney(details.totalCredit)}</dd>
              </div>
            </dl>
          )}
        </div>
      )}
    </Modal>
  );
}
