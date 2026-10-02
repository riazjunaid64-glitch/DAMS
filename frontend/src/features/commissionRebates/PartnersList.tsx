import { useCallback, useEffect, useState } from "react";
import { ActionsMenu, BottomSheet, Button, DataTable, EmptyState, FilterBar, IconMore, IconPencil, StatusBadge, useIsPhone, useToast, type DataTableColumn, type FilterValues } from "../../components/ui";
import { usePagedList, type PagedListQuery } from "../../lib/usePagedList.ts";
import { commissionRebateApi } from "./api.ts";
import { ListFooter, LoadError, PhoneCard } from "./ListParts.tsx";
import { PartnerFormDialog } from "./PartnerFormDialog.tsx";
import { PartnerStatusDialogs } from "./PartnerStatusDialogs.tsx";
import type { Partner } from "./types.ts";

const STATUS_OPTIONS = [{ value: "active", label: "Active" }, { value: "inactive", label: "Inactive" }];
/** Inactive partners stay out of the way until asked for. */
const DEFAULT_STATUS = "active";
/** KAN-97: the search waits 250 ms after typing stops. */
const SEARCH_DEBOUNCE_MS = 250;
const SEARCH_HINT = "Partner, code, contact or tax ID";

const usedIn = (partner: Partner) => `${partner.commissionCount} ${partner.commissionCount === 1 ? "commission" : "commissions"}`;
const taxId = (partner: Partner) => partner.cnic ?? partner.ntn;
const phoneOrEmail = (partner: Partner) => partner.phone ?? partner.email;

type Props = {
  /** The Add partner button lives in the page header; this opens the form from there. */
  adding: boolean;
  onAddClose: () => void;
  /** A partner was saved, activated or deactivated: the cards above the tabs need new figures. */
  onChanged: () => void;
  /** `?partner=` from a link: opens that partner's Edit popup. */
  openPartnerId: number | null;
  onOpened: () => void;
};

