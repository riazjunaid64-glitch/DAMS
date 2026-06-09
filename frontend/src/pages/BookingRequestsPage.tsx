import { useEffect, useState, useCallback } from "react";
import { useNavigate } from "react-router-dom";
import { api } from "../api/api.ts";
import type { User } from "../App.tsx";
import Container from "../lib/Container.tsx";
import Button from "../lib/Button.tsx";

interface BookingRequest {
  id: number;
  unitId: number;
  unitNumber: string;
  unitType: string;
  unitPrice: number;
  projectId: number;
  projectName: string;
  projectLocation: string;
  userId: number | null;
  fullName: string;
  phone: string;
  email: string;
  cnic: string;
  address: string;
  notes: string | null;
  status: "Pending" | "Approved" | "Rejected";
  requestedAt: string;
  reviewedAt: string | null;
  reviewedByUserId: number | null;
  reviewedByName: string | null;
  rejectionReason: string | null;
  createdAt: string;
}

interface BookingRequestList {
  items: BookingRequest[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

interface Stats {
  pending: number;
  approved: number;
  rejected: number;
  total: number;
}

type Props = { user: User | null };

const statusConfig = {
  Pending: { color: "text-amber-400 bg-amber-500/10 border-amber-500/20", label: "Pending Review" },
  Approved: { color: "text-emerald-400 bg-emerald-500/10 border-emerald-500/20", label: "Approved" },
  Rejected: { color: "text-rose-400 bg-rose-500/10 border-rose-500/20", label: "Rejected" },
};

function formatCurrency(value: number) {
  return value.toLocaleString("en-US", { style: "currency", currency: "USD", maximumFractionDigits: 0 });
}

function formatRequestDate(date: string) {
  const parsed = new Date(date);
  if (Number.isNaN(parsed.getTime())) return "Not set";
  return parsed.toLocaleDateString("en-US", { month: "short", day: "numeric", year: "numeric" });
}

export default function BookingRequestsPage({ user }: Props) {
  const navigate = useNavigate();
  const isAdmin = user?.role === "Admin";

  const [requests, setRequests] = useState<BookingRequest[]>([]);
  const [stats, setStats] = useState<Stats | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [activeFilter, setActiveFilter] = useState<"all" | "Pending" | "Approved" | "Rejected">("all");
  const [search, setSearch] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");
  const [dateFilter, setDateFilter] = useState("");
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [selectedRequest, setSelectedRequest] = useState<BookingRequest | null>(null);
  const [actionLoading, setActionLoading] = useState(false);
  const [rejectReason, setRejectReason] = useState("");
  const [showRejectModal, setShowRejectModal] = useState(false);

  const fetchStats = useCallback(async () => {
    try {
      const res = await api("/api/BookingRequest/stats");
      if (res.ok) setStats(await res.json());
    } catch { /* ignore */ }
  }, []);

  const fetchRequests = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const params = new URLSearchParams();
      if (activeFilter !== "all") params.append("status", activeFilter);
      if (debouncedSearch.trim()) params.append("search", debouncedSearch.trim());
      params.append("page", String(page));
      params.append("pageSize", "15");

      const res = await api(`/api/BookingRequest?${params.toString()}`);
      if (!res.ok) throw new Error("Failed to fetch booking requests");

      const data: BookingRequestList = await res.json();
      setRequests(data.items);
      setTotalPages(data.totalPages);
    } catch {
      setError("Unable to load booking requests.");
    } finally {
      setLoading(false);
    }
  }, [activeFilter, debouncedSearch, page]);

  useEffect(() => {
    const timer = window.setTimeout(() => setDebouncedSearch(search), 300);
    return () => window.clearTimeout(timer);
  }, [search]);

  useEffect(() => {
    if (!isAdmin) {
      navigate("/");
      return;
    }
    fetchStats();
    fetchRequests();
  }, [isAdmin, navigate, fetchStats, fetchRequests]);

  const handleApprove = async (id: number) => {
    setActionLoading(true);
    try {
      const res = await api(`/api/BookingRequest/${id}/approve`, { method: "POST" });
      if (!res.ok) {
        const data = await res.json().catch(() => ({}));
        alert(data.message || "Failed to approve request");
        return;
      }
      await fetchStats();
      await fetchRequests();
      setSelectedRequest(null);
    } catch {
      alert("Something went wrong");
    } finally {
      setActionLoading(false);
    }
  };

  const handleReject = async (id: number) => {
    setActionLoading(true);
    try {
      const res = await api(`/api/BookingRequest/${id}/reject`, {
        method: "POST",
        body: JSON.stringify({ rejectionReason: rejectReason.trim() || null }),
      });
      if (!res.ok) {
        const data = await res.json().catch(() => ({}));
        alert(data.message || "Failed to reject request");
        return;
      }
      await fetchStats();
      await fetchRequests();
      setSelectedRequest(null);
      setShowRejectModal(false);
      setRejectReason("");
    } catch {
      alert("Something went wrong");
    } finally {
      setActionLoading(false);
    }
  };

  if (!isAdmin) return null;

  const visibleRequests = dateFilter
    ? requests.filter((req) => req.requestedAt.slice(0, 10) === dateFilter)
    : requests;

  return (
    <>
      {/* Header */}
      <div className="relative overflow-hidden border-b border-[var(--border)]">
        <div className="absolute inset-0 mesh-gradient-subtle" />
        <Container className="relative py-8 sm:py-10">
          <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
            <div>
              <div className="mb-2 inline-flex items-center gap-2 rounded-full bg-indigo-500/[0.08] px-3 py-1">
                <span className="h-1.5 w-1.5 rounded-full bg-indigo-500" />
                <span className="text-[10px] font-semibold uppercase tracking-wider text-indigo-500">
                  Admin Module
                </span>
              </div>
              <h1 className="text-2xl font-bold text-[var(--text-heading)] sm:text-3xl">
                Booking Requests
              </h1>
              <p className="mt-1 text-sm text-[var(--text-muted)]">
                Review and manage customer booking requests
              </p>
            </div>
          </div>

          {/* Stats Cards */}
          {stats && (
            <div className="mt-6 grid grid-cols-2 gap-3 sm:grid-cols-4">
              {[
                { filter: "Pending" as const, label: "Pending", value: stats.pending, color: "text-amber-400", bg: "bg-amber-500/10", border: "border-amber-500/20", activeBorder: "border-amber-400/60", icon: <path d="M12 8v4l2.5 2.5"/>, circle: true },
                { filter: "Approved" as const, label: "Approved", value: stats.approved, color: "text-emerald-400", bg: "bg-emerald-500/10", border: "border-emerald-500/20", activeBorder: "border-emerald-400/60", icon: <path d="M20 6 9 17l-5-5"/>, circle: false },
                { filter: "Rejected" as const, label: "Rejected", value: stats.rejected, color: "text-rose-400", bg: "bg-rose-500/10", border: "border-rose-500/20", activeBorder: "border-rose-400/60", icon: <><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></>, circle: false },
                { filter: "all" as const, label: "Total", value: stats.total, color: "text-[var(--accent)]", bg: "bg-[var(--accent-glow)]", border: "border-[var(--accent-glow-strong)]", activeBorder: "border-[var(--accent)]", icon: <><rect x="3" y="3" width="7" height="7" rx="1"/><rect x="14" y="3" width="7" height="7" rx="1"/><rect x="3" y="14" width="7" height="7" rx="1"/><rect x="14" y="14" width="7" height="7" rx="1"/></>, circle: false },
              ].map((stat) => (
                <button
                  key={stat.label}
                  type="button"
                  onClick={() => { setActiveFilter(stat.filter); setPage(1); }}
                  className={`flex items-center gap-4 rounded-xl border bg-[var(--bg-card)] px-4 py-3 text-left shadow-sm transition-all hover:-translate-y-0.5 hover:shadow-md ${
                    activeFilter === stat.filter ? stat.activeBorder : stat.border
                  }`}
                  aria-pressed={activeFilter === stat.filter}
                >
                  <div className={`flex h-10 w-10 items-center justify-center rounded-full ${stat.bg} ${stat.color}`}>
                    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                      {stat.circle && <circle cx="12" cy="12" r="9" />}
                      {stat.icon}
                    </svg>
                  </div>
                  <div>
                    <p className={`text-xl font-bold ${stat.color}`}>{stat.value}</p>
                    <p className="text-xs text-[var(--text-muted)]">{stat.label}</p>
                  </div>
                </button>
              ))}
            </div>
          )}
        </Container>
      </div>

      {/* Content */}
      <Container className="py-8">
        {/* Search & Filters */}
        <div className="mb-6 flex flex-col gap-3 lg:flex-row lg:items-center">
          <div className="relative flex-1">
            <svg className="absolute left-3 top-1/2 -translate-y-1/2 text-[var(--text-muted)]" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
              <circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/>
            </svg>
            <input
              type="text"
              value={search}
              onChange={(e) => { setSearch(e.target.value); setPage(1); }}
              placeholder="Search by customer name, email, phone, project..."
              className="w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] py-2.5 pl-10 pr-4 text-sm text-[var(--text-primary)] placeholder:text-[var(--text-muted)] transition-all focus:border-[var(--accent)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-glow)]"
            />
          </div>
          <select
            value={activeFilter}
            onChange={(e) => { setActiveFilter(e.target.value as typeof activeFilter); setPage(1); }}
            className="rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-4 py-2.5 text-sm text-[var(--text-primary)] transition-all focus:border-[var(--accent)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-glow)]"
          >
            <option value="all">All Statuses</option>
            <option value="Pending">Pending</option>
            <option value="Approved">Approved</option>
            <option value="Rejected">Rejected</option>
          </select>
          <div className="relative">
            <svg className="absolute left-3 top-1/2 -translate-y-1/2 text-[var(--text-muted)]" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
              <rect x="3" y="4" width="18" height="18" rx="2"/><line x1="16" y1="2" x2="16" y2="6"/><line x1="8" y1="2" x2="8" y2="6"/><line x1="3" y1="10" x2="21" y2="10"/>
            </svg>
            <input
              type="date"
              value={dateFilter}
              onChange={(e) => setDateFilter(e.target.value)}
              className="w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] py-2.5 pl-10 pr-4 text-sm text-[var(--text-primary)] transition-all focus:border-[var(--accent)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-glow)] lg:w-56"
            />
          </div>
        </div>

        {loading && (
          <div className="overflow-x-auto rounded-xl border border-[var(--border)] bg-[var(--bg-card)] shadow-sm">
            <div className="min-w-[1080px]">
              <div className="grid grid-cols-[1.15fr_1.5fr_0.95fr_0.95fr_0.9fr_0.8fr_0.85fr_0.7fr] gap-4 border-b border-[var(--border)] bg-[var(--surface-glass)] px-5 py-4">
                {[...Array(8)].map((_, i) => <div key={i} className="skeleton h-3" />)}
              </div>
              {[...Array(6)].map((_, row) => (
                <div key={row} className="grid grid-cols-[1.15fr_1.5fr_0.95fr_0.95fr_0.9fr_0.8fr_0.85fr_0.7fr] gap-4 border-b border-[var(--border)] px-5 py-5 last:border-b-0">
                  {[...Array(8)].map((_, cell) => <div key={cell} className="skeleton h-4" />)}
                </div>
              ))}
            </div>
          </div>
        )}

        {error && (
          <div className="rounded-2xl border border-rose-500/20 bg-rose-500/[0.06] px-5 py-4 text-sm text-rose-300 flex items-center gap-3">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><circle cx="12" cy="12" r="10"/><line x1="15" y1="9" x2="9" y2="15"/><line x1="9" y1="9" x2="15" y2="15"/></svg>
            {error}
          </div>
        )}

        {!loading && !error && visibleRequests.length === 0 && (
          <div className="py-20 text-center">
            <div className="mx-auto mb-4 flex h-16 w-16 items-center justify-center rounded-2xl bg-[var(--surface-glass)] border border-[var(--border)]">
              <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" className="text-[var(--text-muted)]" strokeLinecap="round">
                <rect x="3" y="4" width="18" height="18" rx="2" ry="2"/>
                <line x1="16" y1="2" x2="16" y2="6"/>
                <line x1="8" y1="2" x2="8" y2="6"/>
                <line x1="3" y1="10" x2="21" y2="10"/>
              </svg>
            </div>
            <h3 className="text-lg font-semibold text-[var(--text-heading)]">No Booking Requests</h3>
            <p className="mt-1 text-sm text-[var(--text-muted)]">
              {requests.length === 0 && activeFilter === "all"
                ? "No booking requests have been submitted yet."
                : "Try adjusting your search, status, or date filter."}
            </p>
          </div>
        )}

        {!loading && !error && visibleRequests.length > 0 && (
          <>
            <div className="overflow-x-auto rounded-xl border border-[var(--border)] bg-[var(--bg-card)] shadow-sm">
              <table className="min-w-[800px] w-full border-collapse text-left">
                <thead>
                  <tr className="border-b border-[var(--border)] bg-[var(--surface-glass)]">
                    {["Customer Name", "Project / Unit", "Total Pay", "Status", "Request Date", "Actions"].map((label) => (
                      <th key={label} className="px-5 py-3 text-[10px] font-bold uppercase tracking-wider text-[var(--text-muted)]">
                        {label}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {visibleRequests.map((req) => {
                    const status = statusConfig[req.status];
                    return (
                      <tr key={req.id} className="group border-b border-[var(--border)] transition-colors hover:bg-[var(--surface-glass-hover)] last:border-b-0">
                        <td className="px-5 py-4">
                          <button
                            type="button"
                            onClick={() => setSelectedRequest(req)}
                            className="max-w-[180px] truncate text-left text-sm font-semibold text-[var(--accent)] group-hover:text-[var(--accent-light)]"
                          >
                            {req.fullName}
                          </button>
                        </td>
                        <td className="px-5 py-4">
                          <p className="max-w-[260px] truncate text-sm font-medium text-[var(--text-primary)]">
                            {req.projectName} - {req.unitNumber}
                          </p>
                          <p className="text-xs text-[var(--text-muted)]">{req.unitType}</p>
                        </td>
                        <td className="px-5 py-4 text-sm font-semibold text-[var(--text-primary)]">{formatCurrency(req.unitPrice)}</td>
                        <td className="px-5 py-4">
                          <span className={`inline-flex rounded-full border px-2.5 py-1 text-[10px] font-semibold ${status.color}`}>
                            {status.label}
                          </span>
                        </td>
                        <td className="px-5 py-4 text-sm text-[var(--text-secondary)]">{formatRequestDate(req.requestedAt)}</td>
                        <td className="px-5 py-4">
                          <button
                            type="button"
                            onClick={() => setSelectedRequest(req)}
                            className="inline-flex items-center gap-1.5 rounded-lg border border-[var(--accent-glow-strong)] bg-[var(--accent-glow)] px-3 py-1.5 text-xs font-semibold text-[var(--accent)] transition-colors hover:border-[var(--accent)] hover:bg-[var(--surface-glass-hover)]"
                          >
                            View Details
                            <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.25" strokeLinecap="round" strokeLinejoin="round"><path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7-10-7-10-7z"/><circle cx="12" cy="12" r="3"/></svg>
                          </button>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>

            {/* Pagination */}
            {totalPages > 1 && (
              <div className="mt-8 flex items-center justify-center gap-2">
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => setPage((p) => Math.max(1, p - 1))}
                  disabled={page === 1}
                >
                  Previous
                </Button>
                <span className="px-4 text-sm text-[var(--text-muted)]">
                  Page {page} of {totalPages}
                </span>
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
                  disabled={page === totalPages}
                >
                  Next
                </Button>
              </div>
            )}
          </>
        )}
      </Container>

      {/* Detail Modal */}
      {selectedRequest && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
          <div
            className="absolute inset-0 bg-black/60 backdrop-blur-sm animate-fade-in"
            onClick={() => setSelectedRequest(null)}
          />
          <div className="relative z-10 w-[600px] max-w-[92vw] max-h-[90vh] overflow-y-auto animate-scale-in rounded-2xl border border-[var(--border)] bg-[var(--modal-bg)] shadow-2xl">
            <div className="absolute right-0 top-0 h-32 w-32 rounded-full bg-indigo-500/[0.08] blur-[60px]" />

            {/* Header */}
            <div className="sticky top-0 z-10 border-b border-[var(--border)] bg-[var(--modal-bg)] px-6 py-5">
              <div className="flex items-center justify-between">
                <div>
                  <div className="mb-1 flex items-center gap-2">
                    <span className={`rounded-full border px-2.5 py-0.5 text-[10px] font-semibold ${statusConfig[selectedRequest.status].color}`}>
                      {statusConfig[selectedRequest.status].label}
                    </span>
                    <span className="text-xs text-[var(--text-muted)]">
                      #{selectedRequest.id}
                    </span>
                  </div>
                  <h3 className="text-lg font-semibold text-[var(--text-heading)]">
                    Booking Request Details
                  </h3>
                </div>
                <button
                  onClick={() => setSelectedRequest(null)}
                  className="flex h-8 w-8 items-center justify-center rounded-lg text-[var(--text-muted)] transition hover:bg-[var(--surface-glass-hover)] hover:text-[var(--text-primary)]"
                >
                  <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                    <line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>
                  </svg>
                </button>
              </div>
            </div>

            {/* Content */}
            <div className="relative p-6 space-y-6">
              {/* Customer Info */}
              <div>
                <h4 className="text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-3">
                  Customer Information
                </h4>
                <div className="grid gap-3 sm:grid-cols-2">
                  <div className="rounded-xl border border-[var(--border)] bg-[var(--surface-glass)] p-3">
                    <p className="text-[10px] uppercase tracking-wider text-[var(--text-muted)] mb-1">Full Name</p>
                    <p className="text-sm font-medium text-[var(--text-primary)]">{selectedRequest.fullName}</p>
                  </div>
                  <div className="rounded-xl border border-[var(--border)] bg-[var(--surface-glass)] p-3">
                    <p className="text-[10px] uppercase tracking-wider text-[var(--text-muted)] mb-1">Phone</p>
                    <p className="text-sm font-medium text-[var(--text-primary)]">{selectedRequest.phone}</p>
                  </div>
                  <div className="rounded-xl border border-[var(--border)] bg-[var(--surface-glass)] p-3">
                    <p className="text-[10px] uppercase tracking-wider text-[var(--text-muted)] mb-1">Email</p>
                    <p className="text-sm font-medium text-[var(--text-primary)]">{selectedRequest.email}</p>
                  </div>
                  <div className="rounded-xl border border-[var(--border)] bg-[var(--surface-glass)] p-3">
                    <p className="text-[10px] uppercase tracking-wider text-[var(--text-muted)] mb-1">CNIC</p>
                    <p className="text-sm font-medium text-[var(--text-primary)]">{selectedRequest.cnic}</p>
                  </div>
                  <div className="sm:col-span-2 rounded-xl border border-[var(--border)] bg-[var(--surface-glass)] p-3">
                    <p className="text-[10px] uppercase tracking-wider text-[var(--text-muted)] mb-1">Address</p>
                    <p className="text-sm font-medium text-[var(--text-primary)]">{selectedRequest.address}</p>
                  </div>
                </div>
              </div>

              {/* Unit Info */}
              <div>
                <h4 className="text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-3">
                  Unit Information
                </h4>
                <div
                  onClick={() => navigate(`/units/${selectedRequest.unitId}`)}
                  className="cursor-pointer rounded-xl border border-[var(--border)] bg-[var(--surface-glass)] p-4 transition-all hover:border-[var(--accent)]/30 hover:bg-[var(--accent-glow)]"
                >
                  <div className="flex items-center gap-4">
                    <div className="flex h-12 w-12 items-center justify-center rounded-xl bg-indigo-500/10 border border-indigo-500/20">
                      <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" className="text-indigo-400" strokeLinecap="round">
                        <path d="M3 9l9-7 9 7v11a2 2 0 01-2 2H5a2 2 0 01-2-2V9z"/>
                        <polyline points="9 22 9 12 15 12 15 22"/>
                      </svg>
                    </div>
                    <div className="flex-1">
                      <div className="flex items-center gap-2">
                        <span className="font-semibold text-[var(--text-heading)]">{selectedRequest.unitNumber}</span>
                        <span className="rounded-md bg-[var(--accent-glow)] px-2 py-0.5 text-[10px] font-medium text-[var(--accent)] uppercase">
                          {selectedRequest.unitType}
                        </span>
                      </div>
                      <p className="text-sm text-[var(--text-muted)]">{selectedRequest.projectName} • {selectedRequest.projectLocation}</p>
                    </div>
                    <div className="text-right">
                      <p className="text-lg font-bold text-[var(--text-heading)]">${selectedRequest.unitPrice.toLocaleString()}</p>
                    </div>
                  </div>
                </div>
              </div>

              {/* Notes */}
              {selectedRequest.notes && (
                <div>
                  <h4 className="text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-3">
                    Customer Notes
                  </h4>
                  <div className="rounded-xl border border-[var(--border)] bg-[var(--surface-glass)] p-4">
                    <p className="text-sm text-[var(--text-primary)]">{selectedRequest.notes}</p>
                  </div>
                </div>
              )}

              {/* Timeline */}
              <div>
                <h4 className="text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)] mb-3">
                  Timeline
                </h4>
                <div className="space-y-3">
                  <div className="flex items-center gap-3 text-sm">
                    <div className="h-2 w-2 rounded-full bg-[var(--text-muted)]" />
                    <span className="text-[var(--text-muted)]">Requested:</span>
                    <span className="text-[var(--text-primary)]">
                      {new Date(selectedRequest.requestedAt).toLocaleString()}
                    </span>
                  </div>
                  {selectedRequest.reviewedAt && (
                    <div className="flex items-center gap-3 text-sm">
                      <div className={`h-2 w-2 rounded-full ${selectedRequest.status === "Approved" ? "bg-emerald-400" : "bg-rose-400"}`} />
                      <span className="text-[var(--text-muted)]">
                        {selectedRequest.status === "Approved" ? "Approved" : "Rejected"}:
                      </span>
                      <span className="text-[var(--text-primary)]">
                        {new Date(selectedRequest.reviewedAt).toLocaleString()}
                        {selectedRequest.reviewedByName && ` by ${selectedRequest.reviewedByName}`}
                      </span>
                    </div>
                  )}
                </div>
              </div>

              {/* Rejection Reason */}
              {selectedRequest.status === "Rejected" && selectedRequest.rejectionReason && (
                <div className="rounded-xl border border-rose-500/20 bg-rose-500/[0.06] p-4">
                  <p className="text-xs font-semibold uppercase tracking-wider text-rose-400 mb-2">Rejection Reason</p>
                  <p className="text-sm text-rose-300">{selectedRequest.rejectionReason}</p>
                </div>
              )}
            </div>

            {/* Footer Actions */}
            {selectedRequest.status === "Pending" && (
              <div className="sticky bottom-0 z-10 flex items-center justify-between border-t border-[var(--border)] px-6 py-4 bg-[var(--surface-glass)]">
                <Button
                  variant="danger"
                  onClick={() => setShowRejectModal(true)}
                  disabled={actionLoading}
                >
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                    <circle cx="12" cy="12" r="10"/><line x1="15" y1="9" x2="9" y2="15"/><line x1="9" y1="9" x2="15" y2="15"/>
                  </svg>
                  Reject
                </Button>
                <Button
                  onClick={() => handleApprove(selectedRequest.id)}
                  disabled={actionLoading}
                >
                  {actionLoading ? (
                    <>
                      <svg className="animate-spin" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                        <path d="M21 12a9 9 0 11-6.219-8.56"/>
                      </svg>
                      Processing...
                    </>
                  ) : (
                    <>
                      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                        <path d="M22 11.08V12a10 10 0 11-5.93-9.14"/>
                        <polyline points="22 4 12 14.01 9 11.01"/>
                      </svg>
                      Approve Booking
                    </>
                  )}
                </Button>
              </div>
            )}
          </div>
        </div>
      )}

      {/* Reject Reason Modal */}
      {showRejectModal && selectedRequest && (
        <div className="fixed inset-0 z-[60] flex items-center justify-center p-4">
          <div
            className="absolute inset-0 bg-black/60 backdrop-blur-sm animate-fade-in"
            onClick={() => { setShowRejectModal(false); setRejectReason(""); }}
          />
          <div className="relative z-10 w-[440px] max-w-[92vw] animate-scale-in rounded-2xl border border-[var(--border)] bg-[var(--modal-bg)] shadow-2xl">
            <div className="p-6">
              <div className="mx-auto mb-4 flex h-12 w-12 items-center justify-center rounded-xl bg-rose-500/10 border border-rose-500/20">
                <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" className="text-rose-400">
                  <circle cx="12" cy="12" r="10"/><line x1="15" y1="9" x2="9" y2="15"/><line x1="9" y1="9" x2="15" y2="15"/>
                </svg>
              </div>
              <h3 className="text-center text-lg font-semibold text-[var(--text-heading)] mb-2">
                Reject Booking Request
              </h3>
              <p className="text-center text-sm text-[var(--text-muted)] mb-4">
                Are you sure you want to reject this booking request? The unit will become available again.
              </p>
              <div className="mb-4">
                <label className="text-sm font-medium text-[var(--text-secondary)] mb-2 block">
                  Reason for rejection (optional)
                </label>
                <textarea
                  value={rejectReason}
                  onChange={(e) => setRejectReason(e.target.value)}
                  placeholder="Enter a reason for rejecting this request..."
                  className="w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-4 py-3 text-sm text-[var(--text-primary)] placeholder:text-[var(--text-muted)] min-h-[100px] resize-none transition-all focus:border-[var(--accent)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-glow)]"
                />
              </div>
              <div className="flex gap-3">
                <Button
                  variant="ghost"
                  className="flex-1"
                  onClick={() => { setShowRejectModal(false); setRejectReason(""); }}
                  disabled={actionLoading}
                >
                  Cancel
                </Button>
                <Button
                  variant="danger"
                  className="flex-1"
                  onClick={() => handleReject(selectedRequest.id)}
                  disabled={actionLoading}
                >
                  {actionLoading ? "Rejecting..." : "Reject Request"}
                </Button>
              </div>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
