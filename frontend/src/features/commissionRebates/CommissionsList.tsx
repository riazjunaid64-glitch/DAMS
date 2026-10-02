import { useCallback, useState } from "react";
import { DataTable, EmptyState, FilterBar, StatusBadge, type DataTableColumn, type FilterValues } from "../../components/ui";
import { usePagedList, type PagedListQuery } from "../../lib/usePagedList.ts";
import { formatPkr } from "../../utils/currency.ts";
import { commissionRebateApi } from "./api.ts";
import { basisName } from "./forms.ts";
import { Amount, BookingLink, CardFigure, ListFooter, LoadError, PhoneCard } from "./ListParts.tsx";
import type { Commission } from "./types.ts";

const COMMISSION_STATUSES = [
  { value: "Pending", label: "Pending" },
  { value: "Paid", label: "Paid" },
  { value: "Cancelled", label: "Cancelled" },
  { value: "ReversalRequired", label: "Reversal required" },
  { value: "Reversed", label: "Reversed" },
];

const basisLine = (commission: Commission) => `${formatPkr(commission.basisAmount)} · ${basisName(commission.calculationBasis)}`;
const ruleLine = (commission: Commission) => commission.ruleNameSnapshot ?? "Manual";

const columns: DataTableColumn<Commission>[] = [
  { key: "booking", header: "Booking", render: (row) => <BookingLink id={row.bookingId} reference={row.bookingReference} /> },
  { key: "partner", header: "Partner", render: (row) => row.partnerName },
  {
    key: "rule",
    header: "Rule / basis",
    className: "min-w-[260px]",
    render: (row) => (
      <>
        <span className="block">{ruleLine(row)}</span>
        <span className="block text-small text-ink-muted">{basisLine(row)}</span>
      </>
    ),
  },
  { key: "commission", header: "Commission", align: "right", render: (row) => <Amount value={row.finalAmount} strong /> },
  { key: "paid", header: "Paid", align: "right", render: (row) => <Amount value={row.paidAmount} /> },
  { key: "remaining", header: "Remaining", align: "right", render: (row) => <Amount value={row.outstandingAmount} /> },
  { key: "recovery", header: "Recovery", align: "right", render: (row) => <Amount value={row.recoveryRequiredAmount} /> },
  { key: "status", header: "Status", align: "right", render: (row) => <StatusBadge status={row.status} /> },
];

const phoneCard = (row: Commission) => (
  <PhoneCard>
    <div className="flex items-start justify-between gap-3">
      <p className="m-0 min-w-0 text-section font-extrabold text-ink">{row.partnerName}</p>
      <StatusBadge status={row.status} />
    </div>
    <p className="m-0 mt-1 text-small text-ink-2"><BookingLink id={row.bookingId} reference={row.bookingReference} /> · {ruleLine(row)}</p>
    <p className="m-0 text-small text-ink-2">{basisLine(row)}</p>
    <div className="mt-3 grid grid-cols-3 gap-x-2 gap-y-1">
      <CardFigure label="Amount" value={row.finalAmount} strong />
      <CardFigure label="Paid" value={row.paidAmount} />
      <CardFigure label="Remaining" value={row.outstandingAmount} />
    </div>
    {row.recoveryRequiredAmount > 0 && (
      <p className="m-0 mt-2 text-small font-bold text-danger">Recovery {formatPkr(row.recoveryRequiredAmount)}</p>
    )}
  </PhoneCard>
);

/** Every commission owed to partners, newest first. Pay, edit, cancel and reverse live on the booking. */
export function CommissionsList() {
  const [status, setStatus] = useState("");
  const fetchPage = useCallback(async ({ skip, take, signal }: PagedListQuery) => {
    const page = await commissionRebateApi.commissions(status, skip, take, signal);
    return { items: page.items, hasMore: page.hasMore, totalCount: page.totalCount ?? null };
  }, [status]);
  const list = usePagedList<Commission>({ queryKey: status, fetchPage });
  const failedEmpty = list.error !== null && list.rows.length === 0;

  return (
    <>
      <FilterBar
        filters={[{ type: "select", key: "status", label: "Status", allLabel: "All statuses", options: COMMISSION_STATUSES }]}
        values={{ status }}
        onChange={(changes: FilterValues) => setStatus(changes.status ?? "")}
        onReset={() => setStatus("")}
      />
      {list.error && <LoadError message="Financial records could not be loaded." onRetry={list.reload} />}
      {!failedEmpty && (
        <DataTable
          columns={columns}
          rows={list.rows}
          rowKey={(row) => row.id}
          loading={list.loading}
          minWidth={980}
          phoneCard={phoneCard}
          empty={<EmptyState title={status ? "No commissions match the current filter." : "No commissions yet. Add them from a booking."} />}
        />
      )}
      <ListFooter list={list} />
    </>
  );
}
