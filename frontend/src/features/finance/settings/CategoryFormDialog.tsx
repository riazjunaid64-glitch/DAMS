import { useId, useState, type FormEvent } from "react";
import { DialogTitle, Modal, Notice, NumberField, OptionalLabel, TextField, Toggle, useToast } from "../../../components/ui";
import { paymentsText } from "../lists.ts";
import * as whtApi from "../whtApi.ts";
import type { ExpenseCategory } from "../whtTypes.ts";
import { categoryBody, categoryFields, categoryReady, type CategoryFields } from "./rules.ts";

const pair = "grid gap-4 md:grid-cols-2";
const toggleBox = "rounded-field border border-line-input px-3";

type Props = {
  /** The category being edited; null adds a new one. */
  category: ExpenseCategory | null;
  onClose: () => void;
  onSaved: () => void;
};

/**
 * Add or edit an expense category and its withholding tax. Switching the tax off hides the rates,
 * the section and the yearly limit; the rates and limit are then saved as 0. Expenses already filed
 * keep the rate they were entered at, whatever is saved here.
 */
export function CategoryFormDialog({ category, onClose, onSaved }: Props) {
  const toast = useToast();
  const formId = useId();
  const [fields, setFields] = useState<CategoryFields>(() => categoryFields(category));
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const set = (changes: Partial<CategoryFields>) => setFields((current) => ({ ...current, ...changes }));
  const ready = categoryReady(fields);
  const used = category !== null && category.usageCount > 0;

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (saving || !ready) return;
    setSaving(true);
    setError(null);
    try {
      await whtApi.saveCategory(category?.id ?? null, categoryBody(fields, category));
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
        ? <DialogTitle title="Edit category" subtitle={used ? `Used by ${paymentsText(category.usageCount)}. They keep the rate they were entered at.` : undefined} />
        : "Add category"}
      primaryAction={{ label: "Save", form: formId, loading: saving, disabled: !ready }}
    >
      <form id={formId} noValidate onSubmit={(event) => void submit(event)} className="flex flex-col gap-4">
        {error && <Notice tone="red" role="alert" title={error} />}
        <TextField label="Category name" required maxLength={150} disabled={saving} value={fields.name} onChange={(event) => set({ name: event.target.value })} />
        <Toggle label="Deduct withholding tax" disabled={saving} checked={fields.withholding} onChange={(withholding) => set({ withholding })} className={toggleBox} />
        {fields.withholding && (
          <>
            <div className={pair}>
              <NumberField label="Filer rate" required suffix="%" decimals={2} disabled={saving} value={fields.filerRate} onChange={(filerRate) => set({ filerRate })} />
              <NumberField label="Non-filer rate" required suffix="%" decimals={2} disabled={saving} value={fields.nonFilerRate} onChange={(nonFilerRate) => set({ nonFilerRate })} />
            </div>
            <div className={pair}>
              <TextField
                label={<OptionalLabel>Tax section</OptionalLabel>}
                maxLength={30}
                placeholder="153(1)(a)"
                disabled={saving}
                value={fields.taxSection}
                onChange={(event) => set({ taxSection: event.target.value })}
              />
              <NumberField label="Yearly limit per vendor" prefix="Rs" decimals={2} disabled={saving} value={fields.yearlyLimit} onChange={(yearlyLimit) => set({ yearlyLimit })} />
            </div>
          </>
        )}
        <TextField
          label={<OptionalLabel>Description</OptionalLabel>}
          maxLength={1000}
          disabled={saving}
          value={fields.description}
          onChange={(event) => set({ description: event.target.value })}
        />
        {category && <Toggle label="Active" disabled={saving} checked={fields.isActive} onChange={(isActive) => set({ isActive })} className={toggleBox} />}
      </form>
    </Modal>
  );
}
