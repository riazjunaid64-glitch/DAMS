import { useEffect, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { api } from "../api/api.ts";
import type { User } from "../App.tsx";
import Button from "../lib/Button.tsx";
import Container from "../lib/Container.tsx";

type Props = { user: User | null };

interface CustomerDetail {
  id: number;
  fullName: string;
  phone: string;
  cnic?: string | null;
  email?: string | null;
  address?: string | null;
  source: string;
  status: string;
  notes?: string | null;
  bookingsCount: number;
  createdAt: string;
}

interface BookingSummary {
  id: number;
  bookingReference: string;
  status: string;
  projectName: string;
  unitNumber: string;
  agreedSalePrice: number;
}

export default function CustomerDetailPage({ user }: Props) {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const customerId = Number(id);
  const [customer, setCustomer] = useState<CustomerDetail | null>(null);
  const [bookings, setBookings] = useState<BookingSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const isAdmin = user?.role === "Admin";

  useEffect(() => {
    if (!isAdmin || !customerId) return;
    const load = async () => {
      setLoading(true);
      setError(null);
      try {
        const [custRes, bookRes] = await Promise.all([
          api(`/api/Customer/${customerId}`),
          api(`/api/Booking/customer/${customerId}`),
        ]);
        if (!custRes.ok) throw new Error("Customer not found");
        setCustomer(await custRes.json());
        if (bookRes.ok) {
          const data = await bookRes.json();
          setBookings(data.items ?? []);
        }
      } catch {
        setError("Unable to load customer.");
      } finally {
        setLoading(false);
      }
    };
    load();
  }, [isAdmin, customerId]);

  if (!isAdmin) {
    return <Container className="py-16 text-center"><p className="text-[var(--text-muted)]">Admin access required.</p></Container>;
  }

  if (loading) {
    return <Container className="py-16 text-center"><p className="text-[var(--text-muted)]">Loading...</p></Container>;
  }

  if (error || !customer) {
    return (
      <Container className="py-16 text-center">
        <p className="text-rose-400">{error ?? "Customer not found."}</p>
        <Button className="mt-4" variant="outline" onClick={() => navigate("/customers")}>Back to Customers</Button>
      </Container>
    );
  }

  return (
    <Container className="py-10">
      <Button variant="ghost" size="sm" className="mb-6" onClick={() => navigate("/customers")}>← Customers</Button>

      <div className="mb-8">
        <h1 className="text-2xl font-bold text-[var(--text-heading)]">{customer.fullName}</h1>
        <p className="mt-1 text-sm text-[var(--text-muted)]">{customer.phone} · {customer.source}</p>
      </div>

      <div className="grid gap-6 lg:grid-cols-2">
        <div className="rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] p-6">
          <h2 className="mb-4 text-sm font-semibold uppercase tracking-wide text-[var(--text-muted)]">Contact</h2>
          <dl className="space-y-3 text-sm">
            <div><dt className="text-[var(--text-muted)]">Email</dt><dd className="text-[var(--text-primary)]">{customer.email ?? "—"}</dd></div>
            <div><dt className="text-[var(--text-muted)]">CNIC</dt><dd className="text-[var(--text-primary)]">{customer.cnic ?? "—"}</dd></div>
            <div><dt className="text-[var(--text-muted)]">Address</dt><dd className="text-[var(--text-primary)]">{customer.address ?? "—"}</dd></div>
            <div><dt className="text-[var(--text-muted)]">Notes</dt><dd className="text-[var(--text-primary)]">{customer.notes ?? "—"}</dd></div>
          </dl>
        </div>

        <div className="rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] p-6">
          <h2 className="mb-4 text-sm font-semibold uppercase tracking-wide text-[var(--text-muted)]">Bookings ({bookings.length})</h2>
          {bookings.length === 0 ? (
            <p className="text-sm text-[var(--text-muted)]">No confirmed bookings yet.</p>
          ) : (
            <ul className="space-y-3">
              {bookings.map((b) => (
                <li key={b.id} className="flex items-center justify-between rounded-xl border border-[var(--border)] px-4 py-3">
                  <div>
                    <p className="font-medium text-[var(--text-heading)]">{b.bookingReference}</p>
                    <p className="text-xs text-[var(--text-muted)]">{b.projectName} · Unit {b.unitNumber}</p>
                  </div>
                  <Link to={`/confirmed-bookings/${b.id}`} className="text-sm text-indigo-400 hover:underline">Open</Link>
                </li>
              ))}
            </ul>
          )}
        </div>
      </div>
    </Container>
  );
}