/** The partner directory: search, status, Edit, and Deactivate / Reactivate in the ⋯ menu. */
export function PartnersList({ adding, onAddClose, onChanged, openPartnerId, onOpened }: Props) {
  const isPhone = useIsPhone();
  const toast = useToast();
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState(DEFAULT_STATUS);
  const [editing, setEditing] = useState<Partner | null>(null);
  const [statusAction, setStatusAction] = useState<{ partner: Partner; active: boolean } | null>(null);
  const [menuFor, setMenuFor] = useState<Partner | null>(null);
  const [lookupFailed, setLookupFailed] = useState(false);
  const [lookupAttempt, setLookupAttempt] = useState(0);

  const fetchPage = useCallback(async ({ skip, take, signal }: PagedListQuery) => {
    const isActive = status === "active" ? true : status === "inactive" ? false : undefined;
    const page = await commissionRebateApi.partners(search, isActive, skip, take, signal);
    return { items: page.items, hasMore: page.hasMore, totalCount: page.totalCount ?? null };
  }, [search, status]);
  const list = usePagedList<Partner>({ queryKey: `${search}|${status}`, fetchPage });
  const failedEmpty = list.error !== null && list.rows.length === 0;

  // A link names a partner by id: fetch exactly that one. The link's parameter is dropped only once the
  // answer is known (found, or no such partner); a failed lookup keeps it so Try again can ask again.
  useEffect(() => {
    if (openPartnerId === null) return;
    let cancelled = false;
    commissionRebateApi.partner(openPartnerId)
      .then((found) => {
        if (cancelled) return;
        if (found) setEditing(found); else toast.error("That partner could not be found.");
        onOpened();
      })
      .catch(() => { if (!cancelled) setLookupFailed(true); });
    return () => { cancelled = true; };
  }, [openPartnerId, lookupAttempt, onOpened, toast]);

  const changed = () => {
    list.reload();
    onChanged();
  };
  // A status change can take the row out of the filtered list, which may empty the last page.
  const statusChanged = () => {
    list.afterDelete();
    onChanged();
  };

  const statusItem = (partner: Partner) => partner.isActive
    ? { label: "Deactivate", danger: true, onSelect: () => setStatusAction({ partner, active: false }) }
    : { label: "Reactivate", onSelect: () => setStatusAction({ partner, active: true }) };

  const actions = (partner: Partner) => (
    <span className="inline-flex items-center justify-end gap-2">
      <Button iconOnly variant="outline" icon={<IconPencil size={16} />} aria-label={`Edit ${partner.name}`} onClick={() => setEditing(partner)} />
      {isPhone ? (
        <Button iconOnly variant="outline" icon={<IconMore size={18} />} aria-label={`More for ${partner.name}`} onClick={() => setMenuFor(partner)} />
      ) : (
        <ActionsMenu trigger="dots" aria-label={`More for ${partner.name}`} items={[statusItem(partner)]} />
      )}
    </span>
  );

  const columns: DataTableColumn<Partner>[] = [
    {
      key: "partner",
      header: "Partner",
      render: (row) => (
        <>
          <span className="block font-bold">{row.name}</span>
          <span className="block text-small text-ink-muted">{row.internalCode}</span>
        </>
      ),
    },
    { key: "type", header: "Type", render: (row) => row.partnerType },
    {
      key: "contact",
      header: "Contact",
      render: (row) => row.contactPerson ? (
        <>
          <span className="block font-bold">{row.contactPerson}</span>
          {phoneOrEmail(row) && <span className="block text-small text-ink-muted">{phoneOrEmail(row)}</span>}
        </>
      ) : <span className="font-bold">{phoneOrEmail(row) ?? "—"}</span>,
    },
    { key: "tax", header: "CNIC / NTN", render: (row) => taxId(row) ?? <span className="text-ink-faint">—</span> },
    { key: "used", header: "Used in", render: (row) => usedIn(row) },
    { key: "status", header: "Status", render: (row) => <StatusBadge status={row.isActive ? "Active" : "Inactive"} /> },
    { key: "actions", header: <span className="sr-only">Actions</span>, align: "right", render: actions },
  ];

  const phoneCard = (row: Partner) => (
    <PhoneCard aside={actions(row)}>
      <div className="flex flex-wrap items-center gap-2">
        <p className="m-0 text-section font-extrabold text-ink">{row.name}</p>
        <StatusBadge status={row.isActive ? "Active" : "Inactive"} />
      </div>
      <p className="m-0 mt-1 text-small text-ink-2">{row.partnerType} · {row.internalCode} · {usedIn(row)}</p>
      {(row.contactPerson || phoneOrEmail(row)) && (
        <p className="m-0 text-small text-ink-2">{[row.contactPerson, phoneOrEmail(row)].filter(Boolean).join(" ")}</p>
      )}
      <p className="m-0 text-small text-ink-2">CNIC / NTN {taxId(row) ?? "—"}</p>
    </PhoneCard>
  );

  const menuItem = menuFor && statusItem(menuFor);

  return (
    <>
      <FilterBar
        search={{ value: search, onSearch: setSearch, placeholder: SEARCH_HINT, debounceMs: SEARCH_DEBOUNCE_MS }}
        filters={[{ type: "select", key: "status", label: "Status", allLabel: "All", options: STATUS_OPTIONS }]}
        values={{ status }}
        defaults={{ status: DEFAULT_STATUS }}
        onChange={(changes: FilterValues) => setStatus(changes.status ?? "")}
        onReset={() => { setSearch(""); setStatus(DEFAULT_STATUS); }}
      />
      {lookupFailed && openPartnerId !== null && (
        <LoadError message="That partner could not be opened." onRetry={() => { setLookupFailed(false); setLookupAttempt((attempt) => attempt + 1); }} />
      )}
      {list.error && <LoadError message="Partners could not be loaded." onRetry={list.reload} />}
      {!failedEmpty && (
        <DataTable
          columns={columns}
          rows={list.rows}
          rowKey={(row) => row.id}
          loading={list.loading}
          minWidth={1000}
          phoneCard={phoneCard}
          empty={<EmptyState title="No partners match the current filters." />}
        />
      )}
      <ListFooter list={list} />

      {(adding || editing) && (
        <PartnerFormDialog
          key={editing?.id ?? "new"}
          partner={editing}
          onClose={() => { setEditing(null); onAddClose(); }}
          onSaved={changed}
        />
      )}
      <PartnerStatusDialogs action={statusAction} onClose={() => setStatusAction(null)} onChanged={statusChanged} />
      <BottomSheet open={menuFor !== null} onClose={() => setMenuFor(null)} title={menuFor ? `${menuFor.name} · ${menuFor.internalCode}` : ""} footer={null}>
        {menuFor && menuItem && (
          <button
            type="button"
            onClick={() => { setMenuFor(null); menuItem.onSelect(); }}
            className={`w-full cursor-pointer border-0 bg-transparent p-0 py-2 text-left text-section font-extrabold ${menuItem.danger ? "text-danger" : "text-ink"}`}
          >
            {menuItem.label} partner
          </button>
        )}
      </BottomSheet>
    </>
  );
}
