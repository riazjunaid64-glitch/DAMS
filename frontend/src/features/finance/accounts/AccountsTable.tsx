import type { ReactNode } from "react";
import { ActionsMenu, Button, DataTable, EmptyState, IconPencil, StatusBadge, useIsPhone, type ActionItem, type DataTableColumn } from "../../../components/ui";
import { cx } from "../../../components/ui/cx.ts";
import { formatRs } from "../whtTypes.ts";
import { accountSubline, groupAccounts, tableRows, typeName, type Account, type AccountTableRow } from "./accountGroups.ts";

type Props = {
  accounts: readonly Account[];
  loading: boolean;
  onOpen: (account: Account) => void;
  onEdit: (account: Account) => void;
  onToggleActive: (account: Account) => void;
};

const money = (value: number, className?: string) => <span className={cx("whitespace-nowrap tabular-nums", className)}>{formatRs(value)}</span>;

/**
 * The chart of accounts: seven groups with one heading row each and a subtotal row on desktop, one card
 * per account and a group total on the heading on a phone. No paging, by design: the group totals
 * must add up over everything shown.
 */
export function AccountsTable({ accounts, loading, onOpen, onEdit, onToggleActive }: Props) {
  const isPhone = useIsPhone();
  const rows = tableRows(groupAccounts(accounts), !isPhone);

  // An active system account has the pencil only: the server refuses to deactivate it.
  const items = (account: Account): ActionItem[] | null => account.isSystemAccount && account.isActive ? null : [
    account.isActive
      ? { label: "Deactivate", danger: true, onSelect: () => onToggleActive(account) }
      : { label: "Reactivate", onSelect: () => onToggleActive(account) },
  ];

  const actions = (account: Account) => {
    const menu = items(account);
    return (
      <span className="inline-flex items-center justify-end gap-2" onClick={(event) => event.stopPropagation()} onKeyDown={(event) => event.stopPropagation()}>
        <Button variant="outline" iconOnly icon={<IconPencil size={18} />} aria-label={`Edit ${account.name}`} onClick={() => onEdit(account)} />
        {menu && <ActionsMenu trigger="dots" aria-label={`More for ${account.name}`} items={menu} />}
      </span>
    );
  };

  const whenRow = (render: (account: Account) => ReactNode) => (row: AccountTableRow) => (row.kind === "row" ? render(row.account) : null);

  const columns: DataTableColumn<AccountTableRow>[] = [
    {
      key: "account",
      header: "Account",
      className: "min-w-[180px]",
      render: (row) => row.kind === "row"
        ? (
          <>
            <span className="block font-extrabold">{row.account.name}</span>
            <span className="block text-small text-ink-muted">{accountSubline(row.account)}</span>
          </>
        )
        : row.kind === "subtotal" ? <span className="whitespace-nowrap">{row.label}</span> : null,
    },
    {
      key: "type",
      header: "Type / holder",
      render: whenRow((account) => (
        <>
          <span className="block">{typeName(account.type)}</span>
          <span className="block text-small text-ink-muted">{account.accountHolderName}</span>
        </>
      )),
    },
    {
      key: "opening",
      header: "Opening",
      align: "right",
      render: (row) => row.kind === "row" ? money(row.account.openingBalance) : row.kind === "subtotal" ? money(row.totals.openingBalance) : null,
    },
    {
      key: "increases",
      header: "Increases",
      align: "right",
      render: (row) => row.kind === "row" ? money(row.account.revenueReceived, "text-success") : row.kind === "subtotal" ? money(row.totals.revenueReceived, "text-success") : null,
    },
    {
      key: "decreases",
      header: "Decreases",
      align: "right",
      render: (row) => row.kind === "row" ? money(row.account.expensesPaid, "text-danger") : row.kind === "subtotal" ? money(row.totals.expensesPaid, "text-danger") : null,
    },
    {
      key: "balance",
      header: "Current balance",
      align: "right",
      render: (row) => row.kind === "row" ? money(row.account.currentBalance, "font-extrabold") : row.kind === "subtotal" ? money(row.totals.currentBalance) : null,
    },
    { key: "status", header: "Status", render: whenRow((account) => <StatusBadge status={account.isActive ? "Active" : "Inactive"} />) },
    { key: "actions", header: <span className="sr-only">Actions</span>, align: "right", render: whenRow(actions) },
  ];

  const phoneCard = (row: AccountTableRow) => {
    if (row.kind !== "row") return null;
    const { account } = row;
    return (
      <button
        type="button"
        onClick={() => onOpen(account)}
        aria-label={`Open ${account.name}`}
        className="block w-full cursor-pointer rounded-card border border-line bg-card p-4 text-left font-ui text-ink focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary"
      >
        <span className="flex items-start justify-between gap-3">
          <span className="min-w-0 text-section font-extrabold">{account.name}</span>
          <StatusBadge status={account.isActive ? "Active" : "Inactive"} />
        </span>
        <span className="mt-0.5 block text-small text-ink-muted">
          {[typeName(account.type), account.accountHolderName, accountSubline(account)].filter(Boolean).join(" · ")}
        </span>
        <span className="mt-2 block text-small text-ink-2">Opening {money(account.openingBalance, "font-extrabold text-ink")}</span>
        <span className="mt-0.5 flex items-end justify-between gap-3">
          <span className="text-small text-ink-2">
            <span className="whitespace-nowrap">In {money(account.revenueReceived, "font-extrabold text-success")}</span>
            {" · "}
            <span className="whitespace-nowrap">Out {money(account.expensesPaid, "font-extrabold text-danger")}</span>
          </span>
          <span className="text-section font-extrabold">{money(account.currentBalance)}</span>
        </span>
      </button>
    );
  };

  return (
    <DataTable
      columns={columns}
      rows={rows}
      rowKey={(row) => row.key}
      rowKind={(row) => row.kind}
      groupLabel={(row) => row.kind === "group"
        ? isPhone
          ? <span className="flex items-baseline justify-between gap-3"><span>{row.label}</span><span className="whitespace-nowrap normal-case tabular-nums text-ink">{formatRs(row.total)}</span></span>
          : row.label
        : null}
      onRowClick={(row) => { if (row.kind === "row") onOpen(row.account); }}
      rowLabel={(row) => (row.kind === "row" ? `Open ${row.account.name}` : "")}
      phoneCard={phoneCard}
      loading={loading}
      minWidth={1000}
      dense
      caption="Finance accounts grouped by balance-sheet section"
      empty={<EmptyState title="No finance accounts found." />}
    />
  );
}
