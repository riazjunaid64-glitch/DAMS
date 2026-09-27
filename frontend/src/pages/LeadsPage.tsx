import { useEffect, useState } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import type { User } from "../App.tsx";
import { can } from "../features/access/permissions.ts";
import {
  Avatar,
  Button,
  cx,
  DataTable,
  DEFAULT_PAGE_SIZE,
  EmptyState,
  FilterBar,
  IconPlus,
  IconSettings,
  IconUsers,
  LoadMore,
  PageHeader,
  Pagination,
  Spinner,
  StatSummary,
  StatusBadge,
  useIsPhone,
  useToast,
  type DataTableColumn,
  type FilterDef,
  type FilterValues,
  type StatCardProps,
} from "../components/ui";
import { formatMonthDay, formatTime, formatWhen } from "../lib/dates.ts";
import { HeldEnquiries } from "../features/leads/HeldEnquiries.tsx";
import { LeadCard } from "../features/leads/LeadCard.tsx";
import { leadStatus, paymentPreferenceLabel, purchaseIntentLabel, statusText, type LeadStatus } from "../features/leads/labels.ts";
import { apiJson, loadCrmLookups, type CrmLookups } from "../features/leads/leadApi.ts";
import { nextFollowUp } from "../features/leads/leadRow.ts";
import { NewLeadDialog } from "../features/leads/NewLeadDialog.tsx";
import type { LeadList, LeadListItem, LeadSummary } from "../features/leads/types.ts";

type Props = { user: User | null };

/** Every URL filter the list and the cards honour; Reset clears all of them. */
const FILTER_KEYS = ["search", "createdFrom", "createdTo", "stageGroup", "sourceId", "employeeId", "unassigned"];
/** Only Admin and Sales manager filter by owner; a Sales employee's list is already only theirs. */
const OWNER_KEYS = ["employeeId", "unassigned"];
const STATUSES: LeadStatus[] = ["InProgress", "Won", "Lost", "Dormant"];
const STATUS_TONES = { InProgress: "blue", Won: "green", Lost: "red", Dormant: "orange" } as const;
/** The Assigned filter's value for "nobody"; any other value is an employee id. */
const UNASSIGNED = "unassigned";

type Loaded<T> = { query: string; data: T };

/**
 * The last list and cards this account saw, so coming back from a lead shows them at once while
 * they refresh quietly behind.
 */
let lastSeen: { account: string; list?: Loaded<LeadList>; summary?: Loaded<LeadSummary> } | null = null;

export default function LeadsPage({ user }: Props) {
  if (!user || !can(user.role, "crm")) {
    return (
      <div className="mx-auto flex w-full max-w-[1500px] flex-col gap-4 px-4 py-5 md:gap-5 md:px-8 md:py-7">
        <EmptyState
          icon={<IconUsers size={26} />}
          title={user ? "The Lead CRM is not part of your role" : "Sign in required"}
          message={user ? undefined : "Sign in with a staff account to open the Lead CRM."}
        />
      </div>
    );
  }
  return <LeadsWorkspace user={user} />;
}

