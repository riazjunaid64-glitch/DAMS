import { useId, useMemo, useState, type FormEvent } from "react";
import { DatePicker, DialogTitle, Dropdown, Modal, Notice, NumberField, TextArea, TextField, useToast } from "../../../components/ui";
import { pakistanToday } from "../../../lib/financePeriods.ts";
import type { useIdempotencyKeys } from "../../../lib/idempotency.ts";
import { RecordProof } from "../home/proof.tsx";
import { loansApi } from "./api.ts";
import { cashAccountChoices, movementDate, rupees } from "./rules.ts";
import type { CashAccount, Loan, LoanTransaction, MovementType } from "./types.ts";

const TYPES: { value: MovementType; label: string }[] = [
  { value: "Repayment", label: "Repayment" },
  { value: "Drawdown", label: "Funds received (drawdown)" },
];

type Fields = { type: MovementType; principal: string; interest: string; date: string; financeAccountId: string; reference: string; note: string };
type Errors = { principal?: string; date?: string; account?: string };

const fieldsOf = (movement: LoanTransaction | null, type: MovementType, today: string): Fields => movement
  ? {
      type: movement.type,
      principal: movement.principalAmount ? String(movement.principalAmount) : "",
      interest: movement.interestAmount ? String(movement.interestAmount) : "",
      date: movement.date.slice(0, 10),
      financeAccountId: String(movement.financeAccountId),
      reference: movement.reference ?? "",
      note: movement.note ?? "",
    }
  : { type, principal: "", interest: "", date: today, financeAccountId: "", reference: "", note: "" };

/** The popup's name: what is being done, and to which kind of movement. */
function titleFor(type: MovementType, correcting: boolean): string {
  if (correcting) return type === "Drawdown" ? "Correct funds received" : "Correct repayment";
  return type === "Drawdown" ? "Receive loan funds" : "Record repayment";
}

const optional = (label: string) => <>{label} <span className="font-normal normal-case tracking-normal text-ink-muted">(optional)</span></>;
const amount = (value: string) => (value === "" ? 0 : Number(value));

type Props = {
  loan: Loan;
  /** The movement being corrected; null records a new one of `type`. */
  movement: LoanTransaction | null;
  type: MovementType;
  cashAccounts: readonly CashAccount[];
  accountsError: string | null;
  /** Held by the page, so a retry after the popup was closed and reopened still carries the same key. */
  keys: ReturnType<typeof useIdempotencyKeys>;
  onClose: () => void;
  onSaved: () => void;
  /** Opens or downloads the saved attachment of the movement being corrected. */
  onOpenAttachment: (movement: LoanTransaction, download: boolean) => void;
};

/**
 * Record a repayment, receive loan funds, or correct either. A repayment is principal, interest or
 * both, and the total leaving the bank is their sum; a drawdown is principal only. Interest is a
 * profit & loss cost and never reduces the principal owed.
 */
