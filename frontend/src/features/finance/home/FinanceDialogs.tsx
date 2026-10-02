import { useRef } from "react";
import type { FinanceAttachmentInfo, FinanceRecordKind } from "../../../api/financeAttachments.ts";
import { openFinanceAttachment } from "../../../api/financeAttachments.ts";
import {
  AttachProof,
  Button,
  DatePicker,
  Dropdown,
  Modal,
  Notice,
  NumberField,
  TextField,
  useToast,
} from "../../../components/ui";
import ExpenseWhtFields from "../../../components/ExpenseWhtFields.tsx";
import { pakistanToday } from "../../../lib/financePeriods.ts";
import { emptyWht, filerLabel, type ExpenseCategory, type VendorOption } from "../whtTypes.ts";
import {
  CANCELLATION_REVENUE_CODE,
  isStaffFloat,
  type AssetPurchaseFormState,
  type ExpenseFormState,
  type FinanceAccountOption,
  type RevenueCategory,
  type RevenueFormState,
} from "./types.ts";

const today = pakistanToday;

export function accountLabel(account: FinanceAccountOption, staffNote = false) {
  const inactive = account.isActive ? "" : " (Inactive)";
  const staff = staffNote && isStaffFloat(account) ? " · Staff float" : "";
  return `${account.name} — ${account.accountHolderName}${staff}${inactive}`;
}

export function cashAccounts(
  accounts: FinanceAccountOption[],
  opts: { staff: boolean; selectedId: string },
) {
  return accounts
    .filter((account) => (opts.staff || !isStaffFloat(account)) && (account.isActive || String(account.id) === opts.selectedId))
    .map((account) => ({ value: String(account.id), label: accountLabel(account, opts.staff) }));
}

function ProofField({
  kind,
  recordId,
  attachment,
  file,
  removed,
  onPick,
  onRemove,
  onUndo,
}: {
  kind: FinanceRecordKind;
  recordId: number | null;
  attachment: FinanceAttachmentInfo | null;
  file: File | null;
  removed: boolean;
  onPick: (file: File) => void;
  onRemove: () => void;
  onUndo: () => void;
}) {
  const toast = useToast();
  const inputRef = useRef<HTMLInputElement>(null);
  const open = async (download: boolean) => {
    if (!attachment || recordId == null) return;
    try {
      await openFinanceAttachment(kind, recordId, attachment.fileName, download);
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "The attachment could not be opened.");
    }
  };

  if (file) {
    return (
      <AttachProof
        label="Attachment"
        file={{ name: file.name, size: file.size, uploaded: false }}
        onPick={onPick}
        onRemove={onRemove}
      />
    );
  }
  if (attachment && !removed) {
    return (
      <div className="flex flex-col gap-2">
        <p className="m-0 text-sm font-extrabold text-ink">{attachment.fileName}</p>
        <div className="flex flex-wrap gap-3">
          <Button type="button" variant="link" onClick={() => void open(false)}>View</Button>
          <Button type="button" variant="link" onClick={() => void open(true)}>Download</Button>
          <Button type="button" variant="link" onClick={() => inputRef.current?.click()}>Replace</Button>
          <Button type="button" variant="link" onClick={onRemove}>Remove</Button>
        </div>
        <input ref={inputRef} type="file" className="sr-only" onChange={(event) => {
          const picked = event.target.files?.[0];
          if (picked) onPick(picked);
          event.target.value = "";
        }} />
      </div>
    );
  }
  return (
    <div className="flex flex-col gap-2">
      {removed && (
        <Button type="button" variant="link" onClick={onUndo}>Undo</Button>
      )}
      <AttachProof label="Attachment" file={null} onPick={onPick} onRemove={() => {}} />
    </div>
  );
}

function projectOptions(projects: { id: number; projectName: string }[]) {
  return [
    { value: "", label: "General (no specific project)" },
    ...projects.map((project) => ({ value: String(project.id), label: project.projectName })),
  ];
}

function vendorChoices(vendors: VendorOption[], selectedId: string, oneOff: string) {
  return [
    { value: "", label: oneOff },
    ...vendors
      .filter((vendor) => vendor.isActive || String(vendor.id) === selectedId)
      .map((vendor) => ({ value: String(vendor.id), label: `${vendor.name} — ${filerLabel(vendor.filerStatus)}` })),
  ];
}

