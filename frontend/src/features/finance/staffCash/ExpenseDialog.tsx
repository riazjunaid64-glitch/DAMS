import { useId, useState, type FormEvent } from "react";
import ExpenseWhtFields from "../../../components/ExpenseWhtFields.tsx";
import { DatePicker, DialogTitle, Dropdown, Modal, Notice, NumberField, TextField, useToast } from "../../../components/ui";
import { pakistanToday } from "../../../lib/financePeriods.ts";
import type { useIdempotencyKeys } from "../../../lib/idempotency.ts";
import { categoryLabel, vendorLabel } from "../home/options.ts";
import { RecordProof } from "../home/proof.tsx";
import { emptyWht, type ExpenseCategory, type VendorOption, type WhtFormValue } from "../whtTypes.ts";
import { staffCashApi } from "./api.ts";
import type { Holder } from "./types.ts";

/** Distinct from a real vendor id, and from the empty string a cleared dropdown would give. */
const ONE_OFF_PAYEE = "__one_off__";

type Fields = {
  categoryId: string;
  projectId: string;
  /** The registered supplier, or ONE_OFF_PAYEE. */
  payee: string;
  /** The payee's name: the vendor's when one is picked, typed for a one-off payee. */
  vendor: string;
  amount: string;
  date: string;
  description: string;
  wht: WhtFormValue;
};

const optional = (label: string) => <>{label} <span className="font-normal normal-case tracking-normal text-ink-muted">(optional)</span></>;

type Props = {
  holder: Holder;
  categories: readonly ExpenseCategory[];
  vendors: readonly VendorOption[];
  projects: readonly { id: number; projectName: string }[];
  lookupsError: string | null;
  /** Held by the page, so a retry after the popup was closed and reopened still carries the same key. */
  keys: ReturnType<typeof useIdempotencyKeys>;
  onClose: () => void;
  onSaved: () => void;
};

/**
 * Money the person spent out of the float: an ordinary company expense with this float as the
 * paying account. The person is charged the net amount; the tax withheld stays in the company and
 * is owed to FBR. The rate depends on the supplier's filer status, which is why the vendor is picked
 * from the list and the shared tax box works the figure out.
 */
