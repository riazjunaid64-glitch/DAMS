import { useId, useState, type FormEvent } from "react";
import { DatePicker, Dropdown, Modal, Notice, NumberField, useToast } from "../../components/ui";
import { useIdempotencyKeys } from "../../lib/idempotency.ts";
import { formatPkr } from "../../utils/currency.ts";
import { bookingApi } from "./bookingApi.ts";
import type { BookingDetail, InstallmentSchedule } from "./detailTypes.ts";
import { DialogTitle } from "./DialogTitle.tsx";
import { PLAN_FREQUENCIES, canSavePlan, planErrors, planFields, planFigures, type PlanErrors } from "./planForm.ts";

type Props = {
  booking: BookingDetail;
  /** The plan being replaced; null when this is the first plan. */
  schedule: InstallmentSchedule | null;
  onClose: () => void;
  /** Reloads the booking and its plan once the server has saved, before the popup closes. */
  onSaved: () => Promise<void> | void;
};

/**
 * Create installment plan / Change plan. Change is the same popup opened on the plan that is there,
 * and it replaces every installment. The summary strip comes from `planForm`, which mirrors how the
 * server builds the rows, so the amount shown here is the amount the schedule will hold.
 */
export function PlanDialog({ booking, schedule, onClose, onSaved }: Props) {
  const toast = useToast();
  const keys = useIdempotencyKeys();
  const formId = useId();
  const changing = schedule?.hasSchedule === true;
  // The sale is recognised at possession, and the server refuses a different price after that.
  const termsFixed = booking.status === "PossessionGiven";

  const [fields, setFields] = useState(() => planFields(booking, schedule));
  const [submitted, setSubmitted] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const set = (change: Partial<typeof fields>) => setFields((current) => ({ ...current, ...change }));

  const figures = planFigures(fields, booking.bookingAmountReceived, booking.rebateCredits);
  const problems = planErrors(fields, figures);
  const shown: PlanErrors = submitted ? problems : {};

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (saving) return;
    setSubmitted(true);
    if (!canSavePlan(problems)) return;
    setSaving(true);
    setError(null);
    const body = {
      agreedSalePrice: figures.agreed,
      discountPercent: figures.discountPercent,
      frequency: fields.frequency,
      numberOfInstallments: figures.installments,
      installmentStartDate: fields.firstDue,
      possessionAmount: figures.possession,
      possessionDueDate: figures.possession > 0 ? fields.possessionDue : null,
      regenerate: changing,
    };
    const signature = `plan:${booking.id}:${JSON.stringify(body)}`;
    try {
      await bookingApi.savePlan(booking.id, body, keys.key(signature, "plan"));
      keys.release(signature);
      toast.success(changing ? "Plan changed." : "Plan created.");
      await onSaved();
      onClose();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "The plan could not be saved.");
    } finally {
      setSaving(false);
    }
  };

  const showStrip = figures.agreed > 0 && figures.pool > 0;
  return (
    <Modal
      open
      onClose={onClose}
      busy={saving}
      size="lg"
      phoneLayout="fullscreen"
      title={<DialogTitle
        title={changing ? "Change plan" : "Create installment plan"}
        subtitle={changing ? `Replaces all ${schedule!.items.length} installments` : booking.bookingReference}
      />}
      primaryAction={{ label: changing ? "Change plan" : "Create plan", form: formId, loading: saving }}
    >
      <form id={formId} noValidate onSubmit={(event) => void submit(event)} className="flex flex-col gap-4">
        {error && <Notice tone="red" role="alert" title={error} />}

        <div className="grid gap-4 md:grid-cols-5">
          <NumberField
            className="md:col-span-3"
            label="Agreed sale price"
            required
            prefix="Rs"
            disabled={termsFixed}
            helper={termsFixed ? "Fixed at possession" : undefined}
            error={shown.agreed}
            value={fields.agreedSalePrice}
            onChange={(value) => set({ agreedSalePrice: value })}
          />
          <NumberField
            className="md:col-span-2"
            label="Discount %"
            disabled={termsFixed}
            error={shown.discount}
            value={fields.discountPercent}
            onChange={(value) => set({ discountPercent: value })}
          />
        </div>

        <div className="grid gap-4 md:grid-cols-2">
          <NumberField
            label="Installments"
            required
            decimals={0}
            error={shown.installments}
            value={fields.installments}
            onChange={(value) => set({ installments: value })}
          />
          <Dropdown
            label="Frequency"
            required
            options={PLAN_FREQUENCIES}
            value={fields.frequency}
            onChange={(frequency) => set({ frequency })}
          />
        </div>

        <DatePicker
          label="First due"
          required
          helper="The due date of installment 1"
          error={shown.firstDue}
          value={fields.firstDue}
          onChange={(firstDue) => set({ firstDue })}
        />

        <div className="grid gap-4 md:grid-cols-2">
          <NumberField
            label="Possession amount"
            prefix="Rs"
            helper="Optional. A last payment due at possession."
            error={shown.possession}
            value={fields.possessionAmount}
            onChange={(value) => set({ possessionAmount: value })}
          />
          {figures.possession > 0 && (
            <DatePicker
              label="Possession due"
              required
              error={shown.possessionDue}
              value={fields.possessionDue}
              onChange={(possessionDue) => set({ possessionDue })}
            />
          )}
        </div>

        {showStrip && (
          <dl className="m-0 flex flex-wrap gap-x-8 gap-y-2 rounded-card border border-gold-line bg-gold-soft px-4 py-3.5">
            {[
              ["To schedule", figures.toSchedule],
              ...(figures.possession > 0 ? [["Possession", figures.possession]] : []),
              ["Each installment", figures.each],
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
