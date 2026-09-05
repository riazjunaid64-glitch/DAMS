import AppSelect from "../lib/AppSelect.tsx";
import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { api } from "../api/api.ts";
import type { User } from "../App.tsx";
import Button from "../lib/Button.tsx";
import Container from "../lib/Container.tsx";
import Pagination from "../lib/Pagination.tsx";

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

// Live stages wear the house gold; the two that end a booking's journey get a colour of their own
// so a completed sale and a cancelled one are told apart at a glance rather than read.
const STATUS_STYLE: Record<string, string> = {
  AwaitingBookingAmount: "text-[var(--accent-light)] bg-[var(--accent-glow)] border-[var(--border-hover)]",
  PaymentPlanActive: "text-[var(--accent-light)] bg-[var(--accent-glow)] border-[var(--border-hover)]",
  PossessionGiven: "text-sky-300 bg-sky-500/10 border-sky-400/30",
  SaleCompleted: "text-emerald-300 bg-emerald-500/10 border-emerald-400/30",
  Cancelled: "text-rose-300 bg-rose-500/10 border-rose-400/30",
};

const STATUS_LABEL: Record<string, string> = {
  AwaitingBookingAmount: "Awaiting Booking Amount",
  PaymentPlanActive: "Payment Plan Active",
  PossessionGiven: "Possession Given",
  SaleCompleted: "Sale Completed",
  Cancelled: "Cancelled",
};

function formatMoney(n: number) {
  return n.toLocaleString(undefined, { minimumFractionDigits: 0, maximumFractionDigits: 2 });
}

