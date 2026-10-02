import { useId, useState, type FormEvent } from "react";
import { Button, ChoiceChips, Dropdown, Modal, Notice, NumberField, TextField, useToast } from "../../components/ui";
import { formatPkr } from "../../utils/currency.ts";
import { DialogTitle } from "../../components/ui/DialogTitle.tsx";
import { commissionRebateApi } from "./api.ts";
import { ReasonDialog } from "./ReasonDialog.tsx";
import { SummaryStrip } from "./SummaryStrip.tsx";
import { REBATE_WAYS, basisName, rebateErrors, rebatePreview, rebateWayLabel, type FormErrors } from "./forms.ts";
import { rebateActions, rebateBasisFor, rebateRequestBody, type RebateFormState } from "./state.ts";
import type { BookingWorkspace, Rebate } from "./types.ts";
import type { RunMutation } from "./runMutation.ts";

type Props = {
  bookingId: number;
  workspace: Pick<BookingWorkspace, "bookingReference" | "agreedSalePrice" | "netSalePrice" | "amountCollected">;
  /** NULL to add the booking's rebate; the rebate to change otherwise. */
  existing: Rebate | null;
  run: RunMutation;
  onClose: () => void;
};

const TYPES = [{ value: "FixedAmount", label: "Fixed amount" }, { value: "Percentage", label: "Percentage" }];

function initial(existing: Rebate | null): RebateFormState {
  if (!existing) {
    return { calculationType: "FixedAmount", calculationBasis: "NetSalePriceAfterDiscount", percentageRate: "", fixedAmount: "", reason: "", method: "CashOrBankPayment" };
  }
  return {
    calculationType: existing.calculationType,
    calculationBasis: existing.calculationBasis,
    percentageRate: existing.percentageRate?.toString() ?? "",
    fixedAmount: existing.fixedAmount?.toString() ?? "",
    reason: existing.reason,
    method: existing.method,
  };
}

/**
 * Add rebate and Edit rebate: one popup. A booking has at most one rebate. How the customer gets it is
 * chosen here and locked after the first time any of it is given; the total never goes below what is given.
 */
export function RebateDialog({ bookingId, workspace, existing, run, onClose }: Props) {
  const toast = useToast();
  const formId = useId();
  const [form, setForm] = useState<RebateFormState>(() => initial(existing));
  const [shown, setShown] = useState<FormErrors<RebateFormState>>({});
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [cancelling, setCancelling] = useState(false);

  const set = (change: Partial<RebateFormState>) => {
    setForm((current) => ({ ...current, ...change }));
    setShown((current) => {
      const next = { ...current };
      for (const key of Object.keys(change)) delete next[key as keyof RebateFormState];
      return next;
    });
  };

  const percentage = form.calculationType === "Percentage";
  const given = existing?.appliedOrPaidAmount ?? 0;
  const wayLocked = !!existing && existing.disbursements.length > 0;
  const { final } = rebatePreview(form, existing, workspace);
  const ways = REBATE_WAYS.some((way) => way.value === form.method)
    ? REBATE_WAYS
    : [{ value: form.method, label: rebateWayLabel(form.method) }, ...REBATE_WAYS];

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (saving) return;
    const problems = rebateErrors(form, existing, workspace);
    setShown(problems);
    if (Object.keys(problems).length > 0) return;

    setSaving(true);
    setError(null);
    try {
      const body = rebateRequestBody(form, existing);
      // A rebate is money back to the customer, so the booking page is told once it is saved.
      await run(() => existing
        ? commissionRebateApi.updateRebate(bookingId, existing.id, body)
        : commissionRebateApi.createRebate(bookingId, body));
      toast.success(existing ? "Rebate updated." : "Rebate saved.");
      onClose();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "The rebate could not be saved.");
      setSaving(false);
    }
  };

  const canCancel = !!existing && rebateActions(existing.status, given).canCancel;

  return (
    <>
      <Modal
        open
        onClose={onClose}
        busy={saving}
        size="md"
        phoneLayout="fullscreen"
        title={<DialogTitle title={existing ? "Edit rebate" : "Add rebate"} subtitle={given > 0 ? `${formatPkr(given)} already given` : workspace.bookingReference} />}
        primaryAction={{ label: existing ? "Save changes" : "Save rebate", form: formId, loading: saving }}
      >
        <form id={formId} noValidate onSubmit={(event) => void submit(event)} className="flex flex-col gap-4">
          {error && <Notice tone="red" role="alert" title={error} />}

          <ChoiceChips variant="segmented" label="Type" required options={TYPES} value={form.calculationType} onChange={(calculationType) => set({ calculationType: calculationType as RebateFormState["calculationType"] })} />
          {percentage ? (
            <NumberField
              label="Percentage"
              required
              suffix="%"
              decimals={4}
              disabled={saving}
              helper={`Of the ${basisName(rebateBasisFor(form, existing)).toLowerCase()}`}
              error={shown.percentageRate}
              value={form.percentageRate}
              onChange={(percentageRate) => set({ percentageRate })}
            />
          ) : (
            <NumberField label="Amount" required prefix="Rs" disabled={saving} error={shown.fixedAmount} value={form.fixedAmount} onChange={(fixedAmount) => set({ fixedAmount })} />
          )}

          <Dropdown
            label="How the customer gets it"
            required
            disabled={saving || wayLocked}
            helper={wayLocked ? "Locked: some of this rebate has already been given." : undefined}
            options={ways}
            value={form.method}
            onChange={(method) => set({ method: method as RebateFormState["method"] })}
          />

          <SummaryStrip label="Net price after rebate" value={formatPkr(Math.max(0, workspace.netSalePrice - final))} />

          <TextField label="Reason (optional)" maxLength={2000} disabled={saving} value={form.reason} onChange={(event) => set({ reason: event.target.value })} />

          {canCancel && (
            <Button variant="link" disabled={saving} onClick={() => setCancelling(true)} className="self-start font-extrabold text-danger">Cancel this rebate</Button>
          )}
        </form>
      </Modal>

      {cancelling && existing && (
        <ReasonDialog
          title="Cancel this rebate?"
          message="The rebate will be cancelled. Nothing has been given to the customer yet."
          confirmLabel="Cancel rebate"
          cancelLabel="Keep rebate"
          keyPrefix="rebate-cancel"
          onClose={() => setCancelling(false)}
          onConfirm={async (reason) => {
            await run(() => commissionRebateApi.rebateStatus(bookingId, existing.id, { targetStatus: "Cancelled", reason, concurrencyToken: existing.concurrencyToken }));
            toast.success("Rebate cancelled.");
            onClose();
          }}
        />
      )}
    </>
  );
}
