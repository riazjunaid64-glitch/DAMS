import { useCallback, useState } from "react";
import { DataTable, EmptyState, FilterBar, StatusBadge, type DataTableColumn, type FilterValues } from "../../components/ui";
import { usePagedList, type PagedListQuery } from "../../lib/usePagedList.ts";
import { formatPkr } from "../../utils/currency.ts";
import { commissionRebateApi } from "./api.ts";
import { rebateWayLabel } from "./forms.ts";
import { Amount, BookingLink, CardFigure, ListFooter, LoadError, PhoneCard } from "./ListParts.tsx";
import type { Rebate } from "./types.ts";

const REBATE_STATUSES = [
  { value: "Pending", label: "Pending" },
  { value: "Applied", label: "Applied" },
  { value: "Paid", label: "Paid" },
  { value: "Cancelled", label: "Cancelled" },
  { value: "ReversalRequired", label: "Reversal required" },
  { value: "Reversed", label: "Reversed" },
];

const columns: DataTableColumn<Rebate>[] = [
  { key: "booking", header: "Booking", render: (row) => <BookingLink id={row.bookingId} reference={row.bookingReference} /> },
  { key: "customer", header: "Customer", render: (row) => row.customerName },
  {
    key: "reason",
    header: "Reason / method",
    className: "min-w-[240px]",
    render: (row) => (
      <>
        <span className="block">{row.reason}</span>
        <span className="block text-small text-ink-muted">{rebateWayLabel(row.method)}</span>
      </>
    ),
  },
  { key: "rebate", header: "Rebate", align: "right", render: (row) => <Amount value={row.finalAmount} strong /> },
  { key: "given", header: "Given", align: "right", render: (row) => <Amount value={row.appliedOrPaidAmount} /> },
  { key: "remaining", header: "Remaining", align: "right", render: (row) => <Amount value={row.outstandingAmount} /> },
  { key: "recovery", header: "Recovery", align: "right", render: (row) => <Amount value={row.recoveryRequiredAmount} /> },
  { key: "status", header: "Status", align: "right", render: (row) => <StatusBadge status={row.status} /> },
];

const phoneCard = (row: Rebate) => (
  <PhoneCard>
    <div className="flex items-start justify-between gap-3">
      <p className="m-0 min-w-0 text-section font-extrabold text-ink">{row.customerName}</p>
      <StatusBadge status={row.status} />
    </div>
    <p className="m-0 mt-1 text-small text-ink-2"><BookingLink id={row.bookingId} reference={row.bookingReference} /> · {row.reason}</p>
    <p className="m-0 text-small text-ink-2">{rebateWayLabel(row.method)}</p>
    <div className="mt-3 grid grid-cols-3 gap-x-2 gap-y-1">
      <CardFigure label="Rebate" value={row.finalAmount} strong />
      <CardFigure label="Given" value={row.appliedOrPaidAmount} />
      <CardFigure label="Remaining" value={row.outstandingAmount} />
    </div>
    {row.recoveryRequiredAmount > 0 && (
      <p className="m-0 mt-2 text-small font-bold text-danger">Recovery {formatPkr(row.recoveryRequiredAmount)}</p>
    )}
  </PhoneCard>
);

/** Every rebate owed to customers, newest first. Entering and giving them happens on the booking. */
export function RebatesList() {
  const [status, setStatus] = useState("");
  const fetchPage = useCallback(async ({ skip, take, signal }: PagedListQuery) => {
    const page = await commissionRebateApi.rebates(status, skip, take, signal);
    return { items: page.items, hasMore: page.hasMore, totalCount: page.totalCount ?? null };
  }, [status]);
  const list = usePagedList<Rebate>({ queryKey: status, fetchPage });
  const failedEmpty = list.error !== null && list.rows.length === 0;

  return (
    <>
      <FilterBar
        filters={[{ type: "select", key: "status", label: "Status", allLabel: "All statuses", options: REBATE_STATUSES }]}
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
          empty={<EmptyState title={status ? "No rebates match the current filter." : "No rebates yet. Add them from a booking."} />}
        />
      )}
      <ListFooter list={list} />
    </>
  );
}
