import { ActionsMenu, Button, DataTable, EmptyState, IconPencil, IconWallet, Notice, type DataTableColumn } from "../../../components/ui";
import { formatRs, type WhtDeposit } from "../whtTypes.ts";
import { ListFooter } from "./ListFooter.tsx";
import { clampPage, depositDate, pageRows, periodCovered } from "./rules.ts";

type Props = {
  /** The whole list; null until it has answered, and again after a failure. */
  rows: readonly WhtDeposit[] | null;
  loading: boolean;
  error: string | null;
  /** A From / To range is set, which changes what "empty" means. */
  ranged: boolean;
  isPhone: boolean;
  page: number;
  shown: number;
  onPage: (page: number) => void;
  onLoadMore: () => void;
  onEdit: (deposit: WhtDeposit) => void;
  onDelete: (deposit: WhtDeposit) => void;
  onRetry: () => void;
};

const nameOf = (deposit: WhtDeposit) => deposit.challanNumber ? deposit.challanNumber : `of ${depositDate(deposit.depositDate)}`;
const rowKey = (deposit: WhtDeposit) => deposit.id;

/**
 * The challans paid to FBR, newest first as the server sends them. Edit is the pencil and Delete sits
 * in the ⋯ menu. The server returns the whole list, so this cuts it into pages of twenty itself.
 */
export function DepositsList({ rows, loading, error, ranged, isPhone, page, shown, onPage, onLoadMore, onEdit, onDelete, onRetry }: Props) {
  const total = rows?.length ?? 0;
  // A delete can empty the last page; the page shown steps back to the new last one.
  const current = clampPage(page, total);
  const visible = rows === null ? [] : isPhone ? rows.slice(0, shown) : pageRows(rows, current);

  const actions = (deposit: WhtDeposit) => (
    <span className="inline-flex items-center justify-end gap-2">
      <Button variant="outline" iconOnly icon={<IconPencil size={18} />} aria-label={`Edit deposit ${nameOf(deposit)}`} onClick={() => onEdit(deposit)} />
      <ActionsMenu trigger="dots" aria-label={`More for deposit ${nameOf(deposit)}`} items={[{ label: "Delete", danger: true, onSelect: () => onDelete(deposit) }]} />
    </span>
  );

  const columns: DataTableColumn<WhtDeposit>[] = [
    { key: "date", header: "Date", render: (deposit) => <span className="whitespace-nowrap text-ink-2">{depositDate(deposit.depositDate)}</span> },
    { key: "challan", header: "Challan / CPR", render: (deposit) => <b className="font-extrabold text-ink">{deposit.challanNumber || "—"}</b> },
    { key: "account", header: "Paid from", render: (deposit) => deposit.financeAccountName || "—" },
    { key: "period", header: "Period covered", render: (deposit) => <span className="whitespace-nowrap text-ink-2">{periodCovered(deposit)}</span> },
    { key: "amount", header: "Amount", align: "right", render: (deposit) => <b className="whitespace-nowrap font-extrabold tabular-nums text-ink">{formatRs(deposit.amount)}</b> },
    { key: "actions", header: <span className="sr-only">Actions</span>, align: "right", render: actions },
  ];

  const phoneCard = (deposit: WhtDeposit) => {
    const covered = periodCovered(deposit);
    return (
      <div className="rounded-card border border-line bg-card p-4 font-ui">
        <div className="flex items-start justify-between gap-3">
          <b className="min-w-0 break-words font-extrabold text-ink">{deposit.challanNumber || "—"}</b>
          <b className="whitespace-nowrap font-extrabold tabular-nums text-ink">{formatRs(deposit.amount)}</b>
        </div>
        <p className="m-0 mt-0.5 text-small text-ink-muted">{[depositDate(deposit.depositDate), deposit.financeAccountName].filter(Boolean).join(" · ")}</p>
        {covered !== "—" && <p className="m-0 mt-0.5 text-small text-ink-muted">Covers {covered}</p>}
        <div className="mt-3 flex justify-end">{actions(deposit)}</div>
      </div>
    );
  };

  return (
    <div className="flex flex-col gap-3">
      {error && <Notice tone="red" role="alert" title={error} action={<Button variant="outline" onClick={onRetry}>Try again</Button>} />}
      {!error && (
        <DataTable
          columns={columns}
          rows={visible}
          rowKey={rowKey}
          phoneCard={phoneCard}
          loading={loading}
          loadingCount={3}
          minWidth={760}
          caption="Deposits to FBR"
          empty={<EmptyState icon={<IconWallet size={26} />} title={ranged ? "Nothing deposited in this period." : "No deposits yet."} />}
        />
      )}
      {!error && rows !== null && (
        <ListFooter total={total} isPhone={isPhone} page={current} onPage={onPage} shown={shown} onLoadMore={onLoadMore} />
      )}
    </div>
  );
}
