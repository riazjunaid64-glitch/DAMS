import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { api } from "../api/api.ts";
import type { User } from "../App.tsx";
import Button from "../lib/Button.tsx";
import Container from "../lib/Container.tsx";
import Field from "../lib/Field.tsx";

type Props = { user: User | null };

interface BookingDetail {
  id: number;
  bookingReference: string;
  customerName: string;
  customerPhone: string;
  projectName: string;
  unitNumber: string;
  status: string;
  agreedSalePrice: number;
  discountAmount: number;
  bookingAmountRequired: number;
  bookingAmountReceived: number;
  bookingAmountRemaining: number;
  totalInstallmentAmount: number;
  installmentPlanStartDate?: string | null;
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
  notes?: string | null;
}

interface InstallmentSchedule {
  bookingId: number;
  bookingReference: string;
  bookingStatus: string;
  agreedSalePrice: number;
  discountAmount: number;
  bookingAmountReceived: number;
  possessionAmount: number;
  installmentPool: number;
  frequency?: string | null;
  numberOfInstallments?: number | null;
  installmentStartDate?: string | null;
  possessionDueDate?: string | null;
  generatedAt?: string | null;
  hasSchedule: boolean;
  canGenerate: boolean;
  canRegenerate: boolean;
  scheduleTotal: number;
  items: ScheduleItem[];
}

const FREQUENCIES = [
  { value: "Monthly", label: "Monthly" },
  { value: "Quarterly", label: "Quarterly" },
  { value: "HalfYearly", label: "Half-Yearly" },
  { value: "Yearly", label: "Yearly" },
];

function formatMoney(n: number) {
  return n.toLocaleString(undefined, { minimumFractionDigits: 0, maximumFractionDigits: 2 });
}

function formatDate(iso: string) {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleDateString();
}

function toDateInput(iso?: string | null) {
  if (!iso) return "";
  return iso.slice(0, 10);
}

