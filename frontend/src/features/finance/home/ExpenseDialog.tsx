import { useState } from "react";
import { api } from "../../../api/api.ts";
import { financeApiError } from "../../../api/financeAttachments.ts";
import ExpenseWhtFields from "../../../components/ExpenseWhtFields.tsx";
import { DatePicker, Dropdown, Modal, Notice, NumberField, TextField, useToast } from "../../../components/ui";
import { moneyRequest, useIdempotencyKeys } from "../../../lib/idempotency.ts";
import { pakistanToday } from "../../../lib/financePeriods.ts";
import { emptyWht, type ExpenseCategory, type VendorOption } from "../whtTypes.ts";
import { positiveAmount } from "./format.ts";
import { categoryLabel, formAccountLabel, usableAccounts, vendorLabel } from "./options.ts";
import { RecordProof } from "./proof.tsx";
import type { ExpenseFormState, FinanceAccountOption } from "./types.ts";

export function ExpenseDialog({
  form,
  projects,
  accounts,
  categories,
  vendors,
  lookupsLoading,
  onChange,
  onClose,
  onSaved,
  onOpenAttachment,
  onDownloadAttachment,
}: {
  form: ExpenseFormState;
  projects: readonly { id: number; projectName: string }[];
  accounts: readonly FinanceAccountOption[];
  categories: readonly ExpenseCategory[];
  vendors: readonly VendorOption[];
  lookupsLoading: boolean;
  onChange: (next: ExpenseFormState) => void;
  onClose: () => void;
  onSaved: () => void;
  onOpenAttachment: () => void;
  onDownloadAttachment: () => void;
}) {
  const toast = useToast();
  const idempotency = useIdempotencyKeys();
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [reasonRequired, setReasonRequired] = useState(false);
  const hasCategory = !!form.categoryId || (form.legacyCategory && form.category.trim() !== "");
  const canSave = !!form.financeAccountId && hasCategory && positiveAmount(form.amount) && !!form.date && !reasonRequired;

  const save = async () => {
    setError(null);
    const amount = Number(form.amount);
    if (!Number.isFinite(amount) || amount <= 0) {
      setError("Enter a valid amount greater than zero.");
      return;
    }
    if (!form.categoryId && !form.legacyCategory) {
      setError("Choose an expense category.");
      return;
    }
    if (!form.categoryId && !form.category.trim()) {
      setError("Category is required.");
      return;
    }
    if (!form.financeAccountId) {
      setError("Select the account this expense was paid from.");
      return;
    }
    if (reasonRequired || saving) return;
    setSaving(true);
    try {
      const body = new FormData();
      if (form.projectId) body.append("projectId", form.projectId);
      body.append("financeAccountId", form.financeAccountId);
      body.append("amount", String(amount));
      if (form.categoryId) body.append("categoryId", form.categoryId);
      body.append("category", form.category.trim());
      body.append("description", form.description.trim());
      if (form.vendorId) body.append("vendorId", form.vendorId);
      body.append("vendor", form.vendor.trim());
      if (form.date) body.append("date", form.date);
      if (form.categoryId) {
        if (form.wht.rate !== "") body.append("whtRate", form.wht.rate);
        if (form.wht.amount !== "") body.append("whtAmount", form.wht.amount);
        if (form.wht.overrideReason.trim()) body.append("whtOverrideReason", form.wht.overrideReason.trim());
      }
      if (form.selectedAttachment) body.append("attachment", form.selectedAttachment);
      if (form.removeAttachment) body.append("removeAttachment", "true");
      if (form.id) body.append("concurrencyToken", form.concurrencyToken);
      const signature = `expense:${form.financeAccountId}:${amount}:${form.date}:${form.categoryId}`;
      const res = form.id
        ? await api(`/api/Finance/expenses/${form.id}/form`, { method: "PUT", body })
        : await api("/api/Finance/expenses/form", moneyRequest(idempotency.key(signature, "expense"), { method: "POST", body }));
      if (!res.ok) {
        setError(await financeApiError(res, "Failed to save expense."));
        return;
      }
      if (!form.id) idempotency.release(signature);
      toast.success("Expense saved");
      onSaved();
      onClose();
    } catch {
      setError("The expense could not be saved. Check your connection and try again.");
    } finally {
      setSaving(false);
    }
  };

  const categoryOptions = [
    ...(form.legacyCategory ? [{ value: "", label: `Keep the original text — "${form.category}"` }] : [{ value: "", label: lookupsLoading ? "Loading categories…" : "Select a category" }]),
    ...categories
      .filter((item) => item.isActive || String(item.id) === form.categoryId)
      .map((item) => ({ value: String(item.id), label: categoryLabel(item) })),
  ];

  return (
    <Modal
      open
      size="lg"
      phoneLayout="fullscreen"
      busy={saving}
      title={form.id ? "Edit expense" : "Add expense"}
      onClose={onClose}
      primaryAction={{ label: "Save", onClick: () => void save(), disabled: !canSave, loading: saving }}
    >
      <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
        {error && <div className="md:col-span-2"><Notice tone="red" role="alert" title={error} /></div>}
        <Dropdown
          className="md:col-span-2"
          label="Paid from account"
          required
          value={form.financeAccountId}
          placeholder="Select account"
          options={[
            { value: "", label: "Select account" },
            ...usableAccounts(accounts, form.financeAccountId, true).map((account) => ({
              value: String(account.id),
              label: formAccountLabel(account, true),
            })),
          ]}
          onChange={(financeAccountId) => onChange({ ...form, financeAccountId })}
        />
        <Dropdown
          className="md:col-span-2"
          label="Category"
          required
          value={form.categoryId}
          placeholder={lookupsLoading ? "Loading categories…" : "Select a category"}
          options={categoryOptions}
          onChange={(categoryId) => {
            const category = categories.find((item) => String(item.id) === categoryId);
            onChange({
              ...form,
              categoryId,
              category: category?.name ?? (categoryId ? form.category : form.legacyCategory ? form.category : ""),
              wht: categoryId ? form.wht : emptyWht(),
            });
          }}
        />
        {form.legacyCategory && !form.categoryId && (
          <TextField
            className="md:col-span-2"
            label="Original category text"
            required
            value={form.category}
            onChange={(event) => onChange({ ...form, category: event.target.value })}
          />
        )}
        <Dropdown
          label="Project"
          value={form.projectId}
          options={[
            { value: "", label: "General (no specific project)" },
            ...projects.map((project) => ({ value: String(project.id), label: project.projectName })),
          ]}
          onChange={(projectId) => onChange({ ...form, projectId })}
        />
        <Dropdown
          label="Vendor"
          value={form.vendorId}
          options={[
            { value: "", label: lookupsLoading ? "Loading vendors…" : "One-off payee" },
            ...vendors
              .filter((vendor) => vendor.isActive || String(vendor.id) === form.vendorId)
              .map((vendor) => ({ value: String(vendor.id), label: vendorLabel(vendor) })),
          ]}
          onChange={(vendorId) => onChange({
            ...form,
            vendorId,
            vendor: vendorId ? (vendors.find((vendor) => String(vendor.id) === vendorId)?.name ?? "") : "",
          })}
        />
        {!form.vendorId && (
          <TextField
            className="md:col-span-2"
            label="Vendor / reference (optional)"
            value={form.vendor}
            onChange={(event) => onChange({ ...form, vendor: event.target.value })}
          />
        )}
        <NumberField label="Gross amount" required prefix="Rs" value={form.amount} onChange={(amount) => onChange({ ...form, amount })} />
        <DatePicker label="Date" required max={pakistanToday()} value={form.date} onChange={(date) => onChange({ ...form, date })} />
        <div className="md:col-span-2">
          <ExpenseWhtFields
            categoryId={form.categoryId}
            vendorId={form.vendorId}
            grossAmount={form.amount}
            date={form.date}
            excludeExpenseId={form.id}
            value={form.wht}
            disabled={saving}
            onChange={(wht) => onChange({ ...form, wht })}
            onReasonRequired={setReasonRequired}
          />
        </div>
        <TextField className="md:col-span-2" label="Description (optional)" value={form.description} onChange={(event) => onChange({ ...form, description: event.target.value })} />
        <div className="md:col-span-2">
          <RecordProof
            saved={form.attachment}
            selected={form.selectedAttachment}
            disabled={saving}
            onSelected={(selectedAttachment) => onChange({ ...form, selectedAttachment })}
            onRemoveSaved={(removeAttachment) => onChange({ ...form, removeAttachment })}
            onOpen={form.id && form.attachment ? onOpenAttachment : undefined}
            onDownload={form.id && form.attachment ? onDownloadAttachment : undefined}
          />
        </div>
      </div>
    </Modal>
  );
}
