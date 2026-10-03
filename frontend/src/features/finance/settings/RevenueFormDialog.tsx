import { useId, useState, type FormEvent } from "react";
import { DialogTitle, Modal, Notice, NumberField, OptionalLabel, TextField, Toggle, useToast } from "../../../components/ui";
import { saveRevenueCategory, type RevenueCategory } from "../revenueCategoryApi.ts";
import { revenueBody, revenueEntriesText, revenueFields, type RevenueFields } from "./rules.ts";

type Props = {
  /** The category being edited; null adds a new one. */
  category: RevenueCategory | null;
  onClose: () => void;
  onSaved: () => void;
};

/**
 * Add or edit a revenue category. Revenue already filed keeps the name it was recorded under, so a
 * rename changes only what new entries are filed under.
 */
export function RevenueFormDialog({ category, onClose, onSaved }: Props) {
  const toast = useToast();
  const formId = useId();
  const [fields, setFields] = useState<RevenueFields>(() => revenueFields(category));
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const set = (changes: Partial<RevenueFields>) => setFields((current) => ({ ...current, ...changes }));
  const ready = fields.name.trim() !== "";
  const used = category !== null && category.revenueCount > 0;

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (saving || !ready) return;
    setSaving(true);
    setError(null);
    try {
      await saveRevenueCategory(category?.id ?? null, revenueBody(fields, category));
      toast.success(category ? "Category saved." : "Category added.");
      onSaved();
      onClose();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "The category could not be saved.");
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
      title={category
        ? <DialogTitle title="Edit category" subtitle={used ? `Used by ${revenueEntriesText(category.revenueCount)}.` : undefined} />
        : "Add category"}
      primaryAction={{ label: "Save", form: formId, loading: saving, disabled: !ready }}
    >
      <form id={formId} noValidate onSubmit={(event) => void submit(event)} className="flex flex-col gap-4">
        {error && <Notice tone="red" role="alert" title={error} />}
        <TextField label="Category name" required maxLength={150} disabled={saving} value={fields.name} onChange={(event) => set({ name: event.target.value })} />
        <TextField
          label={<OptionalLabel>Description</OptionalLabel>}
          maxLength={1000}
          disabled={saving}
          value={fields.description}
          onChange={(event) => set({ description: event.target.value })}
        />
        {/* Nine digits ("999,999,999") at most: the server keeps the order as a 32-bit number. */}
        <NumberField label={<OptionalLabel>Order in lists</OptionalLabel>} decimals={0} maxLength={11} disabled={saving} value={fields.order} onChange={(order) => set({ order })} />
        {category && (
          <Toggle label="Active" disabled={saving} checked={fields.isActive} onChange={(isActive) => set({ isActive })} className="rounded-field border border-line-input px-3" />
        )}
      </form>
    </Modal>
  );
}
