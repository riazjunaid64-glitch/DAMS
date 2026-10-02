import { useId, useState, type FormEvent } from "react";
import { AttachProof, DatePicker, Dropdown, Modal, Notice, NumberField, TextField, useToast } from "../../../components/ui";
import { pakistanToday } from "../../../lib/financePeriods.ts";
import type { useIdempotencyKeys } from "../../../lib/idempotency.ts";
import { DialogTitle } from "../../bookings/DialogTitle.tsx";
import { capitalApi } from "./api.ts";
import { cashAccountLabel } from "./rules.ts";
import type { AccountOption, CapitalTransactionType, Partner } from "./types.ts";

const TYPES: { value: CapitalTransactionType; label: string }[] = [
  { value: "Contribution", label: "Contribution" },
  { value: "Withdrawal", label: "Withdrawal" },
  { value: "ProfitShare", label: "Profit share" },
  { value: "LossShare", label: "Loss share" },
];

/** Only money that goes through a bank or cash account names it; a profit or loss share is a book entry. */
const movesCash = (type: CapitalTransactionType) => type === "Contribution" || type === "Withdrawal";

type Fields = { type: CapitalTransactionType; amount: string; date: string; financeAccountId: string; reference: string; note: string };
type Errors = { amount?: string; date?: string; account?: string };

type Props = {
  partner: Partner;
  /** Active cash and bank accounts, for Contribution and Withdrawal. */
  cashAccounts: readonly AccountOption[];
  accountsError: string | null;
  /** Held by the page, so a retry after the popup was closed and reopened still carries the same key. */
  keys: ReturnType<typeof useIdempotencyKeys>;
  onClose: () => void;
  onSaved: () => void;
};

/**
 * Add transaction: one capital movement for a partner, with an optional bank slip. A movement is
 * never edited afterwards. The same intent carries the same money-request key, so a double click or
 * a retry records it once.
 */
export function TransactionDialog({ partner, cashAccounts, accountsError, keys, onClose, onSaved }: Props) {
  const toast = useToast();
  const formId = useId();
  const [today] = useState(pakistanToday);
  const [fields, setFields] = useState<Fields>(() => ({ type: "Contribution", amount: "", date: today, financeAccountId: "", reference: "", note: "" }));
  const [file, setFile] = useState<File | null>(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [shown, setShown] = useState<Errors>({});
  const cash = movesCash(fields.type);
  const set = (changes: Partial<Fields>, clear: (keyof Errors)[] = []) => {
    setFields((current) => ({ ...current, ...changes }));
    if (clear.length > 0) setShown((current) => ({ ...current, ...Object.fromEntries(clear.map((key) => [key, undefined])) }));
  };
  const ready = fields.amount !== "" && fields.date !== "" && (!cash || fields.financeAccountId !== "");

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (saving || !ready) return;
    const amount = Number(fields.amount);
    const problems: Errors = {};
    if (!Number.isFinite(amount) || amount <= 0) problems.amount = "Enter an amount greater than zero.";
    if (fields.date > today) problems.date = "The date cannot be in the future.";
    if (cash && !fields.financeAccountId) problems.account = "Choose the cash or bank account used.";
    setShown(problems);
    if (Object.values(problems).some(Boolean)) return;

    setSaving(true);
    setError(null);
    const signature = `capital:${partner.id}:${fields.type}:${amount}:${fields.date}`;
    try {
      const body = new FormData();
      body.append("type", fields.type);
      body.append("amount", String(amount));
      body.append("date", fields.date);
      if (cash) body.append("financeAccountId", fields.financeAccountId);
      if (fields.reference.trim()) body.append("reference", fields.reference.trim());
      if (fields.note.trim()) body.append("note", fields.note.trim());
      if (file) body.append("attachment", file);
      await capitalApi.recordTransaction(partner.id, body, keys.key(signature, "capital-movement"));
      keys.release(signature);
      toast.success("Transaction recorded.");
      onSaved();
      onClose();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "The transaction could not be saved.");
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
      title={<DialogTitle title="Add transaction" subtitle={partner.name} />}
      primaryAction={{ label: "Save", form: formId, loading: saving, disabled: !ready }}
    >
      <form id={formId} noValidate onSubmit={(event) => void submit(event)} className="flex flex-col gap-4">
        {error && <Notice tone="red" role="alert" title={error} />}
        {accountsError && <Notice tone="orange" role="alert" title={accountsError} />}
        <Dropdown
          label="Type"
          required
          disabled={saving}
          options={TYPES}
          value={fields.type}
          onChange={(type) => set({ type: type as CapitalTransactionType }, ["account"])}
        />
        <div className="grid gap-4 md:grid-cols-2">
          <NumberField
            label="Amount"
            required
            prefix="Rs"
            decimals={2}
            disabled={saving}
            error={shown.amount}
            value={fields.amount}
            onChange={(amount) => set({ amount }, ["amount"])}
          />
          <DatePicker label="Date" required disabled={saving} max={today} error={shown.date} value={fields.date} onChange={(date) => set({ date }, ["date"])} />
        </div>
        {cash && (
          <Dropdown
            label="Cash or bank account"
            required
            disabled={saving}
            placeholder="Select account"
            options={cashAccounts.filter((account) => account.isActive).map((account) => ({ value: String(account.id), label: cashAccountLabel(account) }))}
            error={shown.account}
            value={fields.financeAccountId}
            onChange={(financeAccountId) => set({ financeAccountId }, ["account"])}
          />
        )}
        <div className="grid gap-4 md:grid-cols-2">
          <TextField label={<>Reference <span className="font-normal text-ink-muted">(optional)</span></>} maxLength={200} disabled={saving} value={fields.reference} onChange={(event) => set({ reference: event.target.value })} />
          <TextField label={<>Note <span className="font-normal text-ink-muted">(optional)</span></>} maxLength={1000} disabled={saving} value={fields.note} onChange={(event) => set({ note: event.target.value })} />
        </div>
        <AttachProof
          label="Attachment"
          disabled={saving}
          file={file ? { name: file.name, size: file.size } : null}
          onPick={setFile}
          onRemove={() => setFile(null)}
        />
      </form>
    </Modal>
  );
}