export function RevenueDialog({
  form,
  setForm,
  projects,
  accounts,
  categories,
  categoriesLoading,
  saving,
  error,
  onClose,
  onSave,
}: {
  form: RevenueFormState;
  setForm: (next: RevenueFormState) => void;
  projects: { id: number; projectName: string }[];
  accounts: FinanceAccountOption[];
  categories: RevenueCategory[];
  categoriesLoading: boolean;
  saving: boolean;
  error: string | null;
  onClose: () => void;
  onSave: () => void;
}) {
  const set = (patch: Partial<RevenueFormState>) => setForm({ ...form, ...patch });
  const chosen = categories.find((category) => String(category.id) === form.revenueCategoryId);
  const categoryOptions = [
    ...(categoriesLoading ? [{ value: "", label: "Loading categories…", disabled: true }] : [{ value: "", label: "Select a category", disabled: true }]),
    ...categories
      .filter((category) => category.isActive || String(category.id) === form.revenueCategoryId)
      .map((category) => ({
        value: String(category.id),
        label: category.isActive ? category.name : `${category.name} (Retired)`,
      })),
  ];
  const ready = Boolean(form.financeAccountId && form.revenueCategoryId && Number(form.amount) > 0);
  return (
    <Modal
      open
      busy={saving}
      size="md"
      phoneLayout="fullscreen"
      title={form.id ? "Edit manual revenue" : "Add manual revenue"}
      onClose={onClose}
      primaryAction={{ label: "Save", onClick: onSave, disabled: !ready || saving, loading: saving }}
    >
      <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
        {error && <Notice tone="red" title={error} className="md:col-span-2" />}
        <Dropdown
          className="md:col-span-2"
          label="Received in account"
          required
          placeholder="Select account"
          options={cashAccounts(accounts, { staff: false, selectedId: form.financeAccountId })}
          value={form.financeAccountId}
          onChange={(value) => set({ financeAccountId: value })}
        />
        <Dropdown label="Project" options={projectOptions(projects)} value={form.projectId} onChange={(value) => set({ projectId: value })} />
        <Dropdown
          label="Revenue category"
          required
          options={categoryOptions}
          value={form.revenueCategoryId}
          onChange={(value) => {
            const category = categories.find((item) => String(item.id) === value);
            set({ revenueCategoryId: value, revenueType: category?.name ?? form.revenueType });
          }}
        />
        {chosen?.code === CANCELLATION_REVENUE_CODE && (
          <Notice
            tone="orange"
            className="md:col-span-2"
            title="Only for a forfeiture from outside DAMS — before go-live, or on something that was never a booking here. Cancelling a booking in DAMS records the retained amount as income by itself, so entering it again here would count it twice."
          />
        )}
        <NumberField label="Amount" required prefix="Rs" value={form.amount} onChange={(amount) => set({ amount })} />
        <DatePicker label="Date" required max={today()} value={form.date} onChange={(date) => set({ date })} />
        <TextField label="Reference" value={form.reference} onChange={(event) => set({ reference: event.target.value })} />
        <TextField label="Description" value={form.description} onChange={(event) => set({ description: event.target.value })} />
        <div className="md:col-span-2">
          <ProofField
            kind="revenue"
            recordId={form.id}
            attachment={form.attachment}
            file={form.selectedAttachment}
            removed={form.removeAttachment}
            onPick={(file) => set({ selectedAttachment: file, removeAttachment: false })}
            onRemove={() => set({ selectedAttachment: null, removeAttachment: true })}
            onUndo={() => set({ removeAttachment: false })}
          />
        </div>
      </div>
    </Modal>
  );
}

