import { useEffect, useState } from "react";
import { useLocation, useNavigate, useSearchParams } from "react-router-dom";
import { api } from "../api/api.ts";
import type { User } from "../App.tsx";
import Button from "../lib/Button.tsx";
import ApplicationForm, { type ApplicationFormData } from "../components/ApplicationForm.tsx";
import { bookingToApplicationForm } from "../utils/bookingToApplicationForm.ts";

type Props = { user: User | null };

type LocationState = { data?: ApplicationFormData; fromCreate?: boolean } | null;

export default function ApplicationFormPage({ user }: Props) {
  const navigate = useNavigate();
  const location = useLocation();
  const [params] = useSearchParams();
  const bookingId = params.get("bookingId");

  const state = location.state as LocationState;
  const [data, setData] = useState<ApplicationFormData | null>(state?.data ?? null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const isAdmin = user?.role === "Admin";
  const fromCreate = state?.fromCreate ?? false;

  // Re-print path: load an existing booking by id when no data was passed via navigation state.
  useEffect(() => {
    if (!isAdmin || state?.data || !bookingId) return;
    const load = async () => {
      setLoading(true);
      setError(null);
      try {
        const res = await api(`/api/Booking/${bookingId}`);
        if (!res.ok) throw new Error("not found");
        setData(bookingToApplicationForm(await res.json()));
      } catch {
        setError("Unable to load this booking's application form.");
      } finally {
        setLoading(false);
      }
    };
    load();
  }, [isAdmin, bookingId, state?.data]);

  if (!isAdmin) {
    return <div className="py-16 text-center text-[var(--text-muted)]">Admin access required.</div>;
  }

  if (loading) {
    return <div className="py-16 text-center text-[var(--text-muted)]">Loading application form...</div>;
  }

  return (
    <div className="min-h-screen bg-[var(--bg-secondary)] py-8">
      {/* Toolbar (hidden when printing) */}
      <div className="no-print mx-auto mb-6 flex max-w-[210mm] items-center justify-between px-4">
        <Button variant="ghost" size="sm" onClick={() => (fromCreate ? navigate("/confirmed-bookings") : navigate(-1))}>
          ← Back
        </Button>
        <div className="flex items-center gap-2">
          {!data && !error && (
            <span className="text-xs text-[var(--text-muted)]">Blank form — print and fill by hand</span>
          )}
          <Button size="sm" onClick={() => window.print()}>Print / Save as PDF</Button>
        </div>
      </div>

      {error && (
        <div className="no-print mx-auto mb-4 max-w-[210mm] px-4 text-sm text-rose-400">{error}</div>
      )}

      <ApplicationForm data={data ?? {}} />
    </div>
  );
}
