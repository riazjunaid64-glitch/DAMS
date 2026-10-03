import type { ReactNode } from "react";
import {
  ActionsMenu,
  Button,
  DataTable,
  IconArrowDown,
  IconArrowUp,
  IconDownload,
  LoadMore,
  Notice,
  Pagination,
  cx,
  type ActionItem,
  type DataTableColumn,
} from "../../../components/ui";
import { AttachedFile } from "../AttachedFile.tsx";
import { figure, moneyIn, movementDate, movementDetail, movementTitle, rupees, signedCash, signedFigure, signedPrincipal } from "./rules.ts";
import type { LoanTransaction } from "./types.ts";

type Activity = {
  items: readonly LoanTransaction[];
  total: number;
  page: number;
  pageSize: number;
  loading: boolean;
  error: string | null;
  goTo: (page: number) => void;
  loadMore: () => void;
  reload: () => void;
};

type Props = {
  activity: Activity;
  isPhone: boolean;
  exporting: boolean;
  onExport: () => void;
  onCorrect: (row: LoanTransaction) => void;
  onDelete: (row: LoanTransaction) => void;
  onOpenAttachment: (row: LoanTransaction, download: boolean) => void;
};

const dash = <span className="text-ink-faint">—</span>;

/** Green for money in, red for money out, a dash for nothing. */
function Signed({ value }: { value: number }) {
  const text = signedFigure(value);
  if (text === null) return dash;
  return <span className={cx("whitespace-nowrap font-bold tabular-nums", value < 0 ? "text-danger" : "text-success")}>{text}</span>;
}

/** Interest is a cost, shown in its own colour; it never changes the principal owed. */
function Interest({ value }: { value: number }) {
  return value > 0 ? <span className="whitespace-nowrap font-bold tabular-nums text-warning">{figure(value)}</span> : dash;
}

function DirectionIcon({ row }: { row: LoanTransaction }) {
  const into = moneyIn(row);
  return (
    <span aria-hidden="true" className={cx("flex size-7 shrink-0 items-center justify-center rounded-full", into ? "bg-success-soft text-success" : "bg-danger-soft text-danger")}>
      {into ? <IconArrowUp size={14} /> : <IconArrowDown size={14} />}
    </span>
  );
}

function rowActions(row: LoanTransaction, props: Props, withAttachment: boolean): ActionItem[] {
  const items: ActionItem[] = [{ label: "Correct", onSelect: () => props.onCorrect(row) }];
  if (withAttachment && row.attachment) items.push({ label: "View attachment", onSelect: () => props.onOpenAttachment(row, false) });
  items.push({ label: "Delete", danger: true, onSelect: () => props.onDelete(row) });
  return items;
}

function PhoneFigure({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="min-w-0">
      <p className="m-0 text-caption font-bold uppercase tracking-[0.4px] text-ink-muted">{label}</p>
      <p className="m-0 mt-0.5 text-body">{children}</p>
    </div>
  );
}

/**
 * The loan's movements, newest first, with the outstanding principal straight after each one. A
 * desktop pages through them; a phone shows them as cards and loads more underneath.
 */
