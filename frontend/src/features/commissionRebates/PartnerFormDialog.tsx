import { useId, useState, type FormEvent, type ReactNode } from "react";
import { Dropdown, Modal, Notice, TextArea, TextField, useToast } from "../../components/ui";
import { commissionRebateApi } from "./api.ts";
import { PARTNER_TYPES } from "./forms.ts";
import type { Partner } from "./types.ts";

type Fields = {
  name: string; partnerType: string; internalCode: string; contactPerson: string; phone: string; email: string; address: string;
  cnic: string; ntn: string; registrationNumber: string;
  bankName: string; accountTitle: string; accountNumber: string; iban: string;
  notes: string;
};

const BLANK: Fields = {
  name: "", partnerType: "Broker", internalCode: "", contactPerson: "", phone: "", email: "", address: "",
  cnic: "", ntn: "", registrationNumber: "", bankName: "", accountTitle: "", accountNumber: "", iban: "", notes: "",
};

const fieldsOf = (partner: Partner): Fields => ({
  name: partner.name, partnerType: partner.partnerType, internalCode: partner.internalCode, contactPerson: partner.contactPerson ?? "",
  phone: partner.phone ?? "", email: partner.email ?? "", address: partner.address ?? "", cnic: partner.cnic ?? "", ntn: partner.ntn ?? "",
  registrationNumber: partner.registrationNumber ?? "", bankName: partner.bankName ?? "", accountTitle: partner.accountTitle ?? "",
  accountNumber: partner.accountNumber ?? "", iban: partner.iban ?? "", notes: partner.notes ?? "",
});

const optional = (label: string): ReactNode => <>{label} <span className="font-normal text-ink-muted">(optional)</span></>;
const orNull = (value: string) => value.trim() || null;
const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

function Group({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="flex flex-col gap-4">
      <h3 className="m-0 text-label font-extrabold uppercase tracking-[0.5px] text-gold-text">{title}</h3>
      {children}
    </section>
  );
}

const pair = "grid gap-4 md:grid-cols-2";

type Props = {
  /** The partner being edited; null adds a new one. */
  partner: Partner | null;
  onClose: () => void;
  /** Called once the save went through, so the list and the cards can reload. */
  onSaved: () => void;
};

/**
 * Add or edit a partner: the full directory record, in four groups. Not the same as the four-field
 * New partner popup that opens on top of Add commission. The code is made by the server when it is
 * left empty on Add, and is required on Edit.
 */
export function PartnerFormDialog({ partner, onClose, onSaved }: Props) {
  const toast = useToast();
  const formId = useId();
  const [fields, setFields] = useState<Fields>(() => (partner ? fieldsOf(partner) : BLANK));
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [emailError, setEmailError] = useState<string | undefined>();
  const set = (changes: Partial<Fields>) => setFields((current) => ({ ...current, ...changes }));
  const text = (key: keyof Fields, label: ReactNode, maxLength: number, extra: { required?: boolean; type?: string; placeholder?: string; error?: string } = {}) => (
    <TextField
      label={label}
      maxLength={maxLength}
      disabled={saving}
      value={fields[key]}
      onChange={(event) => { set({ [key]: event.target.value }); if (key === "email") setEmailError(undefined); }}
      {...extra}
    />
  );

  const ready = fields.name.trim() !== "" && (!partner || fields.internalCode.trim() !== "");

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (saving || !ready) return;
    if (fields.email.trim() && !EMAIL.test(fields.email.trim())) {
      setEmailError("Enter a valid email address.");
      return;
    }
    setSaving(true);
    setError(null);
    try {
      await commissionRebateApi.savePartner({
        name: fields.name.trim(),
        partnerType: fields.partnerType,
        internalCode: orNull(fields.internalCode),
        contactPerson: orNull(fields.contactPerson),
        phone: orNull(fields.phone),
        email: orNull(fields.email),
        address: orNull(fields.address),
        cnic: orNull(fields.cnic),
        ntn: orNull(fields.ntn),
        registrationNumber: orNull(fields.registrationNumber),
        bankName: orNull(fields.bankName),
        accountTitle: orNull(fields.accountTitle),
        accountNumber: orNull(fields.accountNumber),
        iban: orNull(fields.iban),
        notes: orNull(fields.notes),
        ...(partner ? { concurrencyToken: partner.concurrencyToken } : {}),
      }, partner?.id);
      toast.success("Partner saved.");
      onSaved();
      onClose();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "The partner could not be saved.");
      setSaving(false);
    }
  };

  const count = partner?.commissionCount ?? 0;
  const title = partner ? (
    <>
      Edit partner
      <span className="mt-0.5 block text-body font-normal text-ink-muted">{partner.name} · {count} {count === 1 ? "commission" : "commissions"}</span>
    </>
  ) : "Add partner";

  return (
    <Modal
      open
      onClose={onClose}
      busy={saving}
      size="lg"
      phoneLayout="fullscreen"
      title={title}
      primaryAction={{ label: "Save", form: formId, loading: saving, disabled: !ready }}
    >
      <form id={formId} noValidate onSubmit={(event) => void submit(event)} className="flex flex-col gap-6">
        {error && <Notice tone="red" role="alert" title={error} />}
        <Group title="Partner">
          <div className={pair}>
            {text("name", "Name", 200, { required: true })}
            <Dropdown label="Type" required disabled={saving} options={PARTNER_TYPES} value={fields.partnerType} onChange={(partnerType) => set({ partnerType })} />
          </div>
          <div className={pair}>
            {partner
              ? text("internalCode", "Partner code", 80, { required: true })
              : text("internalCode", optional("Partner code"), 80, { placeholder: "Made automatically" })}
            {text("contactPerson", optional("Contact person"), 200)}
          </div>
          <div className={pair}>
            {text("phone", optional("Phone"), 50, { type: "tel" })}
            {text("email", optional("Email"), 200, { type: "email", error: emailError })}
          </div>
          <TextArea label={optional("Address")} rows={3} maxLength={500} disabled={saving} value={fields.address} onChange={(event) => set({ address: event.target.value })} />
        </Group>
        <Group title="Tax">
          <div className={pair}>
            {text("cnic", optional("CNIC"), 50)}
            {text("ntn", optional("NTN / tax number"), 80)}
          </div>
          {text("registrationNumber", optional("Registration number"), 100)}
        </Group>
        <Group title="Bank">
          <div className={pair}>
            {text("bankName", optional("Bank name"), 150)}
            {text("accountTitle", optional("Account title"), 150)}
          </div>
          <div className={pair}>
            {text("accountNumber", optional("Account number"), 100)}
            {text("iban", optional("IBAN"), 100)}
          </div>
        </Group>
        <Group title="Notes">
          <TextArea label={optional("Notes")} rows={3} maxLength={2000} disabled={saving} value={fields.notes} onChange={(event) => set({ notes: event.target.value })} />
        </Group>
      </form>
    </Modal>
  );
}