export function ExpenseDialog({
  form,
  setForm,
  projects,
  accounts,
  categories,
  vendors,
  saving,
  taxBlocksSave,
  onTaxBlocksSave,
  error,
  onClose,
  onSave,
}: {
  form: ExpenseFormState;
  setForm: (next: ExpenseFormState) => void;
  projects: { id: number; projectName: string }[];
  accounts: FinanceAccountOption[];
  categories: ExpenseCategory[];
  vendors: VendorOption[];
  saving: boolean;
  taxBlocksSave: boolean;
  onTaxBlocksSave: (blocked: boolean) => void;
  error: string | null;
  onClose: () => void;
  onSave: () => void;
}) {
  const set = (patch: Partial<ExpenseFormState>) => setForm({ ...form, ...patch });
  const categoryOptions = [
    { value: "", label: "Select a category", disabled: true },
    ...(form.legacyCategory ? [{ value: "__legacy__", label: `Keep the original text — ${form.category}` }] : []),
    ...categories
      .filter((category) => category.isActive || String(category.id) === form.categoryId)
      .map((category) => ({
        value: String(category.id),
        label: `${category.name}${category.taxSection ? ` — s.${category.taxSection}` : ""}${category.isActive ? "" : " (Retired)"}`,
      })),
  ];
  const categoryValue = form.legacyCategory && !form.categoryId ? "__legacy__" : form.categoryId;
  const ready = Boolean(form.financeAccountId && (form.categoryId || form.legacyCategory) && Number(form.amount) > 0) && !taxBlocksSave;
  return (
    <Modal
      open
      busy={saving}
      size="lg"
      phoneLayout="fullscreen"
      title={form.id ? "Edit expense" : "Add expense"}
      onClose={onClose}
      primaryAction={{ label: "Save", onClick: onSave, disabled: !ready || saving, loading: saving }}
    >
      <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
        {error && <Notice tone="red" title={error} className="md:col-span-2" />}
        <Dropdown
          className="md:col-span-2"
          label="Paid from account"
          required
          placeholder="Select account"
          options={cashAccounts(accounts, { staff: true, selectedId: form.financeAccountId })}
          value={form.financeAccountId}
          onChange={(value) => set({ financeAccountId: value })}
        />
        <Dropdown
          className="md:col-span-2"
          label="Category"
          required
          options={categoryOptions}
          value={categoryValue}
          onChange={(value) => {
            if (value === "__legacy__") set({ categoryId: "", legacyCategory: true, wht: emptyWht() });
            else {
              const category = categories.find((item) => String(item.id) === value);
              set({ categoryId: value, legacyCategory: false, category: category?.name ?? "", wht: emptyWht() });
            }
          }}
        />
        {form.legacyCategory && !form.categoryId && (
          <TextField
            className="md:col-span-2"
            label="Original category text"
            value={form.category}
            onChange={(event) => set({ category: event.target.value })}
          />
        )}
        <Dropdown label="Project" options={projectOptions(projects)} value={form.projectId} onChange={(value) => set({ projectId: value })} />
        <Dropdown
          label="Vendor"
          options={vendorChoices(vendors, form.vendorId, "One-off payee")}
          value={form.vendorId}
          onChange={(value) => set({ vendorId: value, ...(value ? { vendor: "" } : {}) })}
        />
        {!form.vendorId && (
          <TextField
            className="md:col-span-2"
            label="Vendor / reference (optional)"
            value={form.vendor}
            onChange={(event) => set({ vendor: event.target.value })}
          />
        )}
        <NumberField label="Gross amount" required prefix="Rs" value={form.amount} onChange={(amount) => set({ amount })} />
        <DatePicker label="Date" required max={today()} value={form.date} onChange={(date) => set({ date })} />
        <div className="md:col-span-2">
          <ExpenseWhtFields
            categoryId={form.categoryId}
            vendorId={form.vendorId}
            grossAmount={form.amount}
            date={form.date}
            excludeExpenseId={form.id}
            value={form.wht}
            onChange={(wht) => set({ wht })}
            disabled={saving}
            onBlocksSave={onTaxBlocksSave}
          />
        </div>
        <TextField className="md:col-span-2" label="Description" value={form.description} onChange={(event) => set({ description: event.target.value })} />
        <div className="md:col-span-2">
          <ProofField
            kind="expense"
            recordId={form.id}
            attachment={form.attachment}
            file={form.selectedAttachment}
            removed={form.removeAttachment}
            onPick={(file) => set({ selectedAttachment: file, removeAttachment: false })}
            onRemove={() => set({ selectedAttachment: null, removeAttachment: true })}
            onUndo={() => set({ removeAttachment: false })}
          />
        </div>
      </div>
    </Modal>
  );
}

