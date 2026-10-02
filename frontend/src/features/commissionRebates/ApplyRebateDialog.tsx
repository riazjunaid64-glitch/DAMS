import { useId, useState, type FormEvent } from "react";
import { DatePicker, Dropdown, Modal, Notice, NumberField, TextArea, TextField, useToast } from "../../components/ui";
import { useIdempotencyKeys } from "../../lib/idempotency.ts";
import { pakistanToday } from "../../lib/financePeriods.ts";
import { formatPkr } from "../../utils/currency.ts";
import type { FinanceAccountOption } from "../bookings/detailTypes.ts";
import { DialogTitle } from "../../components/ui/DialogTitle.tsx";
import { useProofUpload } from "../proof/useProofUpload.ts";
import { commissionRebateApi } from "./api.ts";
import { PaymentOutFields } from "./PaymentOutFields.tsx";
import { applyRebateErrors, newMovementId, type ApplyFields, type FormErrors } from "./forms.ts";
import type { RunMutation } from "./runMutation.ts";
import type { BookingWorkspace, InstallmentOption, Rebate } from "./types.ts";

type Props = {
  bookingId: number;
  rebate: Rebate;
  workspace: Pick<BookingWorkspace, "netSalePrice" | "amountCollected" | "rebateCredits">;
  installments: InstallmentOption[];
  financeAccounts: FinanceAccountOption[];
  /** Set when the account list could not be loaded, so the account field can say why it is empty. */
  accountsError: string | null;
  run: RunMutation;
  /** Re-reads the workspace, so a proof uploaded after the save shows on its row. */
  onProofUploaded: () => Promise<void>;
  onClose: () => void;
};

const installmentLabel = (item: InstallmentOption) =>
  `${item.type === "Possession" ? "Possession installment" : `Installment ${item.sequenceNumber}`} · ${formatPkr(item.remainingBalance)} due`;

/**
 * Apply rebate. The fields follow how the customer gets it, which was chosen with the rebate: paid by
 * cash or bank (an account, a reference, optional proof), taken off one installment, or taken off the
 * outstanding balance.
 */
