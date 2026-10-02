import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Button, DataTable, DatePicker, EmptyState, Modal, Notice, useIsPhone, type DataTableColumn, type DataTableRowKind } from "../../../components/ui";
import { cx } from "../../../components/ui/cx.ts";
import { openAttachmentAt } from "../../../api/financeAttachments.ts";
import { pakistanToday } from "../../../lib/financePeriods.ts";
import { DialogTitle } from "../../bookings/DialogTitle.tsx";
import { capitalApi } from "./api.ts";
import { accountAndReference, formatShare, noteToShow, runningBalance, showDay, statementRangeError, statementSubtitle, typeLabel, type StatementLine } from "./rules.ts";
import { formatRs } from "../whtTypes.ts";
import type { AccountOption, Partner, Statement } from "./types.ts";

/** What a table or phone row shows: the opening line, a movement with its running balance, or the closing line. */
type Line = { key: string; kind: "opening" } | { key: string; kind: "closing" } | { key: string; kind: "movement"; line: StatementLine };

type Props = {
  partner: Partner;
  /** Cash and bank accounts, so a movement can name the account it went through. */
  cashAccounts: readonly AccountOption[];
  onClose: () => void;
};

const dash = <span className="text-ink-faint">—</span>;

/**
 * A partner's statement: opening and closing balance and every movement between, with a running
 * balance. The dates do nothing until Apply; All time clears both and loads at once. Two ranges
 * applied quickly cannot land out of order: only the newest request may write its answer.
 */
