import { useId, useMemo, useState, type FormEvent } from "react";
import { DatePicker, DialogTitle, Dropdown, Modal, Notice, NumberField, OptionalLabel, TextField, useToast } from "../../../components/ui";
import { pakistanToday } from "../../../lib/financePeriods.ts";
import { newIdempotencyKey } from "../../../lib/idempotency.ts";
import { cashAccountChoices } from "../loans/rules.ts";
import type { CashAccount } from "../loans/types.ts";
import { saveDeposit } from "../whtApi.ts";
import { formatRs, type WhtDeposit } from "../whtTypes.ts";
import { depositBody, depositErrors, depositFields, depositReady } from "./rules.ts";


type Props = {
  /** The deposit being edited; null records a new one. */
  deposit: WhtDeposit | null;
  /** What is still owed to FBR; a new deposit opens on it. */
  owed: number;
  accounts: readonly CashAccount[];
  accountsError: string | null;
  onClose: () => void;
  onSaved: () => void;
};

/**
 * A challan paid to FBR out of a cash or bank account. It takes the withheld tax out of the account
 * without being an expense (the cost was booked when the supplier was paid), and the server refuses
 * more than was owed on the deposit's date, which this popup shows as it comes.
 */
export function DepositDialog({ deposit, owed, accounts, accountsError, onClose, onSaved }: Props) {
  const toast = useToast();
  const formId = useId();
  const [today] = useState(pakistanToday);
  const [fields, setFields] = useState(() => depositFields(deposit, owed, today));
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // One key per open popup: pressing Save again after a lost answer is the same deposit, so a save
  // that committed before the connection dropped is recognised instead of paying the challan twice.
  const [requestKey] = useState(() => newIdempotencyKey("wht-deposit"));
  const choices = useMemo(() => cashAccountChoices(accounts, fields.accountId), [accounts, fields.accountId]);
  const errors = depositErrors(fields, today);
  const ready = depositReady(fields, today);
  const set = (changes: Partial<typeof fields>) => setFields((current) => ({ ...current, ...changes }));

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (saving || !ready) return;
    setSaving(true);
    setError(null);
    try {
      await saveDeposit(deposit?.id ?? null, depositBody(fields, deposit?.concurrencyToken ?? null), requestKey);
      toast.success(deposit ? "Deposit saved." : "Deposit recorded.");
      onSaved();
      onClose();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "The deposit could not be saved.");
      setSaving(false);
    }
  };

  return (
    <Modal
      open
      onClose={onClose}
      busy={saving}
      size="md"
      phoneLayout="fullscreen"
      title={<DialogTitle title={deposit ? "Edit deposit to FBR" : "Record deposit to FBR"} subtitle={deposit?.challanNumber} />}
      primaryAction={{ label: "Save", form: formId, loading: saving, disabled: !ready }}
    >
      <form id={formId} noValidate onSubmit={(event) => void submit(event)} className="flex flex-col gap-4">
        {error && <Notice tone="red" role="alert" title={error} />}
        {accountsError && <Notice tone="orange" role="alert" title={accountsError} />}
        <Dropdown
          label="Paid from account"
          required
          disabled={saving}
          placeholder="Select cash or bank account"
          options={choices}
          value={fields.accountId}
          onChange={(accountId) => set({ accountId })}
        />
        <NumberField
          label="Amount"
          required
          prefix="Rs"
          decimals={2}
          disabled={saving}
          value={fields.amount}
          onChange={(amount) => set({ amount })}
          helper={!deposit && owed > 0 ? `Owed now: ${formatRs(owed)}` : undefined}
        />
        <div className="grid gap-4 md:grid-cols-2">
          <DatePicker label="Deposit date" required disabled={saving} max={today} error={errors.date} value={fields.date} onChange={(date) => set({ date })} />
          <TextField label={<OptionalLabel>Challan / CPR number</OptionalLabel>} maxLength={100} disabled={saving} value={fields.challan} onChange={(event) => set({ challan: event.target.value })} />
          <DatePicker label={<OptionalLabel>Period covered from</OptionalLabel>} disabled={saving} value={fields.periodFrom} onChange={(periodFrom) => set({ periodFrom })} />
          <DatePicker label={<OptionalLabel>Period covered to</OptionalLabel>} disabled={saving} min={fields.periodFrom || undefined} error={errors.periodTo} value={fields.periodTo} onChange={(periodTo) => set({ periodTo })} />
        </div>
        <TextField label={<OptionalLabel>Notes</OptionalLabel>} maxLength={1000} disabled={saving} value={fields.notes} onChange={(event) => set({ notes: event.target.value })} />
      </form>
    </Modal>
  );
}
