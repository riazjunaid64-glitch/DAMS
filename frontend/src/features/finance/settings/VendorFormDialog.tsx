import { useId, useState, type FormEvent } from "react";
import { DialogTitle, Dropdown, Modal, Notice, OptionalLabel, TextField, Toggle, useToast } from "../../../components/ui";
import { formatDay } from "../../../lib/dates.ts";
import { paymentsText } from "../lists.ts";
import * as whtApi from "../whtApi.ts";
import { FILER_STATUSES, type FilerStatus, type Vendor } from "../whtTypes.ts";
import { vendorBody, vendorFields, type VendorFields } from "./rules.ts";

const STATUS_OPTIONS = FILER_STATUSES.map(([value, label]) => ({ value, label }));
const pair = "grid gap-4 md:grid-cols-2";
const toggleBox = "rounded-field border border-line-input px-3";

type Props = {
  /** The vendor being edited; null adds a new one. */
  vendor: Vendor | null;
  onClose: () => void;
  onSaved: () => void;
};

/**
 * Add or edit a vendor. The filer status picks the rate for expenses entered from now on; each expense
 * keeps the status it was taxed under. Saving a vendor also links earlier payments typed with the
 * same name to it, which is why "Paid this year" can jump after adding one.
 */
export function VendorFormDialog({ vendor, onClose, onSaved }: Props) {
  const toast = useToast();
  const formId = useId();
  const [fields, setFields] = useState<VendorFields>(() => vendorFields(vendor));
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const set = (changes: Partial<VendorFields>) => setFields((current) => ({ ...current, ...changes }));
  const ready = fields.name.trim() !== "";
  const used = vendor !== null && vendor.paymentCount > 0;

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (saving || !ready) return;
    setSaving(true);
    setError(null);
    try {
      await whtApi.saveVendor(vendor?.id ?? null, vendorBody(fields, vendor));
      toast.success(vendor ? "Vendor saved." : "Vendor added.");
      onSaved();
      onClose();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "The vendor could not be saved.");
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
      title={vendor ? <DialogTitle title="Edit vendor" subtitle={used ? `Used by ${paymentsText(vendor.paymentCount)}` : undefined} /> : "Add vendor"}
      primaryAction={{ label: "Save", form: formId, loading: saving, disabled: !ready }}
    >
      <form id={formId} noValidate onSubmit={(event) => void submit(event)} className="flex flex-col gap-4">
        {error && <Notice tone="red" role="alert" title={error} />}
        <TextField label="Vendor name" required maxLength={200} disabled={saving} value={fields.name} onChange={(event) => set({ name: event.target.value })} />
        <Dropdown
          label="Filer status"
          required
          disabled={saving}
          options={STATUS_OPTIONS}
          value={fields.filerStatus}
          onChange={(filerStatus) => set({ filerStatus: filerStatus as FilerStatus })}
        />
        <div className="flex flex-col gap-2">
          <Toggle label="Checked on the FBR taxpayer list today" disabled={saving} checked={fields.checkedToday} onChange={(checkedToday) => set({ checkedToday })} className={toggleBox} />
          {vendor?.filerStatusCheckedAt && <p className="m-0 text-small text-ink-muted">Last checked {formatDay(vendor.filerStatusCheckedAt)}</p>}
        </div>
        <div className={pair}>
          <TextField label={<OptionalLabel>NTN</OptionalLabel>} maxLength={30} disabled={saving} value={fields.ntn} onChange={(event) => set({ ntn: event.target.value })} />
          <TextField label={<OptionalLabel>CNIC</OptionalLabel>} maxLength={20} disabled={saving} value={fields.cnic} onChange={(event) => set({ cnic: event.target.value })} />
        </div>
        <div className={pair}>
          <TextField label={<OptionalLabel>Phone</OptionalLabel>} maxLength={50} disabled={saving} value={fields.phone} onChange={(event) => set({ phone: event.target.value })} />
          <TextField label={<OptionalLabel>Address</OptionalLabel>} maxLength={500} disabled={saving} value={fields.address} onChange={(event) => set({ address: event.target.value })} />
        </div>
        <TextField label={<OptionalLabel>Notes</OptionalLabel>} maxLength={1000} disabled={saving} value={fields.notes} onChange={(event) => set({ notes: event.target.value })} />
        {vendor && <Toggle label="Active" disabled={saving} checked={fields.isActive} onChange={(isActive) => set({ isActive })} className={toggleBox} />}
      </form>
    </Modal>
  );
}
