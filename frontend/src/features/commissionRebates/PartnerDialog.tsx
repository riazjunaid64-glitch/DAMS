import { useId, useState, type FormEvent } from "react";
import { Dropdown, Modal, Notice, TextField, useToast } from "../../components/ui";
import { commissionRebateApi } from "./api.ts";
import { PARTNER_TYPES } from "./forms.ts";
import type { Partner } from "./types.ts";

type Props = {
  onClose: () => void;
  /** Called with the saved partner so the popup underneath can select it. */
  onSaved: (partner: Partner) => void;
};

/**
 * New partner, opened on top of Add commission. Only what a commission needs; bank details and the
 * other partner fields stay on Finance → Commission & rebates → Partners.
 */
export function PartnerDialog({ onClose, onSaved }: Props) {
  const toast = useToast();
  const formId = useId();
  const [fields, setFields] = useState({ name: "", partnerType: "Agency", phone: "", email: "" });
  const [shown, setShown] = useState<{ name?: string }>({});
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (saving) return;
    if (!fields.name.trim()) {
      setShown({ name: "Enter the partner's name." });
      return;
    }
    setSaving(true);
    setError(null);
    try {
      const partner = await commissionRebateApi.savePartner({
        name: fields.name.trim(), partnerType: fields.partnerType, phone: fields.phone.trim() || null, email: fields.email.trim() || null,
      });
      toast.success("Partner saved.");
      onSaved(partner);
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
      size="sm"
      phoneLayout="fullscreen"
      title="New partner"
      primaryAction={{ label: "Save partner", form: formId, loading: saving }}
    >
      <form id={formId} noValidate onSubmit={(event) => void submit(event)} className="flex flex-col gap-4">
        {error && <Notice tone="red" role="alert" title={error} />}
        <TextField
          label="Name"
          required
          maxLength={200}
          disabled={saving}
          error={shown.name}
          value={fields.name}
          onChange={(event) => { setFields({ ...fields, name: event.target.value }); setShown({}); }}
        />
        <Dropdown label="Type" required disabled={saving} options={PARTNER_TYPES} value={fields.partnerType} onChange={(partnerType) => setFields({ ...fields, partnerType })} />
        <TextField label="Phone" type="tel" maxLength={50} disabled={saving} value={fields.phone} onChange={(event) => setFields({ ...fields, phone: event.target.value })} />
        <TextField label="Email" type="email" maxLength={200} disabled={saving} value={fields.email} onChange={(event) => setFields({ ...fields, email: event.target.value })} />
      </form>
    </Modal>
  );
}
