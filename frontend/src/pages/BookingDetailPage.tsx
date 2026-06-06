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
  isOverdue: boolean;
  paidAt?: string | null;
  notes?: string | null;
}

interface BookingPayment {
  id: number;
  bookingId: number;
  installmentId?: number | null;
  type: string;
  amount: number;
  paymentMethod: string;
  paymentReference?: string | null;
  receiptNumber?: string | null;
  notes?: string | null;
  paidAt: string;
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
  schedulePaid: number;
  scheduleRemaining: number;
  items: ScheduleItem[];
}

const FREQUENCIES = [
  { value: "Monthly", label: "Monthly" },
  { value: "Quarterly", label: "Quarterly" },
  { value: "HalfYearly", label: "Half-Yearly" },
  { value: "Yearly", label: "Yearly" },
];

const PAYMENT_METHODS = [
  { value: "Cash", label: "Cash" },
  { value: "BankTransfer", label: "Bank Transfer" },
  { value: "Cheque", label: "Cheque" },
  { value: "Online", label: "Online" },
];

function statusBadgeClass(status: string) {
  switch (status) {
    case "Paid":
      return "border-emerald-500/30 bg-emerald-500/10 text-emerald-400";
    case "PartiallyPaid":
      return "border-amber-500/30 bg-amber-500/10 text-amber-300";
    case "Overdue":
      return "border-rose-500/30 bg-rose-500/10 text-rose-400";
    default:
      return "border-[var(--border)] text-[var(--text-muted)]";
  }
}

