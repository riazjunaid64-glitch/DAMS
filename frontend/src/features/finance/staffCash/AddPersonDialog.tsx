import { useId, useState, type FormEvent } from "react";
import { Modal, Notice, TextField, useToast } from "../../../components/ui";
import { staffCashApi } from "./api.ts";
import type { Holder } from "./types.ts";

/** A new float starts at zero; the money handed over is recorded next, from the person's panel. */
export function AddPersonDialog({ onClose, onAdded }: { onClose: () => void; onAdded: (holder: Holder) => void }) {
  const toast = useToast();
  const formId = useId();
  const [name, setName] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const trimmed = name.trim();

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (saving || !trimmed) return;
    setSaving(true);
    setError(null);
    try {
      const holder = await staffCashApi.addPerson(trimmed);
      toast.success(`Added ${holder.personName}.`);
      onAdded(holder);
      onClose();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "Could not add this person.");
      setSaving(false);
    }
  };

  return (
    <Modal
      open
      onClose={onClose}
      busy={saving}
      size="sm"
      title="Add person"
      primaryAction={{ label: "Add person", form: formId, loading: saving, disabled: !trimmed }}
    >
      <form id={formId} noValidate onSubmit={(event) => void submit(event)} className="flex flex-col gap-4">
        {error && <Notice tone="red" role="alert" title={error} />}
        <TextField label="Person name" required autoFocus maxLength={100} disabled={saving} value={name} onChange={(event) => setName(event.target.value)} />
      </form>
    </Modal>
  );
}