function LeadsWorkspace({ user }: { user: User }) {
  const navigate = useNavigate();
  const toast = useToast();
  const isPhone = useIsPhone();
  const [params, setParams] = useSearchParams();
  const account = `${user.userId}:${user.role}`;
  const manage = can(user.role, "crm.manage");
  const filterKeys = manage ? FILTER_KEYS : FILTER_KEYS.filter((key) => !OWNER_KEYS.includes(key));

  const pageParam = Number(params.get("page") ?? 1);
  const page = Number.isInteger(pageParam) && pageParam > 0 ? pageParam : 1;
  const filterQuery = new URLSearchParams();
  for (const key of filterKeys) {
    const value = params.get(key);
    if (value && key !== "stageGroup") filterQuery.set(key, value);
  }
  // The cards count by status, so they take every filter except Status itself.
  const summaryQuery = filterQuery.toString();
  const listParams = new URLSearchParams(filterQuery);
  if (params.get("stageGroup")) listParams.set("stageGroup", params.get("stageGroup")!);
  listParams.set("page", String(page));
  listParams.set("pageSize", String(DEFAULT_PAGE_SIZE));
  const listQuery = listParams.toString();

  const remembered = lastSeen?.account === account ? lastSeen : null;
  const [list, setList] = useState<Loaded<LeadList> | null>(remembered?.list ?? null);
  const [summary, setSummary] = useState<Loaded<LeadSummary> | null>(remembered?.summary ?? null);
  const [listFailure, setListFailure] = useState<Loaded<string> | null>(null);
  const [lookups, setLookups] = useState<CrmLookups | null>(null);
  // Bumped to fetch the list and cards again for the same filters (Try again, a held enquiry decided).
  const [refresh, setRefresh] = useState(0);
  const [createOpen, setCreateOpen] = useState(false);

  // A filter, search or page change fetches only the list and the cards, in parallel; the old rows
  // stay on screen until the new ones arrive. A response for filters no longer shown is dropped.
  useEffect(() => {
    const controller = new AbortController();
    apiJson<LeadList>(`/api/leads?${listQuery}`, { signal: controller.signal })
      .then((data) => {
        setList({ query: listQuery, data });
        setListFailure(null);
        lastSeen = { ...(lastSeen?.account === account ? lastSeen : {}), account, list: { query: listQuery, data } };
      })
      .catch((caught: unknown) => {
        if (controller.signal.aborted) return;
        setListFailure({ query: listQuery, data: caught instanceof Error ? caught.message : "The leads could not be loaded." });
      });
    return () => controller.abort();
  }, [account, listQuery, refresh]);

  useEffect(() => {
    const controller = new AbortController();
    apiJson<LeadSummary>(`/api/leads/summary?${summaryQuery}`, { signal: controller.signal })
      .then((data) => {
        setSummary({ query: summaryQuery, data });
        lastSeen = { ...(lastSeen?.account === account ? lastSeen : {}), account, summary: { query: summaryQuery, data } };
      })
      .catch((caught: unknown) => {
        if (!controller.signal.aborted) toast.error(caught instanceof Error ? caught.message : "The lead counts could not be loaded.");
      });
    return () => controller.abort();
  }, [account, summaryQuery, refresh, toast]);

  useEffect(() => {
    loadCrmLookups(account)
      .then(setLookups)
      .catch((caught: unknown) => toast.error(caught instanceof Error ? caught.message : "Sources and staff could not be loaded."));
  }, [account, toast]);

  // Phone lists grow with "Load more" instead of paging. The extra pages belong to the list they
  // extend, so a filter change drops them in the same render and the list restarts from the top.
  const [more, setMore] = useState<{ query: string; items: LeadListItem[]; page: number; loading: boolean } | null>(null);
  const extra = more?.query === listQuery ? more : null;
  const current = list?.query === listQuery ? list.data : null;
  const loadMore = async () => {
    if (!current) return;
    const nextPage = (extra?.page ?? current.page) + 1;
    const q = new URLSearchParams(listQuery);
    q.set("page", String(nextPage));
    setMore({ query: listQuery, items: extra?.items ?? [], page: extra?.page ?? current.page, loading: true });
    try {
      const next = await apiJson<LeadList>(`/api/leads?${q.toString()}`);
      setMore((shown) => shown?.query === listQuery ? { query: listQuery, items: [...shown.items, ...next.items], page: nextPage, loading: false } : shown);
    } catch (caught) {
      setMore((shown) => shown && { ...shown, loading: false });
      toast.error(caught instanceof Error ? caught.message : "More leads could not be loaded.");
    }
  };

  const updateParams = (changes: FilterValues) => {
    const next = new URLSearchParams(params);
    for (const [key, value] of Object.entries(changes)) {
      if (value) next.set(key, value); else next.delete(key);
    }
    if (!("page" in changes)) next.delete("page");
    setParams(next);
  };
  const hasFilters = filterKeys.some((key) => params.get(key));
  const resetFilters = () => updateParams(Object.fromEntries(FILTER_KEYS.map((key) => [key, ""])));
  const applyFilters = ({ assigned, ...changes }: FilterValues) => {
    if (assigned !== undefined) {
      changes.unassigned = assigned === UNASSIGNED ? "true" : "";
      changes.employeeId = assigned === UNASSIGNED ? "" : assigned;
    }
    updateParams(changes);
  };

  const stageGroup = params.get("stageGroup") ?? "";
  const counts = summary?.data;
  const count = (value?: number) => value?.toLocaleString("en-PK") ?? "—";
  const statusCards: StatCardProps[] = STATUSES.map((status) => ({
    label: statusText(status),
    value: count(counts?.[status === "InProgress" ? "inProgress" : (status.toLowerCase() as "won" | "lost" | "dormant")]),
    tone: STATUS_TONES[status],
    selected: stageGroup === status,
    onClick: () => updateParams({ stageGroup: stageGroup === status ? "" : status }),
  }));

  const statusFilter: FilterDef = { type: "select", key: "stageGroup", label: "Status", options: STATUSES.map((status) => ({ value: status, label: statusText(status) })) };
  const sourceFilter: FilterDef = { type: "select", key: "sourceId", label: "Source", options: (lookups?.sources ?? []).map((source) => ({ value: String(source.id), label: source.name })) };
  const fromFilter: FilterDef = { type: "date", key: "createdFrom", label: isPhone ? "Created from" : "From" };
  const toFilter: FilterDef = { type: "date", key: "createdTo", label: "To" };
  // The phone sheet leads with Status and Source; the owner filter is desktop only.
  const filters: FilterDef[] = isPhone
    ? [statusFilter, sourceFilter, fromFilter, toFilter]
    : [fromFilter, toFilter, statusFilter, sourceFilter, ...(manage ? [{
        type: "select" as const,
        key: "assigned",
        label: "Assigned",
        allLabel: "Anyone",
        options: [
          { value: UNASSIGNED, label: "Unassigned" },
          ...(lookups?.staff ?? []).filter((member) => member.canOwnLeads).map((member) => ({ value: String(member.employeeId), label: member.fullName })),
        ],
      }] : [])];
  const filterValues: FilterValues = {
    ...Object.fromEntries(FILTER_KEYS.map((key) => [key, params.get(key) ?? ""])),
    assigned: params.get("unassigned") === "true" ? UNASSIGNED : params.get("employeeId") ?? "",
  };

  const shown = current ? [...current.items, ...(isPhone && extra ? extra.items : [])] : [];
  // Rows from the previous filters stay, dimmed, while the new ones load.
  const rows = current ? shown : list?.data.items ?? [];
  const failure = listFailure?.query === listQuery ? listFailure.data : null;
  const loading = !current && !failure;
  const openLead = (lead: LeadListItem) => navigate(`/crm/leads/${lead.id}`);
  const retry = () => { setListFailure(null); setRefresh((n) => n + 1); };

  return (
    <div className="mx-auto flex w-full max-w-[1500px] flex-col gap-4 px-4 py-5 md:gap-5 md:px-8 md:py-7">
      <PageHeader
        title={manage ? "Leads" : "My leads"}
        subtitle={manage ? "Capture, assign, work and convert property enquiries." : "Leads assigned to you."}
        actions={
          <>
            {can(user.role, "crm.settings") && (isPhone
              ? <Button variant="outline" iconOnly icon={<IconSettings size={18} />} aria-label="CRM settings" onClick={() => navigate("/crm/settings")} />
              : <Button variant="outline" icon={<IconSettings size={16} />} onClick={() => navigate("/crm/settings")}>CRM settings</Button>)}
            <Button icon={<IconPlus size={16} />} onClick={() => setCreateOpen(true)}>New lead</Button>
          </>
        }
      />

      {manage && <HeldEnquiries onResolved={() => setRefresh((n) => n + 1)} />}

      <StatSummary total={{ label: manage ? "Total leads" : "My leads", value: count(counts?.total) }} items={statusCards} />

      <FilterBar
        search={{ value: params.get("search") ?? "", onSearch: (value) => updateParams({ search: value }), placeholder: "Name, phone, city" }}
        filters={filters}
        values={filterValues}
        onChange={applyFilters}
        onReset={resetFilters}
      />

      {failure && !rows.length ? (
        <EmptyState
          icon={<IconUsers size={26} />}
          title="The leads could not be loaded"
          message={failure}
          action={<Button variant="outline" onClick={retry}>Try again</Button>}
        />
      ) : !list ? (
        <div className="flex flex-col gap-2.5">{Array.from({ length: 6 }).map((_, i) => <div key={i} className="h-16 animate-pulse rounded-card bg-track" />)}</div>
      ) : current && current.totalCount === 0 ? (
        hasFilters ? (
          <EmptyState
            icon={<IconUsers size={26} />}
            title="No leads match these filters"
            message="Try a different date range or status, or clear the filters."
            action={<Button variant="outline" onClick={resetFilters}>Reset filters</Button>}
          />
        ) : (
          <EmptyState
            icon={<IconUsers size={26} />}
            title="No leads yet"
            message="New leads assigned to you will show up here."
            action={<Button variant="outline" icon={<IconPlus size={16} />} onClick={() => setCreateOpen(true)}>New lead</Button>}
          />
        )
      ) : (
        <>
          <div aria-busy={loading || undefined} className={cx("relative transition-opacity", (loading || failure) && "opacity-60")}>
            {loading && <Spinner className="absolute top-3 right-3 z-10 text-primary" />}
            {failure && (
              <div role="alert" className="mb-2.5 flex items-center justify-between gap-3 rounded-card border border-danger-line bg-danger-soft px-4 py-2.5 text-small font-bold text-danger">
                {failure}
                <Button variant="outline" size="sm" onClick={retry}>Try again</Button>
              </div>
            )}
            <DataTable
              caption={manage ? "Leads" : "My leads"}
              rows={rows}
              rowKey={(lead) => lead.id}
              columns={leadColumns(manage, openLead)}
              onRowClick={openLead}
              rowLabel={(lead) => `Open ${lead.fullName}`}
              minWidth={1100}
              phoneCard={(lead) => <LeadCard lead={lead} showOwner={manage} />}
            />
          </div>
          {current && (isPhone ? (
            <LoadMore
              shown={(current.page - 1) * current.pageSize + shown.length}
              total={current.totalCount}
              loading={extra?.loading ?? false}
              onLoadMore={() => void loadMore()}
            />
          ) : (
            <Pagination
              page={current.page}
              pageSize={current.pageSize}
              totalCount={current.totalCount}
              totalPages={current.totalPages}
              itemLabel="leads"
              onPageChange={(next) => updateParams({ page: String(next) })}
            />
          ))}
        </>
      )}

      {createOpen && (
        <NewLeadDialog
          onClose={() => setCreateOpen(false)}
          lookups={lookups}
          canAssign={manage}
          onOpenLead={(leadId) => navigate(`/crm/leads/${leadId}`)}
        />
      )}
    </div>
  );
}