export function StatementDialog({ partner, cashAccounts, onClose }: Props) {
  const isPhone = useIsPhone();
  const [today] = useState(pakistanToday);
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [statement, setStatement] = useState<Statement | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [fileError, setFileError] = useState<string | null>(null);
  // The range last asked for, so Try again repeats it rather than whatever is typed in the boxes now.
  const [asked, setAsked] = useState({ from: "", to: "" });
  const newest = useRef(0);
  const inFlight = useRef<AbortController | null>(null);

  const fetchStatement = useCallback((rangeFrom: string, rangeTo: string) => {
    inFlight.current?.abort();
    const controller = new AbortController();
    inFlight.current = controller;
    const ticket = ++newest.current;
    capitalApi.statement(partner.id, rangeFrom, rangeTo, controller.signal)
      .then((loaded) => {
        if (ticket !== newest.current) return;
        setStatement(loaded);
        setError(null);
      })
      .catch((failure: unknown) => {
        if (ticket !== newest.current) return;
        setError(failure instanceof Error ? failure.message : "The statement could not be loaded.");
      })
      .finally(() => {
        if (ticket === newest.current) setLoading(false);
      });
  }, [partner.id]);

  // Opens on All time. Closing the popup cancels whatever is still on its way.
  useEffect(() => {
    fetchStatement("", "");
    return () => {
      newest.current += 1;
      inFlight.current?.abort();
    };
  }, [fetchStatement]);

  const request = (rangeFrom: string, rangeTo: string) => {
    setAsked({ from: rangeFrom, to: rangeTo });
    setLoading(true);
    setError(null);
    setFileError(null);
    fetchStatement(rangeFrom, rangeTo);
  };
  const allTime = () => {
    setFrom("");
    setTo("");
    request("", "");
  };
  const rangeError = statementRangeError(from, to);

  const accountNames = useMemo(() => new Map(cashAccounts.map((account) => [account.id, account.name])), [cashAccounts]);
  const lines = useMemo<Line[]>(() => {
    if (!statement) return [];
    return [
      { key: "opening", kind: "opening" },
      ...runningBalance(statement.openingBalance, statement.transactions).map((line) => ({ key: `m${line.id}`, kind: "movement" as const, line })),
      { key: "closing", kind: "closing" },
    ];
  }, [statement]);

  const openFile = async (row: StatementLine, download: boolean) => {
    if (!row.attachment) return;
    setFileError(null);
    try {
      await openAttachmentAt(`/api/finance/partners/${partner.id}/transactions/${row.id}/attachment`, row.attachment.fileName, download);
    } catch (failure) {
      setFileError(failure instanceof Error ? failure.message : "The attachment could not be opened.");
    }
  };

  const files = (row: StatementLine) => row.attachment && (
    <span className="mt-1 flex flex-wrap items-center gap-x-4">
      <Button variant="link" size="sm" onClick={() => void openFile(row, false)}>View</Button>
      <Button variant="link" size="sm" className="text-primary" onClick={() => void openFile(row, true)}>Download</Button>
    </span>
  );

  const periodStart = showDay(statement?.from);
  const columns: DataTableColumn<Line>[] = [
    {
      key: "date",
      header: "Date",
      className: "whitespace-nowrap align-top text-small text-ink-2",
      render: (row) => row.kind === "movement" ? showDay(row.line.date) : row.kind === "opening" ? periodStart : "",
    },
    {
      key: "particulars",
      header: "Particulars",
      className: "align-top",
      render: (row) => {
        if (row.kind === "opening") return <span className="font-extrabold">Opening balance</span>;
        if (row.kind === "closing") return <span className="font-extrabold">Closing balance</span>;
        const { line } = row;
        const where = accountAndReference(line, accountNames);
        const note = noteToShow(line);
        return (
          <>
            <span className="block font-extrabold">{typeLabel(line.type)}</span>
            {where && <span className="block text-small text-ink-muted">{where}</span>}
            {note && <span className="block text-small text-ink-muted">{note}</span>}
            {line.type === "ProfitShare" && line.profitSharePercentSnapshot != null && <span className="block text-small text-ink-muted">{formatShare(line.profitSharePercentSnapshot)} share</span>}
            {files(line)}
          </>
        );
      },
    },
    {
      key: "debit",
      header: "Debit",
      align: "right",
      className: "whitespace-nowrap align-top text-small",
      render: (row) => row.kind === "movement" && row.line.signed < 0 ? <span className="font-bold text-danger">{formatRs(-row.line.signed)}</span> : row.kind === "movement" ? dash : "",
    },
    {
      key: "credit",
      header: "Credit",
      align: "right",
      className: "whitespace-nowrap align-top text-small",
      render: (row) => row.kind === "movement" && row.line.signed >= 0 ? <span className="font-bold text-success">{formatRs(row.line.signed)}</span> : row.kind === "movement" ? dash : "",
    },
    {
      key: "balance",
      header: "Balance",
      align: "right",
      className: "whitespace-nowrap align-top text-small font-bold",
      render: (row) => row.kind === "movement" ? formatRs(row.line.balance) : statement ? formatRs(row.kind === "opening" ? statement.openingBalance : statement.closingBalance) : "",
    },
  ];
  const rowKind = (row: Line): DataTableRowKind => row.kind === "closing" ? "total" : "row";

  const phoneLine = (row: Line) => {
    if (row.kind === "movement") {
      const { line } = row;
      const where = [showDay(line.date), accountAndReference(line, accountNames)].filter(Boolean).join(" · ");
      const note = noteToShow(line);
      const debit = line.signed < 0;
      return (
        <li key={row.key} className="border-t border-line-soft py-3">
          <div className="flex items-baseline justify-between gap-3">
            <span className="font-extrabold text-ink">{typeLabel(line.type)}</span>
            <span className="whitespace-nowrap font-extrabold text-ink">{formatRs(line.balance)}</span>
          </div>
          <p className="m-0 mt-0.5 text-small text-ink-muted">{where}</p>
          {note && <p className="m-0 text-small text-ink-muted">{note}</p>}
          {line.type === "ProfitShare" && line.profitSharePercentSnapshot != null && <p className="m-0 text-small text-ink-muted">{formatShare(line.profitSharePercentSnapshot)} share</p>}
          <p className="m-0 mt-1 flex gap-4 text-small text-ink-muted">
            <span>Debit {debit ? <b className="text-danger">{formatRs(-line.signed)}</b> : "—"}</span>
            <span>Credit {debit ? "—" : <b className="text-success">{formatRs(line.signed)}</b>}</span>
          </p>
          {files(line)}
        </li>
      );
    }
    return null;
  };

  const closingUp = (statement?.closingBalance ?? 0) >= 0;
  const hasMovements = (statement?.transactions.length ?? 0) > 0;
  const refreshing = loading && statement !== null;

  return (
    <Modal
      open
      onClose={onClose}
      size="lg"
      phoneLayout="fullscreen"
      title={<DialogTitle title={`${partner.name} · Statement`} subtitle={statementSubtitle(partner, isPhone)} />}
      cancelLabel="Close"
    >
      <div className="flex flex-col gap-4">
        <div className="flex flex-col gap-3 md:flex-row md:flex-wrap md:items-center">
          <div className="grid grid-cols-2 gap-3 md:flex md:items-center">
            <DatePicker size="filter" label="From" aria-label="From date" max={today} value={from} onChange={setFrom} className="md:w-[190px]" />
            <DatePicker size="filter" label="To" aria-label="To date" max={today} value={to} onChange={setTo} className="md:w-[190px]" />
          </div>
          {isPhone ? (
            <div className="grid grid-cols-2 gap-3">
              <Button variant="outline" onClick={allTime}>All time</Button>
              <Button variant="outline" disabled={loading || rangeError !== null} onClick={() => request(from, to)}>Apply</Button>
            </div>
          ) : (
            <>
              <Button variant="outline" disabled={loading || rangeError !== null} onClick={() => request(from, to)}>Apply</Button>
              <Button variant="link" onClick={allTime}>All time</Button>
            </>
          )}
        </div>
        {rangeError && <Notice tone="orange" role="alert" title={rangeError} />}
        {error && (
          <Notice
            tone="red"
            role="alert"
            title={error}
            action={<Button variant="outline" onClick={() => request(asked.from, asked.to)}>Try again</Button>}
          />
        )}
        {fileError && <Notice tone="red" role="alert" title={fileError} />}

        {!statement && !error && (
          <div role="status" aria-busy="true" className="flex flex-col gap-3">
            <span className="sr-only">Loading</span>
            <span aria-hidden="true" className="h-20 animate-pulse rounded-card bg-track" />
            <span aria-hidden="true" className="h-40 animate-pulse rounded-card bg-track" />
          </div>
        )}
        {statement && (
          <div className={cx("flex flex-col gap-4 transition-opacity", refreshing && "pointer-events-none opacity-50")} aria-busy={refreshing || undefined}>
            <div className="grid grid-cols-2 gap-4 rounded-card border border-line bg-page px-4 py-3.5">
              <div>
                <p className="m-0 text-label font-extrabold uppercase tracking-[0.4px] text-ink-2">Opening balance</p>
                <p className="m-0 mt-1 text-section font-extrabold text-ink">{formatRs(statement.openingBalance)}</p>
              </div>
              <div>
                <p className="m-0 text-label font-extrabold uppercase tracking-[0.4px] text-ink-2">Closing balance</p>
                <p className={cx("m-0 mt-1 text-section font-extrabold", closingUp ? "text-success" : "text-danger")}>{formatRs(statement.closingBalance)}</p>
              </div>
            </div>
            {!hasMovements ? (
              <EmptyState title="No capital movements in this period." />
            ) : isPhone ? (
              <ul className="m-0 list-none p-0">
                <li className="flex items-baseline justify-between gap-3 py-3">
                  <span className="font-extrabold text-ink">Opening balance{periodStart && ` · ${periodStart}`}</span>
                  <span className="whitespace-nowrap font-extrabold text-ink">{formatRs(statement.openingBalance)}</span>
                </li>
                {lines.map(phoneLine)}
              </ul>
            ) : (
              <DataTable columns={columns} rows={lines} rowKey={(row) => row.key} rowKind={rowKind} maxHeight="46vh" minWidth={0} dense caption="Capital statement" />
            )}
          </div>
        )}
      </div>
    </Modal>
  );
}
