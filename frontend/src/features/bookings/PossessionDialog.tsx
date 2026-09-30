import { useId, useState, type FormEvent } from "react";
import { DatePicker, Modal, Notice, useToast } from "../../components/ui";
import { karachiDateInput, toKarachiInputs } from "../../lib/dates.ts";
import { useIdempotencyKeys } from "../../lib/idempotency.ts";
import { formatPkr } from "../../utils/currency.ts";
import { bookingApi } from "./bookingApi.ts";
import type { BookingDetail } from "./detailTypes.ts";

type Props = {
  booking: Pick<BookingDetail, "id" | "unitNumber" | "outstanding" | "bookingDate">;
  onClose: () => void;
  /** Reloads the booking once the server has saved, before the popup closes. */
  onSaved: () => Promise<void> | void;
};

/** "Give possession of Unit B08?" — a confirm box that asks for the date, today or earlier and never before the booking. */
export function PossessionDialog({ booking, onClose, onSaved }: Props) {
  const toast = useToast();
  const keys = useIdempotencyKeys();
  const formId = useId();
  const today = karachiDateInput(0);
  const [date, setDate] = useState(today);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (saving || !date) return;
    setSaving(true);
    setError(null);
    const signature = `possession:${booking.id}:${date}`;
    try {
      await bookingApi.givePossession(booking.id, date, keys.key(signature, "possession"));
      keys.release(signature);
      toast.success(`Possession of Unit ${booking.unitNumber} given.`);
      await onSaved();
      onClose();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "Possession could not be given.");
      setSaving(false);
    }
  };

  return (
    <Modal
      open
      onClose={onClose}
      busy={saving}
      size="sm"
      title={`Give possession of Unit ${booking.unitNumber}?`}
      primaryAction={{ label: "Give possession", form: formId, loading: saving, disabled: !date }}
    >
      <form id={formId} onSubmit={(event) => void submit(event)} className="flex flex-col gap-4">
        {error && <Notice tone="red" role="alert" title={error} />}
        {booking.outstanding > 0 && <p className="m-0">{formatPkr(booking.outstanding)} is still to be paid on the plan.</p>}
        <DatePicker
          label="Possession date"
          required
          min={toKarachiInputs(booking.bookingDate).date}
          max={today}
          value={date}
          onChange={setDate}
        />
      </form>
    </Modal>
  );
}
