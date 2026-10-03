import { useId, useMemo, useState, type FormEvent } from "react";
import { DialogTitle, Dropdown, Modal, Notice, OptionalLabel, TextField, Toggle, useToast } from "../../../components/ui";
import { loansApi } from "./api.ts";
import { loanAccountChoices } from "./rules.ts";
import type { Loan, LoanAccount } from "./types.ts";

type Fields = { name: string; lenderName: string; financeAccountId: string; isActive: boolean };

const fieldsOf = (loan: Loan | null): Fields => loan
  ? { name: loan.name, lenderName: loan.lenderName ?? "", financeAccountId: String(loan.financeAccountId), isActive: loan.isActive }
  : { name: "", lenderName: "", financeAccountId: "", isActive: true };

type Props = {
  /** The loan being edited; null adds a new one. */
  loan: Loan | null;
  accounts: readonly LoanAccount[];
  accountsError: string | null;
  onClose: () => void;
  onSaved: (loan: Loan) => void;
};

/**
 * Add or edit a loan: its name, the lender, the Liability account that carries the principal owed,
 * and whether it takes new movements. An edit sends the loan's row version back.
 */
export function LoanFormDialog({ loan, accounts, accountsError, onClose, onSaved }: Props) {
  const toast = useToast();
  const formId = useId();
  const [fields, setFields] = useState(() => fieldsOf(loan));
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const set = (changes: Partial<Fields>) => setFields((current) => ({ ...current, ...changes }));
  const choices = useMemo(() => loanAccountChoices(accounts, loan?.id ?? null, fields.financeAccountId), [accounts, loan, fields.financeAccountId]);
  const noAccounts = !accountsError && choices.length === 0;
  const ready = fields.name.trim() !== "" && fields.financeAccountId !== "";

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (saving || !ready) return;
    setSaving(true);
    setError(null);
    try {
      const saved = await loansApi.saveLoan({
        name: fields.name.trim(),
        lenderName: fields.lenderName.trim() || null,
        financeAccountId: Number(fields.financeAccountId),
        isActive: fields.isActive,
        concurrencyToken: loan?.concurrencyToken ?? "",
      }, loan?.id ?? null);
      toast.success("Loan saved.");
      onSaved(saved);
      onClose();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "The loan could not be saved.");
      setSaving(false);
    }
  };

  return (
    <Modal
      open
      onClose={onClose}
      busy={saving}
      size="sm"
      phoneLayout="fullscreen"
      title={loan ? <DialogTitle title="Edit loan" subtitle={loan.name} /> : "Add loan"}
      primaryAction={{ label: "Save loan", form: formId, loading: saving, disabled: !ready }}
    >
      <form id={formId} noValidate onSubmit={(event) => void submit(event)} className="flex flex-col gap-4">
        {error && <Notice tone="red" role="alert" title={error} />}
        {accountsError && <Notice tone="orange" role="alert" title={accountsError} />}
        <TextField label="Loan name" required maxLength={200} disabled={saving} value={fields.name} onChange={(event) => set({ name: event.target.value })} />
        <TextField
          label={<OptionalLabel>Lender name</OptionalLabel>}
          maxLength={200}
          disabled={saving}
          value={fields.lenderName}
          onChange={(event) => set({ lenderName: event.target.value })}
        />
        <Dropdown
          label="Loan liability account"
          required
          disabled={saving}
          placeholder="Select account"
          options={choices}
          value={fields.financeAccountId}
          onChange={(financeAccountId) => set({ financeAccountId })}
          helper={noAccounts ? "Add a Liability account in Manage accounts first." : undefined}
        />
        <Toggle
          label="Active, allows new movements"
          disabled={saving}
          checked={fields.isActive}
          onChange={(isActive) => set({ isActive })}
          className="rounded-field border border-line-input px-3"
        />
      </form>
    </Modal>
  );
}
