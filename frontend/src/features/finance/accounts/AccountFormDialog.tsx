import { useId, useState, type FormEvent } from "react";
import { ConfirmDialog, Dropdown, Modal, Notice, NumberField, TextField, useToast } from "../../../components/ui";
import { DialogTitle } from "../../../components/ui/DialogTitle.tsx";
import { accountsApi } from "./api.ts";
import { TYPE_OPTIONS, openingChangeMessage, parseOpening, takesNoTypedOpening, typeValue, type Account } from "./accountGroups.ts";

type Fields = { name: string; type: string; holder: string; opening: string; ledgerCode: string; bankName: string; description: string };

const BLANK: Fields = { name: "", type: "1", holder: "", opening: "0", ledgerCode: "", bankName: "", description: "" };

const fieldsOf = (account: Account): Fields => ({
  name: account.name,
  type: String(typeValue(account.type)),
  holder: account.accountHolderName,
  opening: String(account.openingBalance),
  ledgerCode: account.ledgerCode ?? "",
  bankName: account.bankOrWalletName ?? "",
  description: account.description ?? "",
});

const optional = (label: string) => <>{label} <span className="font-normal text-ink-muted">(optional)</span></>;
const orNull = (value: string) => value.trim() || null;
const pair = "grid gap-4 md:grid-cols-2";

type Props = {
  /** The account being edited; null adds a new one. */
  account: Account | null;
  /** The go-live date from Finance settings; null until one is saved. */
  goLiveDate: string | null;
  onClose: () => void;
  onSaved: () => void;
};

/**
 * Add or edit an account. The opening balance is typed here and nowhere else: on the account's own
 * side (debit for cash, bank and assets, credit for liabilities and capital), and a minus sign means
 * the other side. Changing it asks first, because every report from the go-live date changes with it.
 */
export function AccountFormDialog({ account, goLiveDate, onClose, onSaved }: Props) {
  const toast = useToast();
  const formId = useId();
  const [fields, setFields] = useState<Fields>(() => (account ? fieldsOf(account) : BLANK));
  const [saving, setSaving] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const set = (changes: Partial<Fields>) => setFields((current) => ({ ...current, ...changes }));

  const system = account?.isSystemAccount ?? false;
  const openingLocked = account ? takesNoTypedOpening(account.systemRole) : false;
  const ready = fields.name.trim() !== "" && fields.holder.trim() !== "";
  const opening = parseOpening(fields.opening);
  const saved = account?.openingBalance ?? 0;
  // Edit: any change to the saved figure. Add: a non-zero figure, once a go-live date exists.
  const needsConfirm = account ? opening !== saved : opening !== 0 && goLiveDate !== null;

  const save = async (afterConfirm: boolean) => {
    if (saving) return;
    setSaving(true);
    setError(null);
    try {
      await accountsApi.save({
        name: fields.name.trim(),
        type: Number(fields.type),
        accountHolderName: fields.holder.trim(),
        openingBalance: opening,
        ledgerCode: orNull(fields.ledgerCode),
        // The server writes the order on every save, so Edit sends the saved one back; Add starts at 0.
        displayOrder: account?.displayOrder ?? 0,
        bankOrWalletName: orNull(fields.bankName),
        description: orNull(fields.description),
        concurrencyToken: account?.concurrencyToken ?? "",
      }, account?.id ?? null);
      toast.success(!account ? "Account added." : afterConfirm ? "Opening balance changed." : "Account saved.");
      onSaved();
      onClose();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "The account could not be saved.");
      setConfirming(false);
      setSaving(false);
    }
  };

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (saving || !ready) return;
    if (needsConfirm) setConfirming(true);
    else void save(false);
  };

  return (
    <>
      <Modal
        open
        onClose={onClose}
        busy={saving}
        size="md"
        phoneLayout="fullscreen"
        title={account ? <DialogTitle title="Edit account" subtitle={system ? account.name : undefined} /> : "Add account"}
        primaryAction={{ label: "Save", form: formId, loading: saving && !confirming, disabled: !ready }}
      >
        <form id={formId} noValidate onSubmit={submit} className="flex flex-col gap-4">
          {error && <Notice tone="red" role="alert" title={error} />}
          {system && <Notice tone="gold" title="System account: the name, type and ledger code can't be changed." />}
          <TextField
            label="Account name"
            required
            maxLength={120}
            placeholder="e.g. Meezan Bank — Current"
            disabled={saving || system}
            value={fields.name}
            onChange={(event) => set({ name: event.target.value })}
          />
          <div className={pair}>
            <Dropdown label="Account type" required disabled={saving || system} options={TYPE_OPTIONS} value={fields.type} onChange={(type) => set({ type })} />
            <TextField label="Account holder" required maxLength={150} disabled={saving} value={fields.holder} onChange={(event) => set({ holder: event.target.value })} />
          </div>
          <div className={pair}>
            <NumberField
              label="Opening balance"
              prefix="Rs"
              decimals={2}
              allowNegative
              disabled={saving || openingLocked}
              helper={openingLocked ? "Worked out from bookings, not typed." : undefined}
              value={fields.opening}
              onChange={(opening) => set({ opening })}
            />
            <TextField label={optional("Ledger code")} maxLength={30} disabled={saving || system} value={fields.ledgerCode} onChange={(event) => set({ ledgerCode: event.target.value })} />
          </div>
          <TextField label={optional("Bank or wallet name")} maxLength={150} disabled={saving} value={fields.bankName} onChange={(event) => set({ bankName: event.target.value })} />
          <TextField label={optional("Description")} maxLength={1000} disabled={saving} value={fields.description} onChange={(event) => set({ description: event.target.value })} />
        </form>
      </Modal>
      <ConfirmDialog
        open={confirming}
        onClose={() => !saving && setConfirming(false)}
        onConfirm={() => void save(true)}
        title="Change the opening balance?"
        message={openingChangeMessage(fields.name.trim() || "This account", saved, opening, goLiveDate)}
        confirmLabel="Change"
        loading={saving}
      />
    </>
  );
}
