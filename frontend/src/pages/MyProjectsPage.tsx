import { useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { api } from "../api/api.ts";
import type { User } from "../App.tsx";
import Container from "../lib/Container.tsx";
import Button from "../lib/Button.tsx";

type Props = { user: User | null };

interface MyProject {
  id: number;
  bookingReference: string;
  projectName: string;
  unitNumber: string;
  unitType: string;
  unitSize: number;
  status: string;
  agreedSalePrice: number;
  bookingAmountReceived: number;
  bookingAmountRequired: number;
  bookingAmountRemaining: number;
  installmentPaid: number;
  installmentRemaining: number;
  hasInstallmentSchedule: boolean;
  bookingDate: string;
  customerName: string;
}

function formatMoney(n: number) {
  return n.toLocaleString(undefined, { minimumFractionDigits: 0, maximumFractionDigits: 2 });
}

function formatDate(iso: string) {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleDateString("en-US", { month: "short", day: "numeric", year: "numeric" });
}

function statusColor(status: string) {
  switch (status) {
    case "PaymentPlanActive":
    case "PossessionGiven":
    case "SaleCompleted":
      return "text-emerald-400 bg-emerald-500/10 border-emerald-500/20";
    case "AwaitingBookingAmount":
      return "text-amber-400 bg-amber-500/10 border-amber-500/20";
    default:
      return "text-[var(--text-muted)] bg-[var(--surface-glass)] border-[var(--border)]";
  }
}

function prettyStatus(status: string) {
  return status.replace(/([A-Z])/g, " $1").trim();
}

export default function MyProjectsPage({ user }: Props) {
  const navigate = useNavigate();
  const [projects, setProjects] = useState<MyProject[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const isClient = user && user.role !== "Admin";

  useEffect(() => {
    if (user?.role === "Admin") {
      navigate("/", { replace: true });
    }
  }, [user, navigate]);

  useEffect(() => {
    if (!isClient) return;
    const load = async () => {
      setLoading(true);
      setError(null);
      try {
        const res = await api("/api/MyProjects");
        if (!res.ok) {
          const d = await res.json().catch(() => ({}));
          setError((d as { message?: string }).message ?? "Failed to load your projects.");
          return;
        }
        const raw = await res.json() as Array<Record<string, unknown>>;
        setProjects(raw.map(r => ({
          id: Number(r.id),
          bookingReference: String(r.bookingReference ?? ""),
          projectName: String(r.projectName ?? ""),
          unitNumber: String(r.unitNumber ?? ""),
          unitType: String(r.unitType ?? ""),
          unitSize: Number(r.unitSize ?? 0),
          status: String(r.status ?? ""),
          agreedSalePrice: Number(r.agreedSalePrice ?? 0),
          bookingAmountReceived: Number(r.bookingAmountReceived ?? 0),
          bookingAmountRequired: Number(r.bookingAmountRequired ?? 0),
          bookingAmountRemaining: Number(r.bookingAmountRemaining ?? 0),
          installmentPaid: Number(r.installmentPaid ?? 0),
          installmentRemaining: Number(r.installmentRemaining ?? 0),
          hasInstallmentSchedule: Boolean(r.hasInstallmentSchedule),
          bookingDate: String(r.bookingDate ?? ""),
          customerName: String(r.customerName ?? ""),
        })));
      } catch {
        setError("Could not load your projects.");
      } finally {
        setLoading(false);
      }
    };
    load();
  }, [isClient]);

  if (user?.role === "Admin") return null;

  if (!user) {
    return (
      <Container className="py-20">
        <div className="mx-auto max-w-lg rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] px-6 py-12 text-center">
          <h1 className="text-xl font-semibold text-[var(--text-heading)]">My Projects</h1>
          <p className="mt-3 text-sm text-[var(--text-muted)]">
            Please log in to view the properties you have purchased.
          </p>
          <Button className="mt-6" onClick={() => navigate("/projects")}>Browse Projects</Button>
        </div>
      </Container>
    );
  }

  return (
    <>
      <div className="relative overflow-hidden border-b border-[var(--border)]">
        <div className="absolute inset-0 mesh-gradient-subtle" />
        <Container className="relative py-8 sm:py-10">
          <div className="mb-2 inline-flex items-center gap-2 rounded-full bg-indigo-500/[0.08] px-3 py-1">
            <span className="h-1.5 w-1.5 rounded-full bg-indigo-500" />
            <span className="text-[10px] font-semibold uppercase tracking-wider text-indigo-500">Your Purchases</span>
          </div>
          <h1 className="text-2xl font-bold text-[var(--text-heading)] sm:text-3xl">My Projects</h1>
          <p className="mt-1 text-sm text-[var(--text-muted)]">
            Properties linked to your account email <span className="text-[var(--text-secondary)]">{user.email}</span>
          </p>
        </Container>
      </div>

      <Container className="py-8">
        {error && (
          <div className="mb-6 rounded-xl border border-rose-500/20 bg-rose-500/[0.06] px-4 py-3 text-sm text-rose-300">{error}</div>
        )}

        {loading && (
          <div className="grid gap-4 sm:grid-cols-2">
            {[...Array(4)].map((_, i) => <div key={i} className="skeleton h-48 rounded-2xl" />)}
          </div>
        )}

        {!loading && !error && projects.length === 0 && (
          <div className="py-20 text-center">
            <div className="mx-auto mb-4 flex h-16 w-16 items-center justify-center rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)]">
              <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" className="text-[var(--text-muted)]" strokeLinecap="round">
                <path d="M3 9l9-7 9 7v11a2 2 0 01-2 2H5a2 2 0 01-2-2z"/><polyline points="9 22 9 12 15 12 15 22"/>
              </svg>
            </div>
            <h3 className="text-lg font-semibold text-[var(--text-heading)]">No purchases found</h3>
            <p className="mt-2 max-w-md mx-auto text-sm text-[var(--text-muted)]">
              We could not find any confirmed bookings for <span className="font-medium text-[var(--text-secondary)]">{user.email}</span>.
              Your purchase email must match your login email.
            </p>
            <Button className="mt-6" onClick={() => navigate("/projects")}>Browse Projects</Button>
          </div>
        )}

        {!loading && projects.length > 0 && (
          <div className="grid gap-5 sm:grid-cols-2">
            {projects.map(p => (
              <Link
                key={p.id}
                to={`/my-projects/${p.id}`}
                className="group rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] p-6 transition-all hover:border-[var(--accent)]/30 hover:shadow-md"
              >
                <div className="mb-4 flex items-start justify-between gap-3">
                  <div>
                    <p className="text-lg font-semibold text-[var(--text-heading)] group-hover:text-[var(--accent)] transition-colors">{p.projectName}</p>
                    <p className="text-sm text-[var(--text-muted)]">{p.unitType} · Unit {p.unitNumber}</p>
                  </div>
                  <span className={`shrink-0 rounded-full border px-2.5 py-1 text-[10px] font-semibold ${statusColor(p.status)}`}>
                    {prettyStatus(p.status)}
                  </span>
                </div>

                <div className="mb-4 grid grid-cols-2 gap-3 text-sm">
                  <div>
                    <p className="text-xs text-[var(--text-muted)]">Booking Ref</p>
                    <p className="font-medium text-[var(--text-secondary)]">{p.bookingReference}</p>
                  </div>
                  <div>
                    <p className="text-xs text-[var(--text-muted)]">Booked On</p>
                    <p className="font-medium text-[var(--text-secondary)]">{formatDate(p.bookingDate)}</p>
                  </div>
                  <div>
                    <p className="text-xs text-[var(--text-muted)]">Sale Price</p>
                    <p className="font-semibold text-[var(--text-primary)]">{formatMoney(p.agreedSalePrice)}</p>
                  </div>
                  <div>
                    <p className="text-xs text-[var(--text-muted)]">Size</p>
                    <p className="font-medium text-[var(--text-secondary)]">{formatMoney(p.unitSize)} sq ft</p>
                  </div>
                </div>

                <div className="mb-3 grid grid-cols-2 gap-2 border-t border-[var(--border)] pt-4 text-xs">
                  <div>
                    <p className="text-[var(--text-muted)]">Booking Paid</p>
                    <p className="font-semibold text-emerald-400">{formatMoney(p.bookingAmountReceived)}</p>
                    <p className="text-[var(--text-muted)]">Remaining {formatMoney(p.bookingAmountRemaining || Math.max(0, p.bookingAmountRequired - p.bookingAmountReceived))}</p>
                  </div>
                  <div>
                    <p className="text-[var(--text-muted)]">Installment Paid</p>
                    <p className="font-semibold text-emerald-400">{p.hasInstallmentSchedule ? formatMoney(p.installmentPaid) : "—"}</p>
                    <p className="text-[var(--text-muted)]">
                      Remaining {p.hasInstallmentSchedule ? formatMoney(p.installmentRemaining) : "—"}
                    </p>
                  </div>
                </div>
                <div className="flex justify-end">
                  <span className="text-xs font-semibold text-[var(--accent)] group-hover:underline">View Details →</span>
                </div>
              </Link>
            ))}
          </div>
        )}
      </Container>
    </>
  );
}