export function LoanActivity(props: Props) {
  const { activity, isPhone, exporting, onExport, onOpenAttachment } = props;

  const columns: DataTableColumn<LoanTransaction>[] = [
    { key: "date", header: "Date", className: "whitespace-nowrap text-ink-2", render: (row) => movementDate(row.date) },
    {
      key: "transaction",
      header: "Transaction",
      render: (row) => (
        <span className="flex items-start gap-2.5">
          <DirectionIcon row={row} />
          <span className="min-w-0">
            <span className="block whitespace-nowrap font-extrabold text-ink">{movementTitle(row.type)}</span>
            <span title={movementDetail(row)} className="block max-w-[220px] truncate text-small text-ink-muted">{movementDetail(row)}</span>
            {row.note && <span className="block max-w-[180px] text-small text-ink-muted">{row.note}</span>}
          </span>
        </span>
      ),
    },
    { key: "account", header: "Account", className: "whitespace-nowrap text-ink-2", render: (row) => row.financeAccountName },
    { key: "principal", header: "Principal (Rs)", align: "right", render: (row) => <Signed value={signedPrincipal(row)} /> },
    { key: "interest", header: "Interest (Rs)", align: "right", render: (row) => <Interest value={row.interestAmount} /> },
    { key: "cash", header: "Cash impact (Rs)", align: "right", render: (row) => <Signed value={signedCash(row)} /> },
    { key: "outstanding", header: "Outstanding (Rs)", align: "right", render: (row) => <span className="whitespace-nowrap font-extrabold tabular-nums">{figure(row.runningBalance)}</span> },
    { key: "attachment", header: "Attachment", render: (row) => <AttachedFile attachment={row.attachment} onOpen={(download) => onOpenAttachment(row, download)} /> },
    {
      key: "actions",
      header: <span className="sr-only">Actions</span>,
      align: "right",
      render: (row) => <ActionsMenu trigger="dots" aria-label={`Actions for the ${movementTitle(row.type).toLowerCase()} on ${movementDate(row.date)}`} items={rowActions(row, props, false)} />,
    },
  ];

  const phoneCard = (row: LoanTransaction) => (
    <div className="rounded-card border border-line bg-card p-4 font-ui">
      <div className="flex items-start gap-2.5">
        <DirectionIcon row={row} />
        <div className="min-w-0">
          <p className="m-0 font-extrabold text-ink">{movementTitle(row.type)}</p>
          <p className="m-0 text-small text-ink-muted">{[movementDate(row.date), row.financeAccountName, movementDetail(row)].join(" · ")}</p>
          {row.note && <p className="m-0 text-small text-ink-muted">{row.note}</p>}
        </div>
      </div>
      <div className="mt-3 grid grid-cols-3 gap-2">
        <PhoneFigure label="Principal"><Signed value={signedPrincipal(row)} /></PhoneFigure>
        <PhoneFigure label="Interest"><Interest value={row.interestAmount} /></PhoneFigure>
        <PhoneFigure label="Cash impact"><Signed value={signedCash(row)} /></PhoneFigure>
      </div>
      <div className="mt-2 flex items-center justify-between gap-3">
        <p className="m-0 text-small text-ink-muted">Outstanding <b className="font-extrabold tabular-nums text-ink">{rupees(row.runningBalance)}</b></p>
        <ActionsMenu trigger="dots" aria-label={`Actions for the ${movementTitle(row.type).toLowerCase()} on ${movementDate(row.date)}`} items={rowActions(row, props, true)} />
      </div>
    </div>
  );

  return (
    <section className="flex flex-col gap-3 font-ui">
      <div className="flex items-center justify-between gap-3">
        <h3 className="m-0 text-section font-extrabold text-ink">Loan activity</h3>
        {!isPhone && (
          <Button size="sm" variant="outline" icon={<IconDownload size={14} />} loading={exporting} disabled={activity.total === 0} onClick={onExport}>
            Export
          </Button>
        )}
      </div>
      {activity.error && (
        <Notice tone="red" role="alert" title={activity.error} action={<Button variant="outline" onClick={activity.reload}>Try again</Button>} />
      )}
      <DataTable
        columns={columns}
        rows={activity.items}
        rowKey={(row) => row.id}
        phoneCard={phoneCard}
        loading={activity.loading}
        loadingCount={4}
        minWidth={900}
        dense
        caption="Loan activity"
        empty={activity.error ? undefined : (
          <p className="m-0 rounded-card border border-dashed border-line bg-card p-8 text-center text-small text-ink-muted">No movements recorded for this loan yet.</p>
        )}
      />
      {activity.total > 0 && (isPhone ? (
        <div className="flex flex-col items-center gap-2">
          <LoadMore shown={activity.items.length} total={activity.total} loading={activity.loading} showCount={false} onLoadMore={activity.loadMore} />
          <p className="m-0 text-small text-ink-muted">
            Showing <b className="font-extrabold text-ink">{activity.items.length.toLocaleString("en-PK")}</b> of <b className="font-extrabold text-ink">{activity.total.toLocaleString("en-PK")}</b> entries
          </p>
        </div>
      ) : (
        <Pagination page={activity.page} pageSize={activity.pageSize} totalCount={activity.total} itemLabel="entries" onPageChange={activity.goTo} />
      ))}
    </section>
  );
}
