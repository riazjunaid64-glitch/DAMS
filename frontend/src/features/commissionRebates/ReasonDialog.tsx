import { useId, useState, type FormEvent, type ReactNode } from "react";
import { Modal, Notice, TextArea } from "../../components/ui";
import { useIdempotencyKeys } from "../../lib/idempotency.ts";

type Props = {
  title: string;
  message: ReactNode;
  /** The red button: "Reverse payment", "Cancel commission". */
  confirmLabel: string;
  /** The outline button that backs out: "Cancel", "Keep commission". */
  cancelLabel?: string;
  /** Prefix of the retry key handed to `onConfirm`. */
  keyPrefix: string;
  /** Does the work. Throws with the server's message when it is refused, and the popup stays open. */
  onConfirm: (reason: string, key: string) => Promise<void>;
  onClose: () => void;
};

/**
 * A small confirm box that asks for a reason: reversing a payment, cancelling a commission or a
 * rebate. The same reason keeps the same retry key, so pressing the button again after a dropped
 * connection is recognised as the retry it is rather than doing the work twice.
 */
export function ReasonDialog({ title, message, confirmLabel, cancelLabel = "Cancel", keyPrefix, onConfirm, onClose }: Props) {
  const keys = useIdempotencyKeys();
  const formId = useId();
  const [reason, setReason] = useState("");
  const [shown, setShown] = useState<string | undefined>();
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (saving) return;
    const text = reason.trim();
    if (!text) {
      setShown("Enter the reason.");
      return;
    }
    setSaving(true);
    setError(null);
    const signature = `${keyPrefix}:${text}`;
    try {
      await onConfirm(text, keys.key(signature, keyPrefix));
      keys.release(signature);
      onClose();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "That could not be done.");
      setSaving(false);
    }
  };

  return (
    <Modal
      open
      onClose={onClose}
      busy={saving}
      size="sm"
      title={title}
      cancelLabel={cancelLabel}
      primaryAction={{ label: confirmLabel, variant: "danger", form: formId, loading: saving }}
    >
      <form id={formId} noValidate onSubmit={(event) => void submit(event)} className="flex flex-col gap-4">
        {error && <Notice tone="red" role="alert" title={error} />}
        <p className="m-0">{message}</p>
        <TextArea
          label="Reason"
          required
          rows={3}
          maxLength={2000}
          disabled={saving}
          error={shown}
          value={reason}
          onChange={(event) => { setReason(event.target.value); setShown(undefined); }}
        />
      </form>
    </Modal>
  );
}