export function ApplyRebateDialog({ bookingId, rebate, workspace, installments, financeAccounts, accountsError, run, onProofUploaded, onClose }: Props) {
  const toast = useToast();
  const keys = useIdempotencyKeys();
  const proof = useProofUpload();
  const formId = useId();
  const method = rebate.method;
  const cash = method === "CashOrBankPayment";
  const offInstallment = method === "InstallmentAdjustment";

  const [fields, setFields] = useState<ApplyFields>({
    amount: String(rebate.outstandingAmount),
    method: "BankTransfer",
    accountId: financeAccounts.length === 1 ? String(financeAccounts[0]!.id) : "",
    reference: "",
    date: pakistanToday(),
    installmentId: "",
    notes: "",
  });
  const [shown, setShown] = useState<FormErrors<ApplyFields>>({});
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const set = (change: Partial<ApplyFields>) => {
    setFields((current) => ({ ...current, ...change }));
    setShown((current) => {
      const next = { ...current };
      for (const key of Object.keys(change)) delete next[key as keyof ApplyFields];
      // Cash needs no reference, so a missing-reference message must not outlive the method that asked for it.
      if (change.method !== undefined) delete next.reference;
      return next;
    });
  };

  const unpaid = installments.filter((item) => item.remainingBalance > 0);
  const chosen = unpaid.find((item) => String(item.id) === fields.installmentId);
  const balance = Math.max(0, Math.round((workspace.netSalePrice - workspace.amountCollected - workspace.rebateCredits) * 100) / 100);
  // The most this application may be: what is left of the rebate, and what is left to take it off.
  const limit = cash ? rebate.outstandingAmount
    : offInstallment ? Math.min(rebate.outstandingAmount, chosen?.remainingBalance ?? rebate.outstandingAmount)
      : Math.min(rebate.outstandingAmount, balance);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (saving) return;
    const problems = applyRebateErrors(method, fields, limit);
    setShown(problems);
    if (Object.keys(problems).length > 0) return;

    setSaving(true);
    setError(null);
    // The same intent keeps the same key, so pressing the button again after a dropped connection is
    // recognised as the retry it is rather than giving the customer the rebate twice.
    const signature = `disbursement:${rebate.id}:${JSON.stringify(fields)}`;
    try {
      const saved = await run(() => commissionRebateApi.disburseRebate(bookingId, rebate.id, {
        method,
        amount: Number(fields.amount),
        appliedAt: fields.date,
        financeAccountId: cash ? Number(fields.accountId) : null,
        installmentId: offInstallment ? Number(fields.installmentId) : null,
        paymentMethod: cash ? fields.method : null,
        reference: fields.reference.trim() || null,
        notes: fields.notes.trim() || null,
        idempotencyKey: keys.key(signature, "rebate-disbursement"),
        rebateConcurrencyToken: rebate.concurrencyToken,
      }));
      keys.release(signature);
      toast.success(cash ? "Payment recorded." : "Rebate applied.");
      // A failed proof never undoes the payment: the popup closes with a warning and the file is
      // attached later from the row.
      const rowId = newMovementId(rebate.disbursements, saved.rebates.find((r) => r.id === rebate.id)?.disbursements ?? []);
      if (cash && proof.hasFile) {
        if (rowId !== null && await proof.upload("RebateDisbursement", rowId)) await onProofUploaded().catch(() => undefined);
        else toast.error("The payment was saved, but its proof did not upload. Attach it from the payment.");
      }
      onClose();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "The rebate could not be applied.");
      setSaving(false);
    }
  };

  const amountField = (
    <NumberField
      label="Amount"
      required
      prefix="Rs"
      disabled={saving}
      helper={cash ? undefined : offInstallment ? (chosen ? `Up to ${formatPkr(limit)}` : undefined) : `Up to ${formatPkr(limit)}`}
      error={shown.amount}
      value={fields.amount}
      onChange={(amount) => set({ amount })}
    />
  );

  return (
    <Modal
      open
      onClose={onClose}
      busy={saving}
      size="md"
      phoneLayout="fullscreen"
      title={<DialogTitle title="Apply rebate" subtitle={`${formatPkr(rebate.outstandingAmount)} remaining`} />}
      primaryAction={{ label: "Apply rebate", form: formId, loading: saving }}
    >
      <form id={formId} noValidate onSubmit={(event) => void submit(event)} className="flex flex-col gap-4">
        {error && <Notice tone="red" role="alert" title={error} />}
        {cash && accountsError && <Notice tone="orange" role="alert" title={accountsError} />}

        {offInstallment && (
          <Dropdown
            label="Installment"
            required
            disabled={saving}
            placeholder="Select an installment"
            error={shown.installmentId}
            options={unpaid.map((item) => ({ value: String(item.id), label: installmentLabel(item) }))}
            value={fields.installmentId}
            onChange={(installmentId) => set({ installmentId })}
          />
        )}
        {amountField}

        {cash ? (
          <PaymentOutFields fields={fields} errors={shown} onChange={set} financeAccounts={financeAccounts} proof={proof.fieldProps} disabled={saving} />
        ) : (
          <>
            <DatePicker label="Date" required disabled={saving} max={pakistanToday()} error={shown.date} value={fields.date} onChange={(date) => set({ date })} />
            {method === "CreditNote" && (
              <TextField label="Reference" required maxLength={200} disabled={saving} error={shown.reference} value={fields.reference} onChange={(event) => set({ reference: event.target.value })} />
            )}
            <TextArea
              label={method === "Other" ? "How the customer gets it" : "Notes"}
              required={method === "Other"}
              rows={3}
              maxLength={2000}
              disabled={saving}
              error={shown.notes}
              value={fields.notes}
              onChange={(event) => set({ notes: event.target.value })}
            />
          </>
        )}
      </form>
    </Modal>
  );
}