export function MovementDialog({ loan, movement, type, cashAccounts, accountsError, keys, onClose, onSaved, onOpenAttachment }: Props) {
  const toast = useToast();
  const formId = useId();
  const [today] = useState(pakistanToday);
  const [fields, setFields] = useState(() => fieldsOf(movement, type, today));
  const [file, setFile] = useState<File | null>(null);
  const [removeSaved, setRemoveSaved] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [shown, setShown] = useState<Errors>({});
  const repayment = fields.type === "Repayment";
  const choices = useMemo(() => cashAccountChoices(cashAccounts, fields.financeAccountId), [cashAccounts, fields.financeAccountId]);
  const set = (changes: Partial<Fields>, clear: (keyof Errors)[] = []) => {
    setFields((current) => ({ ...current, ...changes }));
    if (clear.length > 0) setShown((current) => ({ ...current, ...Object.fromEntries(clear.map((key) => [key, undefined])) }));
  };
  const principal = amount(fields.principal);
  const interest = repayment ? amount(fields.interest) : 0;
  const ready = (repayment ? fields.principal !== "" || fields.interest !== "" : fields.principal !== "") && fields.date !== "" && fields.financeAccountId !== "";

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (saving || !ready) return;
    const problems: Errors = {};
    if (!Number.isFinite(principal) || !Number.isFinite(interest)) problems.principal = "Enter valid amounts.";
    else if (!repayment && principal <= 0) problems.principal = "Enter the principal received.";
    else if (repayment && principal + interest <= 0) problems.principal = "A repayment needs principal, interest or both.";
    if (fields.date > today) problems.date = "The date cannot be in the future.";
    if (!fields.financeAccountId) problems.account = "Choose the cash or bank account used.";
    setShown(problems);
    if (Object.values(problems).some(Boolean)) return;

    setSaving(true);
    setError(null);
    const body = new FormData();
    body.append("type", fields.type);
    body.append("principalAmount", String(principal));
    body.append("interestAmount", String(interest));
    body.append("date", fields.date);
    body.append("financeAccountId", fields.financeAccountId);
    if (fields.reference.trim()) body.append("reference", fields.reference.trim());
    if (fields.note.trim()) body.append("note", fields.note.trim());
    if (file) body.append("attachment", file);
    try {
      if (movement) {
        body.append("concurrencyToken", movement.concurrencyToken);
        if (removeSaved) body.append("removeAttachment", "true");
        await loansApi.correctMovement(loan.id, movement.id, body);
        toast.success("Correction saved.");
      } else {
        // The same intent carries the same key, so a retry the operator cannot see the result of is recorded once.
        const signature = `loan:${loan.id}:${fields.type}:${principal}:${interest}:${fields.date}`;
        await loansApi.recordMovement(loan.id, body, keys.key(signature, "loan-movement"));
        keys.release(signature);
        toast.success(repayment ? "Repayment recorded." : "Loan funds recorded.");
      }
      onSaved();
      onClose();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "The loan movement could not be saved.");
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
      title={<DialogTitle title={titleFor(fields.type, movement !== null)} subtitle={movement ? `${loan.name} · ${movementDate(movement.date)}` : loan.name} />}
      primaryAction={{ label: movement ? "Save correction" : "Save", form: formId, loading: saving, disabled: !ready }}
    >
      <form id={formId} noValidate onSubmit={(event) => void submit(event)} className="flex flex-col gap-4">
        {error && <Notice tone="red" role="alert" title={error} />}
        {accountsError && <Notice tone="orange" role="alert" title={accountsError} />}
        <Dropdown
          label="Movement"
          required
          disabled={saving}
          options={TYPES}
          value={fields.type}
          onChange={(next) => set({ type: next as MovementType }, ["principal"])}
        />
        {repayment ? (
          <>
            <div className="grid gap-4 md:grid-cols-2">
              <NumberField label="Principal portion" prefix="Rs" decimals={2} disabled={saving} error={shown.principal} value={fields.principal} onChange={(value) => set({ principal: value }, ["principal"])} />
              <NumberField label="Interest portion" prefix="Rs" decimals={2} disabled={saving} value={fields.interest} onChange={(value) => set({ interest: value }, ["principal"])} />
            </div>
            <div className="flex items-center justify-between gap-3 rounded-field border border-line bg-page px-3 py-3">
              <span className="text-small font-bold text-ink-2">Total leaving the bank</span>
              <strong className="text-section font-extrabold tabular-nums text-ink">{rupees(Number.isFinite(principal + interest) ? principal + interest : 0)}</strong>
            </div>
          </>
        ) : (
          <NumberField label="Principal received" required prefix="Rs" decimals={2} disabled={saving} error={shown.principal} value={fields.principal} onChange={(value) => set({ principal: value }, ["principal"])} />
        )}
        <DatePicker label="Date" required disabled={saving} max={today} error={shown.date} value={fields.date} onChange={(date) => set({ date }, ["date"])} />
        <Dropdown
          label={repayment ? "Paid from account" : "Received in account"}
          required
          disabled={saving}
          placeholder="Select account"
          options={choices}
          error={shown.account}
          value={fields.financeAccountId}
          onChange={(financeAccountId) => set({ financeAccountId }, ["account"])}
        />
        <TextField label={optional("Reference")} maxLength={200} disabled={saving} value={fields.reference} onChange={(event) => set({ reference: event.target.value })} />
        <TextArea label={optional("Note")} rows={3} maxLength={1000} disabled={saving} value={fields.note} onChange={(event) => set({ note: event.target.value })} />
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