export default function BookingDetailPage({ user }: Props) {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const bookingId = Number(id);

  const [booking, setBooking] = useState<BookingDetail | null>(null);
  const [schedule, setSchedule] = useState<InstallmentSchedule | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [showRegenerateConfirm, setShowRegenerateConfirm] = useState(false);

  const [form, setForm] = useState({
    agreedSalePrice: "",
    discountAmount: "0",
    discountReason: "",
    frequency: "Monthly",
    numberOfInstallments: "12",
    installmentStartDate: "",
    possessionAmount: "0",
    possessionDueDate: "",
  });

  const isAdmin = user?.role === "Admin";

  const load = useCallback(async () => {
    if (!bookingId) return;
    setLoading(true);
    setError(null);
    try {
      const [bookRes, schedRes] = await Promise.all([
        api(`/api/Booking/${bookingId}`),
        api(`/api/Booking/${bookingId}/installments`),
      ]);
      if (!bookRes.ok) throw new Error("Booking not found");
      const b: BookingDetail = await bookRes.json();
      setBooking(b);

      if (schedRes.ok) {
        const s: InstallmentSchedule = await schedRes.json();
        setSchedule(s);
        if (!s.hasSchedule) {
          setForm((prev) => ({
            ...prev,
            agreedSalePrice: String(b.agreedSalePrice),
            discountAmount: String(b.discountAmount ?? 0),
            installmentStartDate: toDateInput(b.installmentPlanStartDate) || new Date().toISOString().slice(0, 10),
          }));
        } else {
          setForm((prev) => ({
            ...prev,
            agreedSalePrice: String(s.agreedSalePrice),
            discountAmount: String(s.discountAmount ?? 0),
            frequency: s.frequency ?? "Monthly",
            numberOfInstallments: String(s.numberOfInstallments ?? 12),
            installmentStartDate: toDateInput(s.installmentStartDate),
            possessionAmount: String(s.possessionAmount ?? 0),
            possessionDueDate: toDateInput(s.possessionDueDate),
          }));
        }
      }
    } catch {
      setError("Unable to load booking.");
    } finally {
      setLoading(false);
    }
  }, [bookingId]);

  useEffect(() => {
    if (isAdmin) load();
  }, [isAdmin, load]);

  const previewPool = useMemo(() => {
    const agreed = Number(form.agreedSalePrice) || 0;
    const possession = Number(form.possessionAmount) || 0;
    const received = booking?.bookingAmountReceived ?? 0;
    return Math.max(0, agreed - received - possession);
  }, [form.agreedSalePrice, form.possessionAmount, booking?.bookingAmountReceived]);

  const previewPerInstallment = useMemo(() => {
    const n = Number(form.numberOfInstallments) || 0;
    if (n <= 0 || previewPool <= 0) return 0;
    return Math.round((previewPool / n) * 100) / 100;
  }, [previewPool, form.numberOfInstallments]);

  const handleGenerate = async (e: FormEvent, regenerate: boolean) => {
    e.preventDefault();
    setSubmitting(true);
    setError(null);
    try {
      const possessionAmt = Number(form.possessionAmount) || 0;
      const body = {
        agreedSalePrice: Number(form.agreedSalePrice),
        discountAmount: Number(form.discountAmount) || 0,
        discountReason: form.discountReason.trim() || null,
        frequency: form.frequency,
        numberOfInstallments: Number(form.numberOfInstallments),
        installmentStartDate: form.installmentStartDate,
        possessionAmount: possessionAmt,
        possessionDueDate: possessionAmt > 0 ? form.possessionDueDate || null : null,
        regenerate,
      };
      const res = await api(`/api/Booking/${bookingId}/installment-plan/generate`, {
        method: "POST",
        body: JSON.stringify(body),
      });
      const data = await res.json();
      if (!res.ok) throw new Error(data.message || "Failed to generate schedule");
      setSchedule(data);
      setShowRegenerateConfirm(false);
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to generate schedule.");
    } finally {
      setSubmitting(false);
    }
  };

  if (!isAdmin) {
    return <Container className="py-16 text-center"><p className="text-[var(--text-muted)]">Admin access required.</p></Container>;
  }

  if (loading) {
    return <Container className="py-16 text-center"><p className="text-[var(--text-muted)]">Loading...</p></Container>;
  }

  if (!booking) {
    return (
      <Container className="py-16 text-center">
        <p className="text-rose-400">{error ?? "Booking not found."}</p>
        <Button className="mt-4" variant="outline" onClick={() => navigate("/confirmed-bookings")}>Back</Button>
      </Container>
    );
  }

  const canShowPlanForm = booking.status === "PaymentPlanActive" && schedule && (schedule.canGenerate || schedule.canRegenerate);
  const planLocked = schedule?.hasSchedule && !schedule.canRegenerate;

  return (
    <Container className="py-10">
      <Button variant="ghost" size="sm" className="mb-6" onClick={() => navigate("/confirmed-bookings")}>← Confirmed Bookings</Button>

      <div className="mb-8 flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <h1 className="text-2xl font-bold text-[var(--text-heading)]">{booking.bookingReference}</h1>
          <p className="mt-1 text-sm text-[var(--text-muted)]">
            {booking.customerName} · {booking.projectName} · Unit {booking.unitNumber}
          </p>
        </div>
        <span className="inline-flex w-fit rounded-full border border-indigo-500/20 bg-indigo-500/10 px-3 py-1 text-xs font-medium text-indigo-400">
          {booking.status.replace(/([A-Z])/g, " $1").trim()}
        </span>
      </div>

      {error && <div className="mb-6 rounded-xl border border-rose-500/20 bg-rose-500/10 px-4 py-3 text-sm text-rose-400">{error}</div>}

      {/* Financial summary */}
      <div className="mb-8 grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        {[
          { label: "Agreed Sale Price", value: formatMoney(booking.agreedSalePrice) },
          { label: "Booking Amount Received", value: `${formatMoney(booking.bookingAmountReceived)} / ${formatMoney(booking.bookingAmountRequired)}` },
          { label: "Booking Amount Remaining", value: formatMoney(booking.bookingAmountRemaining) },
          { label: "Installment Pool (preview)", value: formatMoney(schedule?.installmentPool ?? previewPool) },
        ].map((item) => (
          <div key={item.label} className="rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] p-4">
            <p className="text-xs text-[var(--text-muted)]">{item.label}</p>
            <p className="mt-1 text-lg font-semibold text-[var(--text-heading)]">{item.value}</p>
          </div>
        ))}
      </div>

      {booking.status !== "PaymentPlanActive" && (
        <div className="mb-8 rounded-2xl border border-amber-500/20 bg-amber-500/10 px-4 py-3 text-sm text-amber-300">
          Installment schedule can be generated once the booking amount is fully received and status is Payment Plan Active.
        </div>
      )}

      {/* Plan configuration */}
      {canShowPlanForm && (
        <div className="mb-8 rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] p-6">
          <h2 className="mb-1 text-lg font-semibold text-[var(--text-heading)]">
            {schedule?.hasSchedule ? "Regenerate Installment Plan" : "Configure Installment Plan"}
          </h2>
          <p className="mb-6 text-sm text-[var(--text-muted)]">
            Define the negotiated structure for this booking. Each booking has its own schedule.
          </p>

          {planLocked && (
            <div className="mb-4 rounded-xl border border-[var(--border)] bg-[var(--surface-glass-hover)] px-4 py-3 text-sm text-[var(--text-muted)]">
              Schedule is locked because payments exist or installments are no longer all pending.
            </div>
          )}

          <form onSubmit={(e) => {
            if (schedule?.hasSchedule && schedule.canRegenerate) {
              e.preventDefault();
              setShowRegenerateConfirm(true);
            } else {
              handleGenerate(e, false);
            }
          }} className="grid gap-4 sm:grid-cols-2">
            <Field label="Agreed Sale Price" type="number" min="0" step="0.01" required
              value={form.agreedSalePrice} disabled={planLocked}
              onChange={(e) => setForm({ ...form, agreedSalePrice: e.target.value })} />
            <Field label="Discount Amount" type="number" min="0" step="0.01"
              value={form.discountAmount} disabled={planLocked}
              onChange={(e) => setForm({ ...form, discountAmount: e.target.value })} />
            <label className="flex flex-col gap-1.5 text-sm font-medium text-[var(--text-secondary)]">
              <span>Frequency</span>
              <select value={form.frequency} onChange={(e) => setForm({ ...form, frequency: e.target.value })}
                disabled={planLocked}
                className="w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-4 py-3 text-sm text-[var(--text-primary)]">
                {FREQUENCIES.map((f) => <option key={f.value} value={f.value}>{f.label}</option>)}
              </select>
            </label>
            <Field label="Number of Installments" type="number" min={1} max={600} required
              value={form.numberOfInstallments} disabled={planLocked}
              onChange={(e) => setForm({ ...form, numberOfInstallments: e.target.value })} />
            <Field label="Installment Start Date" type="date" required
              value={form.installmentStartDate} disabled={planLocked}
              onChange={(e) => setForm({ ...form, installmentStartDate: e.target.value })} />
            <Field label="Possession Amount (optional)" type="number" min="0" step="0.01"
              value={form.possessionAmount} disabled={planLocked}
              onChange={(e) => setForm({ ...form, possessionAmount: e.target.value })} />
            {Number(form.possessionAmount) > 0 && (
              <Field label="Possession Due Date" type="date" required
                value={form.possessionDueDate} disabled={planLocked}
                onChange={(e) => setForm({ ...form, possessionDueDate: e.target.value })} />
            )}

            <div className="sm:col-span-2 rounded-xl border border-[var(--border)] bg-[var(--surface-glass-hover)] px-4 py-3 text-sm text-[var(--text-secondary)]">
              <strong>Preview:</strong> {form.numberOfInstallments} installments × ~{formatMoney(previewPerInstallment)}
              {Number(form.possessionAmount) > 0 && ` + possession ${formatMoney(Number(form.possessionAmount))}`}
              {" "}(pool {formatMoney(previewPool)})
            </div>

            {!planLocked && (
              <div className="sm:col-span-2">
                <Button type="submit" disabled={submitting}>
                  {submitting ? "Generating..." : schedule?.hasSchedule ? "Regenerate Schedule" : "Generate Schedule"}
                </Button>
              </div>
            )}
          </form>

          {showRegenerateConfirm && (
            <div className="mt-4 rounded-xl border border-amber-500/30 bg-amber-500/10 p-4">
              <p className="text-sm text-amber-200">This will replace the existing pending schedule. Continue?</p>
              <div className="mt-3 flex gap-2">
                <Button size="sm" type="button" onClick={() => { void handleGenerate({ preventDefault: () => {} } as FormEvent, true); }} disabled={submitting}>Yes, regenerate</Button>
                <Button size="sm" variant="ghost" onClick={() => setShowRegenerateConfirm(false)}>Cancel</Button>
              </div>
            </div>
          )}
        </div>
      )}

      {/* Schedule table */}
      {schedule?.hasSchedule && schedule.items.length > 0 && (
        <div className="rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] overflow-hidden">
          <div className="border-b border-[var(--border)] px-6 py-4 flex justify-between items-center">
            <div>
              <h2 className="text-lg font-semibold text-[var(--text-heading)]">Installment Schedule</h2>
              <p className="text-xs text-[var(--text-muted)]">
                Generated {schedule.generatedAt ? formatDate(schedule.generatedAt) : "—"} · Total {formatMoney(schedule.scheduleTotal)}
              </p>
            </div>
          </div>
          <table className="w-full text-left text-sm">
            <thead className="border-b border-[var(--border)] bg-[var(--surface-glass-hover)]">
              <tr>
                {["#", "Type", "Due Date", "Amount", "Paid", "Remaining", "Status", "Notes"].map((h) => (
                  <th key={h} className="px-4 py-3 font-medium text-[var(--text-muted)]">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {schedule.items.map((item) => (
                <tr key={item.id} className="border-b border-[var(--border)] last:border-0">
                  <td className="px-4 py-3">{item.type === "Possession" ? "—" : item.sequenceNumber}</td>
                  <td className="px-4 py-3">
                    <span className={item.type === "Possession" ? "text-violet-400" : "text-[var(--text-secondary)]"}>
                      {item.type === "Possession" ? "Possession" : "Regular"}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-[var(--text-secondary)]">{formatDate(item.dueDate)}</td>
                  <td className="px-4 py-3 font-medium text-[var(--text-heading)]">{formatMoney(item.amount)}</td>
                  <td className="px-4 py-3 text-[var(--text-secondary)]">{formatMoney(item.amountPaid)}</td>
                  <td className="px-4 py-3 text-[var(--text-secondary)]">{formatMoney(item.remainingBalance)}</td>
                  <td className="px-4 py-3">
                    <span className="rounded-full border border-[var(--border)] px-2 py-0.5 text-xs">{item.status}</span>
                  </td>
                  <td className="px-4 py-3 text-[var(--text-muted)] text-xs max-w-[160px] truncate">{item.notes ?? "—"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Container>
  );
}
