import { useId, useMemo, useState, type FormEvent } from "react";
import { Button, ChoiceChips, Dropdown, Modal, Notice, NumberField, TextArea, useToast } from "../../components/ui";
import { formatPkr } from "../../utils/currency.ts";
import { DialogTitle } from "../bookings/DialogTitle.tsx";
import { commissionRebateApi } from "./api.ts";
import { PartnerDialog } from "./PartnerDialog.tsx";
import { ReasonDialog } from "./ReasonDialog.tsx";
import { SummaryStrip } from "./SummaryStrip.tsx";
import { basisName, basisOptions, commissionErrors, commissionPreview, type FormErrors } from "./forms.ts";
import { commissionActions, commissionAdjustmentNote, commissionAllocationPercent, commissionBases, commissionRequestBody, isEditableBasis, type CommissionFormState } from "./state.ts";
import type { BookingWorkspace, Commission, Partner } from "./types.ts";
import type { RunMutation } from "./runMutation.ts";

type Props = {
  bookingId: number;
  workspace: Pick<BookingWorkspace, "agreedSalePrice" | "netSalePrice" | "amountCollected">;
  /** NULL to add a commission; the commission to change otherwise. */
  existing: Commission | null;
  partners: Partner[];
  /** Partners that already hold a live commission on this booking: one each, so they are not offered. */
  takenPartnerIds: ReadonlySet<number>;
  run: RunMutation;
  /** A partner created from this popup is added to the list the panel holds. */
  onPartnerCreated: (partner: Partner) => void;
  onClose: () => void;
};

const TYPES = [{ value: "FixedAmount", label: "Fixed amount" }, { value: "Percentage", label: "Percentage" }];

function initial(existing: Commission | null): CommissionFormState {
  if (!existing) {
    return { partnerId: "", calculationType: "Percentage", calculationBasis: "NetSalePriceAfterDiscount", percentageRate: "", fixedAmount: "", notes: "" };
  }
  return {
    partnerId: String(existing.partnerId),
    calculationType: existing.calculationType,
    calculationBasis: existing.calculationBasis,
    percentageRate: existing.percentageRate?.toString() ?? "",
    fixedAmount: existing.fixedAmount?.toString() ?? "",
    notes: existing.manualReason ?? "",
  };
}

/**
 * Add commission and Edit commission: one popup. A commission is always saved as set by hand with the
 * chosen type and basis; the automatic rules of the Finance page are not used here. Editing keeps the
 * partner fixed and never goes below what is already paid.
 */
