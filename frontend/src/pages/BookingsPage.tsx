import { useEffect, useState, type ReactNode } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import type { User } from "../App.tsx";
import {
  Button,
  DataTable,
  EmptyState,
  FilterBar,
  IconAlert,
  IconCalendarCheck,
  IconCheck,
  IconPlus,
  IconPrinter,
  IconSearch,
  LoadMore,
  PageHeader,
  Pagination,
  Spinner,
  StatSummary,
  StatusBadge,
  cx,
  useIsPhone,
  useToast,
  type DataTableColumn,
  type FilterDef,
  type FilterValues,
  type StatCardProps,
} from "../components/ui";
import { DEFAULT_PAGE_SIZE } from "../components/ui/pageItems.ts";
import { can } from "../features/access/permissions.ts";
import { formatPhone } from "../features/bookings/format.ts";
import { BOOKING_STATUSES, BOOKING_STATUS_OPTIONS, termsNotSet } from "../features/bookings/statusNames.ts";
import { apiJson } from "../features/leads/leadApi.ts";
import { useProjects } from "../contexts/projectsContextValue.ts";
import { formatDay } from "../lib/dates.ts";
import { formatPkr } from "../utils/currency.ts";
import { statusLabel, statusTone } from "../components/ui/statusTone.ts";

type Props = { user: User | null };

interface BookingRow {
  id: number;
  bookingReference: string;
  customerName: string;
  customerPhone: string;
  projectName: string;
  unitNumber: string;
  status: string;
  agreedSalePrice: number;
  bookingAmountReceived: number;
  bookingAmountRequired: number;
  bookingDate: string;
}

