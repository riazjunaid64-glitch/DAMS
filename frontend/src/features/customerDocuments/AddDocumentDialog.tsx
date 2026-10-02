import { useRef, useState } from "react";
import { apiUpload } from "../../api/api.ts";
import { AttachProof, Dropdown, Modal, Notice, TextField, useToast } from "../../components/ui";
import { uploadFailure } from "./documentApi.ts";
import { DOCUMENT_RULE } from "./documentFile.ts";
import type { DocumentTypeOption } from "./types.ts";

const OTHER = "other";

type Props = {
  customerId: number;
  customerName: string;
  types: DocumentTypeOption[];
  onClose: () => void;
  /** Reloads the tab once the server has saved, before the popup closes. */
  onSaved: () => Promise<void> | void;
};

/** Add document: choose a type (or Other and name it), choose the file, Save — it is created already Uploaded. */
export function AddDocumentDialog({ customerId, customerName, types, onClose, onSaved }: Props) {
  const toast = useToast();
  const [choice, setChoice] = useState("");
  const [name, setName] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const [progress, setProgress] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);
  const controller = useRef<AbortController | null>(null);
  const saving = progress !== null;
  const named = choice === OTHER ? name.trim() !== "" : choice !== "";
  const options = [...types.map((t) => ({ value: String(t.categoryId), label: t.name })), { value: OTHER, label: "Other" }];

  const save = async () => {
    if (!file || !named) return;
    const form = new FormData();
    if (choice === OTHER) form.append("name", name.trim());
    else form.append("categoryId", choice);
    form.append("file", file);
    controller.current = new AbortController();
    setError(null);
    setProgress(0);
    try {
      const response = await apiUpload(`/api/customer-documents/customers/${customerId}/documents`, form, setProgress, controller.current.signal);
      if (!response.ok) {
        setError(await uploadFailure(response));
        return;
      }
      toast.success("Document added");
      await onSaved();
      onClose();
    } catch (caught) {
      if (!(caught instanceof DOMException && caught.name === "AbortError")) setError("The document could not be uploaded. Try again.");
    } finally {
      setProgress(null);
    }
  };

  return (
    <Modal
      open
      onClose={onClose}
      title={<>Add document<span className="block text-small font-normal text-ink-muted">{customerName}</span></>}
      phoneLayout="fullscreen"
      busy={saving}
      primaryAction={{ label: "Save", onClick: () => void save(), disabled: !file || !named, loading: saving }}
    >
      <div className="flex flex-col gap-4">
        {error && <Notice tone="red" role="alert" title={error} />}
        <Dropdown
          label="Document"
          required
          options={options}
          value={choice}
          onChange={setChoice}
          placeholder="Choose a document"
          disabled={saving}
        />
        {choice === OTHER && (
          <TextField label="Name" required maxLength={150} value={name} disabled={saving} onChange={(event) => setName(event.target.value)} />
        )}
        <AttachProof
          label="File"
          required
          rule={DOCUMENT_RULE}
          file={file ? { name: file.name, size: file.size, uploaded: false } : null}
          progress={progress}
          disabled={saving}
          onPick={(picked) => { setFile(picked); setError(null); }}
          onRemove={() => {
            if (saving) controller.current?.abort();
            else setFile(null);
          }}
        />
      </div>
    </Modal>
  );
}