function prettyStatus(status: string) {
  return status.replace(/([A-Z])/g, " $1").trim();
}

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
  const [payments, setPayments] = useState<BookingPayment[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [showRegenerateConfirm, setShowRegenerateConfirm] = useState(false);

  const [payTarget, setPayTarget] = useState<ScheduleItem | null>(null);
  const [paySubmitting, setPaySubmitting] = useState(false);
  const [payError, setPayError] = useState<string | null>(null);
  const [payForm, setPayForm] = useState({
    amount: "",
    paymentMethod: "Cash",
    paymentReference: "",
    notes: "",
    paidAt: new Date().toISOString().slice(0, 10),
  });

  // Booking amount: set negotiated terms + record booking-amount payments.
  const [showFinancials, setShowFinancials] = useState(false);
  const [finSubmitting, setFinSubmitting] = useState(false);
  const [finError, setFinError] = useState<string | null>(null);
  const [finForm, setFinForm] = useState({
    agreedSalePrice: "",
    discountAmount: "0",
    discountReason: "",
    bookingAmountRequired: "",
    bookingAmountDueDate: "",
  });

  const [showBookingPay, setShowBookingPay] = useState(false);
  const [bookingPaySubmitting, setBookingPaySubmitting] = useState(false);
  const [bookingPayError, setBookingPayError] = useState<string | null>(null);
  const [bookingPayForm, setBookingPayForm] = useState({
    amount: "",
    paymentMethod: "Cash",
    paymentReference: "",
    notes: "",
    paidAt: new Date().toISOString().slice(0, 10),
  });

  const [cancelling, setCancelling] = useState(false);
  const [showCancel, setShowCancel] = useState(false);
  const [cancelReason, setCancelReason] = useState("");

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
      const [bookRes, schedRes, payRes] = await Promise.all([
        api(`/api/Booking/${bookingId}`),
        api(`/api/Booking/${bookingId}/installments`),
        api(`/api/Booking/${bookingId}/payments`),
      ]);
      if (!bookRes.ok) throw new Error("Booking not found");
      const b: BookingDetail = await bookRes.json();
      setBooking(b);

      setFinForm({
        agreedSalePrice: String(b.agreedSalePrice || ""),
        discountAmount: String(b.discountAmount ?? 0),
        discountReason: "",
        bookingAmountRequired: b.bookingAmountRequired ? String(b.bookingAmountRequired) : "",
        bookingAmountDueDate: "",
      });

      if (payRes.ok) {
        setPayments(await payRes.json());
      }

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

  const openPayModal = (item: ScheduleItem) => {
    setPayError(null);
    setPayForm({
      amount: String(item.remainingBalance),
      paymentMethod: "Cash",
      paymentReference: "",
      notes: "",
      paidAt: new Date().toISOString().slice(0, 10),
    });
    setPayTarget(item);
  };

  const handleRecordPayment = async (e: FormEvent) => {
    e.preventDefault();
    if (!payTarget) return;
    setPaySubmitting(true);
    setPayError(null);
    try {
      const body = {
        amount: Number(payForm.amount),
        paymentMethod: payForm.paymentMethod,
        paymentReference: payForm.paymentReference.trim() || null,
        notes: payForm.notes.trim() || null,
        paidAt: payForm.paidAt || null,
      };
      const res = await api(`/api/Booking/${bookingId}/installments/${payTarget.id}/payment`, {
        method: "POST",
        body: JSON.stringify(body),
      });
      const data = await res.json();
      if (!res.ok) throw new Error(data.message || "Failed to record payment");
      setSchedule(data);
      setPayTarget(null);
      await load();
    } catch (err) {
      setPayError(err instanceof Error ? err.message : "Failed to record payment.");
    } finally {
      setPaySubmitting(false);
    }
  };

  const handleSaveFinancials = async (e: FormEvent) => {
    e.preventDefault();
    setFinSubmitting(true);
    setFinError(null);
    try {
      const body = {
        agreedSalePrice: Number(finForm.agreedSalePrice),
        discountAmount: Number(finForm.discountAmount) || 0,
        discountReason: finForm.discountReason.trim() || null,
        bookingAmountRequired: Number(finForm.bookingAmountRequired),
        bookingAmountDueDate: finForm.bookingAmountDueDate || null,
      };
      const res = await api(`/api/Booking/${bookingId}/financials`, {
        method: "PUT",
        body: JSON.stringify(body),
      });
      const data = await res.json();
      if (!res.ok) throw new Error(data.message || "Failed to save terms");
      setShowFinancials(false);
      await load();
    } catch (err) {
      setFinError(err instanceof Error ? err.message : "Failed to save terms.");
    } finally {
      setFinSubmitting(false);
    }
  };

  const openBookingPay = () => {
    setBookingPayError(null);
    const remaining = booking ? booking.bookingAmountRequired - booking.bookingAmountReceived : 0;
    setBookingPayForm({
      amount: remaining > 0 ? String(remaining) : "",
      paymentMethod: "Cash",
      paymentReference: "",
      notes: "",
      paidAt: new Date().toISOString().slice(0, 10),
    });
    setShowBookingPay(true);
  };

  const handleRecordBookingPay = async (e: FormEvent) => {
    e.preventDefault();
    setBookingPaySubmitting(true);
    setBookingPayError(null);
    try {
      const body = {
        amount: Number(bookingPayForm.amount),
        paymentMethod: bookingPayForm.paymentMethod,
        paymentReference: bookingPayForm.paymentReference.trim() || null,
        notes: bookingPayForm.notes.trim() || null,
        paidAt: bookingPayForm.paidAt || null,
      };
      const res = await api(`/api/Booking/${bookingId}/booking-amount-payment`, {
        method: "POST",
        body: JSON.stringify(body),
      });
      const data = await res.json();
      if (!res.ok) throw new Error(data.message || "Failed to record payment");
      setShowBookingPay(false);
      await load();
    } catch (err) {
      setBookingPayError(err instanceof Error ? err.message : "Failed to record payment.");
    } finally {
      setBookingPaySubmitting(false);
    }
  };

  const handleCancelBooking = async () => {
    setCancelling(true);
    try {
      const res = await api(`/api/Booking/${bookingId}/cancel`, {
        method: "POST",
        body: JSON.stringify({ reason: cancelReason.trim() || null }),
      });
      const data = await res.json().catch(() => ({}));
      if (!res.ok) throw new Error(data.message || "Failed to cancel booking");
      setShowCancel(false);
      setCancelReason("");
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to cancel booking.");
    } finally {
      setCancelling(false);
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
        <div className="flex items-center gap-3">
          <span className="inline-flex w-fit rounded-full border border-indigo-500/20 bg-indigo-500/10 px-3 py-1 text-xs font-medium text-indigo-400">
            {booking.status.replace(/([A-Z])/g, " $1").trim()}
          </span>
          {booking.status !== "Cancelled" && booking.status !== "PossessionGiven" && booking.status !== "SaleCompleted" && (
            <Button variant="danger" size="sm" onClick={() => { setCancelReason(""); setShowCancel(true); }}>
              Cancel Booking
            </Button>
          )}
        </div>
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

      {/* Booking amount workflow (step before installment plan) */}
      {booking.status === "AwaitingBookingAmount" && (
        <div className="mb-8 rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] p-6">
          <div className="flex flex-col gap-1 sm:flex-row sm:items-center sm:justify-between">
            <div>
              <h2 className="text-lg font-semibold text-[var(--text-heading)]">Booking Amount</h2>
              <p className="text-sm text-[var(--text-muted)]">
                Set the negotiated terms, then record the booking amount. Once it is fully received, the
                installment plan unlocks.
              </p>
            </div>
            <div className="flex gap-2">
              <Button variant="outline" size="sm" onClick={() => { setFinError(null); setShowFinancials((v) => !v); }}>
                {booking.bookingAmountRequired > 0 ? "Edit Terms" : "Set Terms"}
              </Button>
              <Button
                size="sm"
                disabled={booking.bookingAmountRequired <= 0 || booking.bookingAmountRemaining <= 0}
                onClick={openBookingPay}
              >
                Record Payment
              </Button>
            </div>
          </div>

          {booking.bookingAmountRequired <= 0 && !showFinancials && (
            <div className="mt-4 rounded-xl border border-amber-500/20 bg-amber-500/10 px-4 py-3 text-sm text-amber-300">
              No booking amount has been set yet. Click <strong>Set Terms</strong> to enter the agreed sale price and
              the required booking amount before recording payments.
            </div>
          )}

          {showFinancials && (
            <form onSubmit={handleSaveFinancials} className="mt-5 grid gap-4 sm:grid-cols-2">
              {finError && (
                <div className="sm:col-span-2 rounded-xl border border-rose-500/20 bg-rose-500/10 px-4 py-3 text-sm text-rose-400">
                  {finError}
                </div>
              )}
              <Field label="Agreed Sale Price" type="number" min="0" step="0.01" required
                value={finForm.agreedSalePrice}
                onChange={(e) => setFinForm({ ...finForm, agreedSalePrice: e.target.value })} />
              <Field label="Booking Amount Required" type="number" min="0" step="0.01" required
                value={finForm.bookingAmountRequired}
                onChange={(e) => setFinForm({ ...finForm, bookingAmountRequired: e.target.value })} />
              <Field label="Discount Amount" type="number" min="0" step="0.01"
                value={finForm.discountAmount}
                onChange={(e) => setFinForm({ ...finForm, discountAmount: e.target.value })} />
              <Field label="Booking Amount Due Date (optional)" type="date"
                value={finForm.bookingAmountDueDate}
                onChange={(e) => setFinForm({ ...finForm, bookingAmountDueDate: e.target.value })} />
              <div className="sm:col-span-2">
                <Field label="Discount Reason (optional)"
                  value={finForm.discountReason}
                  onChange={(e) => setFinForm({ ...finForm, discountReason: e.target.value })} />
              </div>
              <div className="sm:col-span-2 flex gap-2">
                <Button type="submit" disabled={finSubmitting}>{finSubmitting ? "Saving..." : "Save Terms"}</Button>
                <Button type="button" variant="ghost" onClick={() => setShowFinancials(false)} disabled={finSubmitting}>Cancel</Button>
              </div>
            </form>
          )}
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
                {" · "}Paid {formatMoney(schedule.schedulePaid)} · Remaining {formatMoney(schedule.scheduleRemaining)}
              </p>
            </div>
          </div>
          <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="border-b border-[var(--border)] bg-[var(--surface-glass-hover)]">
              <tr>
                {["#", "Type", "Due Date", "Amount", "Paid", "Remaining", "Status", "Notes", ""].map((h, i) => (
                  <th key={h || `col-${i}`} className="px-4 py-3 font-medium text-[var(--text-muted)]">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {schedule.items.map((item) => {
                const isPayable = booking.status === "PaymentPlanActive" && item.status !== "Paid" && item.remainingBalance > 0;
                return (
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
                    <span className={`rounded-full border px-2 py-0.5 text-xs ${statusBadgeClass(item.status)}`}>{prettyStatus(item.status)}</span>
                  </td>
                  <td className="px-4 py-3 text-[var(--text-muted)] text-xs max-w-[160px] truncate">{item.notes ?? "—"}</td>
                  <td className="px-4 py-3 text-right">
                    {isPayable && (
                      <Button size="sm" variant="outline" onClick={() => openPayModal(item)}>Record Payment</Button>
                    )}
                  </td>
                </tr>
                );
              })}
            </tbody>
          </table>
          </div>
        </div>
      )}

      {/* Payment history */}
      {payments.length > 0 && (
        <div className="mt-8 rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] overflow-hidden">
          <div className="border-b border-[var(--border)] px-6 py-4">
            <h2 className="text-lg font-semibold text-[var(--text-heading)]">Payment History</h2>
            <p className="text-xs text-[var(--text-muted)]">All recorded payments for this booking with receipt numbers.</p>
          </div>
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="border-b border-[var(--border)] bg-[var(--surface-glass-hover)]">
                <tr>
                  {["Receipt #", "Date", "Type", "For", "Amount", "Method", "Reference", ""].map((h, i) => (
                    <th key={h || `col-${i}`} className="px-4 py-3 font-medium text-[var(--text-muted)]">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {payments.map((p) => {
                  const inst = p.installmentId ? schedule?.items.find((i) => i.id === p.installmentId) : null;
                  const forLabel = p.type === "BookingAmount"
                    ? "Booking Amount"
                    : inst
                      ? (inst.type === "Possession" ? "Possession" : `Installment ${inst.sequenceNumber}`)
                      : "Installment";
                  return (
                    <tr key={p.id} className="border-b border-[var(--border)] last:border-0">
                      <td className="px-4 py-3 font-mono text-xs text-[var(--text-heading)]">{p.receiptNumber ?? "—"}</td>
                      <td className="px-4 py-3 text-[var(--text-secondary)]">{formatDate(p.paidAt)}</td>
                      <td className="px-4 py-3 text-[var(--text-muted)] text-xs">{prettyStatus(p.type)}</td>
                      <td className="px-4 py-3 text-[var(--text-secondary)]">{forLabel}</td>
                      <td className="px-4 py-3 font-medium text-[var(--text-heading)]">{formatMoney(p.amount)}</td>
                      <td className="px-4 py-3 text-[var(--text-secondary)]">{prettyStatus(p.paymentMethod)}</td>
                      <td className="px-4 py-3 text-[var(--text-muted)] text-xs max-w-[160px] truncate">{p.paymentReference ?? "—"}</td>
                      <td className="px-4 py-3 text-right">
                        <Button size="sm" variant="outline" onClick={() => window.open(`/receipt/${bookingId}/${p.id}`, "_blank")}>
                          Receipt
                        </Button>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* Record installment payment modal */}
      {payTarget && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4" onClick={() => !paySubmitting && setPayTarget(null)}>
          <div className="w-full max-w-md rounded-2xl border border-[var(--border)] bg-[var(--modal-bg)] p-6 shadow-xl" onClick={(e) => e.stopPropagation()}>
            <h3 className="text-lg font-semibold text-[var(--text-heading)]">Record Installment Payment</h3>
            <p className="mt-1 text-sm text-[var(--text-muted)]">
              {payTarget.type === "Possession" ? "Possession payment" : `Installment ${payTarget.sequenceNumber}`} · Due {formatDate(payTarget.dueDate)} · Remaining {formatMoney(payTarget.remainingBalance)}
            </p>

            {payError && <div className="mt-4 rounded-xl border border-rose-500/20 bg-rose-500/10 px-4 py-3 text-sm text-rose-400">{payError}</div>}

            <form onSubmit={handleRecordPayment} className="mt-4 grid gap-4">
              <Field label="Amount" type="number" min="0.01" step="0.01" required
                value={payForm.amount}
                onChange={(e) => setPayForm({ ...payForm, amount: e.target.value })} />
              <label className="flex flex-col gap-1.5 text-sm font-medium text-[var(--text-secondary)]">
                <span>Payment Method</span>
                <select value={payForm.paymentMethod} onChange={(e) => setPayForm({ ...payForm, paymentMethod: e.target.value })}
                  className="w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-4 py-3 text-sm text-[var(--text-primary)]">
                  {PAYMENT_METHODS.map((m) => <option key={m.value} value={m.value}>{m.label}</option>)}
                </select>
              </label>
              <Field label="Reference (optional)" value={payForm.paymentReference}
                onChange={(e) => setPayForm({ ...payForm, paymentReference: e.target.value })} />
              <Field label="Payment Date" type="date" value={payForm.paidAt}
                onChange={(e) => setPayForm({ ...payForm, paidAt: e.target.value })} />
              <Field label="Notes (optional)" value={payForm.notes}
                onChange={(e) => setPayForm({ ...payForm, notes: e.target.value })} />

              <div className="flex gap-2">
                <Button type="submit" disabled={paySubmitting}>{paySubmitting ? "Recording..." : "Record Payment"}</Button>
                <Button type="button" variant="ghost" onClick={() => setPayTarget(null)} disabled={paySubmitting}>Cancel</Button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Record booking-amount payment modal */}
      {showBookingPay && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4" onClick={() => !bookingPaySubmitting && setShowBookingPay(false)}>
          <div className="w-full max-w-md rounded-2xl border border-[var(--border)] bg-[var(--modal-bg)] p-6 shadow-xl" onClick={(e) => e.stopPropagation()}>
            <h3 className="text-lg font-semibold text-[var(--text-heading)]">Record Booking Amount Payment</h3>
            <p className="mt-1 text-sm text-[var(--text-muted)]">
              Required {formatMoney(booking.bookingAmountRequired)} · Received {formatMoney(booking.bookingAmountReceived)} · Remaining {formatMoney(booking.bookingAmountRemaining)}
            </p>

            {bookingPayError && <div className="mt-4 rounded-xl border border-rose-500/20 bg-rose-500/10 px-4 py-3 text-sm text-rose-400">{bookingPayError}</div>}

            <form onSubmit={handleRecordBookingPay} className="mt-4 grid gap-4">
              <Field label="Amount" type="number" min="0.01" step="0.01" required
                value={bookingPayForm.amount}
                onChange={(e) => setBookingPayForm({ ...bookingPayForm, amount: e.target.value })} />
              <label className="flex flex-col gap-1.5 text-sm font-medium text-[var(--text-secondary)]">
                <span>Payment Method</span>
                <select value={bookingPayForm.paymentMethod} onChange={(e) => setBookingPayForm({ ...bookingPayForm, paymentMethod: e.target.value })}
                  className="w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-4 py-3 text-sm text-[var(--text-primary)]">
                  {PAYMENT_METHODS.map((m) => <option key={m.value} value={m.value}>{m.label}</option>)}
                </select>
              </label>
              <Field label="Reference (optional)" value={bookingPayForm.paymentReference}
                onChange={(e) => setBookingPayForm({ ...bookingPayForm, paymentReference: e.target.value })} />
              <Field label="Payment Date" type="date" value={bookingPayForm.paidAt}
                onChange={(e) => setBookingPayForm({ ...bookingPayForm, paidAt: e.target.value })} />
              <Field label="Notes (optional)" value={bookingPayForm.notes}
                onChange={(e) => setBookingPayForm({ ...bookingPayForm, notes: e.target.value })} />

              <div className="flex gap-2">
                <Button type="submit" disabled={bookingPaySubmitting}>{bookingPaySubmitting ? "Recording..." : "Record Payment"}</Button>
                <Button type="button" variant="ghost" onClick={() => setShowBookingPay(false)} disabled={bookingPaySubmitting}>Cancel</Button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Cancel booking modal */}
      {showCancel && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4" onClick={() => !cancelling && setShowCancel(false)}>
          <div className="w-full max-w-md rounded-2xl border border-[var(--border)] bg-[var(--modal-bg)] p-6 shadow-xl" onClick={(e) => e.stopPropagation()}>
            <h3 className="text-lg font-semibold text-[var(--text-heading)]">Cancel Booking</h3>
            <p className="mt-1 text-sm text-[var(--text-muted)]">
              This releases unit {booking.unitNumber} back to the market. This cannot be undone.
            </p>

            <div className="mt-4 grid gap-4">
              <Field label="Reason (optional)" value={cancelReason}
                onChange={(e) => setCancelReason(e.target.value)} />
              <div className="flex gap-2">
                <Button type="button" variant="danger" onClick={handleCancelBooking} disabled={cancelling}>
                  {cancelling ? "Cancelling..." : "Confirm Cancel"}
                </Button>
                <Button type="button" variant="ghost" onClick={() => setShowCancel(false)} disabled={cancelling}>Keep Booking</Button>
              </div>
            </div>
          </div>
        </div>
      )}
    </Container>
  );
}