interface BookingList {
  items: BookingRow[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

interface StatusCounts {
  total: number;
  awaitingBookingAmount: number;
  paymentPlanActive: number;
  possessionGiven: number;
  saleCompleted: number;
  cancelled: number;
}

const COUNT_KEYS = {
  AwaitingBookingAmount: "awaitingBookingAmount",
  PaymentPlanActive: "paymentPlanActive",
  PossessionGiven: "possessionGiven",
  SaleCompleted: "saleCompleted",
} as const satisfies Record<(typeof BOOKING_STATUSES)[number], keyof StatusCounts>;

/** Every URL filter the list and the cards honour; Reset clears all of them. */
const FILTER_KEYS = ["search", "status", "projectId"];

type Loaded<T> = { query: string; data: T };

export default function BookingsPage({ user }: Props) {
  if (!user || !can(user.role, "bookings")) {
    return (
      <div className="mx-auto flex w-full max-w-[1500px] flex-col gap-4 px-4 py-5 md:px-8 md:py-7">
        <EmptyState
          icon={<IconCalendarCheck size={26} />}
          title={user ? "Bookings are not part of your role" : "Sign in required"}
          message={user ? undefined : "Sign in with a staff account to open Bookings."}
        />
      </div>
    );
  }
  return <BookingsWorkspace />;
}

function BookingsWorkspace() {
  const navigate = useNavigate();
  const toast = useToast();
  const isPhone = useIsPhone();
  const { projects } = useProjects();
  const [params, setParams] = useSearchParams();

  const pageParam = Number(params.get("page") ?? 1);
  const page = Number.isInteger(pageParam) && pageParam > 0 ? pageParam : 1;
  // The cards count by status, so they take the search and project but not Status itself.
  const summaryParams = new URLSearchParams();
  for (const key of ["search", "projectId"]) {
    const value = params.get(key);
    if (value) summaryParams.set(key, value);
  }
  const summaryQuery = summaryParams.toString();
  const listParams = new URLSearchParams(summaryParams);
  if (params.get("status")) listParams.set("status", params.get("status")!);
  listParams.set("page", String(page));
  listParams.set("pageSize", String(DEFAULT_PAGE_SIZE));
  const listQuery = listParams.toString();

  const [list, setList] = useState<Loaded<BookingList> | null>(null);
  const [summary, setSummary] = useState<Loaded<StatusCounts> | null>(null);
  const [failure, setFailure] = useState<Loaded<string> | null>(null);
  // Bumped to fetch the list and cards again for the same filters (Try again).
  const [refresh, setRefresh] = useState(0);

  // A filter, search or page change fetches only the list and the cards, in parallel; the old rows
  // stay on screen until the new ones arrive. A response for filters no longer shown is dropped.
  useEffect(() => {
    const controller = new AbortController();
    apiJson<BookingList>(`/api/Booking?${listQuery}`, { signal: controller.signal })
      .then((data) => {
        setList({ query: listQuery, data });
        setFailure(null);
      })
      .catch((caught: unknown) => {
        if (controller.signal.aborted) return;
        setFailure({ query: listQuery, data: caught instanceof Error ? caught.message : "The bookings could not be loaded." });
      });
    return () => controller.abort();
  }, [listQuery, refresh]);

  useEffect(() => {
    const controller = new AbortController();
    apiJson<StatusCounts>(`/api/Booking/summary?${summaryQuery}`, { signal: controller.signal })
      .then((data) => setSummary({ query: summaryQuery, data }))
      .catch((caught: unknown) => {
        if (!controller.signal.aborted) toast.error(caught instanceof Error ? caught.message : "The booking counts could not be loaded.");
      });
    return () => controller.abort();
  }, [summaryQuery, refresh, toast]);

  // Phone lists grow with "Load more" instead of paging. The extra pages belong to the list they
  // extend, so a filter change drops them in the same render and the list restarts from the top.
  const [more, setMore] = useState<{ query: string; items: BookingRow[]; page: number; loading: boolean } | null>(null);
  const extra = more?.query === listQuery ? more : null;
  const current = list?.query === listQuery ? list.data : null;
  const loadMore = async () => {
    if (!current) return;
    const nextPage = (extra?.page ?? current.page) + 1;
    const next = new URLSearchParams(listQuery);
    next.set("page", String(nextPage));
    setMore({ query: listQuery, items: extra?.items ?? [], page: extra?.page ?? current.page, loading: true });
    try {
      const data = await apiJson<BookingList>(`/api/Booking?${next.toString()}`);
      setMore((shown) => shown?.query === listQuery ? { query: listQuery, items: [...shown.items, ...data.items], page: nextPage, loading: false } : shown);
    } catch (caught) {
      setMore((shown) => shown && { ...shown, loading: false });
      toast.error(caught instanceof Error ? caught.message : "More bookings could not be loaded.");
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
  const hasFilters = FILTER_KEYS.some((key) => params.get(key));
  const resetFilters = () => updateParams(Object.fromEntries(FILTER_KEYS.map((key) => [key, ""])));

  const status = params.get("status") ?? "";
  const counts = summary?.data;
  const count = (value?: number) => value?.toLocaleString("en-PK") ?? "—";
  const statusCards: StatCardProps[] = BOOKING_STATUSES.map((value) => ({
    label: statusLabel(value),
    value: count(counts?.[COUNT_KEYS[value]]),
    tone: statusTone(value),
    selected: status === value,
    onClick: () => updateParams({ status: status === value ? "" : value }),
  }));

  const filters: FilterDef[] = [
    { type: "select", key: "status", label: "Status", options: BOOKING_STATUS_OPTIONS },
    { type: "select", key: "projectId", label: "Project", options: projects.map((project) => ({ value: String(project.id), label: project.projectName })) },
  ];
  const filterValues: FilterValues = Object.fromEntries(FILTER_KEYS.map((key) => [key, params.get(key) ?? ""]));

  const shown = current ? [...current.items, ...(isPhone && extra ? extra.items : [])] : [];
  // Rows from the previous filters stay, dimmed, while the new ones load.
  const rows = current ? shown : list?.data.items ?? [];
  const failed = failure?.query === listQuery ? failure.data : null;
  const loading = !current && !failed;
  const openBooking = (booking: BookingRow) => navigate(`/confirmed-bookings/${booking.id}`);
  const retry = () => { setFailure(null); setRefresh((n) => n + 1); };

  return (
    <div className="mx-auto flex w-full max-w-[1500px] flex-col gap-4 px-4 py-5 md:gap-5 md:px-8 md:py-7">
      <PageHeader
        title="Bookings"
        actions={isPhone ? (
          <>
            <Button variant="outline" iconOnly icon={<IconPrinter size={18} />} aria-label="Print blank form" onClick={() => navigate("/application-form")} />
            <Button iconOnly icon={<IconPlus size={18} />} aria-label="New booking" onClick={() => navigate("/confirmed-bookings/new")} />
          </>
        ) : (
          <>
            <Button variant="outline" icon={<IconPrinter size={16} />} onClick={() => navigate("/application-form")}>Print blank form</Button>
            <Button icon={<IconPlus size={16} />} onClick={() => navigate("/confirmed-bookings/new")}>New booking</Button>
          </>
        )}
        className="max-md:flex-row max-md:items-center max-md:justify-between"
      />

      <StatSummary
        total={{ label: "Total bookings", value: count(counts?.total), selected: status === "", onClick: () => updateParams({ status: "" }) }}
        items={statusCards}
      />

      <FilterBar
        search={{ value: params.get("search") ?? "", onSearch: (value) => updateParams({ search: value }), placeholder: isPhone ? "Reference, name, phone, unit" : "Reference, customer, phone or unit" }}
        filters={filters}
        values={filterValues}
        onChange={updateParams}
        onReset={resetFilters}
      />

      {failed && !rows.length ? (
        <EmptyState
          icon={<span className="flex size-12 items-center justify-center rounded-field bg-danger-soft text-danger"><IconAlert size={24} /></span>}
          title="Bookings could not be loaded"
          message={failed}
          action={<Button variant="outline" onClick={retry}>Try again</Button>}
        />
      ) : !list ? (
        <div className="flex flex-col gap-2.5">{Array.from({ length: 3 }).map((_, i) => <div key={i} className="h-16 animate-pulse rounded-card bg-track" />)}</div>
      ) : current && current.totalCount === 0 ? (
        <EmptyState
          icon={<span className="flex size-12 items-center justify-center rounded-field bg-selected text-ink"><IconSearch size={22} /></span>}
          title={hasFilters ? "No bookings match these filters" : "No bookings yet"}
          message={hasFilters ? undefined : "New bookings will show up here."}
          // The desktop filter bar has its own Reset link; only the phone, with no bar, needs a button here.
          action={hasFilters && isPhone ? <Button variant="outline" onClick={resetFilters}>Reset filters</Button> : undefined}
        />
      ) : (
        <>
          <div aria-busy={loading || undefined} className={cx("relative transition-opacity", (loading || failed) && "opacity-60")}>
            {loading && <Spinner className="absolute top-3 right-3 z-10 text-primary" />}
            {failed && (
              <div role="alert" className="mb-2.5 flex items-center justify-between gap-3 rounded-card border border-danger-line bg-danger-soft px-4 py-2.5 text-small font-bold text-danger">
                {failed}
                <Button variant="outline" size="sm" onClick={retry}>Try again</Button>
              </div>
            )}
            <DataTable
              caption="Bookings"
              rows={rows}
              rowKey={(booking) => booking.id}
              columns={bookingColumns(openBooking)}
              onRowClick={openBooking}
              rowLabel={(booking) => `Open ${booking.bookingReference}`}
              minWidth={1000}
              phoneCard={(booking) => <BookingCard booking={booking} onOpen={() => openBooking(booking)} />}
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
              itemLabel="bookings"
              onPageChange={(next) => updateParams({ page: String(next) })}
            />
          ))}
        </>
      )}
    </div>
  );
}

const NOT_SET = <span className="font-normal text-ink-faint">Not set</span>;

function agreedPrice(booking: BookingRow): ReactNode {
  return termsNotSet(booking) ? <span className="font-extrabold text-ink">Not set</span> : <span className="font-extrabold text-ink">{formatPkr(booking.agreedSalePrice)}</span>;
}

/**
 * The booking amount in its four states: terms not set, fully received (with a green tick), or
 * part received (progress bar and what is still due, which a cancelled booking no longer owes).
 */
function BookingAmount({ booking }: { booking: BookingRow }) {
  const { bookingAmountReceived: received, bookingAmountRequired: required } = booking;
  if (termsNotSet(booking) || (required === 0 && received === 0)) return NOT_SET;
  if (required === 0 || received >= required) {
    return (
      <>
        <span className="block font-extrabold text-ink">{formatPkr(received)}</span>
        {required > 0 && (
          <span className="flex items-center gap-1 text-small font-bold text-success"><IconCheck size={14} />Received</span>
        )}
      </>
    );
  }
  const due = required - received;
  return (
    <>
      <span className="block font-extrabold text-ink">{formatPkr(received)} <span className="font-normal text-ink-muted">of {formatPkr(required)}</span></span>
      <span className="mt-1 block h-1 w-full min-w-28 overflow-hidden rounded-full bg-line-soft">
        <span className="block h-full rounded-full bg-gold" style={{ width: `${Math.min(100, Math.max(0, (received / required) * 100))}%` }} />
      </span>
      {booking.status !== "Cancelled" && <span className="mt-0.5 block text-small font-bold text-warning">{formatPkr(due)} due</span>}
    </>
  );
}

function bookingColumns(open: (booking: BookingRow) => void): DataTableColumn<BookingRow>[] {
  return [
    {
      key: "booking",
      header: "Booking",
      render: (booking) => (
        <>
          <span className="block font-extrabold text-ink">{booking.bookingReference}</span>
          <span className="block text-small text-ink-muted">{formatDay(booking.bookingDate)}</span>
        </>
      ),
    },
    {
      key: "customer",
      header: "Customer",
      render: (booking) => (
        <>
          <span className="block font-extrabold text-ink">{booking.customerName}</span>
          <span className="block text-small text-ink-muted">{formatPhone(booking.customerPhone)}</span>
        </>
      ),
    },
    {
      key: "unit",
      header: "Unit",
      render: (booking) => (
        <>
          <span className="block font-extrabold text-ink">Unit {booking.unitNumber}</span>
          <span className="block text-small text-ink-muted">{booking.projectName}</span>
        </>
      ),
    },
    { key: "price", header: "Agreed price", render: agreedPrice },
    { key: "amount", header: "Booking amount", render: (booking) => <BookingAmount booking={booking} /> },
    { key: "status", header: "Status", render: (booking) => <StatusBadge status={booking.status} /> },
    {
      key: "open",
      header: <span className="sr-only">Open</span>,
      align: "right",
      render: (booking) => (
        <Button
          variant="outline"
          size="sm"
          aria-label={`Details of ${booking.bookingReference}`}
          onClick={(event) => { event.stopPropagation(); open(booking); }}
        >
          Details
        </Button>
      ),
    },
  ];
}

const caption = "text-caption font-bold uppercase tracking-[0.4px] text-ink-muted";

function BookingCard({ booking, onOpen }: { booking: BookingRow; onOpen: () => void }) {
  return (
    <button
      type="button"
      onClick={onOpen}
      className="block w-full cursor-pointer rounded-card border border-line bg-card p-4 text-left font-ui focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary"
    >
      <span className="flex items-center justify-between gap-2">
        <span className="min-w-0 truncate text-small font-bold text-ink-2">{booking.bookingReference}</span>
        <StatusBadge status={booking.status} />
      </span>
      <span className="mt-1.5 block text-section font-extrabold text-ink">{booking.customerName}</span>
      <span className="block text-small text-ink-muted">{formatPhone(booking.customerPhone)} · Booked {formatDay(booking.bookingDate)}</span>
      <span className="mt-3 flex items-center gap-2 rounded-field bg-page px-3 py-2 text-small">
        <IconCalendarCheck size={16} className="shrink-0 text-ink-faint" />
        <span className="font-extrabold text-ink">Unit {booking.unitNumber}</span>
        <span className="truncate text-ink-muted">· {booking.projectName}</span>
      </span>
      <span className="mt-3 grid grid-cols-2 gap-4">
        <span className="block min-w-0">
          <span className={caption}>Agreed price</span>
          <span className="mt-0.5 block text-body">{agreedPrice(booking)}</span>
        </span>
        <span className="block min-w-0">
          <span className={caption}>Booking amount</span>
          <span className="mt-0.5 block text-body"><BookingAmount booking={booking} /></span>
        </span>
      </span>
    </button>
  );
}