export function ExpenseDialog({ holder, categories, vendors, projects, lookupsError, keys, onClose, onSaved }: Props) {
  const toast = useToast();
  const formId = useId();
  const [today] = useState(pakistanToday);
  const [fields, setFields] = useState<Fields>({
    categoryId: "", projectId: "", payee: ONE_OFF_PAYEE, vendor: "", amount: "", date: today, description: "", wht: emptyWht(),
  });
  const [file, setFile] = useState<File | null>(null);
  const [reasonRequired, setReasonRequired] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [dateError, setDateError] = useState<string | undefined>();
  const amount = Number(fields.amount);
  const oneOff = fields.payee === ONE_OFF_PAYEE;
  const vendorId = oneOff ? "" : fields.payee;
  const ready = fields.categoryId !== "" && fields.amount !== "" && Number.isFinite(amount) && amount > 0 && fields.date !== "" && !reasonRequired;
  const set = (changes: Partial<Fields>) => setFields((current) => ({ ...current, ...changes }));

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (saving || !ready) return;
    if (fields.date > today) {
      setDateError("The date cannot be in the future.");
      return;
    }
    setSaving(true);
    setError(null);
    const body = new FormData();
    body.append("financeAccountId", String(holder.financeAccountId));
    body.append("amount", String(amount));
    body.append("date", fields.date);
    // The category carries the withholding rate; the server recomputes and refuses a figure that does not belong to it.
    body.append("categoryId", fields.categoryId);
    if (fields.projectId) body.append("projectId", fields.projectId);
    // The id is what carries filer status into the rate; the name is still sent so a one-off payee is named on the record.
    if (vendorId) body.append("vendorId", vendorId);
    if (fields.vendor.trim()) body.append("vendor", fields.vendor.trim());
    if (fields.wht.rate !== "") body.append("whtRate", fields.wht.rate);
    if (fields.wht.amount !== "") body.append("whtAmount", fields.wht.amount);
    if (fields.wht.overrideReason.trim()) body.append("whtOverrideReason", fields.wht.overrideReason.trim());
    if (fields.description.trim()) body.append("description", fields.description.trim());
    if (file) body.append("attachment", file);
    const signature = `staff-expense:${holder.financeAccountId}:${amount}:${fields.date}:${fields.categoryId}`;
    try {
      await staffCashApi.recordExpense(body, keys.key(signature, "expense"));
      keys.release(signature);
      toast.success("Expense recorded.");
      onSaved();
      onClose();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "Could not record this expense.");
      setSaving(false);
    }
  };

  const categoryOptions = categories
    .filter((category) => category.isActive)
    .map((category) => ({ value: String(category.id), label: categoryLabel(category) }));
  const projectOptions = [{ value: "", label: "General — no project" }, ...projects.map((project) => ({ value: String(project.id), label: project.projectName }))];
  const payeeOptions = [
    { value: ONE_OFF_PAYEE, label: "One-off payee" },
    ...vendors
      .filter((vendor) => vendor.isActive || String(vendor.id) === vendorId)
      .map((vendor) => ({ value: String(vendor.id), label: vendorLabel(vendor) })),
  ];

  // One column on a phone (amount and date straight after the category); on a desktop project and
  // payee side by side, then amount and date side by side, as drawn.
  return (
    <Modal
      open
      onClose={onClose}
      busy={saving}
      size="lg"
      phoneLayout="fullscreen"
      title={<DialogTitle title="Record an expense" subtitle={`Paid from ${holder.personName}'s float`} />}
      primaryAction={{ label: "Save", form: formId, loading: saving, disabled: !ready }}
    >
      <form id={formId} noValidate onSubmit={(event) => void submit(event)} className="grid grid-cols-1 gap-4 md:grid-cols-2">
        {error && <div className="md:col-span-2"><Notice tone="red" role="alert" title={error} /></div>}
        {lookupsError && <div className="md:col-span-2"><Notice tone="orange" role="alert" title={lookupsError} /></div>}
        <Dropdown
          className="order-1 md:col-span-2"
          label="Category"
          required
          disabled={saving}
          placeholder="Select a category"
          options={categoryOptions}
          value={fields.categoryId}
          onChange={(categoryId) => set({ categoryId })}
        />
        <Dropdown className="order-4 md:order-2" label="Project" disabled={saving} options={projectOptions} value={fields.projectId} onChange={(projectId) => set({ projectId })} />
        <Dropdown
          className="order-5 md:order-3"
          label="Paid to"
          disabled={saving}
          options={payeeOptions}
          value={fields.payee}
          // A different supplier can mean a different filer status, so the tax is worked out again.
          onChange={(payee) => set({
            payee,
            vendor: payee === ONE_OFF_PAYEE ? "" : vendors.find((vendor) => String(vendor.id) === payee)?.name ?? "",
            wht: emptyWht(),
          })}
        />
        {/* A registered supplier is already named by the picker; only a one-off payee needs typing. */}
        {oneOff && (
          <TextField className="order-6 md:order-4 md:col-span-2" label={optional("Payee name")} maxLength={200} disabled={saving} value={fields.vendor} onChange={(event) => set({ vendor: event.target.value })} />
        )}
        <NumberField className="order-2 md:order-5" label="Gross amount" required prefix="Rs" decimals={2} disabled={saving} value={fields.amount} onChange={(value) => set({ amount: value })} />
        <DatePicker
          className="order-3 md:order-6"
          label="Date"
          required
          disabled={saving}
          max={today}
          error={dateError}
          value={fields.date}
          onChange={(date) => { set({ date }); setDateError(undefined); }}
        />
        <TextField className="order-7 md:col-span-2" label={optional("What it was for")} maxLength={1000} disabled={saving} value={fields.description} onChange={(event) => set({ description: event.target.value })} />
        {fields.categoryId && (
          <div className="order-8 md:col-span-2">
            <ExpenseWhtFields
              categoryId={fields.categoryId}
              vendorId={vendorId}
              grossAmount={fields.amount}
              date={fields.date}
              excludeExpenseId={null}
              value={fields.wht}
              disabled={saving}
              onChange={(wht) => set({ wht })}
              onReasonRequired={setReasonRequired}
            />
          </div>
        )}
        <div className="order-9 md:col-span-2">
          <RecordProof saved={null} selected={file} disabled={saving} onSelected={setFile} onRemoveSaved={() => undefined} />
        </div>
      </form>
    </Modal>
  );
}