export function CommissionDialog({ bookingId, workspace, existing, partners, takenPartnerIds, run, onPartnerCreated, onClose }: Props) {
  const toast = useToast();
  const formId = useId();
  const [form, setForm] = useState<CommissionFormState>(() => initial(existing));
  const [shown, setShown] = useState<FormErrors<CommissionFormState>>({});
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [newPartner, setNewPartner] = useState(false);
  const [cancelling, setCancelling] = useState(false);

  const set = (change: Partial<CommissionFormState>) => {
    setForm((current) => ({ ...current, ...change }));
    setShown((current) => {
      const next = { ...current };
      for (const key of Object.keys(change)) delete next[key as keyof CommissionFormState];
      return next;
    });
  };

  // A rule-driven commission takes its figures from the rule, which the server recalculates on save, so
  // the popup states what the rule says instead of offering inputs whose changes would be discarded.
  const ruleDriven = !!existing && !existing.isManual;
  const percentage = form.calculationType === "Percentage";
  const basisLocked = !!existing && !isEditableBasis(existing.calculationBasis, commissionBases);
  const amount = ruleDriven ? existing.finalAmount : commissionPreview(form, existing, workspace);
  const adjustmentNote = commissionAdjustmentNote(existing, ruleDriven, amount);

  const partnerOptions = useMemo(
    () => partners
      .filter((partner) => String(partner.id) === form.partnerId || !takenPartnerIds.has(partner.id))
      .map((partner) => ({ value: String(partner.id), label: partner.name })),
    [partners, takenPartnerIds, form.partnerId],
  );
  // An edit names its own partner even when the directory list no longer holds them (made inactive).
  const partnerChoices = existing && !partnerOptions.some((option) => option.value === String(existing.partnerId))
    ? [{ value: String(existing.partnerId), label: existing.partnerName }, ...partnerOptions]
    : partnerOptions;

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (saving) return;
    const problems = commissionErrors(form, existing, workspace);
    setShown(problems);
    if (Object.keys(problems).length > 0) return;

    setSaving(true);
    setError(null);
    try {
      const body = commissionRequestBody(form, existing);
      await run(() => existing
        ? commissionRebateApi.updateCommission(bookingId, existing.id, body)
        : commissionRebateApi.createCommission(bookingId, body), false);
      toast.success(existing ? "Commission updated." : "Commission saved.");
      onClose();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "The commission could not be saved.");
      setSaving(false);
    }
  };

  const canCancel = !!existing && commissionActions(existing.status, existing.paidAmount).canCancel;
  const title = existing ? "Edit commission" : "Add commission";
  const subtitle = existing
    ? `${existing.partnerName}${existing.paidAmount > 0 ? ` · ${formatPkr(existing.paidAmount)} already paid` : ""}`
    : `Net sale price ${formatPkr(workspace.netSalePrice)}`;

  return (
    <>
      <Modal
        open
        onClose={onClose}
        busy={saving}
        size="md"
        phoneLayout="fullscreen"
        title={<DialogTitle title={title} subtitle={subtitle} />}
        primaryAction={{ label: existing ? "Save changes" : "Save commission", form: formId, loading: saving }}
      >
        <form id={formId} noValidate onSubmit={(event) => void submit(event)} className="flex flex-col gap-4">
          {error && <Notice tone="red" role="alert" title={error} />}

          <div className="flex items-end gap-3">
            <Dropdown
              className="flex-1"
              label="Partner"
              required
              searchable
              placeholder="Select partner"
              disabled={saving || !!existing}
              error={shown.partnerId}
              options={partnerChoices}
              value={form.partnerId}
              onChange={(partnerId) => set({ partnerId })}
            />
            {!existing && <Button variant="outline" disabled={saving} onClick={() => setNewPartner(true)}>+ New partner</Button>}
          </div>

          {ruleDriven ? (
            <Notice tone="gold" title={`This commission follows the rule “${existing.ruleNameSnapshot ?? "set on the Finance page"}”.`} message="Its amount is worked out by the rule; only the partner's notes can change here." />
          ) : (
            <>
              <ChoiceChips variant="segmented" label="Type" required options={TYPES} value={form.calculationType} onChange={(calculationType) => set({ calculationType: calculationType as CommissionFormState["calculationType"] })} />
              {percentage ? (
                <div className="grid gap-4 md:grid-cols-2">
                  <NumberField label="Percentage" required suffix="%" decimals={4} disabled={saving} error={shown.percentageRate} value={form.percentageRate} onChange={(percentageRate) => set({ percentageRate })} />
                  <Dropdown
                    label="Worked out on"
                    required
                    disabled={saving || basisLocked}
                    options={basisLocked ? [{ value: form.calculationBasis, label: basisName(form.calculationBasis) }] : basisOptions(commissionBases)}
                    value={form.calculationBasis}
                    onChange={(calculationBasis) => set({ calculationBasis: calculationBasis as CommissionFormState["calculationBasis"] })}
                  />
                </div>
              ) : (
                <NumberField label="Amount" required prefix="Rs" disabled={saving} error={shown.fixedAmount} value={form.fixedAmount} onChange={(fixedAmount) => set({ fixedAmount })} />
              )}
            </>
          )}

          <SummaryStrip
            label="Commission"
            value={formatPkr(amount)}
            note={ruleDriven ? adjustmentNote : [commissionAllocationPercent(form, existing) !== 100 && `${commissionAllocationPercent(form, existing)}% allocation applied.`, adjustmentNote].filter(Boolean).join(" ") || undefined}
          />

          <TextArea label="Notes" rows={3} maxLength={2000} disabled={saving} value={form.notes} onChange={(event) => set({ notes: event.target.value })} />

          {canCancel && (
            <Button variant="link" disabled={saving} onClick={() => setCancelling(true)} className="self-start font-extrabold text-danger">Cancel this commission</Button>
          )}
        </form>
      </Modal>

      {newPartner && (
        <PartnerDialog
          onClose={() => setNewPartner(false)}
          onSaved={(partner) => { onPartnerCreated(partner); set({ partnerId: String(partner.id) }); }}
        />
      )}
      {cancelling && existing && (
        <ReasonDialog
          title="Cancel this commission?"
          message={`The commission for ${existing.partnerName} will be cancelled. Nothing has been paid on it.`}
          confirmLabel="Cancel commission"
          cancelLabel="Keep commission"
          keyPrefix="commission-cancel"
          onClose={() => setCancelling(false)}
          onConfirm={async (reason) => {
            await run(() => commissionRebateApi.commissionStatus(bookingId, existing.id, { targetStatus: "Cancelled", reason, concurrencyToken: existing.concurrencyToken }), false);
            toast.success("Commission cancelled.");
            onClose();
          }}
        />
      )}
    </>
  );
}
