import { Link, useNavigate } from "react-router-dom";
import { Button, EmptyState, IconFile, StatusBadge, cx } from "../../components/ui";
import { formatDay } from "../../lib/dates.ts";
import { formatPkr } from "../../utils/currency.ts";
import { stillDue, type CustomerBooking } from "./customerBookings.ts";

function Figure({ label, value, className, valueClass }: { label: string; value: string; className?: string; valueClass?: string }) {
  return (
    <div className={className}>
      <dt className="text-caption font-bold uppercase tracking-[0.4px] text-ink-muted">{label}</dt>
      <dd className={cx("m-0 mt-0.5 text-body font-extrabold tabular-nums md:text-[17px]", valueClass ?? "text-ink")}>{value}</dd>
    </div>
  );
}

function BookingCard({ booking }: { booking: CustomerBooking }) {
  const navigate = useNavigate();
  const open = () => navigate(`/confirmed-bookings/${booking.id}`);
  return (
    // The booking number is a link stretched over the whole card, so the card opens the booking
    // without nesting the Details button inside a link.
    <article className="relative flex flex-col gap-3 rounded-card border border-line bg-card p-4 font-ui text-ink transition-colors focus-within:border-ink-faint hover:border-ink-faint md:flex-row md:items-center md:gap-6 md:px-5">
      <div className="min-w-0 md:w-[260px] md:shrink-0">
        <div className="flex items-center justify-between gap-3">
          <Link
            to={`/confirmed-bookings/${booking.id}`}
            className="text-section font-extrabold text-ink no-underline after:absolute after:inset-0 after:content-[''] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary"
          >
            {booking.bookingReference}
          </Link>
          <StatusBadge status={booking.status} />
        </div>
        <p className="m-0 mt-1.5 flex items-center gap-2 text-body text-ink-2">
          <IconFile size={16} className="shrink-0 text-ink-muted" />
          <b className="font-extrabold text-ink">Unit {booking.unitNumber}</b>
          <span>· {booking.projectName}</span>
        </p>
        <p className="m-0 mt-0.5 text-small text-ink-muted">Booked {formatDay(booking.bookingDate)}</p>
      </div>
      <dl className="m-0 grid grid-cols-2 gap-x-6 gap-y-3 md:flex md:flex-1 md:items-center md:gap-10">
        <Figure label="Net price" value={formatPkr(booking.netPrice)} />
        <Figure label="Paid" value={formatPkr(booking.collected)} valueClass="text-success" />
        <Figure label="Still due" value={stillDue(booking)} className="max-md:col-span-2" />
      </dl>
      <Button size="sm" variant="outline" className="relative z-10 max-md:hidden" aria-label={`Details of ${booking.bookingReference}`} onClick={open}>
        Details
      </Button>
    </article>
  );
}

/** The Bookings tab: one card per booking, newest first, or the shared empty state. */
export function CustomerBookings({ bookings }: { bookings: CustomerBooking[] }) {
  if (bookings.length === 0) return <EmptyState title="No bookings yet" />;
  return (
    <ul className="m-0 flex list-none flex-col gap-3 p-0">
      {bookings.map((booking) => <li key={booking.id}><BookingCard booking={booking} /></li>)}
    </ul>
  );
}
