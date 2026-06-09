import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { api } from "../api/api.ts";
import type { User } from "../App.tsx";
import Container from "../lib/Container.tsx";
import Pagination from "../lib/Pagination.tsx";

type Props = { user: User | null };

interface Customer {
  id: number;
  fullName: string;
  phone: string;
  cnic?: string | null;
  email?: string | null;
  source: string;
  status: string;
  bookingsCount: number;
  createdAt: string;
}

interface CustomerList {
  items: Customer[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

const SOURCE_LABELS: Record<string, string> = {
  Website: "Website",
  WalkIn: "Walk-In",
  Phone: "Phone",
  Referral: "Referral",
  Other: "Other",
};

export default function CustomersPage({ user }: Props) {
  const navigate = useNavigate();
  const [customers, setCustomers] = useState<Customer[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState("");
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
        const res = await api(`/api/Customer?${params}`);
        if (!res.ok) throw new Error("Failed to load customers");
        const data: CustomerList = await res.json();
        setCustomers(data.items ?? []);
        setTotalPages(data.totalPages ?? 1);
      } catch {
        setError("Unable to load customers.");
      } finally {
        setLoading(false);
      }
    };
    load();
  }, [isAdmin, page, search]);

  if (!isAdmin) {
    return (
      <Container className="py-16 text-center">
        <p className="text-[var(--text-muted)]">Admin access required.</p>
      </Container>
    );
  }

  return (
    <Container className="py-10">
      <div className="mb-8 flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <h1 className="text-2xl font-bold text-[var(--text-heading)]">Customers</h1>
          <p className="mt-1 text-sm text-[var(--text-muted)]">Business customers linked to bookings</p>
        </div>
        <input
          type="search"
          placeholder="Search name, phone, CNIC, email..."
          value={search}
          onChange={(e) => { setSearch(e.target.value); setPage(1); }}
          className="w-full max-w-sm rounded-xl border border-[var(--border)] bg-[var(--surface-glass)] px-4 py-2.5 text-sm text-[var(--text-primary)] outline-none focus:border-indigo-500/50"
        />
      </div>

      {error && (
        <div className="mb-6 rounded-xl border border-rose-500/20 bg-rose-500/10 px-4 py-3 text-sm text-rose-400">{error}</div>
      )}

      <div className="overflow-hidden rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)]">
        <table className="w-full text-left text-sm">
          <thead className="border-b border-[var(--border)] bg-[var(--surface-glass-hover)]">
            <tr>
              {["Name", "Phone", "Email", "Source", "Bookings", "Status", ""].map((h) => (
                <th key={h} className="px-4 py-3 font-medium text-[var(--text-muted)]">{h}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {loading ? (
              <tr><td colSpan={7} className="px-4 py-12 text-center text-[var(--text-muted)]">Loading...</td></tr>
            ) : customers.length === 0 ? (
              <tr><td colSpan={7} className="px-4 py-12 text-center text-[var(--text-muted)]">No customers found.</td></tr>
            ) : customers.map((c) => (
              <tr key={c.id} className="border-b border-[var(--border)] last:border-0 hover:bg-[var(--surface-glass-hover)]">
                <td className="px-4 py-3 font-medium text-[var(--text-heading)]">{c.fullName}</td>
                <td className="px-4 py-3 text-[var(--text-secondary)]">{c.phone}</td>
                <td className="px-4 py-3 text-[var(--text-secondary)]">{c.email ?? "—"}</td>
                <td className="px-4 py-3 text-[var(--text-secondary)]">{SOURCE_LABELS[c.source] ?? c.source}</td>
                <td className="px-4 py-3 text-[var(--text-secondary)]">{c.bookingsCount}</td>
                <td className="px-4 py-3">
                  <span className="rounded-full border border-emerald-500/20 bg-emerald-500/10 px-2.5 py-0.5 text-xs text-emerald-400">{c.status}</span>
                </td>
                <td className="px-4 py-3 text-right">
                  <button
                    type="button"
                    onClick={() => navigate(`/customers/${c.id}`)}
                    className="inline-flex items-center gap-1.5 rounded-lg border border-[var(--accent-glow-strong)] bg-[var(--accent-glow)] px-3 py-1.5 text-xs font-semibold text-[var(--accent)] transition-colors hover:border-[var(--accent)] hover:bg-[var(--surface-glass-hover)]"
                  >
                    View
                    <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.25" strokeLinecap="round" strokeLinejoin="round"><path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7-10-7-10-7z"/><circle cx="12" cy="12" r="3"/></svg>
                  </button>
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
