import { useState } from "react";
import { ConfirmDialog, TextArea, useToast } from "../../components/ui";
import { api } from "../../api/api.ts";

type Props = {
  customer: { id: number; fullName: string };
  onClose: () => void;
  /** Reloads the page once the server has saved, before the popup closes. */
  onSaved: () => Promise<void> | void;
};

/** "Block Usman Tariq?" — the shared confirm box with a required Reason field. */
export function BlockCustomerDialog({ customer, onClose, onSaved }: Props) {
  const toast = useToast();
  const [reason, setReason] = useState("");
  const [shown, setShown] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const empty = reason.trim() === "";

  const confirm = async () => {
    if (empty) {
      setShown(true);
      return;
    }
    setSaving(true);
    setError(null);
    try {
      const response = await api(`/api/Customer/${customer.id}/block`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ reason: reason.trim() }),
      });
      if (!response.ok) {
        const body = (await response.json().catch(() => ({}))) as { message?: string };
        setError(body.message ?? "The customer could not be blocked.");
        return;
      }
      toast.success("Customer blocked");
      await onSaved();
      onClose();
    } catch {
      setError("The customer could not be blocked.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <ConfirmDialog
      open
      onClose={onClose}
      onConfirm={() => void confirm()}
      title={`Block ${customer.fullName}?`}
      message="New bookings for this customer will be stopped. Existing bookings stay as they are."
      confirmLabel="Block customer"
      danger
      loading={saving}
    >
      <TextArea
        label="Reason"
        required
        rows={3}
        maxLength={500}
        disabled={saving}
        error={error ?? (shown && empty ? "Enter the reason for blocking this customer." : undefined)}
        value={reason}
        onChange={(event) => setReason(event.target.value)}
      />
    </ConfirmDialog>
  );
}
