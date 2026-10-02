import { useId, useMemo, useState, type FormEvent } from "react";
import { DatePicker, Dropdown, Modal, Notice, TextField, useToast } from "../../../components/ui";
import { DialogTitle } from "../../bookings/DialogTitle.tsx";
import { capitalApi } from "./api.ts";
import { capitalAccountChoices, serverDay } from "./rules.ts";
import type { AccountOption, Partner } from "./types.ts";

type Fields = { name: string; cnic: string; ntn: string; financeAccountId: string; joinedDate: string; exitedDate: string };

const BLANK: Fields = { name: "", cnic: "", ntn: "", financeAccountId: "", joinedDate: "", exitedDate: "" };

const fieldsOf = (partner: Partner): Fields => ({
  name: partner.name,
  cnic: partner.cnic ?? "",
  ntn: partner.ntn ?? "",
  financeAccountId: partner.financeAccountId === null ? "" : String(partner.financeAccountId),
  joinedDate: serverDay(partner.joinedDate) ?? "",
  exitedDate: serverDay(partner.exitedDate) ?? "",
});

const optional = (label: string) => <>{label} <span className="font-normal text-ink-muted">(optional)</span></>;
const orNull = (value: string) => value.trim() || null;
const pair = "grid gap-4 md:grid-cols-2";

type Props = {
  /** The partner being edited; null adds a new one. */
  partner: Partner | null;
  partners: readonly Partner[];
  /** Every account, from which the Capital ones are offered. */
  accounts: readonly AccountOption[];
  accountsError: string | null;
  onClose: () => void;
  onSaved: () => void;
};

/**
 * Add or edit a partner. There is no share field: a new partner is saved with share 0 and inactive,
 * and the owner sets the share in Edit shares. Edit sends the partner's saved share, Active setting
 * and token back unchanged, because the server writes all of them on every save.
 */
export function PartnerFormDialog({ partner, partners, accounts, accountsError, onClose, onSaved }: Props) {
  const toast = useToast();
  const formId = useId();
  const [fields, setFields] = useState<Fields>(() => (partner ? fieldsOf(partner) : BLANK));
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const set = (changes: Partial<Fields>) => setFields((current) => ({ ...current, ...changes }));
  const choices = useMemo(() => capitalAccountChoices(accounts, partners, partner), [accounts, partners, partner]);
  const noAccounts = !accountsError && choices.length === 0;
  const ready = fields.name.trim() !== "";
  // The picker already refuses days before Joined; this catches Joined being moved past an Exited date.
  const exitError = fields.joinedDate && fields.exitedDate && fields.exitedDate < fields.joinedDate ? "Exit date cannot be before joined date." : undefined;

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (saving || !ready || exitError) return;
    setSaving(true);
    setError(null);
    try {
      await capitalApi.savePartner({
        name: fields.name.trim(),
        cnic: orNull(fields.cnic),
        ntn: orNull(fields.ntn),
        profitSharePercent: partner?.profitSharePercent ?? 0,
        financeAccountId: fields.financeAccountId === "" ? null : Number(fields.financeAccountId),
        isActive: partner?.isActive ?? false,
        joinedDate: fields.joinedDate || null,
        exitedDate: fields.exitedDate || null,
        concurrencyToken: partner?.concurrencyToken ?? null,
      }, partner?.id ?? null);
      toast.success("Partner saved.");
      onSaved();
      onClose();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "The partner could not be saved.");
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
      title={partner ? <DialogTitle title="Edit partner" subtitle={partner.name} /> : "Add partner"}
      primaryAction={{ label: "Save partner", form: formId, loading: saving, disabled: !ready }}
    >
      <form id={formId} noValidate onSubmit={(event) => void submit(event)} className="flex flex-col gap-4">
        {error && <Notice tone="red" role="alert" title={error} />}
        {accountsError && <Notice tone="orange" role="alert" title={accountsError} />}
        <TextField label="Name" required maxLength={200} disabled={saving} value={fields.name} onChange={(event) => set({ name: event.target.value })} />
        <div className={pair}>
          <TextField label={optional("CNIC")} maxLength={20} disabled={saving} value={fields.cnic} onChange={(event) => set({ cnic: event.target.value })} />
          <TextField label={optional("NTN")} maxLength={30} disabled={saving} value={fields.ntn} onChange={(event) => set({ ntn: event.target.value })} />
        </div>
        <Dropdown
          label={optional("Capital account")}
          disabled={saving}
          placeholder="Select account"
          options={[{ value: "", label: "Select account" }, ...choices]}
          value={fields.financeAccountId}
          onChange={(financeAccountId) => set({ financeAccountId })}
          helper={noAccounts ? "Add a Capital account in Manage accounts first." : undefined}
        />
        <div className={pair}>
          <DatePicker label={optional("Joined date")} disabled={saving} value={fields.joinedDate} onChange={(joinedDate) => set({ joinedDate })} />
          <DatePicker label={optional("Exited date")} disabled={saving} min={fields.joinedDate || undefined} error={exitError} value={fields.exitedDate} onChange={(exitedDate) => set({ exitedDate })} />
        </div>
      </form>
    </Modal>
  );
}
