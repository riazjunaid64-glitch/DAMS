import { useState } from "react";
import { Modal, Notice, useToast } from "../../components/ui";
import { useIdempotencyKeys } from "../../lib/idempotency.ts";
import { bookingApi } from "./bookingApi.ts";
import type { BookingDetail } from "./detailTypes.ts";

type Props = {
  booking: Pick<BookingDetail, "id" | "unitNumber">;
  onClose: () => void;
  /** Reloads the booking once the server has saved, before the popup closes. */
  onSaved: () => Promise<void> | void;
};

/** "Complete the sale?" — the confirm the header's Complete sale button opens once nothing is owed. */
export function CompleteSaleDialog({ booking, onClose, onSaved }: Props) {
  const toast = useToast();
  const keys = useIdempotencyKeys();
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const confirm = async () => {
    if (saving) return;
    setSaving(true);
    setError(null);
    const signature = `complete:${booking.id}`;
    try {
      await bookingApi.completeSale(booking.id, keys.key(signature, "complete"));
      keys.release(signature);
      toast.success(`Sale completed. Unit ${booking.unitNumber} is now Sold.`);
      await onSaved();
      onClose();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "The sale could not be completed.");
      setSaving(false);
    }
  };

  return (
    <Modal
      open
      onClose={onClose}
      busy={saving}
      size="sm"
      title="Complete the sale?"
      primaryAction={{ label: "Complete sale", variant: "success", loading: saving, onClick: () => void confirm() }}
    >
      <div className="flex flex-col gap-3">
        {error && <Notice tone="red" role="alert" title={error} />}
        <p className="m-0">Unit {booking.unitNumber} becomes Sold.</p>
      </div>
    </Modal>
  );
}
