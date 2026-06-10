import { useCallback, useEffect, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { api } from "../api/api.ts";
import type { User } from "../App.tsx";
import Container from "../lib/Container.tsx";
import Button from "../lib/Button.tsx";
import TabLayout from "../lib/TabLayout.tsx";

type Props = { user: User | null };

interface BookingFull {
  id: number;
  bookingReference: string;
  customerName: string;
  customerFatherName?: string | null;
  customerPhone: string;
  customerEmail?: string | null;
  customerCnic?: string | null;
  customerAddress?: string | null;
  projectName: string;
  projectId: number;
  unitNumber: string;
  unitType: string;
  unitFloorNumber: number;
  unitSize: number;
  status: string;
  agreedSalePrice: number;
  discountAmount: number;
  bookingAmountRequired: number;
  bookingAmountReceived: number;
  bookingAmountRemaining: number;
  totalInstallmentAmount: number;
  installmentPaid: number;
  installmentRemaining: number;
  hasInstallmentSchedule: boolean;
  bookingDate: string;
  possessionDate?: string | null;
  tower?: string | null;
  apartmentCategory?: string | null;
  payments: Array<{
    id: number;
    type: string;
    amount: number;
    paymentMethod: string;
    paymentReference?: string | null;
    receiptNumber?: string | null;
    paidAt: string;
  }>;
}

interface ScheduleItem {
  id: number;
  sequenceNumber: number;
  type: string;
  dueDate: string;
  amount: number;
  status: string;
  amountPaid: number;
  remainingBalance: number;
  isOverdue: boolean;
}

interface InstallmentSchedule {
  hasSchedule: boolean;
  scheduleTotal: number;
  schedulePaid: number;
  scheduleRemaining: number;
  frequency?: string | null;
  numberOfInstallments?: number | null;
  items: ScheduleItem[];
}

const TABS = [
  { id: "overview", label: "Overview", icon: <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M3 9l9-7 9 7v11a2 2 0 01-2 2H5a2 2 0 01-2-2z"/></svg> },
  { id: "payments", label: "Payment History", icon: <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><rect x="1" y="4" width="22" height="16" rx="2"/><line x1="1" y1="10" x2="23" y2="10"/></svg> },
  { id: "installments", label: "Installments", icon: <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><line x1="12" y1="1" x2="12" y2="23"/><path d="M17 5H9.5a3.5 3.5 0 000 7h5a3.5 3.5 0 010 7H6"/></svg> },
];

function formatMoney(n: number) {
  return n.toLocaleString(undefined, { minimumFractionDigits: 0, maximumFractionDigits: 2 });
}

function formatDate(iso: string) {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleDateString();
}

function prettyStatus(status: string) {
  return status.replace(/([A-Z])/g, " $1").trim();
}

function statusBadge(status: string) {
  switch (status) {
    case "Paid": return "border-emerald-500/30 bg-emerald-500/10 text-emerald-400";
    case "PartiallyPaid": return "border-amber-500/30 bg-amber-500/10 text-amber-300";
    case "Overdue": return "border-rose-500/30 bg-rose-500/10 text-rose-400";
    default: return "border-[var(--border)] text-[var(--text-muted)]";
  }
}

export default function MyProjectDetailPage({ user }: Props) {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const bookingId = Number(id);
  const [booking, setBooking] = useState<BookingFull | null>(null);
  const [schedule, setSchedule] = useState<InstallmentSchedule | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState("overview");

  const isClient = user && user.role !== "Admin";

  useEffect(() => {
    if (user?.role === "Admin") {
      navigate("/", { replace: true });
    }
  }, [user, navigate]);

  const load = useCallback(async () => {
    if (!bookingId || Number.isNaN(bookingId)) return;
    setLoading(true);
    setError(null);
    try {
      const [bookRes, schedRes] = await Promise.all([
        api(`/api/MyProjects/${bookingId}`),
        api(`/api/MyProjects/${bookingId}/installments`),
      ]);
      if (!bookRes.ok) throw new Error("Not found");
      const b = await bookRes.json() as BookingFull;
      setBooking({ ...b, payments: b.payments ?? [] });
      if (schedRes.ok) setSchedule(await schedRes.json());
    } catch {
      setError("Unable to load project details.");
    } finally {
      setLoading(false);
    }
  }, [bookingId]);

  useEffect(() => {
    if (!isClient) return;
    load();
  }, [isClient, load]);

  if (user?.role === "Admin") return null;

  if (!user) {
    return (
      <Container className="py-20 text-center">
        <p className="text-[var(--text-muted)]">Please log in to view this project.</p>
        <Button className="mt-4" onClick={() => navigate("/my-projects")}>Back to My Projects</Button>
      </Container>
    );
  }

  if (loading) {
    return <Container className="py-20"><div className="skeleton h-64 rounded-2xl" /></Container>;
  }

  if (error || !booking) {
    return (
      <Container className="py-20 text-center">
        <p className="text-rose-400">{error ?? "Project not found."}</p>
        <Button className="mt-4" variant="outline" onClick={() => navigate("/my-projects")}>← My Projects</Button>
      </Container>
    );
  }

  return (
    <>
      <div className="relative overflow-hidden border-b border-[var(--border)]">
        <div className="absolute inset-0 mesh-gradient-subtle" />
        <Container className="relative py-8 sm:py-10">
          <Link to="/my-projects" className="mb-4 inline-flex items-center gap-1 text-sm text-[var(--text-muted)] hover:text-[var(--text-primary)]">
            ← My Projects
          </Link>
          <h1 className="text-2xl font-bold text-[var(--text-heading)] sm:text-3xl">{booking.projectName}</h1>
          <p className="mt-1 text-sm text-[var(--text-muted)]">
            {booking.unitType} · Unit {booking.unitNumber} · {booking.bookingReference}
          </p>
        </Container>
      </div>

      <Container className="py-6">
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {[
            {
              label: "Booking Amount Paid",
              value: formatMoney(booking.bookingAmountReceived),
              sub: `of ${formatMoney(booking.bookingAmountRequired)}`,
              color: "text-emerald-400",
            },
            {
              label: "Booking Remaining",
              value: formatMoney(booking.bookingAmountRemaining ?? Math.max(0, booking.bookingAmountRequired - booking.bookingAmountReceived)),
              sub: "due on booking amount",
              color: booking.bookingAmountRemaining > 0 ? "text-amber-400" : "text-emerald-400",
            },
            {
              label: "Installment Paid",
              value: formatMoney(booking.installmentPaid ?? schedule?.schedulePaid ?? 0),
              sub: booking.hasInstallmentSchedule ? "from installment plan" : "no plan yet",
              color: "text-emerald-400",
            },
            {
              label: "Installment Remaining",
              value: formatMoney(booking.installmentRemaining ?? schedule?.scheduleRemaining ?? 0),
              sub: booking.hasInstallmentSchedule ? "left on schedule" : "—",
              color: (booking.installmentRemaining ?? 0) > 0 ? "text-rose-400" : "text-emerald-400",
            },
          ].map(item => (
            <div key={item.label} className="rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] p-4">
              <p className="text-xs text-[var(--text-muted)]">{item.label}</p>
              <p className={`mt-1 text-xl font-bold ${item.color}`}>{item.value}</p>
              <p className="mt-0.5 text-[10px] text-[var(--text-muted)]">{item.sub}</p>
            </div>
          ))}
        </div>
      </Container>

      <TabLayout tabs={TABS} activeTab={activeTab} onTabChange={setActiveTab}>
        <Container className="py-8">
          {activeTab === "overview" && (
            <div className="grid gap-6 lg:grid-cols-2">
              <div className="rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] p-6">
                <h2 className="section-title mb-4">Unit Details</h2>
                <dl className="space-y-3 text-sm">
                  {[
                    ["Project", booking.projectName],
                    ["Unit", `${booking.unitType} (${booking.unitNumber})`],
                    ["Floor", String(booking.unitFloorNumber)],
                    ["Size", `${formatMoney(booking.unitSize)} sq ft`],
                    ["Category", booking.apartmentCategory ?? "—"],
                    ["Tower / Block", booking.tower ?? "—"],
                    ["Status", prettyStatus(booking.status)],
                    ["Booking Date", formatDate(booking.bookingDate)],
                    ["Possession Date", booking.possessionDate ? formatDate(booking.possessionDate) : "—"],
                  ].map(([label, value]) => (
                    <div key={label} className="flex justify-between gap-4 border-b border-[var(--border)] pb-2 last:border-0">
                      <dt className="text-[var(--text-muted)]">{label}</dt>
                      <dd className="font-medium text-[var(--text-secondary)] text-right">{value}</dd>
                    </div>
                  ))}
                </dl>
              </div>

              <div className="space-y-6">
                <div className="rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] p-6">
                  <h2 className="section-title mb-4">Your Details</h2>
                  <dl className="space-y-3 text-sm">
                    {[
                      ["Name", booking.customerName],
                      ["Father Name", booking.customerFatherName ?? "—"],
                      ["Phone", booking.customerPhone],
                      ["Email", booking.customerEmail ?? "—"],
                      ["CNIC", booking.customerCnic ?? "—"],
                      ["Address", booking.customerAddress ?? "—"],
                    ].map(([label, value]) => (
                      <div key={label} className="flex justify-between gap-4 border-b border-[var(--border)] pb-2 last:border-0">
                        <dt className="text-[var(--text-muted)]">{label}</dt>
                        <dd className="font-medium text-[var(--text-secondary)] text-right max-w-[60%]">{value}</dd>
                      </div>
                    ))}
                  </dl>
                </div>

                <div className="rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] p-6">
                  <h2 className="section-title mb-4">Financial Summary</h2>
                  <dl className="space-y-3 text-sm">
                    {[
                      ["Agreed Sale Price", formatMoney(booking.agreedSalePrice)],
                      ["Discount", formatMoney(booking.discountAmount)],
                      ["Booking Amount Required", formatMoney(booking.bookingAmountRequired)],
                      ["Booking Amount Paid", formatMoney(booking.bookingAmountReceived)],
                      ["Booking Amount Remaining", formatMoney(booking.bookingAmountRemaining ?? Math.max(0, booking.bookingAmountRequired - booking.bookingAmountReceived))],
                      ["Installment Paid", formatMoney(booking.installmentPaid ?? 0)],
                      ["Installment Remaining", formatMoney(booking.installmentRemaining ?? 0)],
                      ["Installment Total", formatMoney(booking.totalInstallmentAmount)],
                    ].map(([label, value]) => (
                      <div key={label} className="flex justify-between gap-4 border-b border-[var(--border)] pb-2 last:border-0">
                        <dt className="text-[var(--text-muted)]">{label}</dt>
                        <dd className="font-semibold text-[var(--text-primary)]">{value}</dd>
                      </div>
                    ))}
                  </dl>
                </div>
              </div>
            </div>
          )}

          {activeTab === "payments" && (
            <div className="space-y-4">
              <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
                <div>
                  <h2 className="section-title">Payment History</h2>
                  <p className="text-sm text-[var(--text-muted)]">
                    {booking.payments.length} payment{booking.payments.length !== 1 ? "s" : ""} · Total{" "}
                    <span className="font-semibold text-emerald-400">
                      {formatMoney(booking.payments.reduce((s, p) => s + p.amount, 0))}
                    </span>
                  </p>
                </div>
              </div>
            <div className="overflow-x-auto rounded-xl border border-[var(--border)] bg-[var(--bg-card)] shadow-sm">
              <table className="min-w-[700px] w-full border-collapse text-left">
                <thead>
                  <tr className="border-b border-[var(--border)] bg-[var(--surface-glass)]">
                    {["Receipt #", "Date", "Type", "Amount", "Method", "Reference", "Print"].map(h => (
                      <th key={h} className="px-5 py-3 text-[10px] font-bold uppercase tracking-wider text-[var(--text-muted)]">{h}</th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {booking.payments.length === 0 ? (
                    <tr><td colSpan={7} className="px-5 py-12 text-center text-sm text-[var(--text-muted)]">No payments recorded yet.</td></tr>
                  ) : booking.payments.map(p => (
                    <tr key={p.id} className="border-b border-[var(--border)] hover:bg-[var(--surface-glass-hover)] last:border-b-0">
                      <td className="px-5 py-4 text-sm font-medium text-[var(--text-primary)]">{p.receiptNumber ?? "—"}</td>
                      <td className="px-5 py-4 text-sm text-[var(--text-secondary)]">{formatDate(p.paidAt)}</td>
                      <td className="px-5 py-4 text-sm text-[var(--text-secondary)]">{prettyStatus(p.type)}</td>
                      <td className="px-5 py-4 text-sm font-semibold text-emerald-400">{formatMoney(p.amount)}</td>
                      <td className="px-5 py-4 text-sm text-[var(--text-secondary)]">{prettyStatus(p.paymentMethod)}</td>
                      <td className="px-5 py-4 text-sm text-[var(--text-muted)]">{p.paymentReference ?? "—"}</td>
                      <td className="px-5 py-4">
                        <button
                          type="button"
                          onClick={() => window.open(`/receipt/${booking.id}/${p.id}`, "_blank")}
                          className="inline-flex items-center gap-1.5 rounded-lg border border-[var(--accent-glow-strong)] bg-[var(--accent-glow)] px-3 py-1.5 text-xs font-semibold text-[var(--accent)] transition-colors hover:border-[var(--accent)] hover:bg-[var(--surface-glass-hover)]"
                        >
                          <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><polyline points="6 9 6 2 18 2 18 9"/><path d="M6 18H4a2 2 0 01-2-2v-5a2 2 0 012-2h16a2 2 0 012 2v5a2 2 0 01-2 2h-2"/><rect x="6" y="14" width="12" height="8"/></svg>
                          Print
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            </div>
          )}

          {activeTab === "installments" && (
            schedule?.hasSchedule || booking.hasInstallmentSchedule ? (
              <div className="space-y-4">
                <div className="grid gap-3 sm:grid-cols-3">
                  {[
                    ["Installment Total", schedule?.scheduleTotal ?? (booking.installmentPaid + booking.installmentRemaining)],
                    ["Installment Paid", schedule?.schedulePaid ?? booking.installmentPaid],
                    ["Installment Remaining", schedule?.scheduleRemaining ?? booking.installmentRemaining],
                  ].map(([label, val]) => (
                    <div key={String(label)} className="rounded-xl border border-[var(--border)] bg-[var(--surface-glass)] p-4">
                      <p className="text-xs text-[var(--text-muted)]">{label}</p>
                      <p className={`text-xl font-bold ${String(label).includes("Paid") ? "text-emerald-400" : String(label).includes("Remaining") ? "text-rose-400" : "text-[var(--text-heading)]"}`}>
                        {formatMoney(Number(val))}
                      </p>
                    </div>
                  ))}
                </div>
                <div className="overflow-x-auto rounded-xl border border-[var(--border)] bg-[var(--bg-card)] shadow-sm">
                  <table className="min-w-[700px] w-full border-collapse text-left">
                    <thead>
                      <tr className="border-b border-[var(--border)] bg-[var(--surface-glass)]">
                        {["#", "Type", "Due Date", "Amount", "Paid", "Remaining", "Status"].map(h => (
                          <th key={h} className="px-5 py-3 text-[10px] font-bold uppercase tracking-wider text-[var(--text-muted)]">{h}</th>
                        ))}
                      </tr>
                    </thead>
                    <tbody>
                      {(schedule?.items ?? []).map(item => (
                        <tr key={item.id} className="border-b border-[var(--border)] hover:bg-[var(--surface-glass-hover)] last:border-b-0">
                          <td className="px-5 py-4 text-sm text-[var(--text-secondary)]">{item.sequenceNumber}</td>
                          <td className="px-5 py-4 text-sm text-[var(--text-secondary)]">{prettyStatus(item.type)}</td>
                          <td className="px-5 py-4 text-sm text-[var(--text-secondary)]">{formatDate(item.dueDate)}</td>
                          <td className="px-5 py-4 text-sm font-medium text-[var(--text-primary)]">{formatMoney(item.amount)}</td>
                          <td className="px-5 py-4 text-sm text-emerald-400">{formatMoney(item.amountPaid)}</td>
                          <td className="px-5 py-4 text-sm text-[var(--text-secondary)]">{formatMoney(item.remainingBalance)}</td>
                          <td className="px-5 py-4">
                            <span className={`inline-flex rounded-full border px-2.5 py-1 text-[10px] font-semibold ${statusBadge(item.status)}`}>
                              {item.isOverdue ? "Overdue" : prettyStatus(item.status)}
                            </span>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </div>
            ) : (
              <div className="py-16 text-center text-sm text-[var(--text-muted)]">Installment plan has not been generated yet.</div>
            )
          )}
        </Container>
      </TabLayout>
    </>
  );
}
