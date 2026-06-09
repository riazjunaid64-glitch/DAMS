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

const STATUS_STYLE: Record<string, string> = {
  AwaitingBookingAmount: "text-amber-400 bg-amber-500/10 border-amber-500/20",
  PaymentPlanActive: "text-indigo-400 bg-indigo-500/10 border-indigo-500/20",
  PossessionGiven: "text-blue-400 bg-blue-500/10 border-blue-500/20",
  SaleCompleted: "text-emerald-400 bg-emerald-500/10 border-emerald-500/20",
  Cancelled: "text-rose-400 bg-rose-500/10 border-rose-500/20",
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

  useEffect(() => {
    if (!isAdmin) return;
    const load = async () => {
      setLoading(true);
      setError(null);
      try {
        const params = new URLSearchParams({ page: String(page), pageSize: "20" });
        if (search.trim()) params.set("search", search.trim());
        if (statusFilter) params.set("status", statusFilter);
        const res = await api(`/api/Booking?${params}`);
        if (!res.ok) throw new Error("Failed to load bookings");
        const data: BookingList = await res.json();
        setBookings(data.items ?? []);
        setTotalPages(data.totalPages ?? 1);
      } catch {
        setError("Unable to load confirmed bookings.");
      } finally {
        setLoading(false);
      }
    };
    load();
  }, [isAdmin, page, search, statusFilter]);

  if (!isAdmin) {
    return <Container className="py-16 text-center"><p className="text-[var(--text-muted)]">Admin access required.</p></Container>;
  }

  return (
    <Container className="py-10">
      <div className="mb-8 flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
        <div>
          <h1 className="text-2xl font-bold text-[var(--text-heading)]">Confirmed Bookings</h1>
          <p className="mt-1 text-sm text-[var(--text-muted)]">Sales bookings after approval or walk-in creation</p>
        </div>
        <div className="flex flex-col gap-2 sm:flex-row sm:flex-wrap sm:items-center">
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
          <select
            value={statusFilter}
            onChange={(e) => { setStatusFilter(e.target.value); setPage(1); }}
            className="rounded-xl border border-[var(--border)] bg-[var(--surface-glass)] px-4 py-2.5 text-sm text-[var(--text-primary)]"
          >
            <option value="">All statuses</option>
            {Object.entries(STATUS_LABEL).map(([k, v]) => (
              <option key={k} value={k}>{v}</option>
            ))}
          </select>
          <input
            type="search"
            placeholder="Search reference, customer, unit..."
            value={search}
            onChange={(e) => { setSearch(e.target.value); setPage(1); }}
            className="w-full min-w-[240px] rounded-xl border border-[var(--border)] bg-[var(--surface-glass)] px-4 py-2.5 text-sm text-[var(--text-primary)] outline-none focus:border-indigo-500/50"
          />
        </div>
      </div>

      {error && <div className="mb-6 rounded-xl border border-rose-500/20 bg-rose-500/10 px-4 py-3 text-sm text-rose-400">{error}</div>}

      <div className="overflow-hidden rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)]">
        <table className="w-full text-left text-sm">
          <thead className="border-b border-[var(--border)] bg-[var(--surface-glass-hover)]">
            <tr>
              {["Reference", "Customer", "Project / Unit", "Agreed Price", "Booking Amt", "Status", ""].map((h) => (
                <th key={h} className="px-4 py-3 font-medium text-[var(--text-muted)]">{h}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {loading ? (
              <tr><td colSpan={7} className="px-4 py-12 text-center text-[var(--text-muted)]">Loading...</td></tr>
            ) : bookings.length === 0 ? (
              <tr><td colSpan={7} className="px-4 py-12 text-center text-[var(--text-muted)]">No bookings found.</td></tr>
            ) : bookings.map((b) => (
              <tr key={b.id} className="border-b border-[var(--border)] last:border-0 hover:bg-[var(--surface-glass-hover)]">
                <td className="px-4 py-3 font-medium text-[var(--text-heading)]">{b.bookingReference}</td>
                <td className="px-4 py-3">
                  <p className="text-[var(--text-primary)]">{b.customerName}</p>
                  <p className="text-xs text-[var(--text-muted)]">{b.customerPhone}</p>
                </td>
                <td className="px-4 py-3 text-[var(--text-secondary)]">{b.projectName} · {b.unitNumber}</td>
                <td className="px-4 py-3 text-[var(--text-secondary)]">{formatMoney(b.agreedSalePrice)}</td>
                <td className="px-4 py-3 text-[var(--text-secondary)]">
                  {formatMoney(b.bookingAmountReceived)} / {formatMoney(b.bookingAmountRequired)}
                </td>
                <td className="px-4 py-3">
                  <span className={`rounded-full border px-2.5 py-0.5 text-xs ${STATUS_STYLE[b.status] ?? ""}`}>
                    {STATUS_LABEL[b.status] ?? b.status}
                  </span>
                </td>
                <td className="px-4 py-3 text-right">
                  <Button size="sm" variant="ghost" onClick={() => navigate(`/confirmed-bookings/${b.id}`)}>Open</Button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <Pagination currentPage={page} totalPages={totalPages} onPageChange={setPage} />
    </Container>
  );
}
