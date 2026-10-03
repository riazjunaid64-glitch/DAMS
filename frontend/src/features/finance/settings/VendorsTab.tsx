import { useCallback, useState, type ReactNode } from "react";
import {
  Button,
  DataTable,
  EmptyState,
  FilterBar,
  IconPencil,
  IconUsers,
  Notice,
  StatusBadge,
  statusLabel,
  useIsPhone,
  type DataTableColumn,
  type FilterDef,
} from "../../../components/ui";
import { usePagedList, type PagedListQuery } from "../../../lib/usePagedList.ts";
import { CardFigure, ListFooter } from "../ListParts.tsx";
import * as whtApi from "../whtApi.ts";
import { FILER_STATUSES, formatRs, type FilerStatus, type Vendor } from "../whtTypes.ts";
import { vendorTaxId } from "./rules.ts";
import { VendorFormDialog } from "./VendorFormDialog.tsx";

/** Today's rule: the search runs 250 ms after typing stops. */
const SEARCH_DEBOUNCE_MS = 250;
const FILTERS: readonly FilterDef[] = [
  { type: "select", key: "filerStatus", label: "Filer status", options: FILER_STATUSES.map(([value]) => ({ value, label: statusLabel(value) })) },
];
const money = "whitespace-nowrap tabular-nums";
const vendorKey = (vendor: Vendor) => vendor.id;

/** The vendor's name in bold, with a grey Inactive badge once it can't be picked for new expenses. */
function VendorName({ vendor }: { vendor: Vendor }) {
  return (
    <span className="inline-flex min-w-0 flex-wrap items-center gap-2">
      <b className="min-w-0 break-words font-extrabold text-ink">{vendor.name}</b>
      {!vendor.isActive && <StatusBadge status="Inactive" />}
    </span>
  );
}

type Props = {
  /** The header's Add vendor was pressed. */
  adding: boolean;
  onAddClose: () => void;
};

/**
 * Finance settings > Vendors: the suppliers paid often enough to keep, with the filer status that
 * picks their tax rate and what they were paid this financial year. The server pages, filters and
 * counts this list; twenty a page.
 */
export function VendorsTab({ adding, onAddClose }: Props) {
  const isPhone = useIsPhone();
  const [search, setSearch] = useState("");
  const [filerStatus, setFilerStatus] = useState<FilerStatus | "">("");
  const [editing, setEditing] = useState<Vendor | null>(null);

  const fetchPage = useCallback(async ({ skip, take, signal }: PagedListQuery) => {
    const page = await whtApi.listVendors({ search, filerStatus, skip, take }, signal);
    return { items: page.items, hasMore: page.hasMore, totalCount: page.totalCount ?? null };
  }, [search, filerStatus]);
  const list = usePagedList<Vendor>({ queryKey: `${search}|${filerStatus}`, fetchPage, itemKey: vendorKey });
  const failedEmpty = list.error !== null && list.rows.length === 0;
  const filtered = search !== "" || filerStatus !== "";

  const edit = (vendor: Vendor) => (
    <Button variant="outline" iconOnly icon={<IconPencil size={18} />} aria-label={`Edit ${vendor.name}`} onClick={() => setEditing(vendor)} />
  );

  const columns: DataTableColumn<Vendor>[] = [
    {
      key: "vendor",
      header: "Vendor",
      render: (vendor) => (
        <>
          <VendorName vendor={vendor} />
          <span className="block text-small text-ink-muted">{vendor.phone || "—"}</span>
        </>
      ),
    },
    { key: "status", header: "Filer status", render: (vendor) => <StatusBadge status={vendor.filerStatus} /> },
    { key: "taxId", header: "NTN / CNIC", render: (vendor) => <span className="whitespace-nowrap text-ink-2">{vendorTaxId(vendor) ?? <span className="text-ink-muted">Not recorded</span>}</span> },
    { key: "paid", header: "Paid this year", align: "right", render: (vendor) => <b className={`${money} font-extrabold`}>{formatRs(vendor.yearToDateGross)}</b> },
    { key: "tax", header: "Tax withheld", align: "right", render: (vendor) => <b className={`${money} font-extrabold`}>{formatRs(vendor.yearToDateWht)}</b> },
    { key: "payments", header: "Payments", align: "right", render: (vendor) => <span className="tabular-nums text-ink-muted">{vendor.paymentCount.toLocaleString("en-PK")}</span> },
    { key: "actions", header: <span className="sr-only">Actions</span>, align: "right", render: edit },
  ];

  const phoneCard = (vendor: Vendor): ReactNode => (
    <div className="rounded-card border border-line bg-card p-4 font-ui">
      <div className="flex items-start justify-between gap-3">
        <VendorName vendor={vendor} />
        <StatusBadge status={vendor.filerStatus} />
      </div>
      <p className="m-0 mt-0.5 text-small text-ink-muted">{vendorTaxId(vendor) ?? "Not recorded"} · {vendor.phone || "—"}</p>
      <div className="mt-3 grid grid-cols-3 gap-2">
        <CardFigure label="Paid this year">{formatRs(vendor.yearToDateGross)}</CardFigure>
        <CardFigure label="Tax withheld">{formatRs(vendor.yearToDateWht)}</CardFigure>
        <CardFigure label="Payments">{vendor.paymentCount.toLocaleString("en-PK")}</CardFigure>
      </div>
      <div className="mt-3">{edit(vendor)}</div>
    </div>
  );

  return (
    <div className="flex flex-col gap-3 md:gap-4">
      <FilterBar
        search={{ value: search, onSearch: setSearch, placeholder: "Name, NTN, CNIC or phone", debounceMs: SEARCH_DEBOUNCE_MS }}
        filters={FILTERS}
        values={{ filerStatus }}
        onChange={(changes) => setFilerStatus((changes.filerStatus ?? filerStatus) as FilerStatus | "")}
        onReset={() => { setSearch(""); setFilerStatus(""); }}
      />
      {list.error && <Notice tone="red" role="alert" title={list.error} action={<Button variant="outline" onClick={list.reload}>Try again</Button>} />}
      {!failedEmpty && (
        <DataTable
          columns={columns}
          rows={list.rows}
          rowKey={vendorKey}
          phoneCard={phoneCard}
          loading={list.loading}
          loadingCount={5}
          minWidth={960}
          caption="Vendors"
          empty={(
            <EmptyState
              icon={<IconUsers size={26} />}
              title={filtered ? "No vendors found." : "No vendors yet. Add the suppliers you pay regularly."}
            />
          )}
        />
      )}
      {!failedEmpty && (
        <ListFooter
          total={list.total ?? 0}
          isPhone={isPhone}
          page={list.page}
          onPage={list.setPage}
          shown={list.rows.length}
          onLoadMore={list.loadMore}
          loading={list.loadingMore}
        />
      )}
      {(adding || editing) && (
        <VendorFormDialog
          key={editing?.id ?? "new"}
          vendor={editing}
          onClose={() => { setEditing(null); onAddClose(); }}
          onSaved={list.reload}
        />
      )}
    </div>
  );
}
