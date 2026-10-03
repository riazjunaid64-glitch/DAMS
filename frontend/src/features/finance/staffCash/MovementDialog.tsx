import { useId, useMemo, useState, type FormEvent } from "react";
import { DatePicker, DialogTitle, Dropdown, Modal, Notice, NumberField, OptionalLabel, TextArea, TextField, useToast } from "../../../components/ui";
import { pakistanToday } from "../../../lib/financePeriods.ts";
import type { useIdempotencyKeys } from "../../../lib/idempotency.ts";
import { RecordProof } from "../home/proof.tsx";
import { cashAccountChoices } from "../loans/rules.ts";
import type { CashAccount } from "../loans/types.ts";
import { staffCashApi } from "./api.ts";
import { accountLabel, giveAction, movementChoices, movementDate, movementTitle, returnAmount, type MovementIntent } from "./rules.ts";
import type { Holder, HistoryItem, MovementType } from "./types.ts";

type Fields = { type: MovementType; amount: string; date: string; accountId: string; reference: string; note: string };
type Errors = { date?: string };

function fieldsOf(holder: Holder, intent: MovementIntent, movement: HistoryItem | null, today: string): Fields {
  if (movement) {
    return {
      type: movement.movementType ?? "FundsGiven",
      amount: String(movement.grossAmount),
      date: movement.date.slice(0, 10),
      accountId: movement.counterpartyFinanceAccountId == null ? "" : String(movement.counterpartyFinanceAccountId),
      reference: movement.reference ?? "",
      note: movement.note ?? "",
    };
  }
  return {
    type: intent === "return" ? "FundsReturned" : "FundsGiven",
    amount: intent === "return" ? returnAmount(holder) : intent === "settle" ? giveAction(holder).amount : "",
    date: today,
    accountId: "",
    reference: "",
    note: "",
  };
}


type Props = {
  holder: Holder;
  /** Which button opened it; ignored when correcting. */
  intent: MovementIntent;
  /** The money move being corrected; null records a new one. */
  movement: HistoryItem | null;
  cashAccounts: readonly CashAccount[];
  accountsError: string | null;
  /** Held by the page, so a retry after the popup was closed and reopened still carries the same key. */
  keys: ReturnType<typeof useIdempotencyKeys>;
  onClose: () => void;
  onSaved: () => void;
  /** Opens or downloads the saved slip of the movement being corrected. */
  onOpenAttachment: (movement: HistoryItem, download: boolean) => void;
};

/**
 * Money handed to the person, or cash they hand back, between the float and a company cash or bank
 * account. It only moves company cash, so it never changes profit.
 */
export function MovementDialog({ holder, intent, movement, cashAccounts, accountsError, keys, onClose, onSaved, onOpenAttachment }: Props) {
  const toast = useToast();
  const formId = useId();
  const [today] = useState(pakistanToday);
  const [fields, setFields] = useState(() => fieldsOf(holder, intent, movement, today));
  const [file, setFile] = useState<File | null>(null);
  const [removeSaved, setRemoveSaved] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [shown, setShown] = useState<Errors>({});
  const choices = useMemo(() => cashAccountChoices(cashAccounts, fields.accountId), [cashAccounts, fields.accountId]);
  const amount = Number(fields.amount);
  const ready = fields.amount !== "" && Number.isFinite(amount) && amount > 0 && fields.date !== "" && fields.accountId !== "";
  const set = (changes: Partial<Fields>, clear?: keyof Errors) => {
    setFields((current) => ({ ...current, ...changes }));
    if (clear) setShown((current) => ({ ...current, [clear]: undefined }));
  };

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (saving || !ready) return;
    if (fields.date > today) {
      setShown({ date: "The date cannot be in the future." });
      return;
    }
    setSaving(true);
    setError(null);
    const body = new FormData();
    body.append("type", fields.type);
    body.append("amount", String(amount));
    body.append("date", fields.date);
    body.append("counterpartyFinanceAccountId", fields.accountId);
    if (fields.reference.trim()) body.append("reference", fields.reference.trim());
    if (fields.note.trim()) body.append("note", fields.note.trim());
    if (file) body.append("attachment", file);
    try {
      if (movement) {
        body.append("concurrencyToken", movement.concurrencyToken ?? "");
        // A replacement already takes the saved file's place; the server refuses both together.
        if (removeSaved && !file) body.append("removeAttachment", "true");
        await staffCashApi.correctMovement(holder.financeAccountId, movement.recordId, body);
        toast.success("Movement corrected.");
      } else {
        // The same intent carries the same key, so a retry the operator cannot see the result of is recorded once.
        const signature = `staff-cash:${holder.financeAccountId}:${fields.type}:${amount}:${fields.date}`;
        await staffCashApi.recordMovement(holder.financeAccountId, body, keys.key(signature, "staff-cash-transfer"));
        keys.release(signature);
        toast.success("Movement recorded.");
      }
      onSaved();
      onClose();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "Could not record this movement.");
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
      title={<DialogTitle
        title={movementTitle(fields.type, intent, movement !== null)}
        subtitle={movement ? `${holder.personName} · ${movementDate(movement.date)}` : holder.personName}
      />}
      primaryAction={{ label: movement ? "Save correction" : "Save", form: formId, loading: saving, disabled: !ready }}
    >
      <form id={formId} noValidate onSubmit={(event) => void submit(event)} className="flex flex-col gap-4">
        {error && <Notice tone="red" role="alert" title={error} />}
        {accountsError && <Notice tone="orange" role="alert" title={accountsError} />}
        <Dropdown
          label="Movement"
          required
          disabled={saving}
          options={movementChoices(holder, movement?.movementType ?? null)}
          value={fields.type}
          onChange={(next) => set({ type: next as MovementType })}
        />
        <div className="grid gap-4 md:grid-cols-2">
          <NumberField label="Amount" required prefix="Rs" decimals={2} disabled={saving} value={fields.amount} onChange={(value) => set({ amount: value })} />
          <DatePicker label="Date" required disabled={saving} max={today} error={shown.date} value={fields.date} onChange={(date) => set({ date }, "date")} />
        </div>
        <Dropdown
          label={accountLabel(fields.type)}
          required
          disabled={saving}
          placeholder="Select account"
          options={choices}
          value={fields.accountId}
          onChange={(accountId) => set({ accountId })}
        />
        <TextField label={<OptionalLabel>Reference</OptionalLabel>} maxLength={200} disabled={saving} value={fields.reference} onChange={(event) => set({ reference: event.target.value })} />
        <TextArea label={<OptionalLabel>Note</OptionalLabel>} rows={3} maxLength={1000} disabled={saving} value={fields.note} onChange={(event) => set({ note: event.target.value })} />
        <RecordProof
          saved={movement?.attachment ?? null}
          selected={file}
          disabled={saving}
          onSelected={setFile}
          onRemoveSaved={setRemoveSaved}
          onOpen={movement ? () => onOpenAttachment(movement, false) : undefined}
          onDownload={movement ? () => onOpenAttachment(movement, true) : undefined}
        />
      </form>
    </Modal>
  );
}
