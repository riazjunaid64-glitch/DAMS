import { useCallback, useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { api } from "../api/api.ts";
import type { User } from "../App.tsx";
import { ConfirmDialog, EmptyState, Notice, Tabs, Button as UiButton, IconUsers, useToast } from "../components/ui";
import { can } from "../features/access/permissions.ts";
import CustomerDocumentsPanel from "../features/customerDocuments/CustomerDocumentsPanel.tsx";
import type { DocumentChecklist } from "../features/customerDocuments/types.ts";
import { BlockCustomerDialog } from "../features/customers/BlockCustomerDialog.tsx";
import { CustomerBookings } from "../features/customers/CustomerBookings.tsx";
import type { CustomerBooking } from "../features/customers/customerBookings.ts";
import { CustomerHeader } from "../features/customers/CustomerHeader.tsx";
import { CustomerOverview } from "../features/customers/CustomerOverview.tsx";
import { EditCustomerDialog } from "../features/customers/EditCustomerDialog.tsx";
import type { CustomerDetail } from "../features/customers/customerForm.ts";

type Props = { user: User | null };

type Dialog = "edit" | "block" | "unblock" | null;

export default function CustomerDetailPage({ user }: Props) {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const toast = useToast();
  const customerId = Number(id);
  const allowed = can(user?.role, "customers");

  const [customer, setCustomer] = useState<CustomerDetail | null>(null);
  const [bookings, setBookings] = useState<CustomerBooking[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState("overview");
  const [documents, setDocuments] = useState<DocumentChecklist | null>(null);
  const [documentsLoading, setDocumentsLoading] = useState(true);
  const [documentsError, setDocumentsError] = useState<string | null>(null);
  const [dialog, setDialog] = useState<Dialog>(null);
  const [unblocking, setUnblocking] = useState(false);
  const [unblockError, setUnblockError] = useState<string | null>(null);

  const loadDocuments = useCallback(async () => {
    setDocumentsLoading(true);
    setDocumentsError(null);
    try {
      const response = await api(`/api/customer-documents/customers/${customerId}`);
      if (!response.ok) {
        const body = await response.json().catch(() => ({})) as { message?: string };
        throw new Error(body.message ?? "Unable to load customer documents.");
      }
      setDocuments(await response.json());
    } catch (caught) {
      setDocumentsError(caught instanceof Error ? caught.message : "Unable to load customer documents.");
    } finally {
      setDocumentsLoading(false);
    }
  }, [customerId]);

  // Reads the customer and their bookings. The first load blanks the page; a reload after a save keeps
  // it up and swaps the figures, so a popup that is closing never sits over a "Loading..." screen.
  const load = useCallback(async (first = false) => {
    if (first) setLoading(true);
    setError(null);
    try {
      const [customerResponse, bookingsResponse] = await Promise.all([
        api(`/api/Customer/${customerId}`),
        api(`/api/Booking/customer/${customerId}`),
      ]);
      if (!customerResponse.ok) throw new Error("Customer not found");
      setCustomer(await customerResponse.json());
      if (bookingsResponse.ok) setBookings(((await bookingsResponse.json()) as { items?: CustomerBooking[] }).items ?? []);
      await loadDocuments();
    } catch {
      setError("Unable to load customer.");
    } finally {
      setLoading(false);
    }
  }, [customerId, loadDocuments]);

  useEffect(() => {
    if (allowed && customerId) void load(true);
  }, [allowed, customerId, load]);

  const unblock = async () => {
    setUnblocking(true);
    setUnblockError(null);
    try {
      const response = await api(`/api/Customer/${customerId}/unblock`, { method: "POST" });
      if (!response.ok) {
        const body = await response.json().catch(() => ({})) as { message?: string };
        setUnblockError(body.message ?? "The customer could not be unblocked.");
        return;
      }
      toast.success("Customer unblocked");
      await load();
      setDialog(null);
    } catch {
      setUnblockError("The customer could not be unblocked.");
    } finally {
      setUnblocking(false);
    }
  };

  if (!allowed) {
    return (
      <div className="mx-auto w-full max-w-[1500px] px-4 py-5 md:px-8 md:py-7">
        <EmptyState icon={<IconUsers size={26} />} title={user ? "You don't have access to Customers." : "Sign in required"} />
      </div>
    );
  }
  if (loading) return <p className="py-16 text-center text-sm text-ink-muted">Loading...</p>;
  if (error && !customer) {
    return (
      <div className="mx-auto flex w-full max-w-[1500px] flex-col items-center gap-4 px-4 py-16 text-center">
        <p className="m-0 text-body font-bold text-danger">{error}</p>
        <UiButton variant="outline" onClick={() => navigate("/customers")}>Back to Customers</UiButton>
      </div>
    );
  }
  if (!customer) return null;

  return (
    <div className="mx-auto flex w-full max-w-[1500px] flex-col gap-4 px-4 py-5 md:gap-5 md:px-8 md:py-7">
      <CustomerHeader
        customer={customer}
        onBack={() => navigate("/customers")}
        onEdit={() => setDialog("edit")}
        onBlock={() => setDialog("block")}
        onUnblock={() => { setUnblockError(null); setDialog("unblock"); }}
        onNewBooking={() => navigate(`/confirmed-bookings/new?customerId=${customer.id}`)}
      />

      {error && <Notice tone="red" role="alert" title={error} />}

      <Tabs
        aria-label="Customer sections"
        phoneDropdownFrom={99}
        value={activeTab}
        onChange={setActiveTab}
        items={[
          { id: "overview", label: "Overview" },
          { id: "bookings", label: "Bookings", count: bookings.length > 0 ? bookings.length : undefined },
          { id: "documents", label: "Documents" },
        ]}
      />

      {activeTab === "overview" && <CustomerOverview customer={customer} />}

      {activeTab === "bookings" && <CustomerBookings bookings={bookings} />}

      {activeTab === "documents" && (
        <CustomerDocumentsPanel customerId={customerId} checklist={documents} loading={documentsLoading} error={documentsError} onRefresh={loadDocuments} />
      )}

      {dialog === "edit" && (
        <EditCustomerDialog customer={customer} onClose={() => setDialog(null)} onOpenCustomer={(other) => navigate(`/customers/${other}`)} onSaved={() => load()} />
      )}
      {dialog === "block" && <BlockCustomerDialog customer={customer} onClose={() => setDialog(null)} onSaved={() => load()} />}
      <ConfirmDialog
        open={dialog === "unblock"}
        onClose={() => setDialog(null)}
        onConfirm={() => void unblock()}
        title={`Unblock ${customer.fullName}?`}
        message="New bookings are allowed again."
        confirmLabel="Unblock"
        loading={unblocking}
      >
        {unblockError && <p role="alert" className="m-0 text-small font-bold text-danger">{unblockError}</p>}
      </ConfirmDialog>
    </div>
  );
}
