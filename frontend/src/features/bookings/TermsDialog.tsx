import { useId, useState, type FormEvent } from "react";
import { ChoiceChips, DatePicker, Modal, Notice, NumberField, TextField, useToast } from "../../components/ui";
import { karachiDateInput } from "../../lib/dates.ts";
import { useIdempotencyKeys } from "../../lib/idempotency.ts";
import { formatPkr } from "../../utils/currency.ts";
import { bookingApi } from "./bookingApi.ts";
import type { BookingDetail } from "./detailTypes.ts";
import { DialogTitle } from "./DialogTitle.tsx";
import { BOOKING_CHIPS, CUSTOM_CHIP, canSaveTerms, chipFor, termsErrors, termsFigures } from "./termsForm.ts";

type Props = {
  booking: BookingDetail;
  onClose: () => void;
  /** Reloads the booking once the server has saved, before the popup closes. */
  onSaved: () => Promise<void> | void;
};

/**
 * Set terms / Edit terms. Edit is the same popup once some money is already received, and then the
 * booking amount cannot go below it. The figures come from `termsForm`, which mirrors the server's
 * rules, so what is refused here is what the server would refuse.
 */
export function TermsDialog({ booking, onClose, onSaved }: Props) {
  const toast = useToast();
  const keys = useIdempotencyKeys();
  const formId = useId();
  const received = booking.bookingAmountReceived;
  const editing = received > 0;
  const today = karachiDateInput(0);

  const agreed = booking.agreedSalePrice > 0 ? booking.agreedSalePrice : booking.listPrice;
  const [fields, setFields] = useState({
    agreedSalePrice: String(agreed),
    discountPercent: String(booking.discountPercent ?? 0),
    chip: booking.bookingAmountRequired > 0 ? chipFor(booking.bookingAmountRequired, agreed) : "10",
    customAmount: booking.bookingAmountRequired > 0 ? String(booking.bookingAmountRequired) : "",
  });
  const [reason, setReason] = useState(booking.discountReason ?? "");
  // A due date already gone cannot be promised again, so it starts empty rather than pre-filled invalid.
  const savedDue = booking.bookingAmountDueDate?.slice(0, 10) ?? "";
  const [dueBy, setDueBy] = useState(savedDue >= today ? savedDue : "");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const figures = termsFigures(fields);
  const errors = termsErrors(figures, received);
  const canSave = canSaveTerms(figures, errors);
  const locked = fields.chip !== CUSTOM_CHIP;
  const set = (change: Partial<typeof fields>) => setFields((current) => ({ ...current, ...change }));

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (!canSave || saving) return;
    setSaving(true);
    setError(null);
    const body = {
      agreedSalePrice: figures.agreed,
      discountPercent: figures.discountPercent,
      // Blank clears it: the reason field is only on screen while there is a discount.
      discountReason: figures.discountPercent > 0 ? reason.trim() : "",
      bookingAmountRequired: figures.bookingAmount,
      bookingAmountDueDate: dueBy || null,
    };
    const signature = `terms:${booking.id}:${JSON.stringify(body)}`;
    try {
      const saved = await bookingApi.saveTerms(booking.id, body, keys.key(signature, "terms"));
      keys.release(signature);
      toast.success(saved.status === "PaymentPlanActive"
        ? "Terms saved. The booking amount is already covered, so the booking is now on a payment plan."
        : "Terms saved.");
      await onSaved();
      onClose();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "The terms could not be saved.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      open
      onClose={onClose}
      busy={saving}
      size="lg"
      phoneLayout="fullscreen"
      title={<DialogTitle
        title={editing ? "Edit terms" : "Set terms"}
        subtitle={editing ? `${booking.bookingReference} · ${formatPkr(received)} already received` : `${booking.bookingReference} · Unit ${booking.unitNumber}`}
      />}
      primaryAction={{ label: "Save terms", form: formId, loading: saving, disabled: !canSave }}
    >
      <form id={formId} onSubmit={(event) => void submit(event)} className="flex flex-col gap-4">
        {error && <Notice tone="red" role="alert" title={error} />}

        <div className="grid gap-4 md:grid-cols-5">
          <NumberField
            className="md:col-span-3"
            label="Agreed sale price"
            required
            prefix="Rs"
            helper={`List price ${formatPkr(booking.listPrice)}`}
            value={fields.agreedSalePrice}
            onChange={(value) => set({ agreedSalePrice: value })}
          />
          <NumberField
            className="md:col-span-2"
            label="Discount %"
            error={errors.discount}
            value={fields.discountPercent}
            onChange={(value) => set({ discountPercent: value })}
          />
        </div>

        {figures.discountPercent > 0 && (
          <TextField
            label="Discount reason"
            helper={`Discount ${formatPkr(figures.discount)}`}
            maxLength={500}
            value={reason}
            onChange={(event) => setReason(event.target.value)}
          />
        )}

        <div className="flex flex-col gap-3">
          <ChoiceChips
            label="Booking amount"
            required
            options={BOOKING_CHIPS}
            value={fields.chip}
            onChange={(chip) => set({
              chip,
              // Custom starts from the amount the chip had worked out, so nothing typed is lost.
              customAmount: chip === CUSTOM_CHIP && figures.bookingAmount > 0 ? String(figures.bookingAmount) : fields.customAmount,
            })}
          />
          <NumberField
            aria-label="Booking amount in rupees"
            prefix="Rs"
            disabled={locked}
            error={errors.amount}
            value={locked ? String(figures.bookingAmount) : fields.customAmount}
            onChange={(value) => set({ customAmount: value })}
          />
        </div>

        <DatePicker label="Due by" min={today} value={dueBy} onChange={setDueBy} />

        {/* Hidden while anything is in red: a summary of an amount the server would refuse is noise. */}
        {!errors.amount && !errors.discount && figures.agreed > 0 && (
          <dl className="m-0 flex flex-wrap gap-x-8 gap-y-2 rounded-card border border-gold-line bg-gold-soft px-4 py-3.5">
            {[
              ["Net price", figures.net],
              ["Booking amount", figures.bookingAmount],
              ["Left for installments", figures.leftForInstallments],
            ].map(([label, amount]) => (
              <div key={label as string}>
                <dt className="text-caption font-bold uppercase tracking-[0.4px] text-gold-text">{label}</dt>
                <dd className="m-0 mt-0.5 text-[17px] font-extrabold text-primary">{formatPkr(amount as number)}</dd>
              </div>
            ))}
          </dl>
        )}
      </form>
    </Modal>
  );
}
