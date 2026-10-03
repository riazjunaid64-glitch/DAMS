import {
  ActionsMenu,
  Button,
  DataTable,
  LoadMore,
  Notice,
  Pagination,
  cx,
  type ActionItem,
  type DataTableColumn,
} from "../../../components/ui";
import { AttachedFile } from "../AttachedFile.tsx";
import { canChangeMovement, movementDate, movementName, movementPlace, movementRef, rupees, signedRupees, taxLine } from "./rules.ts";
import type { Holder, HistoryItem } from "./types.ts";

export type MovementsList = {
  rows: readonly HistoryItem[];
  total: number;
  page: number;
  pageSize: number;
  loading: boolean;
  loadingMore: boolean;
  error: string | null;
  setPage: (page: number) => void;
  loadMore: () => void;
  reload: () => void;
};

type Props = {
  holder: Holder;
  list: MovementsList;
  isPhone: boolean;
  onCorrect: (row: HistoryItem) => void;
  onDelete: (row: HistoryItem) => void;
  onOpenAttachment: (row: HistoryItem, download: boolean) => void;
};

const rowKey = (row: HistoryItem) => `${row.recordType}-${row.recordId}`;

function Amount({ value }: { value: number }) {
  return <span className={cx("whitespace-nowrap font-bold tabular-nums", value < 0 ? "text-danger" : "text-success")}>{signedRupees(value)}</span>;
}

function Balance({ value }: { value: number }) {
  return <b className={cx("whitespace-nowrap font-extrabold tabular-nums", value < 0 ? "text-danger" : "text-ink")}>{rupees(value)}</b>;
}

function Tax({ row }: { row: HistoryItem }) {
  const line = taxLine(row);
  return line ? <span className="block text-small text-gold-text">{line}</span> : null;
}

/**
 * The person's movements, newest first, with the float's balance straight after each one. A desktop
 * pages through them; a phone shows them as cards and loads more underneath. Only a money move has
 * Correct and Delete; an expense is changed in the Finance home table.
 */
export function Movements({ holder, list, isPhone, onCorrect, onDelete, onOpenAttachment }: Props) {
  const actions = (row: HistoryItem) => {
    if (!canChangeMovement(holder, row)) return null;
    const items: ActionItem[] = [
      { label: "Correct", onSelect: () => onCorrect(row) },
      { label: "Delete", danger: true, onSelect: () => onDelete(row) },
    ];
    return <ActionsMenu trigger="dots" aria-label={`Actions for the ${movementName(row).toLowerCase()} on ${movementDate(row.date)}`} items={items} />;
  };

  const columns: DataTableColumn<HistoryItem>[] = [
    {
      key: "movement",
      header: "Date / movement",
      render: (row) => (
        <span className="block min-w-0">
          <span className="block font-extrabold text-ink">{movementName(row)}</span>
          <span className="block whitespace-nowrap text-small text-ink-muted">{movementDate(row.date)}</span>
          {row.note && <span className="block max-w-[220px] text-small text-ink-muted">{row.note}</span>}
        </span>
      ),
    },
    {
      key: "place",
      header: "Account / project",
      render: (row) => (
        <span className="block min-w-0">
          <span className="block text-ink">{movementPlace(row)}</span>
          {movementRef(row) && <span className="block text-small text-ink-muted">{movementRef(row)}</span>}
          <Tax row={row} />
        </span>
      ),
    },
    { key: "amount", header: "Amount", align: "right", render: (row) => <Amount value={row.amount} /> },
    { key: "balance", header: "Balance", align: "right", render: (row) => <Balance value={row.runningBalance} /> },
    { key: "attachment", header: "Attachment", render: (row) => <AttachedFile attachment={row.attachment} onOpen={(download) => onOpenAttachment(row, download)} /> },
    { key: "actions", header: <span className="sr-only">Actions</span>, align: "right", render: actions },
  ];

  const phoneCard = (row: HistoryItem) => (
    <div className="rounded-card border border-line bg-card p-4 font-ui">
      <div className="flex items-start justify-between gap-3">
        <p className="m-0 min-w-0 font-extrabold text-ink">{movementName(row)}</p>
        <Amount value={row.amount} />
      </div>
      <p className="m-0 mt-0.5 text-small text-ink-muted">{[movementDate(row.date), movementPlace(row), movementRef(row)].filter(Boolean).join(" · ")}</p>
      {row.note && <p className="m-0 mt-0.5 text-small text-ink">{row.note}</p>}
      <Tax row={row} />
      <div className="mt-3 flex items-center justify-between gap-3">
        <p className="m-0 text-small text-ink-muted">Balance <Balance value={row.runningBalance} /></p>
        <span className="flex items-center gap-2">
          {row.attachment && <AttachedFile attachment={row.attachment} onOpen={(download) => onOpenAttachment(row, download)} />}
          {actions(row)}
        </span>
      </div>
    </div>
  );

  return (
    <section className="flex flex-col gap-3 font-ui">
      <h3 className="m-0 text-section font-extrabold text-ink">Movements</h3>
      {list.error && (
        <Notice tone="red" role="alert" title={list.error} action={<Button variant="outline" onClick={list.reload}>Try again</Button>} />
      )}
      <DataTable
        columns={columns}
        rows={list.rows}
        rowKey={rowKey}
        phoneCard={phoneCard}
        loading={list.loading}
        loadingCount={4}
        minWidth={820}
        dense
        caption="Movements"
        empty={list.error ? undefined : (
          <p className="m-0 rounded-card border border-dashed border-line bg-card p-8 text-center text-small text-ink-muted">No movements yet. Record money given to begin this float.</p>
        )}
      />
      {list.total > 0 && (isPhone ? (
        <div className="flex flex-col items-center gap-2">
          <LoadMore shown={list.rows.length} total={list.total} loading={list.loadingMore} showCount={false} onLoadMore={list.loadMore} />
          <p className="m-0 text-small text-ink-muted">
            Showing <b className="font-extrabold text-ink">{list.rows.length.toLocaleString("en-PK")}</b> of <b className="font-extrabold text-ink">{list.total.toLocaleString("en-PK")}</b> entries
          </p>
        </div>
      ) : (
        <Pagination page={list.page} pageSize={list.pageSize} totalCount={list.total} itemLabel="entries" onPageChange={list.setPage} />
      ))}
    </section>
  );
}
