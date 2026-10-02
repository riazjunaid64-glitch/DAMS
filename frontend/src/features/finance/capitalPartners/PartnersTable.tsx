import { ActionsMenu, Button, DataTable, EmptyState, IconPlus, StatusBadge, type ActionItem, type DataTableColumn } from "../../../components/ui";
import { cx } from "../../../components/ui/cx.ts";
import { formatRs } from "../whtTypes.ts";
import { formatShare } from "./rules.ts";
import type { Partner } from "./types.ts";

type Props = {
  partners: readonly Partner[];
  loading: boolean;
  onAdd: () => void;
  onStatement: (partner: Partner) => void;
  onTransaction: (partner: Partner) => void;
  onEdit: (partner: Partner) => void;
};

const money = (value: number, className?: string) => <span className={cx("whitespace-nowrap tabular-nums", className)}>{formatRs(value)}</span>;

/** The partner list: one table on desktop, one card per partner on a phone. No paging, by design. */
export function PartnersTable({ partners, loading, onAdd, onStatement, onTransaction, onEdit }: Props) {
  const items = (partner: Partner): ActionItem[] => [
    // The server refuses a movement for an inactive partner (and for one without a capital account).
    { label: "Add transaction", disabled: !partner.isActive, onSelect: () => onTransaction(partner) },
    { label: "Edit partner", onSelect: () => onEdit(partner) },
  ];

  const actions = (partner: Partner, fill = false) => (
    <span className={cx("inline-flex items-center justify-end gap-2", fill && "flex w-full")}>
      <Button variant="outline" size="sm" className={fill ? "flex-1" : undefined} onClick={() => onStatement(partner)} aria-label={`Statement for ${partner.name}`}>Statement</Button>
      <ActionsMenu trigger="dots" aria-label={`More for ${partner.name}`} items={items(partner)} />
    </span>
  );

  const columns: DataTableColumn<Partner>[] = [
    {
      key: "partner",
      header: "Partner",
      className: "min-w-[140px]",
      render: (row) => (
        <>
          <span className="flex flex-wrap items-center gap-2">
            <span className="font-extrabold">{row.name}</span>
            {!row.isActive && <StatusBadge status="Inactive" />}
          </span>
          <span className="block text-small text-ink-muted">{row.financeAccountName ?? "No capital account"}</span>
        </>
      ),
    },
    { key: "share", header: "Share", render: (row) => <span className={cx("font-extrabold", !row.isActive && "font-normal text-ink-faint")}>{formatShare(row.profitSharePercent)}</span> },
    { key: "opening", header: "Opening", align: "right", render: (row) => money(row.openingBalance) },
    { key: "contributions", header: "Contributions", align: "right", render: (row) => money(row.contributions, "text-success") },
    { key: "withdrawals", header: "Withdrawals", align: "right", render: (row) => money(row.withdrawals, "text-danger") },
    { key: "profit", header: "Profit share", align: "right", render: (row) => money(row.profitShare) },
    { key: "loss", header: "Loss share", align: "right", render: (row) => money(row.lossShare) },
    { key: "closing", header: "Closing", align: "right", render: (row) => money(row.closingBalance, "font-extrabold") },
    { key: "actions", header: <span className="sr-only">Actions</span>, align: "right", render: (row) => actions(row) },
  ];

  const figure = (label: string, value: number, className?: string) => (
    <div className="min-w-0">
      <p className="m-0 text-caption font-bold uppercase tracking-[0.4px] text-ink-2">{label}</p>
      {money(value, cx("text-sm font-extrabold", className))}
    </div>
  );

  const phoneCard = (row: Partner) => (
    <article className="flex flex-col gap-3 rounded-card border border-line bg-card p-4 font-ui">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="m-0 text-section font-extrabold text-ink">{row.name}</p>
          <p className="m-0 text-small text-ink-muted">{row.financeAccountName ?? "No capital account"}</p>
        </div>
        <div className="flex shrink-0 items-center gap-2">
          <span className={cx("text-sm font-extrabold", row.isActive ? "text-ink" : "text-ink-faint")}>{formatShare(row.profitSharePercent)}</span>
          <StatusBadge status={row.isActive ? "Active" : "Inactive"} />
        </div>
      </div>
      <div className="flex items-baseline justify-between gap-3 rounded-field bg-page px-3 py-2.5">
        <span className="text-small font-bold text-ink-2">Closing</span>
        {money(row.closingBalance, "text-section font-extrabold text-ink")}
      </div>
      <div className="grid grid-cols-2 gap-x-4 gap-y-3">
        {figure("Opening", row.openingBalance)}
        {figure("Contributions", row.contributions, "text-success")}
        {figure("Withdrawals", row.withdrawals, "text-danger")}
        {figure("Profit share", row.profitShare)}
        {figure("Loss share", row.lossShare)}
      </div>
      {actions(row, true)}
    </article>
  );

  return (
    <DataTable
      columns={columns}
      rows={partners}
      rowKey={(row) => row.id}
      loading={loading}
      minWidth={1000}
      dense
      phoneCard={phoneCard}
      empty={<EmptyState title="No capital partners yet" action={<Button icon={<IconPlus size={16} />} onClick={onAdd}>Add partner</Button>} />}
    />
  );
}
