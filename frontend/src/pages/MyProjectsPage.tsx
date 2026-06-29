import { useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { api } from "../api/api.ts";
import type { User } from "../App.tsx";
import Container from "../lib/Container.tsx";
import Button from "../lib/Button.tsx";
import { formatPkr } from "../utils/currency.ts";

type Props = { user: User | null };

/* ------------------------------------------------------------------ */
/* Types                                                               */
/* ------------------------------------------------------------------ */

interface MyBooking {
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

interface MyRequest {
  id: number;
  unitId: number;
  unitNumber: string;
  unitType: string;
  unitPrice: number;
  projectName: string;
  projectLocation: string;
  status: "Pending" | "Approved" | "Rejected";
  requestedAt: string;
  reviewedAt: string | null;
  rejectionReason: string | null;
  notes: string | null;
}

/** A normalised entry in the customer journey, built from either a request or a booking. */
interface JourneyItem {
  key: string;
  kind: "request" | "booking";
  /** sort weight — lower shows first */
  rank: number;
  title: string; // project name
  subtitle: string; // unit type · unit number
  price: number;
  date: string;
  reference?: string;
  /** index of the active step in JOURNEY_STEPS; -1 when declined/cancelled */
  currentStep: number;
  badgeLabel: string;
  badgeClass: string;
  declined?: { reason: string | null };
  booking?: MyBooking;
  request?: MyRequest;
}

/* ------------------------------------------------------------------ */
/* Journey definition                                                  */
/* ------------------------------------------------------------------ */

const JOURNEY_STEPS = [
  "Requested",
  "Approved",
  "Booking Amount",
  "Installments",
  "Possession",
] as const;

function formatMoney(n: number) {
  return formatPkr(n);
}

function formatDate(iso: string) {
  const d = new Date(iso);
  return Number.isNaN(d.getTime())
    ? iso
    : d.toLocaleDateString("en-US", { month: "short", day: "numeric", year: "numeric" });
}

/* ------------------------------------------------------------------ */
/* Mapping requests + bookings → journey items                         */
/* ------------------------------------------------------------------ */

function requestToJourney(r: MyRequest): JourneyItem | null {
  // Approved requests already surface as confirmed bookings — avoid duplicates.
  if (r.status === "Approved") return null;

  if (r.status === "Rejected") {
    return {
      key: `req-${r.id}`,
      kind: "request",
      rank: 40,
      title: r.projectName,
      subtitle: `${r.unitType} · Unit ${r.unitNumber}`,
      price: r.unitPrice,
      date: r.reviewedAt ?? r.requestedAt,
      currentStep: -1,
      badgeLabel: "Declined",
      badgeClass: "text-rose-400 bg-rose-500/10 border-rose-500/20",
      declined: { reason: r.rejectionReason },
      request: r,
    };
  }

  // Pending
  return {
    key: `req-${r.id}`,
    kind: "request",
    rank: 0,
    title: r.projectName,
    subtitle: `${r.unitType} · Unit ${r.unitNumber}`,
    price: r.unitPrice,
    date: r.requestedAt,
    currentStep: 0,
    badgeLabel: "Under Review",
    badgeClass: "text-amber-400 bg-amber-500/10 border-amber-500/20",
    request: r,
  };
}

function bookingToJourney(b: MyBooking): JourneyItem {
  let currentStep = 2;
  let badgeLabel = "Awaiting Booking Amount";
  let badgeClass = "text-amber-400 bg-amber-500/10 border-amber-500/20";
  let rank = 10;

  switch (b.status) {
    case "AwaitingBookingAmount":
      currentStep = 2;
      badgeLabel = "Awaiting Booking Amount";
      badgeClass = "text-amber-400 bg-amber-500/10 border-amber-500/20";
      rank = 10;
      break;
    case "PaymentPlanActive":
      currentStep = 3;
      badgeLabel = "Installments Active";
      badgeClass = "text-indigo-400 bg-indigo-500/10 border-indigo-500/20";
      rank = 11;
      break;
    case "PossessionGiven":
      currentStep = 4;
      badgeLabel = "Possession Given";
      badgeClass = "text-emerald-400 bg-emerald-500/10 border-emerald-500/20";
      rank = 20;
      break;
    case "SaleCompleted":
      currentStep = 5; // all done
      badgeLabel = "Sale Completed";
      badgeClass = "text-emerald-400 bg-emerald-500/10 border-emerald-500/20";
      rank = 21;
      break;
    case "Cancelled":
      currentStep = -1;
      badgeLabel = "Cancelled";
      badgeClass = "text-rose-400 bg-rose-500/10 border-rose-500/20";
      rank = 41;
      break;
  }

  return {
    key: `book-${b.id}`,
    kind: "booking",
    rank,
    title: b.projectName,
    subtitle: `${b.unitType} · Unit ${b.unitNumber}`,
    price: b.agreedSalePrice,
    date: b.bookingDate,
    reference: b.bookingReference,
    currentStep,
    badgeLabel,
    badgeClass,
    declined: b.status === "Cancelled" ? { reason: null } : undefined,
    booking: b,
  };
}

/* ------------------------------------------------------------------ */
/* Journey stepper                                                     */
/* ------------------------------------------------------------------ */

function JourneyStepper({ currentStep }: { currentStep: number }) {
  return (
    <div className="flex items-center">
      {JOURNEY_STEPS.map((label, i) => {
        const done = i < currentStep;
        const active = i === currentStep;
        const dotClass = done
          ? "bg-emerald-500 border-emerald-500 text-white"
          : active
          ? "bg-[var(--accent)] border-[var(--accent)] text-white ring-4 ring-[var(--accent-glow)]"
          : "bg-[var(--surface-glass)] border-[var(--border)] text-[var(--text-muted)]";
        const lineClass = i < currentStep ? "bg-emerald-500" : "bg-[var(--border)]";
        return (
          <div key={label} className="flex flex-1 flex-col items-center last:flex-none">
            <div className="flex w-full items-center">
              <div
                className={`flex h-6 w-6 shrink-0 items-center justify-center rounded-full border text-[10px] font-bold transition-all ${dotClass}`}
              >
                {done ? (
                  <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round">
                    <polyline points="20 6 9 17 4 12" />
                  </svg>
                ) : (
                  i + 1
                )}
              </div>
              {i < JOURNEY_STEPS.length - 1 && <div className={`h-0.5 flex-1 ${lineClass}`} />}
            </div>
            <span
              className={`mt-1.5 max-w-[64px] text-center text-[9px] font-medium leading-tight ${
                active ? "text-[var(--accent)]" : done ? "text-emerald-400" : "text-[var(--text-muted)]"
              }`}
            >
              {label}
            </span>
          </div>
        );
      })}
    </div>
  );
}

/* ------------------------------------------------------------------ */
/* Cards                                                               */
/* ------------------------------------------------------------------ */

function CardShell({ item, children, to }: { item: JourneyItem; children: React.ReactNode; to?: string }) {
  const inner = (
    <>
      <div className="mb-4 flex items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="truncate text-lg font-semibold text-[var(--text-heading)] group-hover:text-[var(--accent)] transition-colors">
            {item.title}
          </p>
          <p className="truncate text-sm text-[var(--text-muted)]">{item.subtitle}</p>
        </div>
        <span className={`shrink-0 rounded-full border px-2.5 py-1 text-[10px] font-semibold ${item.badgeClass}`}>
          {item.badgeLabel}
        </span>
      </div>
      {children}
    </>
  );

  const base =
    "group block rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] p-6 transition-all";

  if (to) {
    return (
      <Link to={to} className={`${base} hover:border-[var(--accent)]/30 hover:shadow-md`}>
        {inner}
      </Link>
    );
  }
  return <div className={base}>{inner}</div>;
}

function PendingCard({ item }: { item: JourneyItem }) {
  return (
    <CardShell item={item}>
      <div className="mb-5 grid grid-cols-2 gap-3 text-sm">
        <div>
          <p className="text-xs text-[var(--text-muted)]">Unit Price</p>
          <p className="font-semibold text-[var(--text-primary)]">{formatMoney(item.price)}</p>
        </div>
        <div>
          <p className="text-xs text-[var(--text-muted)]">Requested On</p>
          <p className="font-medium text-[var(--text-secondary)]">{formatDate(item.date)}</p>
        </div>
      </div>

      <div className="mb-4">
        <JourneyStepper currentStep={item.currentStep} />
      </div>

      <div className="flex items-start gap-2.5 rounded-xl border border-amber-500/20 bg-amber-500/[0.06] px-3.5 py-3">
        <span className="mt-0.5 h-2 w-2 shrink-0 rounded-full bg-amber-400 animate-pulse" />
        <p className="text-xs leading-relaxed text-amber-200/90">
          Your request has been received and is being reviewed by our team. We&apos;ll notify you as soon as it&apos;s
          approved, and your booking details will appear here.
        </p>
      </div>
    </CardShell>
  );
}

function DeclinedCard({ item }: { item: JourneyItem }) {
  const navigate = useNavigate();
  return (
    <CardShell item={item}>
      <div className="mb-4 grid grid-cols-2 gap-3 text-sm">
        <div>
          <p className="text-xs text-[var(--text-muted)]">Unit Price</p>
          <p className="font-semibold text-[var(--text-primary)]">{formatMoney(item.price)}</p>
        </div>
        <div>
          <p className="text-xs text-[var(--text-muted)]">Reviewed On</p>
          <p className="font-medium text-[var(--text-secondary)]">{formatDate(item.date)}</p>
        </div>
      </div>
      {item.declined?.reason && (
        <div className="mb-4 rounded-xl border border-rose-500/20 bg-rose-500/[0.06] px-3.5 py-3">
          <p className="mb-1 text-[10px] font-semibold uppercase tracking-wider text-rose-400">Reason</p>
          <p className="text-xs leading-relaxed text-rose-200/90">{item.declined.reason}</p>
        </div>
      )}
      <Button variant="outline" size="sm" onClick={() => navigate("/projects")}>
        Browse Other Units
      </Button>
    </CardShell>
  );
}

/** Stage-specific banner telling the customer exactly what happens next. */
function bookingActionBanner(b: MyBooking): { tone: "approved" | "info" | "success"; title: string; body: string } {
  const bookingRemaining =
    b.bookingAmountRemaining || Math.max(0, b.bookingAmountRequired - b.bookingAmountReceived);

  switch (b.status) {
    case "AwaitingBookingAmount":
      if (b.bookingAmountReceived <= 0) {
        return {
          tone: "approved",
          title: "🎉 Your request has been approved!",
          body:
            b.bookingAmountRequired > 0
              ? `Please pay the booking amount of ${formatMoney(b.bookingAmountRequired)} to confirm your unit. Our team will guide you through the payment.`
              : "Our team will contact you shortly to arrange your booking amount and confirm your unit.",
        };
      }
      return {
        tone: "info",
        title: "Booking amount in progress",
        body: `You've paid ${formatMoney(b.bookingAmountReceived)} of ${formatMoney(
          b.bookingAmountRequired
        )}. ${formatMoney(bookingRemaining)} remaining to confirm your unit.`,
      };
    case "PaymentPlanActive":
      return {
        tone: "info",
        title: "Booking confirmed — installment plan active",
        body: "Your booking amount is received. View your installment schedule and download payment receipts below.",
      };
    case "PossessionGiven":
      return { tone: "success", title: "Possession granted", body: "Congratulations! Possession of your unit has been handed over." };
    case "SaleCompleted":
      return { tone: "success", title: "Sale completed", body: "All payments are complete and your purchase is finalised. Thank you!" };
    default:
      return { tone: "info", title: "", body: "" };
  }
}

const bannerTone: Record<string, string> = {
  approved: "border-emerald-500/25 bg-emerald-500/[0.08] text-emerald-200/90",
  info: "border-indigo-500/20 bg-indigo-500/[0.06] text-indigo-200/90",
  success: "border-emerald-500/25 bg-emerald-500/[0.08] text-emerald-200/90",
};

function BookingCard({ item }: { item: JourneyItem }) {
  const b = item.booking!;
  const bookingRemaining =
    b.bookingAmountRemaining || Math.max(0, b.bookingAmountRequired - b.bookingAmountReceived);
  const banner = bookingActionBanner(b);
  return (
    <CardShell item={item} to={`/my-projects/${b.id}`}>
      <div className="mb-4 grid grid-cols-2 gap-3 text-sm">
        <div>
          <p className="text-xs text-[var(--text-muted)]">Booking Ref</p>
          <p className="font-medium text-[var(--text-secondary)]">{b.bookingReference}</p>
        </div>
        <div>
          <p className="text-xs text-[var(--text-muted)]">Sale Price</p>
          <p className="font-semibold text-[var(--text-primary)]">{formatMoney(b.agreedSalePrice)}</p>
        </div>
      </div>

      <div className="mb-5">
        <JourneyStepper currentStep={item.currentStep} />
      </div>

      {banner.title && (
        <div className={`mb-4 rounded-xl border px-3.5 py-3 ${bannerTone[banner.tone]}`}>
          <p className="text-xs font-semibold">{banner.title}</p>
          <p className="mt-0.5 text-xs leading-relaxed opacity-90">{banner.body}</p>
        </div>
      )}

      <div className="mb-3 grid grid-cols-2 gap-2 border-t border-[var(--border)] pt-4 text-xs">
        <div>
          <p className="text-[var(--text-muted)]">Booking Paid</p>
          <p className="font-semibold text-emerald-400">{formatMoney(b.bookingAmountReceived)}</p>
          <p className="text-[var(--text-muted)]">Remaining {formatMoney(bookingRemaining)}</p>
        </div>
        <div>
          <p className="text-[var(--text-muted)]">Installment Paid</p>
          <p className="font-semibold text-emerald-400">
            {b.hasInstallmentSchedule ? formatMoney(b.installmentPaid) : "—"}
          </p>
          <p className="text-[var(--text-muted)]">
            Remaining {b.hasInstallmentSchedule ? formatMoney(b.installmentRemaining) : "—"}
          </p>
        </div>
      </div>
      <div className="flex justify-end">
        <span className="text-xs font-semibold text-[var(--accent)] group-hover:underline">
          View details &amp; receipts →
        </span>
      </div>
    </CardShell>
  );
}

/* ------------------------------------------------------------------ */
/* Page                                                                */
/* ------------------------------------------------------------------ */

export default function MyProjectsPage({ user }: Props) {
  const navigate = useNavigate();
  const [items, setItems] = useState<JourneyItem[]>([]);
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
        const [bookRes, reqRes] = await Promise.all([
          api("/api/MyProjects"),
          api("/api/BookingRequest/my-requests"),
        ]);

        if (!bookRes.ok && !reqRes.ok) {
          setError("Failed to load your projects.");
          return;
        }

        const bookings: MyBooking[] = bookRes.ok ? await bookRes.json() : [];
        const requests: MyRequest[] = reqRes.ok ? await reqRes.json() : [];

        const journey: JourneyItem[] = [
          ...bookings.map(bookingToJourney),
          ...requests.map(requestToJourney).filter((x): x is JourneyItem => x !== null),
        ];

        journey.sort((a, b) => {
          if (a.rank !== b.rank) return a.rank - b.rank;
          return new Date(b.date).getTime() - new Date(a.date).getTime();
        });

        setItems(journey);
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
            Please log in to view the properties you have requested or purchased.
          </p>
          <Button className="mt-6" onClick={() => navigate("/projects")}>
            Browse Projects
          </Button>
        </div>
      </Container>
    );
  }

  const pendingCount = items.filter((i) => i.kind === "request" && i.currentStep === 0).length;
  const activeCount = items.filter((i) => i.kind === "booking" && i.currentStep >= 0 && i.currentStep < 5).length;
  const completedCount = items.filter((i) => i.currentStep >= 5).length;

  return (
    <>
      <div className="relative overflow-hidden border-b border-[var(--border)]">
        <div className="absolute inset-0 mesh-gradient-subtle" />
        <Container className="relative py-8 sm:py-10">
          <div className="mb-2 inline-flex items-center gap-2 rounded-full bg-indigo-500/[0.08] px-3 py-1">
            <span className="h-1.5 w-1.5 rounded-full bg-indigo-500" />
            <span className="text-[10px] font-semibold uppercase tracking-wider text-indigo-500">Your Journey</span>
          </div>
          <h1 className="text-2xl font-bold text-[var(--text-heading)] sm:text-3xl">My Projects</h1>
          <p className="mt-1 text-sm text-[var(--text-muted)]">
            Track every property from booking request to possession ·{" "}
            <span className="text-[var(--text-secondary)]">{user.email}</span>
          </p>

          {!loading && items.length > 0 && (
            <div className="mt-6 grid max-w-2xl grid-cols-3 gap-3">
              {[
                { label: "Under Review", value: pendingCount, color: "text-amber-400" },
                { label: "Active", value: activeCount, color: "text-indigo-400" },
                { label: "Completed", value: completedCount, color: "text-emerald-400" },
              ].map((s) => (
                <div key={s.label} className="rounded-xl border border-[var(--border)] bg-[var(--bg-card)] px-4 py-3">
                  <p className={`text-xl font-bold ${s.color}`}>{s.value}</p>
                  <p className="text-xs text-[var(--text-muted)]">{s.label}</p>
                </div>
              ))}
            </div>
          )}
        </Container>
      </div>

      <Container className="py-8">
        {error && (
          <div className="mb-6 rounded-xl border border-rose-500/20 bg-rose-500/[0.06] px-4 py-3 text-sm text-rose-300">
            {error}
          </div>
        )}

        {loading && (
          <div className="grid gap-5 sm:grid-cols-2">
            {[...Array(4)].map((_, i) => (
              <div key={i} className="skeleton h-64 rounded-2xl" />
            ))}
          </div>
        )}

        {!loading && !error && items.length === 0 && (
          <div className="py-20 text-center">
            <div className="mx-auto mb-4 flex h-16 w-16 items-center justify-center rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)]">
              <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" className="text-[var(--text-muted)]" strokeLinecap="round">
                <path d="M3 9l9-7 9 7v11a2 2 0 01-2 2H5a2 2 0 01-2-2z" />
                <polyline points="9 22 9 12 15 12 15 22" />
              </svg>
            </div>
            <h3 className="text-lg font-semibold text-[var(--text-heading)]">No projects yet</h3>
            <p className="mx-auto mt-2 max-w-md text-sm text-[var(--text-muted)]">
              You haven&apos;t requested any units yet. Browse available properties and submit a booking request — it
              will appear here and you can track it all the way to possession.
            </p>
            <Button className="mt-6" onClick={() => navigate("/projects")}>
              Browse Projects
            </Button>
          </div>
        )}

        {!loading && items.length > 0 && (
          <div
            className={
              items.length === 1
                ? "mx-auto max-w-xl"
                : "grid gap-5 sm:grid-cols-2"
            }
          >
            {items.map((item) => {
              if (item.kind === "booking" && !item.declined) return <BookingCard key={item.key} item={item} />;
              if (item.declined) return <DeclinedCard key={item.key} item={item} />;
              return <PendingCard key={item.key} item={item} />;
            })}
          </div>
        )}
      </Container>
    </>
  );
}