const telHref = (phone: string) => `tel:${phone.replace(/[^\d+]/g, "")}`;
const stop = (event: { stopPropagation: () => void }) => event.stopPropagation();

/**
 * The table's columns. Admin and Sales manager see who owns each lead; a Sales employee, whose
 * leads are all their own, sees when each one is next due instead.
 */
function leadColumns(manage: boolean, openLead: (lead: LeadListItem) => void): DataTableColumn<LeadListItem>[] {
  const muted = "block text-small text-ink-muted";
  const columns: (DataTableColumn<LeadListItem> | false)[] = [
    {
      key: "lead",
      header: "Lead",
      render: (lead) => (
        <>
          <span className="block font-extrabold">{lead.fullName}</span>
          {lead.phone && <a href={telHref(lead.phone)} onClick={stop} className="block whitespace-nowrap text-small text-ink no-underline hover:underline">{lead.phone}</a>}
          <span className={cx(muted, "whitespace-nowrap")}>{lead.leadReference}</span>
        </>
      ),
    },
    { key: "city", header: "City", render: (lead) => lead.city || "—" },
    {
      key: "requirement",
      header: "Requirement",
      render: (lead) => {
        const payment = paymentPreferenceLabel(lead.paymentPreference);
        const buyingFor = purchaseIntentLabel(lead.purchaseIntent);
        if (!lead.propertyType && !payment && !buyingFor) return "—";
        return (
          <>
            {lead.propertyType && <span className="block font-extrabold">{lead.propertyType}</span>}
            <span className="mt-1 flex flex-wrap gap-1">
              {payment && <StatusBadge status={lead.paymentPreference} tone={payment.tone}>{payment.label}</StatusBadge>}
              {buyingFor && <StatusBadge status={lead.purchaseIntent} tone="grey">{buyingFor}</StatusBadge>}
            </span>
          </>
        );
      },
    },
    { key: "source", header: "Source", render: (lead) => lead.sourceName },
    { key: "status", header: "Status", render: (lead) => <StatusBadge status={leadStatus(lead)} /> },
    manage && {
      key: "assigned",
      header: "Assigned",
      render: (lead) => lead.assignedEmployeeName
        ? <span className="flex items-center gap-2"><Avatar name={lead.assignedEmployeeName} size={28} />{lead.assignedEmployeeName}</span>
        : <span className="font-bold text-danger">Unassigned</span>,
    },
    !manage && {
      key: "next",
      header: "Next follow-up",
      className: "max-w-[180px]",
      render: (lead) => {
        const due = nextFollowUp(lead);
        if (!due) return "—";
        return (
          <>
            <span className={cx("block font-bold", due.overdue && "text-danger")}>{due.text}</span>
            {lead.nextActionSummary && <span className={muted}>{lead.nextActionSummary}</span>}
          </>
        );
      },
    },
    {
      key: "activity",
      header: "Last activity",
      className: "max-w-[210px]",
      render: (lead) => lead.lastActivitySummary
        ? <>{lead.lastActivitySummary}{lead.lastActivityAt && <span className={muted}>{formatWhen(lead.lastActivityAt)}</span>}</>
        : "—",
    },
    {
      key: "created",
      header: "Created",
      className: "whitespace-nowrap",
      render: (lead) => <><span className="block font-bold">{formatMonthDay(lead.createdAt)}</span><span className={muted}>{formatTime(lead.createdAt)}</span></>,
    },
    {
      key: "details",
      header: <span className="sr-only">Details</span>,
      align: "right",
      render: (lead) => <Button variant="outline" size="sm" onClick={(event) => { event.stopPropagation(); openLead(lead); }}>Details</Button>,
    },
  ];
  return columns.filter((column): column is DataTableColumn<LeadListItem> => column !== false);
}
