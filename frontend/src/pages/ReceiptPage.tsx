import { useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { api } from "../api/api.ts";
import type { User } from "../App.tsx";
import PaymentReceipt, { type PaymentReceiptData } from "../components/PaymentReceipt.tsx";
import { Button, Notice } from "../components/ui";
import { can } from "../features/access/permissions.ts";
import { PaperPage } from "../features/printing/PaperPage.tsx";
import { receiptSubtitle } from "../features/printing/paper.ts";
import { usePageTrail } from "../layouts/trail.ts";

type Props = { user: User | null };

/**
 * One payment receipt, inside the app. Bookings users read it from the booking; a customer reads only
 * their own from the portal (the server checks that the purchase is theirs).
 */
export default function ReceiptPage({ user }: Props) {
  const { bookingId, paymentId } = useParams<{ bookingId: string; paymentId: string }>();
  const navigate = useNavigate();
  const [receipt, setReceipt] = useState<PaymentReceiptData | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const isStaff = can(user?.role, "bookings");
  const bookingPath = `/confirmed-bookings/${bookingId}`;
  const back = () => (isStaff ? navigate(bookingPath) : navigate(-1));

  usePageTrail(
    isStaff && receipt
      ? [{ label: receipt.bookingReference, to: bookingPath }, { label: `Receipt ${receipt.receiptNumber ?? `#${receipt.paymentId}`}` }]
      : [],
  );

  useEffect(() => {
    if (!user || !bookingId || !paymentId) return;
    const load = async () => {
      setLoading(true);
      setError(null);
      try {
        const url = isStaff
          ? `/api/Booking/${bookingId}/payments/${paymentId}/receipt`
          : `/api/MyProjects/${bookingId}/payments/${paymentId}/receipt`;
        const res = await api(url);
        if (!res.ok) throw new Error("Receipt not found");
        setReceipt(await res.json());
      } catch {
        setReceipt(null);
        setError("Unable to load receipt.");
      } finally {
        setLoading(false);
      }
    };
    void load();
  }, [user, isStaff, bookingId, paymentId]);

  if (!user) {
    return <div className="py-16 text-center text-[var(--text-muted)]">Please log in to view this receipt.</div>;
  }

  if (loading) {
    return <div className="py-16 text-center text-[var(--text-muted)]">Loading receipt...</div>;
  }

  if (error || !receipt) {
    return (
      <div className="mx-auto flex w-full max-w-[640px] flex-col items-start gap-4 px-4 py-10 font-ui">
        <Notice tone="red" role="alert" title={error ?? "Receipt not found."} />
        <Button variant="outline" onClick={back}>Back</Button>
      </div>
    );
  }

  return (
    <PaperPage
      title={`Receipt ${receipt.receiptNumber ?? `#${receipt.paymentId}`}`}
      subtitle={receiptSubtitle(receipt)}
      onBack={back}
      backLabel={isStaff ? "Back to booking" : "Back"}
    >
      <PaymentReceipt data={receipt} preparedByName={user.firstName} />
    </PaperPage>
  );
}
