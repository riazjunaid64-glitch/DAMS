import type { ReactNode } from "react";
import { Button, DataTable, EmptyState, StatusBadge } from "../../../components/ui";
import type { DataTableColumn } from "../../../components/ui";
import { IconArrowUpRight, IconDownload, IconPaperclip, IconPencil, IconTrash } from "../../../components/ui/icons.tsx";
import type { FinanceAttachmentInfo, FinanceRecordKind } from "../../../api/financeAttachments.ts";
import { formatCostAmount, formatDate, formatMoney, projectLabel } from "./format.ts";
import type { CostLine, CostSource, CustomerDepositLine, FinanceRow, FinanceView, OverdueLine, RevenueLine } from "./types.ts";

const OPEN_LABEL: Partial<Record<CostSource, string>> = {
  commission: "Open in Commissions & rebates",
  rebate: "Open in Commissions & rebates",
  customerCredit: "Open in Commissions & rebates",
  loanInterest: "Open in Loans",
};

const EMPTY: Record<FinanceView, string> = {
  revenue: "No revenue for the selected filters.",
  totalExpenses: "No costs for the selected filters.",
  customerDeposits: "No customer deposits held for the selected filters.",
  overdue: "No overdue installments for the selected filters.",
};

const TITLE: Record<FinanceView, string> = {
  revenue: "Revenue",
  totalExpenses: "Total expenses",
  customerDeposits: "Customer deposits",
  overdue: "Overdue",
};

function sourceBadge(source: string): { label: string; tone: "grey" | "green" | "orange" } {
  if (source === "Unit Sale") return { label: "Unit sale", tone: "green" };
  if (source === "Cancellation Retained") return { label: "Cancellation retained", tone: "orange" };
  if (source === "Manual Revenue") return { label: "Manual revenue", tone: "grey" };
  return { label: source, tone: "grey" };
}

function money(value: number, className = "text-ink") {
  return <span className={`whitespace-nowrap font-bold tabular-nums ${className}`}>{formatMoney(value)}</span>;
}

function AttachedChip({ onOpen }: { onOpen: () => void }) {
  return (
    <button
      type="button"
      onClick={(event) => { event.stopPropagation(); onOpen(); }}
      className="inline-flex h-6 cursor-pointer items-center gap-1 rounded-full border-0 bg-steel-soft px-2.5 text-label font-bold text-steel"
    >
      <IconPaperclip size={12} />
      Attached
    </button>
  );
}

function IconButtons({
  editLabel = "Edit",
  deleteLabel = "Delete",
  editBusy,
  deleteBusy,
  onEdit,
  onDelete,
}: {
  editLabel?: string;
  deleteLabel?: string;
  editBusy?: boolean;
  deleteBusy?: boolean;
  onEdit: () => void;
  onDelete: () => void;
}) {
  return (
    <span className="inline-flex justify-end gap-1">
      <Button
        iconOnly
        variant="outline"
        icon={<IconPencil size={16} />}
        aria-label={editLabel}
        loading={editBusy}
        onClick={(event) => { event.stopPropagation(); onEdit(); }}
      />
      <Button
        iconOnly
        variant="danger"
        icon={<IconTrash size={16} />}
        aria-label={deleteLabel}
        loading={deleteBusy}
        onClick={(event) => { event.stopPropagation(); onDelete(); }}
      />
    </span>
  );
}