export default function ConfirmedBookingsPage({ user }: Props) {
  const navigate = useNavigate();
  const [bookings, setBookings] = useState<BookingRow[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState("");
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);

  const isAdmin = user?.role === "Admin";

  const [debouncedSearch, setDebouncedSearch] = useState(search);
  useEffect(() => {
    const timer = setTimeout(() => {
      setDebouncedSearch(search);
      setPage(1);
    }, 400);
    return () => clearTimeout(timer);
  }, [search]);

  useEffect(() => {
    if (!isAdmin) return;
    const controller = new AbortController();
    const load = async () => {
      setLoading(true);
      setError(null);
      try {
        const params = new URLSearchParams({ page: String(page), pageSize: "20" });
        if (debouncedSearch.trim()) params.set("search", debouncedSearch.trim());
        if (statusFilter) params.set("status", statusFilter);
        const res = await api(`/api/Booking?${params}`, { signal: controller.signal });
        if (!res.ok) throw new Error("Failed to load bookings");
        const data: BookingList = await res.json();
        setBookings(data.items ?? []);
        setTotalPages(data.totalPages ?? 1);
      } catch (err) {
        if (err instanceof Error && err.name === "AbortError") return;
        setError("Unable to load confirmed bookings.");
      } finally {
        setLoading(false);
      }
    };
    load();
    return () => controller.abort();
  }, [isAdmin, page, debouncedSearch, statusFilter]);

  if (!isAdmin) {
    return <Container className="py-16 text-center"><p className="text-[var(--text-muted)]">Admin access required.</p></Container>;
  }

  return (
    <Container className="py-10">
      <div className="mb-6 flex flex-col gap-5 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <span className="text-[10px] font-semibold uppercase tracking-[0.18em] text-[var(--accent)]">
            Bookings
          </span>
          <h1 className="mt-1.5 text-2xl font-bold tracking-tight text-[var(--text-heading)] sm:text-3xl">
            Confirmed Bookings
          </h1>
          <p className="mt-1.5 text-sm text-[var(--text-muted)]">
            Sales bookings after approval or walk-in creation
          </p>
        </div>
        <div className="flex shrink-0 flex-wrap items-center gap-2.5">
          <Button size="sm" onClick={() => navigate("/confirmed-bookings/new")}>
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
              <line x1="12" y1="5" x2="12" y2="19" /><line x1="5" y1="12" x2="19" y2="12" />
            </svg>
            Add Booking
          </Button>
          <Button size="sm" variant="outline" onClick={() => navigate("/application-form")}>
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <polyline points="6 9 6 2 18 2 18 9" /><path d="M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2" /><rect x="6" y="14" width="12" height="8" />
            </svg>
            Print Blank Form
          </Button>
        </div>
      </div>

      {/* Finding a booking and narrowing the list are one job, so they share one bar rather than
          trailing the page actions — which is what left the search box wrapped under the buttons
          at anything narrower than a wide desktop. */}
      <div className="mb-5 flex flex-col gap-3 rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] p-3 sm:flex-row sm:items-center">
        <div className="relative flex-1">
          <svg
            className="pointer-events-none absolute left-3.5 top-1/2 -translate-y-1/2 text-[var(--text-muted)]"
            width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor"
            strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"
          >
            <circle cx="11" cy="11" r="8" /><line x1="21" y1="21" x2="16.65" y2="16.65" />
          </svg>
          <input
            type="search"
            aria-label="Search bookings"
            placeholder="Search reference, customer, project, unit..."
            value={search}
            onChange={(e) => { setSearch(e.target.value); setPage(1); }}
            className="w-full rounded-xl border border-transparent bg-transparent py-2.5 pl-10 pr-4 text-sm text-[var(--text-primary)] outline-none transition-colors placeholder:text-[var(--text-muted)] focus:border-[var(--border-hover)] focus:bg-[var(--surface-glass)]"
          />
        </div>
        <AppSelect
          value={statusFilter}
          onChange={(e) => { setStatusFilter(e.target.value); setPage(1); }}
          aria-label="Filter by status"
          className="w-full rounded-xl border border-[var(--border)] bg-[var(--surface-glass)] px-4 py-2.5 text-sm text-[var(--text-primary)] sm:w-56"
        >
          <option value="">All statuses</option>
          {Object.entries(STATUS_LABEL).map(([k, v]) => (
            <option key={k} value={k}>{v}</option>
          ))}
        </AppSelect>
      </div>

      {error && <div className="mb-6 rounded-xl border border-rose-500/20 bg-rose-500/10 px-4 py-3 text-sm text-rose-400">{error}</div>}

      <div className="overflow-hidden rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)]">
        {/* The table keeps its own horizontal scroll so seven columns never push the page sideways. */}
        <div className="overflow-x-auto">
          <table className="data-table w-full min-w-[900px] text-left text-sm">
            <thead className="border-b border-[var(--border)] bg-[var(--surface-glass-hover)]">
              <tr>
                {["Reference", "Customer", "Project / Unit", "Agreed Price", "Booking Amt", "Status", "Action"].map((h) => (
                  <th
                    key={h}
                    className={`px-5 py-3.5 text-[10px] font-bold uppercase tracking-[0.12em] text-[var(--text-muted)] ${h === "Action" ? "text-right" : ""}`}
                  >
                    {h}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {loading ? (
                <tr><td colSpan={7} className="px-5 py-14 text-center text-[var(--text-muted)]">Loading…</td></tr>
              ) : bookings.length === 0 ? (
                <tr><td colSpan={7} className="px-5 py-14 text-center text-[var(--text-muted)]">No bookings found.</td></tr>
              ) : bookings.map((b) => (
                <tr
                  key={b.id}
                  className="border-b border-[var(--border)] transition-colors last:border-0 hover:bg-[var(--surface-glass-hover)]"
                >
                  <td className="px-5 py-4 font-semibold text-[var(--text-heading)]">{b.bookingReference}</td>
                  <td className="px-5 py-4">
                    <p className="font-medium text-[var(--text-primary)]">{b.customerName}</p>
                    <p className="mt-0.5 text-xs text-[var(--text-muted)]">{b.customerPhone}</p>
                  </td>
                  <td className="px-5 py-4 text-[var(--text-secondary)]">
                    {b.projectName} <span className="text-[var(--text-muted)]">·</span> {b.unitNumber}
                  </td>
                  {/* Money is read by comparing rows, so it lines up on the digits. */}
                  <td className="px-5 py-4 tabular-nums text-[var(--text-secondary)]">{formatMoney(b.agreedSalePrice)}</td>
                  <td className="px-5 py-4 tabular-nums text-[var(--text-secondary)]">
                    {formatMoney(b.bookingAmountReceived)}
                    <span className="text-[var(--text-muted)]"> / {formatMoney(b.bookingAmountRequired)}</span>
                  </td>
                  <td className="px-5 py-4">
                    <span className={`inline-flex whitespace-nowrap rounded-full border px-3 py-1 text-xs font-medium ${STATUS_STYLE[b.status] ?? "border-[var(--border)] text-[var(--text-secondary)]"}`}>
                      {STATUS_LABEL[b.status] ?? b.status}
                    </span>
                  </td>
                  <td className="px-5 py-4 text-right">
                    <Button
                      size="sm"
                      variant="outline"
                      aria-label={`Open booking ${b.bookingReference}`}
                      onClick={() => navigate(`/confirmed-bookings/${b.id}`)}
                    >
                      Open
                      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                        <line x1="5" y1="12" x2="19" y2="12" /><polyline points="12 5 19 12 12 19" />
                      </svg>
                    </Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      <Pagination currentPage={page} totalPages={totalPages} onPageChange={setPage} />
    </Container>
  );
}
