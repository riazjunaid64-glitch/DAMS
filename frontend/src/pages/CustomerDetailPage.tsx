import { useEffect, useState, type FormEvent } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { api } from "../api/api.ts";
import type { User } from "../App.tsx";
import Button from "../lib/Button.tsx";
import Container from "../lib/Container.tsx";
import Field from "../lib/Field.tsx";
import TabLayout from "../lib/TabLayout.tsx";
import CustomerDocumentsPanel from "../features/customerDocuments/CustomerDocumentsPanel.tsx";
import CustomerDocumentHistory from "../features/customerDocuments/CustomerDocumentHistory.tsx";
import DocumentSummaryBadge from "../features/customerDocuments/DocumentSummaryBadge.tsx";
import type { DocumentChecklist, DocumentSummary } from "../features/customerDocuments/types.ts";

type Props = { user: User | null };

interface CustomerDetail {
  id: number;
  fullName: string;
  fatherName?: string | null;
  phone: string;
  cnic?: string | null;
  email?: string | null;
  address?: string | null;
  source: string;
  status: string;
  notes?: string | null;
  bookingsCount: number;
  createdAt: string;
  documentSummary: DocumentSummary;
}

const STATUS_OPTIONS = ["Active", "Inactive", "Blocked"];

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
  const [activeTab, setActiveTab] = useState("overview");
  const [documents, setDocuments] = useState<DocumentChecklist | null>(null);
  const [documentsLoading, setDocumentsLoading] = useState(true);
  const [documentsError, setDocumentsError] = useState<string | null>(null);

  const [editing, setEditing] = useState(false);
  const [saving, setSaving] = useState(false);
  const [editError, setEditError] = useState<string | null>(null);
  const [editForm, setEditForm] = useState({
    fullName: "",
    fatherName: "",
    phone: "",
    cnic: "",
    email: "",
    address: "",
    status: "Active",
    notes: "",
  });

  const isAdmin = user?.role === "Admin";

  const loadDocuments = async () => {
    setDocumentsLoading(true); setDocumentsError(null);
    try {
      const response = await api(`/api/customer-documents/customers/${customerId}`);
      if (!response.ok) {
        const body = await response.json().catch(() => ({})) as { message?: string };
        throw new Error(body.message ?? "Unable to load customer documents.");
      }
      setDocuments(await response.json());
    } catch (caught) {
      setDocumentsError(caught instanceof Error ? caught.message : "Unable to load customer documents.");
    } finally { setDocumentsLoading(false); }
  };

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
      await loadDocuments();
    } catch {
      setError("Unable to load customer.");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    if (!isAdmin || !customerId) return;
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isAdmin, customerId]);

  const openEdit = () => {
    if (!customer) return;
    setEditError(null);
    setEditForm({
      fullName: customer.fullName ?? "",
      fatherName: customer.fatherName ?? "",
      phone: customer.phone ?? "",
      cnic: customer.cnic ?? "",
      email: customer.email ?? "",
      address: customer.address ?? "",
      status: customer.status ?? "Active",
      notes: customer.notes ?? "",
    });
    setEditing(true);
  };

  const handleSave = async (e: FormEvent) => {
    e.preventDefault();
    setSaving(true);
    setEditError(null);
    try {
      const body = {
        fullName: editForm.fullName.trim(),
        fatherName: editForm.fatherName.trim() || null,
        phone: editForm.phone.trim(),
        cnic: editForm.cnic.trim() || null,
        email: editForm.email.trim() || null,
        address: editForm.address.trim() || null,
        status: editForm.status,
        notes: editForm.notes.trim() || null,
      };
      const res = await api(`/api/Customer/${customerId}`, {
        method: "PUT",
        body: JSON.stringify(body),
      });
      const data = await res.json();
      if (!res.ok) throw new Error(data.message || "Failed to update customer");
      setEditing(false);
      await load();
    } catch (err) {
      setEditError(err instanceof Error ? err.message : "Failed to update customer.");
    } finally {
      setSaving(false);
    }
  };

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

      <div className="mb-8 flex items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold text-[var(--text-heading)]">{customer.fullName}</h1>
          <p className="mt-1 text-sm text-[var(--text-muted)]">{customer.phone} · {customer.source}</p>
          <div className="mt-3"><DocumentSummaryBadge summary={documents?.summary ?? customer.documentSummary} /></div>
        </div>
        <Button size="sm" variant="outline" onClick={openEdit}>Edit Details</Button>
      </div>

      <TabLayout tabs={[
        { id: "overview", label: "Overview" },
        { id: "documents", label: "Documents", badge: documents?.summary.missing || documents?.summary.awaitingReview || undefined },
        { id: "bookings", label: "Bookings", badge: bookings.length || undefined },
        { id: "history", label: "History", badge: documents?.history.length || undefined },
      ]} activeTab={activeTab} onTabChange={setActiveTab} ariaLabel="Customer sections">
      {activeTab === "overview" && <div className="grid gap-6 lg:grid-cols-2">
        <div className="rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] p-6">
          <h2 className="mb-4 text-sm font-semibold uppercase tracking-wide text-[var(--text-muted)]">Contact</h2>
          <dl className="space-y-3 text-sm">
            <div><dt className="text-[var(--text-muted)]">Father / Husband Name</dt><dd className="text-[var(--text-primary)]">{customer.fatherName ?? "—"}</dd></div>
            <div><dt className="text-[var(--text-muted)]">Email</dt><dd className="text-[var(--text-primary)]">{customer.email ?? "—"}</dd></div>
            <div><dt className="text-[var(--text-muted)]">CNIC</dt><dd className="text-[var(--text-primary)]">{customer.cnic ?? "—"}</dd></div>
            <div><dt className="text-[var(--text-muted)]">Address</dt><dd className="text-[var(--text-primary)]">{customer.address ?? "—"}</dd></div>
            <div><dt className="text-[var(--text-muted)]">Notes</dt><dd className="text-[var(--text-primary)]">{customer.notes ?? "—"}</dd></div>
          </dl>
        </div>
        <div className="rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] p-6"><h2 className="mb-4 text-sm font-semibold uppercase tracking-wide text-[var(--text-muted)]">Document readiness</h2><DocumentSummaryBadge summary={documents?.summary ?? customer.documentSummary} /><p className="mt-4 text-sm text-[var(--text-muted)]">Required documents are tracked as operational warnings. Customer, booking, payment, and installment work remains available.</p></div>
      </div>}

      {activeTab === "bookings" && <div className="rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] p-6">
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
        </div>}
      {activeTab === "documents" && <CustomerDocumentsPanel customerId={customerId} checklist={documents} loading={documentsLoading} error={documentsError} onRefresh={loadDocuments} />}
      {activeTab === "history" && (documentsLoading && !documents ? <p className="py-12 text-center text-sm text-[var(--text-muted)]">Loading history…</p> : documentsError && !documents ? <p className="py-12 text-center text-sm text-rose-300">{documentsError}</p> : <CustomerDocumentHistory history={documents?.history ?? []} />)}
      </TabLayout>

      {editing && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4" onClick={() => !saving && setEditing(false)}>
          <div className="w-full max-w-lg rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-6 shadow-xl" onClick={(e) => e.stopPropagation()}>
            <h3 className="text-lg font-semibold text-[var(--text-heading)]">Edit Customer</h3>
            <p className="mt-1 text-sm text-[var(--text-muted)]">Father / Husband name appears on official payment receipts.</p>

            {editError && <div className="mt-4 rounded-xl border border-rose-500/20 bg-rose-500/10 px-4 py-3 text-sm text-rose-400">{editError}</div>}

            <form onSubmit={handleSave} className="mt-4 grid gap-4 sm:grid-cols-2">
              <Field label="Full Name" required value={editForm.fullName}
                onChange={(e) => setEditForm({ ...editForm, fullName: e.target.value })} />
              <Field label="Father / Husband Name" value={editForm.fatherName}
                onChange={(e) => setEditForm({ ...editForm, fatherName: e.target.value })} />
              <Field label="Phone" required value={editForm.phone}
                onChange={(e) => setEditForm({ ...editForm, phone: e.target.value })} />
              <Field label="CNIC" value={editForm.cnic}
                onChange={(e) => setEditForm({ ...editForm, cnic: e.target.value })} />
              <Field label="Email" type="email" value={editForm.email}
                onChange={(e) => setEditForm({ ...editForm, email: e.target.value })} />
              <label className="flex flex-col gap-1.5 text-sm font-medium text-[var(--text-secondary)]">
                <span>Status</span>
                <select value={editForm.status} onChange={(e) => setEditForm({ ...editForm, status: e.target.value })}
                  className="w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-4 py-3 text-sm text-[var(--text-primary)]">
                  {STATUS_OPTIONS.map((s) => <option key={s} value={s}>{s}</option>)}
                </select>
              </label>
              <div className="sm:col-span-2">
                <Field label="Address" value={editForm.address}
                  onChange={(e) => setEditForm({ ...editForm, address: e.target.value })} />
              </div>
              <div className="sm:col-span-2">
                <Field label="Notes" value={editForm.notes}
                  onChange={(e) => setEditForm({ ...editForm, notes: e.target.value })} />
              </div>

              <div className="sm:col-span-2 flex gap-2">
                <Button type="submit" disabled={saving}>{saving ? "Saving..." : "Save Changes"}</Button>
                <Button type="button" variant="ghost" onClick={() => setEditing(false)} disabled={saving}>Cancel</Button>
              </div>
            </form>
          </div>
        </div>
      )}
    </Container>
  );
}
