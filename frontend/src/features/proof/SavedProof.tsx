import { useId, useRef, useState } from "react";
import { PROOF_ACCEPT, proofFileError } from "../../components/ui/proofFile.ts";
import { openProof, uploadProof, type ProofFile, type ProofOwnerType } from "./proofApi.ts";

type Props = {
  ownerType: ProofOwnerType;
  ownerId: number;
  /** The proof already stored on the record, if any. */
  proof?: ProofFile | null;
  /** Called once a file is stored, so the screen can reload the record. */
  onChanged: () => void | Promise<void>;
};

const linkClass = "cursor-pointer border-0 bg-transparent p-0 text-left text-sm font-bold text-gold-text underline-offset-4 hover:underline focus-visible:outline-2 focus-visible:outline-primary disabled:cursor-not-allowed disabled:opacity-60";

/**
 * Proof on a record that is already saved: the file name (opens the file) when there is one, and an
 * "Attach proof" link when there is not. It is also how a proof whose first upload failed gets sent
 * again — the record stays saved and the file goes on it from here.
 */
export function SavedProof({ ownerType, ownerId, proof, onChanged }: Props) {
  const inputId = useId();
  const inputRef = useRef<HTMLInputElement>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const open = async () => {
    setError(null);
    try {
      await openProof(proof!.id);
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "The proof could not be opened.");
    }
  };

  const attach = async (file: File | undefined) => {
    if (inputRef.current) inputRef.current.value = "";
    if (!file) return;
    const refusal = proofFileError(file);
    if (refusal) {
      setError(refusal);
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await uploadProof(ownerType, ownerId, file);
      await onChanged();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "The proof could not be uploaded.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <span className="flex flex-col items-start gap-0.5">
      {proof ? (
        <button type="button" className={`${linkClass} max-w-[180px] truncate`} title={proof.fileName} onClick={() => void open()}>
          {proof.fileName}
        </button>
      ) : (
        <>
          <button type="button" className={linkClass} disabled={busy} onClick={() => inputRef.current?.click()}>
            {busy ? "Uploading…" : "Attach proof"}
          </button>
          <input
            ref={inputRef}
            id={inputId}
            type="file"
            accept={PROOF_ACCEPT}
            aria-label="Attach proof file"
            className="sr-only"
            tabIndex={-1}
            onChange={(event) => void attach(event.target.files?.[0])}
          />
        </>
      )}
      {error && <span role="alert" className="text-xs font-bold text-rose-400">{error}</span>}
    </span>
  );
}
