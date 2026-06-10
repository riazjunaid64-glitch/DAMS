import { useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { api } from "../api/api.ts";
import type { User } from "../App.tsx";
import Button from "../lib/Button.tsx";
import PaymentReceipt, { type PaymentReceiptData } from "../components/PaymentReceipt.tsx";

type Props = { user: User | null };

export default function ReceiptPage({ user }: Props) {
  const { bookingId, paymentId } = useParams<{ bookingId: string; paymentId: string }>();
  const navigate = useNavigate();
  const [receipt, setReceipt] = useState<PaymentReceiptData | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const isAdmin = user?.role === "Admin";

  useEffect(() => {
    if (!user || !bookingId || !paymentId) return;
    const load = async () => {
      setLoading(true);
      setError(null);
      try {
        const url = isAdmin
          ? `/api/Booking/${bookingId}/payments/${paymentId}/receipt`
          : `/api/MyProjects/${bookingId}/payments/${paymentId}/receipt`;
        const res = await api(url);
        if (!res.ok) throw new Error("Receipt not found");
        setReceipt(await res.json());
      } catch {
        setError("Unable to load receipt.");
      } finally {
        setLoading(false);
      }
    };
    load();
  }, [user, isAdmin, bookingId, paymentId]);

  if (!user) {
    return <div className="py-16 text-center text-[var(--text-muted)]">Please log in to view this receipt.</div>;
  }

  if (loading) {
    return <div className="py-16 text-center text-[var(--text-muted)]">Loading receipt...</div>;
  }

  if (error || !receipt) {
    return (
      <div className="py-16 text-center">
        <p className="text-rose-400">{error ?? "Receipt not found."}</p>
        <Button className="mt-4" variant="outline" onClick={() => navigate(-1)}>Back</Button>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-[var(--bg-secondary)] py-8">
      {/* Toolbar (hidden when printing) */}
      <div className="no-print mx-auto mb-6 flex max-w-[210mm] items-center justify-between px-4">
        <Button variant="ghost" size="sm" onClick={() => navigate(-1)}>← Back</Button>
        <div className="flex gap-2">
          <Button size="sm" onClick={() => window.print()}>Print / Save as PDF</Button>
        </div>
      </div>

      <PaymentReceipt data={receipt} />
    </div>
  );
}
