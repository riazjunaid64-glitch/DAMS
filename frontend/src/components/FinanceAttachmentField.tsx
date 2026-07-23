import { useRef, useState } from "react";
import type { FinanceAttachmentInfo } from "../api/financeAttachments";

export const FINANCE_ATTACHMENT_MAX_SIZE = 15 * 1024 * 1024;
const ACCEPTED_EXTENSIONS = [".pdf", ".jpg", ".jpeg", ".png", ".webp", ".doc", ".docx", ".xls", ".xlsx"];
const ACCEPT = "image/jpeg,image/png,image/webp,.pdf,.doc,.docx,.xls,.xlsx";

interface Props {
  existing: FinanceAttachmentInfo | null;
  selected: File | null;
  removeExisting: boolean;
  disabled?: boolean;
  onSelected: (file: File | null) => void;
  onRemoveExisting: (remove: boolean) => void;
  onViewExisting: () => void;
  onDownloadExisting: () => void;
}

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

function validateFile(file: File): string | null {
  const extension = `.${file.name.split(".").pop()?.toLowerCase() ?? ""}`;
  if (!ACCEPTED_EXTENSIONS.includes(extension))
    return "This file type is not supported. Choose a PDF, JPG, PNG, WebP, Word, or Excel file.";
  if (file.size === 0) return "The selected attachment is empty.";
  if (file.size > FINANCE_ATTACHMENT_MAX_SIZE)
    return "The attachment is too large. The maximum allowed size is 15 MB.";
  return null;
}

export default function FinanceAttachmentField({
  existing,
  selected,
  removeExisting,
  disabled = false,
  onSelected,
  onRemoveExisting,
  onViewExisting,
  onDownloadExisting,
}: Props) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [error, setError] = useState<string | null>(null);

  const choose = (file?: File) => {
    if (!file) return;
    const validationError = validateFile(file);
    if (validationError) {
      setError(validationError);
      if (inputRef.current) inputRef.current.value = "";
      return;
    }
    setError(null);
    onRemoveExisting(false);
    onSelected(file);
  };

  const clearSelected = () => {
    setError(null);
    onSelected(null);
    if (inputRef.current) inputRef.current.value = "";
  };

  return (
    <div className="space-y-2">
      <div className="flex items-center justify-between gap-3">
        <label className="text-xs font-medium text-[var(--text-secondary)]">Supporting attachment (optional)</label>
        <span className="text-[10px] text-[var(--text-muted)]">PDF, photos, Word, Excel · max 15 MB</span>
      </div>

      {existing && !removeExisting && !selected && (
        <div className="flex flex-wrap items-center gap-2 rounded-xl border border-[var(--border)] bg-[var(--surface-glass)] p-3">
          <div className="min-w-0 flex-1">
            <p className="truncate text-sm font-medium text-[var(--text-primary)]">{existing.fileName}</p>
            <p className="text-xs text-[var(--text-muted)]">{formatBytes(existing.fileSize)} · Existing attachment</p>
          </div>
          <button type="button" onClick={onViewExisting} disabled={disabled} className="text-xs font-semibold text-[var(--accent)] hover:underline disabled:opacity-50">View</button>
          <button type="button" onClick={onDownloadExisting} disabled={disabled} className="text-xs font-semibold text-[var(--text-secondary)] hover:underline disabled:opacity-50">Download</button>
          <button type="button" onClick={() => onRemoveExisting(true)} disabled={disabled} className="text-xs font-semibold text-rose-400 hover:underline disabled:opacity-50">Remove</button>
        </div>
      )}

      {existing && removeExisting && !selected && (
        <div className="flex items-center justify-between gap-3 rounded-xl border border-amber-500/20 bg-amber-500/[0.07] p-3">
          <p className="text-xs text-amber-300">{existing.fileName} will be removed when you save.</p>
          <button type="button" onClick={() => onRemoveExisting(false)} disabled={disabled} className="text-xs font-semibold text-amber-200 hover:underline">Undo</button>
        </div>
      )}

      {selected ? (
        <div className="flex items-center gap-3 rounded-xl border border-[var(--accent)]/30 bg-[var(--surface-glass)] p-3">
          <div className="flex h-12 w-12 items-center justify-center rounded-lg bg-[var(--surface-glass-hover)] text-[10px] font-bold text-[var(--text-muted)]">
            {selected.type.startsWith("image/") ? "PHOTO" : "FILE"}
          </div>
          <div className="min-w-0 flex-1">
            <p className="truncate text-sm font-medium text-[var(--text-primary)]">{selected.name}</p>
            <p className="text-xs text-[var(--text-muted)]">{formatBytes(selected.size)}{existing ? " · Will replace existing attachment" : " · Ready to upload"}</p>
          </div>
          <button type="button" onClick={clearSelected} disabled={disabled} className="text-xs font-semibold text-rose-400 hover:underline disabled:opacity-50">Remove</button>
        </div>
      ) : (
        <button
          type="button"
          disabled={disabled}
          onClick={() => inputRef.current?.click()}
          className="w-full rounded-xl border border-dashed border-[var(--border)] bg-[var(--surface-glass)] px-4 py-3 text-left transition-colors hover:border-[var(--accent)] disabled:cursor-not-allowed disabled:opacity-50"
        >
          <span className="block text-sm font-medium text-[var(--text-primary)]">{existing && !removeExisting ? "Replace attachment" : "Choose attachment"}</span>
          <span className="block text-xs text-[var(--text-muted)]">Select a document or a photo from this device</span>
        </button>
      )}

      <input
        ref={inputRef}
        type="file"
        accept={ACCEPT}
        disabled={disabled}
        className="sr-only"
        onChange={(event) => choose(event.target.files?.[0])}
      />
      {error && <p role="alert" className="text-xs text-rose-300">{error}</p>}
    </div>
  );
}