export function AssetDialog({
  form,
  setForm,
  projects,
  accounts,
  assetAccounts,
  categories,
  vendors,
  saving,
  taxBlocksSave,
  onTaxBlocksSave,
  error,
  onClose,
  onSave,
}: {
  form: AssetPurchaseFormState;
  setForm: (next: AssetPurchaseFormState) => void;
  projects: { id: number; projectName: string }[];
  accounts: FinanceAccountOption[];
  assetAccounts: FinanceAccountOption[];
  categories: ExpenseCategory[];
  vendors: VendorOption[];
  saving: boolean;
  taxBlocksSave: boolean;
  onTaxBlocksSave: (blocked: boolean) => void;
  error: string | null;
  onClose: () => void;
  onSave: () => void;
}) {
  const set = (patch: Partial<AssetPurchaseFormState>) => setForm({ ...form, ...patch });
  const known = assetAccounts.some((account) => String(account.id) === form.assetAccountId);
  const assetOptions = [
    { value: "", label: "Select a fixed asset account", disabled: true },
    ...(!known && form.assetAccountId ? [{ value: form.assetAccountId, label: `${form.assetAccountName} (as recorded)` }] : []),
    ...assetAccounts
      .filter((account) => account.isActive || String(account.id) === form.assetAccountId)
      .map((account) => ({ value: String(account.id), label: accountLabel(account) })),
  ];
  const categoryOptions = [
    { value: "", label: "Select a category", disabled: true },
    ...categories
      .filter((category) => category.isActive || String(category.id) === form.categoryId)
      .map((category) => ({
        value: String(category.id),
        label: `${category.name}${category.taxSection ? ` — s.${category.taxSection}` : ""}${category.isActive ? "" : " (Retired)"}`,
      })),
  ];
  const ready = Boolean(
    form.itemName.trim() && form.assetAccountId && form.financeAccountId && form.categoryId && Number(form.amount) > 0,
  ) && !taxBlocksSave;
  return (
    <Modal
      open
      busy={saving}
      size="lg"
      phoneLayout="fullscreen"
      title={form.id ? "Edit fixed asset purchase" : "Record fixed asset purchase"}
      onClose={onClose}
      primaryAction={{ label: "Save", onClick: onSave, disabled: !ready || saving, loading: saving }}
    >
      <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
        {error && <Notice tone="red" title={error} className="md:col-span-2" />}
        <TextField label="What was bought" required value={form.itemName} onChange={(event) => set({ itemName: event.target.value })} placeholder="3 office desks" />
        <Dropdown label="Asset account" required options={assetOptions} value={form.assetAccountId} onChange={(value) => set({ assetAccountId: value })} />
        <Dropdown
          className="md:col-span-2"
          label="Paid from account"
          required
          placeholder="Select account"
          options={cashAccounts(accounts, { staff: false, selectedId: form.financeAccountId })}
          value={form.financeAccountId}
          onChange={(value) => set({ financeAccountId: value })}
        />
        <Dropdown label="Project" options={projectOptions(projects)} value={form.projectId} onChange={(value) => set({ projectId: value })} />
        <NumberField label="Cost" required prefix="Rs" value={form.amount} onChange={(amount) => set({ amount })} />
        <Dropdown
          className="md:col-span-2"
          label="Category (for tax)"
          required
          options={categoryOptions}
          value={form.categoryId}
          onChange={(value) => {
            const category = categories.find((item) => String(item.id) === value);
            set({ categoryId: value, category: category?.name ?? "", wht: emptyWht() });
          }}
        />
        <Dropdown
          label="Supplier"
          options={vendorChoices(vendors, form.vendorId, "One-off supplier")}
          value={form.vendorId}
          onChange={(value) => set({ vendorId: value, ...(value ? { vendor: "" } : {}) })}
        />
        {!form.vendorId && (
          <TextField label="Supplier / reference (optional)" value={form.vendor} onChange={(event) => set({ vendor: event.target.value })} />
        )}
        <DatePicker label="Date" required max={today()} value={form.date} onChange={(date) => set({ date })} />
        <TextField label="Notes" value={form.description} onChange={(event) => set({ description: event.target.value })} />
        <div className="md:col-span-2">
          <ExpenseWhtFields
            categoryId={form.categoryId}
            vendorId={form.vendorId}
            grossAmount={form.amount}
            date={form.date}
            excludeExpenseId={null}
            excludeAssetPurchaseId={form.id}
            capitalised
            value={form.wht}
            onChange={(wht) => set({ wht })}
            disabled={saving}
            onBlocksSave={onTaxBlocksSave}
          />
        </div>
        <div className="md:col-span-2">
          <ProofField
            kind="assetPurchase"
            recordId={form.id}
            attachment={form.attachment}
            file={form.selectedAttachment}
            removed={form.removeAttachment}
            onPick={(file) => set({ selectedAttachment: file, removeAttachment: false })}
            onRemove={() => set({ selectedAttachment: null, removeAttachment: true })}
            onUndo={() => set({ removeAttachment: false })}
          />
        </div>
      </div>
    </Modal>
  );
}
