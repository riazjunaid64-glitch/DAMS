import { useState } from "react";
import { api } from "../../../api/api.ts";
import { financeApiError } from "../../../api/financeAttachments.ts";
import ExpenseWhtFields from "../../../components/ExpenseWhtFields.tsx";
import { DatePicker, Dropdown, Modal, Notice, NumberField, TextField, useToast } from "../../../components/ui";
import { moneyRequest, useIdempotencyKeys } from "../../../lib/idempotency.ts";
import { pakistanToday } from "../../../lib/financePeriods.ts";
import type { ExpenseCategory, VendorOption } from "../whtTypes.ts";
import { emptyWht } from "../whtTypes.ts";
import { positiveAmount } from "./format.ts";
import { categoryLabel, formAccountLabel, usableAccounts, vendorLabel } from "./options.ts";
import { RecordProof } from "./proof.tsx";
import type { AssetPurchaseFormState, FinanceAccountOption } from "./types.ts";

export function AssetPurchaseDialog({
  form,
  projects,
  accounts,
  assetAccounts,
  assetAccountsLoading,
  categories,
  vendors,
  lookupsLoading,
  onChange,
  onClose,
  onSaved,
  onOpenAttachment,
  onDownloadAttachment,
}: {
  form: AssetPurchaseFormState;
  projects: readonly { id: number; projectName: string }[];
  accounts: readonly FinanceAccountOption[];
  assetAccounts: readonly FinanceAccountOption[];
  assetAccountsLoading: boolean;
  categories: readonly ExpenseCategory[];
  vendors: readonly VendorOption[];
  lookupsLoading: boolean;
  onChange: (next: AssetPurchaseFormState) => void;
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
  const recordedElsewhere = !!form.assetAccountId && !assetAccounts.some((account) => String(account.id) === form.assetAccountId);
  const canSave = !!form.itemName.trim() && !!form.assetAccountId && !!form.financeAccountId && !!form.categoryId && positiveAmount(form.amount) && !!form.date && !reasonRequired;

  const save = async () => {
    setError(null);
    const amount = Number(form.amount);
    if (!Number.isFinite(amount) || amount <= 0) {
      setError("Enter a valid amount greater than zero.");
      return;
    }
    if (!form.itemName.trim()) {
      setError("Describe what was bought, e.g. \"3 office desks\".");
      return;
    }
    if (!form.assetAccountId) {
      setError("Select the asset account this purchase belongs to.");
      return;
    }
    if (!form.financeAccountId) {
      setError("Select the account this purchase was paid from.");
      return;
    }
    if (!form.categoryId) {
      setError("Choose a category.");
      return;
    }
    if (reasonRequired || saving) return;
    setSaving(true);
    try {
      const body = new FormData();
      if (form.projectId) body.append("projectId", form.projectId);
      body.append("assetAccountId", form.assetAccountId);
      body.append("financeAccountId", form.financeAccountId);
      body.append("amount", String(amount));
      body.append("itemName", form.itemName.trim());
      body.append("categoryId", form.categoryId);
      body.append("category", form.category.trim());
      body.append("description", form.description.trim());
      if (form.vendorId) body.append("vendorId", form.vendorId);
      body.append("vendor", form.vendor.trim());
      if (form.date) body.append("date", form.date);
      if (form.wht.rate !== "") body.append("whtRate", form.wht.rate);
      if (form.wht.amount !== "") body.append("whtAmount", form.wht.amount);
      if (form.wht.overrideReason.trim()) body.append("whtOverrideReason", form.wht.overrideReason.trim());
      if (form.selectedAttachment) body.append("attachment", form.selectedAttachment);
      if (form.removeAttachment) body.append("removeAttachment", "true");
      if (form.id) body.append("concurrencyToken", form.concurrencyToken);
      const signature = `asset:${form.financeAccountId}:${form.assetAccountId}:${amount}:${form.date}`;
      const res = form.id
        ? await api(`/api/Finance/asset-purchases/${form.id}/form`, { method: "PUT", body })
        : await api("/api/Finance/asset-purchases/form", moneyRequest(idempotency.key(signature, "asset-purchase"), { method: "POST", body }));
      if (!res.ok) {
        setError(await financeApiError(res, "Failed to save the asset purchase."));
        return;
      }
      if (!form.id) idempotency.release(signature);
      toast.success("Purchase saved");
      onSaved();
      onClose();
    } catch {
      setError("The purchase could not be saved. Check your connection and try again.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      open
      size="lg"
      phoneLayout="fullscreen"
      busy={saving}
      title={form.id ? "Edit fixed asset purchase" : "Record fixed asset purchase"}
      onClose={onClose}
      primaryAction={{ label: "Save", onClick: () => void save(), disabled: !canSave, loading: saving }}
    >
      <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
        {error && <div className="md:col-span-2"><Notice tone="red" role="alert" title={error} /></div>}
        <TextField label="What was bought" required value={form.itemName} onChange={(event) => onChange({ ...form, itemName: event.target.value })} />
        <Dropdown
          label="Asset account"
          required
          value={form.assetAccountId}
          placeholder={assetAccountsLoading ? "Loading asset accounts…" : "Select a fixed asset account"}
          options={[
            { value: "", label: assetAccountsLoading ? "Loading asset accounts…" : "Select a fixed asset account" },
            ...(recordedElsewhere ? [{ value: form.assetAccountId, label: `${form.assetAccountName} (as recorded)` }] : []),
            ...assetAccounts
              .filter((account) => account.isActive || String(account.id) === form.assetAccountId)
              .map((account) => ({ value: String(account.id), label: `${account.name}${account.isActive ? "" : " (Inactive)"}` })),
          ]}
          onChange={(assetAccountId) => onChange({ ...form, assetAccountId })}
        />
        <Dropdown
          className="md:col-span-2"
          label="Paid from account"
          required
          value={form.financeAccountId}
          placeholder="Select account"
          options={[
            { value: "", label: "Select account" },
            ...usableAccounts(accounts, form.financeAccountId, false).map((account) => ({
              value: String(account.id),
              label: formAccountLabel(account),
            })),
          ]}
          onChange={(financeAccountId) => onChange({ ...form, financeAccountId })}
        />
        <Dropdown
          label="Project"
          value={form.projectId}
          options={[
            { value: "", label: "General (no specific project)" },
            ...projects.map((project) => ({ value: String(project.id), label: project.projectName })),
          ]}
          onChange={(projectId) => onChange({ ...form, projectId })}
        />
        <NumberField label="Cost" required prefix="Rs" value={form.amount} onChange={(amount) => onChange({ ...form, amount })} />
        <Dropdown
          className="md:col-span-2"
          label="Category (for tax)"
          required
          value={form.categoryId}
          placeholder={lookupsLoading ? "Loading categories…" : "Select a category"}
          options={[
            { value: "", label: lookupsLoading ? "Loading categories…" : "Select a category" },
            ...categories
              .filter((item) => item.isActive || String(item.id) === form.categoryId)
              .map((item) => ({ value: String(item.id), label: categoryLabel(item) })),
          ]}
          onChange={(categoryId) => {
            const category = categories.find((item) => String(item.id) === categoryId);
            onChange({
              ...form,
              categoryId,
              category: category?.name ?? "",
              wht: categoryId ? form.wht : emptyWht(),
            });
          }}
        />
        <Dropdown
          label="Supplier"
          value={form.vendorId}
          options={[
            { value: "", label: lookupsLoading ? "Loading suppliers…" : "One-off supplier" },
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
            label="Supplier / reference (optional)"
            value={form.vendor}
            onChange={(event) => onChange({ ...form, vendor: event.target.value })}
          />
        )}
        <DatePicker label="Date" required max={pakistanToday()} value={form.date} onChange={(date) => onChange({ ...form, date })} />
        <TextField label="Notes (optional)" value={form.description} onChange={(event) => onChange({ ...form, description: event.target.value })} />
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
            disabled={saving}
            onChange={(wht) => onChange({ ...form, wht })}
            onReasonRequired={setReasonRequired}
          />
        </div>
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
