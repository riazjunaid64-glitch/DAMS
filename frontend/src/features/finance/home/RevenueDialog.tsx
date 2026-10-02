import { useState } from "react";
import { api } from "../../../api/api.ts";
import { financeApiError } from "../../../api/financeAttachments.ts";
import { Button, DatePicker, Dropdown, Modal, Notice, NumberField, TextField, useToast } from "../../../components/ui";
import { moneyRequest, useIdempotencyKeys } from "../../../lib/idempotency.ts";
import { pakistanToday } from "../../../lib/financePeriods.ts";
import { positiveAmount } from "./format.ts";
import { formAccountLabel, usableAccounts } from "./options.ts";
import { RecordProof } from "./proof.tsx";
import { CANCELLATION_REVENUE_CODE, type FinanceAccountOption, type RevenueCategory, type RevenueFormState } from "./types.ts";

export function RevenueDialog({
  form,
  projects,
  accounts,
  categories,
  categoriesLoading,
  lookupError = null,
  onRetryLookups,
  accountLookupError = null,
  onRetryAccounts,
  onChange,
  onClose,
  onSaved,
  onOpenAttachment,
  onDownloadAttachment,
}: {
  form: RevenueFormState;
  projects: readonly { id: number; projectName: string }[];
  accounts: readonly FinanceAccountOption[];
  categories: readonly RevenueCategory[];
  categoriesLoading: boolean;
  lookupError?: string | null;
  onRetryLookups?: () => void;
  accountLookupError?: string | null;
  onRetryAccounts?: () => void;
  onChange: (next: RevenueFormState) => void;
  onClose: () => void;
  onSaved: () => void;
  onOpenAttachment: () => void;
  onDownloadAttachment: () => void;
}) {
  const toast = useToast();
  const idempotency = useIdempotencyKeys();
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const chosen = categories.find((item) => String(item.id) === form.revenueCategoryId);
  const canSave = !!form.financeAccountId && !!form.revenueCategoryId && positiveAmount(form.amount) && !!form.date;

  const save = async () => {
    setError(null);
    const amount = Number(form.amount);
    if (!Number.isFinite(amount) || amount <= 0) {
      setError("Enter a valid amount greater than zero.");
      return;
    }
    if (!form.revenueCategoryId) {
      setError("Choose a revenue category.");
      return;
    }
    if (!form.financeAccountId) {
      setError("Select the account where this revenue was received.");
      return;
    }
    if (saving) return;
    setSaving(true);
    try {
      const body = new FormData();
      if (form.projectId) body.append("projectId", form.projectId);
      body.append("financeAccountId", form.financeAccountId);
      body.append("amount", String(amount));
      body.append("revenueType", (chosen?.name ?? form.revenueType).trim());
      body.append("revenueCategoryId", form.revenueCategoryId);
      body.append("description", form.description.trim());
      body.append("reference", form.reference.trim());
      if (form.date) body.append("date", form.date);
      if (form.selectedAttachment) body.append("attachment", form.selectedAttachment);
      if (form.removeAttachment) body.append("removeAttachment", "true");
      if (form.id) body.append("concurrencyToken", form.concurrencyToken);
      const signature = `revenue:${form.financeAccountId}:${amount}:${form.date}:${form.revenueCategoryId}`;
      const res = form.id
        ? await api(`/api/Finance/revenue/${form.id}/form`, { method: "PUT", body })
        : await api("/api/Finance/revenue/form", moneyRequest(idempotency.key(signature, "revenue"), { method: "POST", body }));
      if (!res.ok) {
        setError(await financeApiError(res, "Failed to save revenue entry."));
        return;
      }
      if (!form.id) idempotency.release(signature);
      toast.success("Revenue saved");
      onSaved();
      onClose();
    } catch {
      setError("The revenue entry could not be saved. Check your connection and try again.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      open
      size="md"
      phoneLayout="fullscreen"
      busy={saving}
      title={form.id ? "Edit manual revenue" : "Add manual revenue"}
      onClose={onClose}
      primaryAction={{ label: "Save", onClick: () => void save(), disabled: !canSave, loading: saving }}
    >
      <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
        {accountLookupError && (
          <div className="md:col-span-2">
            <Notice tone="red" role="alert" title={accountLookupError} action={onRetryAccounts ? <Button variant="outline" onClick={onRetryAccounts}>Try again</Button> : undefined} />
          </div>
        )}
        {lookupError && (
          <div className="md:col-span-2">
            <Notice tone="red" role="alert" title={lookupError} action={onRetryLookups ? <Button variant="outline" onClick={onRetryLookups}>Try again</Button> : undefined} />
          </div>
        )}
        {error && <div className="md:col-span-2"><Notice tone="red" role="alert" title={error} /></div>}
        <Dropdown
          className="md:col-span-2"
          label="Received in account"
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
        <Dropdown
          label="Revenue category"
          required
          value={form.revenueCategoryId}
          placeholder={categoriesLoading ? "Loading categories…" : "Select a category"}
          options={[
            { value: "", label: categoriesLoading ? "Loading categories…" : "Select a category" },
            ...categories
              .filter((item) => item.isActive || String(item.id) === form.revenueCategoryId)
              .map((item) => ({ value: String(item.id), label: `${item.name}${item.isActive ? "" : " (Retired)"}` })),
          ]}
          onChange={(revenueCategoryId) => {
            const category = categories.find((item) => String(item.id) === revenueCategoryId);
            onChange({ ...form, revenueCategoryId, revenueType: category?.name ?? form.revenueType });
          }}
        />
        {chosen?.code === CANCELLATION_REVENUE_CODE && (
          <div className="md:col-span-2">
            <Notice
              tone="orange"
              role="alert"
              title="Only for a forfeiture from outside DAMS — before go-live, or on something that was never a booking here. Cancelling a booking in DAMS records the retained amount as income by itself, so entering it again here would count it twice."
            />
          </div>
        )}
        <NumberField label="Amount" required prefix="Rs" value={form.amount} onChange={(amount) => onChange({ ...form, amount })} />
        <DatePicker label="Date" required max={pakistanToday()} value={form.date} onChange={(date) => onChange({ ...form, date })} />
        <TextField className="md:col-span-2" label="Reference (optional)" value={form.reference} onChange={(event) => onChange({ ...form, reference: event.target.value })} />
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