export function FinanceTable({
  view,
  rows,
  loading,
  busyKey,
  showEmpty = true,
  onEditRevenue,
  onDeleteRevenue,
  onOpenCost,
  onDeleteCost,
  onOpenAttachment,
  onDownloadAttachment,
}: {
  view: FinanceView;
  rows: readonly FinanceRow[];
  loading: boolean;
  busyKey: string | null;
  showEmpty?: boolean;
  onEditRevenue: (row: RevenueLine) => void;
  onDeleteRevenue: (row: RevenueLine) => void;
  onOpenCost: (row: CostLine) => void;
  onDeleteCost: (row: CostLine) => void;
  onOpenAttachment: (kind: FinanceRecordKind, id: number, attachment: FinanceAttachmentInfo) => void;
  onDownloadAttachment: (kind: FinanceRecordKind, id: number, attachment: FinanceAttachmentInfo) => void;
}) {
  const attachment = (kind: FinanceRecordKind, id: number | null, file: FinanceAttachmentInfo | null, phone = false) => {
    if (!file || id == null) return <span className="text-small text-ink-muted">None</span>;
    return (
      <span className="inline-flex items-center gap-1">
        <AttachedChip onOpen={() => onOpenAttachment(kind, id, file)} />
        {!phone && (
          <Button
            iconOnly
            variant="ghost"
            icon={<IconDownload size={16} />}
            aria-label={`Download ${file.fileName}`}
            title={`Download ${file.fileName}`}
            onClick={(event) => { event.stopPropagation(); onDownloadAttachment(kind, id, file); }}
          />
        )}
      </span>
    );
  };

  const columns = columnsFor(view, attachment, busyKey, onEditRevenue, onDeleteRevenue, onOpenCost, onDeleteCost);
  const phoneCard = (row: FinanceRow) => phoneFor(view, row, attachment, busyKey, onEditRevenue, onDeleteRevenue, onOpenCost, onDeleteCost);

  return (
    <section className="overflow-hidden rounded-card border border-line bg-card">
      <h2 className="m-0 border-b border-line-soft px-4 py-3.5 text-body font-extrabold text-ink">{TITLE[view]}</h2>
      <DataTable
        columns={columns}
        rows={rows}
        rowKey={(row) => rowKey(view, row)}
        loading={loading}
        minWidth={view === "customerDeposits" || view === "overdue" ? 1080 : 980}
        empty={showEmpty ? <EmptyState title={EMPTY[view]} /> : undefined}
        onRowClick={view === "totalExpenses" ? (row) => onOpenCost(row as CostLine) : undefined}
        rowLabel={view === "totalExpenses" ? (row) => (row as CostLine).label : undefined}
        phoneCard={phoneCard}
        className="[&>div]:rounded-none [&>div]:border-0"
      />
    </section>
  );
}

function rowKey(view: FinanceView, row: FinanceRow): string {
  if (view === "revenue") {
    const item = row as RevenueLine;
    return item.rowId
      || (item.manualRevenueId != null ? `Manual Revenue:${item.manualRevenueId}` : `${item.source}:${item.reference || item.date}`);
  }
  if (view === "totalExpenses") {
    const item = row as CostLine;
    return `${item.source}-${item.sourceId}`;
  }
  if (view === "customerDeposits") return `deposit-${(row as CustomerDepositLine).bookingId}`;
  const item = row as OverdueLine;
  return `overdue-${item.bookingReference}-${item.sequenceNumber}-${item.dueDate}`;
}

