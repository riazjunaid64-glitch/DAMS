import { useEffect, useState } from "react";
import { useLocation, useNavigate, useSearchParams } from "react-router-dom";
import { api } from "../api/api.ts";
import type { User } from "../App.tsx";
import ApplicationForm, { type ApplicationFormData } from "../components/ApplicationForm.tsx";
import { Button, Notice } from "../components/ui";
import { can } from "../features/access/permissions.ts";
import { PaperPage } from "../features/printing/PaperPage.tsx";
import { usePageTrail } from "../layouts/trail.ts";
import { bookingToApplicationForm, type BookingForForm } from "../utils/bookingToApplicationForm.ts";

type Props = { user: User | null };

type LocationState = { data?: ApplicationFormData; fromCreate?: boolean } | null;

/** "BK-000013 · Usman Tariq · Unit B08" — who the form is for. */
function subtitleFor(b: BookingForForm | null, data: ApplicationFormData | null): string | undefined {
  if (b) return [b.bookingReference, b.customerName, b.unitNumber ? `Unit ${b.unitNumber}` : ""].filter(Boolean).join(" · ");
  if (data) return [data.fullName, data.apartmentNumber ? `Unit ${data.apartmentNumber}` : ""].filter(Boolean).join(" · ") || undefined;
  return undefined;
}

/** The application form inside the app: one booking's (`?bookingId=`), or blank (no id) to fill by hand. */
export default function ApplicationFormPage({ user }: Props) {
  const navigate = useNavigate();
  const location = useLocation();
  const [params] = useSearchParams();
  const bookingId = params.get("bookingId");

  const state = location.state as LocationState;
  const [booking, setBooking] = useState<BookingForForm | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const canUseBookings = can(user?.role, "bookings");
  const fromCreate = state?.fromCreate ?? false;
  const blank = !bookingId && !state?.data;
  const bookingPath = bookingId ? `/confirmed-bookings/${bookingId}` : "/confirmed-bookings";

  // Re-print path: load an existing booking by id when no data was passed via navigation state.
  useEffect(() => {
    if (!canUseBookings || state?.data || !bookingId) return;
    const load = async () => {
      setLoading(true);
      setError(null);
      try {
        const res = await api(`/api/Booking/${bookingId}`);
        if (!res.ok) throw new Error("not found");
        setBooking(await res.json());
      } catch {
        setError("Unable to load this booking's application form.");
      } finally {
        setLoading(false);
      }
    };
    void load();
  }, [canUseBookings, bookingId, state?.data]);

  const data = state?.data ?? (booking ? bookingToApplicationForm(booking) : null);

  usePageTrail(
    !canUseBookings
      ? []
      : blank
        ? [{ label: "Blank form" }]
        : booking
          ? [{ label: booking.bookingReference ?? "Booking", to: bookingPath }, { label: "Application form" }]
          : [],
  );

  if (!canUseBookings) {
    return <div className="py-16 text-center text-[var(--text-muted)]">You do not have access to bookings.</div>;
  }

  if (loading) {
    return <div className="py-16 text-center text-[var(--text-muted)]">Loading application form...</div>;
  }

  const back = () => navigate(fromCreate || blank ? "/confirmed-bookings" : bookingPath);

  if (error) {
    return (
      <div className="mx-auto flex w-full max-w-[640px] flex-col items-start gap-4 px-4 py-10 font-ui">
        <Notice tone="red" role="alert" title={error} />
        <Button variant="outline" onClick={back}>Back</Button>
      </div>
    );
  }

  return (
    <PaperPage
      title={blank ? "Blank application form" : "Application form"}
      subtitle={subtitleFor(booking, data)}
      onBack={back}
      backLabel={blank || fromCreate ? "Back to Bookings" : "Back to booking"}
    >
      <ApplicationForm data={data ?? {}} />
    </PaperPage>
  );
}
