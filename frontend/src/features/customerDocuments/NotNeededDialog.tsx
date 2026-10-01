import { useState } from "react";
import { ConfirmDialog, TextArea, useToast } from "../../components/ui";
import { documentJson, jsonBody } from "./documentApi.ts";
import type { DocumentRequirement } from "./types.ts";

type Props = {
  customerId: number;
  requirement: DocumentRequirement;
  onClose: () => void;
  /** Reloads the tab once the server has saved, before the popup closes. */
  onSaved: () => Promise<void> | void;
};

/** "Customer photo not needed?" — the shared confirm box with a required Reason. */
export function NotNeededDialog({ customerId, requirement, onClose, onSaved }: Props) {
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
      await documentJson(`/api/customer-documents/customers/${customerId}/requirements/${requirement.id}/not-needed`,
        jsonBody("POST", { reason: reason.trim(), concurrencyToken: requirement.concurrencyToken }));
      toast.success("Marked not needed");
      await onSaved();
      onClose();
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "The document could not be marked not needed.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <ConfirmDialog
      open
      onClose={onClose}
      onConfirm={() => void confirm()}
      title={`${requirement.name} not needed?`}
      message="It moves to Done and stops counting as missing."
      confirmLabel="Mark not needed"
      loading={saving}
    >
      <TextArea
        label="Reason"
        required
        rows={3}
        maxLength={500}
        disabled={saving}
        error={error ?? (shown && empty ? "Enter why this document is not needed." : undefined)}
        value={reason}
        onChange={(event) => setReason(event.target.value)}
      />
    </ConfirmDialog>
  );
}