function columnsFor(
  view: FinanceView,
  attachment: (kind: FinanceRecordKind, id: number | null, file: FinanceAttachmentInfo | null) => ReactNode,
  busyKey: string | null,
  onEditRevenue: (row: RevenueLine) => void,
  onDeleteRevenue: (row: RevenueLine) => void,
  onOpenCost: (row: CostLine) => void,
  onDeleteCost: (row: CostLine) => void,
): DataTableColumn<FinanceRow>[] {
  if (view === "revenue") {
    return [
      { key: "date", header: "Date", render: (row) => formatDate((row as RevenueLine).date) },
      { key: "project", header: "Project", render: (row) => projectLabel((row as RevenueLine).projectName) },
      { key: "account", header: "Received in", render: (row) => {
        const item = row as RevenueLine;
        return (
          <span>
            {item.financeAccountName ?? "Unassigned"}
            {item.accountHolderName && <small className="block text-small text-ink-muted">{item.accountHolderName}</small>}
          </span>
        );
      } },
      { key: "type", header: "Revenue type", render: (row) => (row as RevenueLine).revenueType },
      { key: "amount", header: "Amount", align: "right", render: (row) => money((row as RevenueLine).amount, "text-success") },
      { key: "source", header: "Source", render: (row) => {
        const badge = sourceBadge((row as RevenueLine).source);
        return <StatusBadge status={badge.label} tone={badge.tone}>{badge.label}</StatusBadge>;
      } },
      { key: "attachment", header: "Attachment", render: (row) => {
        const item = row as RevenueLine;
        return attachment("revenue", item.manualRevenueId, item.attachment);
      } },
      { key: "actions", header: "", align: "right", render: (row) => {
        const item = row as RevenueLine;
        if (item.manualRevenueId == null) return null;
        return (
          <IconButtons
            onEdit={() => onEditRevenue(item)}
            onDelete={() => onDeleteRevenue(item)}
          />
        );
      } },
    ];
  }

  if (view === "totalExpenses") {
    return [
      { key: "date", header: "Date", render: (row) => formatDate((row as CostLine).date) },
      { key: "project", header: "Project", render: (row) => projectLabel((row as CostLine).projectName) },
      { key: "cost", header: "Cost", render: (row) => (row as CostLine).label },
      { key: "amount", header: "Amount", align: "right", render: (row) => {
        const cost = formatCostAmount((row as CostLine).amount);
        return <span className={`whitespace-nowrap font-bold tabular-nums ${cost.className}`}>{cost.text}</span>;
      } },
      { key: "attachment", header: "Attachment", render: (row) => {
        const item = row as CostLine;
        if (item.source !== "expense" && item.source !== "assetPurchase") return <span className="text-ink-muted">—</span>;
        return attachment(item.source, item.sourceId, item.attachment);
      } },
      { key: "actions", header: "", align: "right", render: (row) => {
        const item = row as CostLine;
        const owned = item.source === "expense" || item.source === "assetPurchase";
        if (!owned) {
          const label = OPEN_LABEL[item.source] ?? "Open";
          return (
            <Button
              iconOnly
              variant="outline"
              icon={<IconArrowUpRight size={16} />}
              aria-label={label}
              title={label}
              onClick={(event) => { event.stopPropagation(); onOpenCost(item); }}
            />
          );
        }
        const key = `${item.source}:${item.sourceId}`;
        return (
          <IconButtons
            editBusy={busyKey === `edit:${key}`}
            deleteBusy={busyKey === `delete:${key}`}
            onEdit={() => onOpenCost(item)}
            onDelete={() => onDeleteCost(item)}
          />
        );
      } },
    ];
  }

  if (view === "customerDeposits") {
    return [
      { key: "booking", header: "Booking", render: (row) => {
        const item = row as CustomerDepositLine;
        return <span>{item.bookingReference}<small className="block text-small text-ink-muted">{item.unitNumber}</small></span>;
      } },
      { key: "customer", header: "Customer", render: (row) => {
        const item = row as CustomerDepositLine;
        return <span>{item.customerName}<small className="block text-small text-ink-muted">{projectLabel(item.projectName)}</small></span>;
      } },
      { key: "status", header: "Current status", render: (row) => <StatusBadge status={(row as CustomerDepositLine).bookingStatus || "—"} /> },
      { key: "sale", header: "Net sale value", align: "right", render: (row) => money((row as CustomerDepositLine).netSaleValue) },
      { key: "cash", header: "Cash received", align: "right", render: (row) => money((row as CustomerDepositLine).customerCashReceived, "text-success") },
      { key: "held", header: "Deposit held", align: "right", render: (row) => money((row as CustomerDepositLine).depositBalance, "text-info") },
      { key: "cleared", header: "Cleared by", render: (row) => clearedBy(row as CustomerDepositLine) },
    ];
  }

  return [
    { key: "due", header: "Due date", render: (row) => <span className="font-bold text-danger">{formatDate((row as OverdueLine).dueDate)}</span> },
    { key: "booking", header: "Booking", render: (row) => (row as OverdueLine).bookingReference },
    { key: "customer", header: "Customer", render: (row) => (row as OverdueLine).customerName },
    { key: "project", header: "Project", render: (row) => projectLabel((row as OverdueLine).projectName) },
    { key: "unit", header: "Unit", render: (row) => (row as OverdueLine).unitNumber },
    { key: "installment", header: "Installment", render: (row) => {
      const item = row as OverdueLine;
      return `#${item.sequenceNumber} · ${item.installmentType}`;
    } },
    { key: "amount", header: "Amount", align: "right", render: (row) => money((row as OverdueLine).amount) },
    { key: "paid", header: "Paid", align: "right", render: (row) => money((row as OverdueLine).paidAmount, "text-success") },
    { key: "overdue", header: "Overdue", align: "right", render: (row) => money((row as OverdueLine).overdueAmount, "text-danger") },
  ];
}

function clearedBy(row: CustomerDepositLine) {
  if (row.recognitionDate) {
    return <span><span className="font-bold text-success">Possession</span>{" · "}{formatDate(row.recognitionDate)}</span>;
  }
  if (row.cancellationDate) {
    return <span><span className="font-bold text-danger">Cancelled</span>{" · "}{formatDate(row.cancellationDate)}</span>;
  }
  return <span className="text-ink-muted">—</span>;
}

