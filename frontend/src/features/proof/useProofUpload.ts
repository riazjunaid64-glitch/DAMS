import { useEffect, useRef, useState } from "react";
import type { AttachProofProps } from "../../components/ui";
import { uploadProof, type ProofOwnerType } from "./proofApi";

/**
 * Holds the proof file of a form. The user can pick one at any time; nothing is sent until the
 * record itself is saved, and then `upload` sends it to that record. Proof is optional, so a failed
 * upload never undoes the save: `upload` resolves false and the caller warns and moves on.
 */
export function useProofUpload() {
  const [file, setFile] = useState<File | null>(null);
  const [progress, setProgress] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [uploaded, setUploaded] = useState(false);
  const controller = useRef<AbortController | null>(null);

  useEffect(() => () => controller.current?.abort(), []);

  const reset = () => {
    controller.current?.abort();
    controller.current = null;
    setFile(null);
    setProgress(null);
    setError(null);
    setUploaded(false);
  };

  /** Sends the picked file to the saved record. True when there was nothing to send, it was stored, or the person stopped it; false when it failed. */
  const upload = async (ownerType: ProofOwnerType, ownerId: number): Promise<boolean> => {
    if (!file) return true;
    const abort = new AbortController();
    controller.current = abort;
    setError(null);
    setProgress(0);
    try {
      await uploadProof(ownerType, ownerId, file, setProgress, abort.signal);
      setUploaded(true);
      return true;
    } catch (failure) {
      if (abort.signal.aborted) return true;
      setError(failure instanceof Error ? failure.message : "The proof could not be uploaded.");
      return false;
    } finally {
      if (controller.current === abort) controller.current = null;
      setProgress(null);
    }
  };

  const fieldProps: Pick<AttachProofProps, "file" | "onPick" | "onRemove" | "progress" | "error"> = {
    file: file && { name: file.name, size: file.size, uploaded },
    onPick: (picked) => {
      setFile(picked);
      setError(null);
      setUploaded(false);
    },
    onRemove: reset,
    progress,
    error,
  };

  return { file, hasFile: file !== null, upload, reset, fieldProps };
}