function phoneFor(
  view: FinanceView,
  row: FinanceRow,
  attachment: (kind: FinanceRecordKind, id: number | null, file: FinanceAttachmentInfo | null, phone?: boolean) => ReactNode,
  busyKey: string | null,
  onEditRevenue: (row: RevenueLine) => void,
  onDeleteRevenue: (row: RevenueLine) => void,
  onOpenCost: (row: CostLine) => void,
  onDeleteCost: (row: CostLine) => void,
): ReactNode {
  if (view === "revenue") {
    const item = row as RevenueLine;
    const badge = sourceBadge(item.source);
    return (
      <article className="rounded-card border border-line bg-card p-4">
        <div className="flex items-start justify-between gap-3">
          <p className="m-0 font-extrabold text-ink">{item.revenueType}</p>
          {money(item.amount, "text-success")}
        </div>
        <p className="m-0 mt-1 text-small text-ink-muted">{projectLabel(item.projectName)} · {formatDate(item.date)}</p>
        <p className="m-0 mt-0.5 text-small text-ink-2">Received in {item.financeAccountName ?? "Unassigned"}</p>
        <div className="mt-3 flex flex-wrap items-center gap-2">
          <StatusBadge status={badge.label} tone={badge.tone}>{badge.label}</StatusBadge>
          {item.attachment && item.manualRevenueId != null && attachment("revenue", item.manualRevenueId, item.attachment, true)}
          {item.manualRevenueId != null && (
            <IconButtons onEdit={() => onEditRevenue(item)} onDelete={() => onDeleteRevenue(item)} />
          )}
        </div>
      </article>
    );
  }

  if (view === "totalExpenses") {
    const item = row as CostLine;
    const cost = formatCostAmount(item.amount);
    const owned = item.source === "expense" || item.source === "assetPurchase";
    const key = `${item.source}:${item.sourceId}`;
    const label = OPEN_LABEL[item.source];
    return (
      <article className="rounded-card border border-line bg-card p-4" onClick={() => onOpenCost(item)}>
        <div className="flex items-start justify-between gap-3">
          <p className="m-0 font-extrabold text-ink">{item.label}</p>
          <span className={`whitespace-nowrap font-bold tabular-nums ${cost.className}`}>{cost.text}</span>
        </div>
        <p className="m-0 mt-1 text-small text-ink-muted">{projectLabel(item.projectName)} · {formatDate(item.date)}</p>
        <div className="mt-3 flex flex-wrap items-center gap-2">
          {owned ? (
            <>
              {item.attachment ? attachment(item.source as FinanceRecordKind, item.sourceId, item.attachment, true) : <span className="text-small text-ink-muted">None</span>}
              <IconButtons
                editBusy={busyKey === `edit:${key}`}
                deleteBusy={busyKey === `delete:${key}`}
                onEdit={() => onOpenCost(item)}
                onDelete={() => onDeleteCost(item)}
              />
            </>
          ) : (
            <button
              type="button"
              className="cursor-pointer border-0 bg-transparent p-0 text-small font-bold text-primary"
              onClick={(event) => { event.stopPropagation(); onOpenCost(item); }}
            >
              {label} ↗
            </button>
          )}
        </div>
      </article>
    );
  }

  if (view === "customerDeposits") {
    const item = row as CustomerDepositLine;
    return (
      <article className="rounded-card border border-line bg-card p-4">
        <div className="flex items-center justify-between gap-3">
          <span className="text-small font-bold text-ink-muted">{item.bookingReference}</span>
          <StatusBadge status={item.bookingStatus || "—"} />
        </div>
        <p className="m-0 mt-1 font-extrabold text-ink">{item.customerName}</p>
        <p className="m-0 mt-0.5 text-small text-ink-muted">{item.unitNumber} · {projectLabel(item.projectName)}</p>
        <dl className="m-0 mt-3 grid grid-cols-2 gap-x-4 gap-y-2">
          <Field label="Net sale value" value={formatMoney(item.netSaleValue)} />
          <Field label="Cash received" value={formatMoney(item.customerCashReceived)} className="text-success" />
          <Field label="Deposit held" value={formatMoney(item.depositBalance)} className="text-info" />
          <div>
            <dt className="text-label font-bold uppercase tracking-[0.4px] text-ink-muted">Cleared by</dt>
            <dd className="m-0 mt-0.5 text-small">{clearedBy(item)}</dd>
          </div>
        </dl>
      </article>
    );
  }

  const item = row as OverdueLine;
  return (
    <article className="rounded-card border border-line bg-card p-4">
      <div className="flex items-center justify-between gap-3">
        <span className="text-small font-bold text-ink-muted">{item.bookingReference}</span>
        <span className="text-small font-bold text-danger">Due {formatDate(item.dueDate)}</span>
      </div>
      <p className="m-0 mt-1 font-extrabold text-ink">{item.customerName}</p>
      <p className="m-0 mt-0.5 text-small text-ink-muted">
        {item.unitNumber} · {projectLabel(item.projectName)} · Installment #{item.sequenceNumber} · {item.installmentType}
      </p>
      <dl className="m-0 mt-3 grid grid-cols-3 gap-2">
        <Field label="Amount" value={formatMoney(item.amount)} />
        <Field label="Paid" value={formatMoney(item.paidAmount)} className="text-success" />
        <Field label="Overdue" value={formatMoney(item.overdueAmount)} className="text-danger" />
      </dl>
    </article>
  );
}

function Field({ label, value, className }: { label: string; value: string; className?: string }) {
  return (
    <div>
      <dt className="text-label font-bold uppercase tracking-[0.4px] text-ink-muted">{label}</dt>
      <dd className={`m-0 mt-0.5 text-small font-bold ${className ?? "text-ink"}`}>{value}</dd>
    </div>
  );
}
