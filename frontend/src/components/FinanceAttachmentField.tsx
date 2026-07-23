import { useRef, useState } from "react";
import type { FinanceAttachmentInfo } from "../api/financeAttachments";
import ModalPortal from "../lib/ModalPortal";

export const FINANCE_ATTACHMENT_MAX_SIZE = 15 * 1024 * 1024;
const ACCEPTED_EXTENSIONS = [".pdf", ".jpg", ".jpeg", ".png", ".webp", ".doc", ".docx", ".xls", ".xlsx"];
const IMAGE_ACCEPT = "image/jpeg,image/png,image/webp";
const DOCUMENT_ACCEPT = ".pdf,.doc,.docx,.xls,.xlsx";

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
  const galleryInputRef = useRef<HTMLInputElement>(null);
  const cameraInputRef = useRef<HTMLInputElement>(null);
  const documentInputRef = useRef<HTMLInputElement>(null);
  const [error, setError] = useState<string | null>(null);
  const [sourcePickerOpen, setSourcePickerOpen] = useState(false);

  const clearInputs = () => {
    if (galleryInputRef.current) galleryInputRef.current.value = "";
    if (cameraInputRef.current) cameraInputRef.current.value = "";
    if (documentInputRef.current) documentInputRef.current.value = "";
  };

  const choose = (file?: File) => {
    if (!file) return;
    const validationError = validateFile(file);
    if (validationError) {
      setError(validationError);
      clearInputs();
      return;
    }
    setError(null);
    onRemoveExisting(false);
    onSelected(file);
  };

  const clearSelected = () => {
    setError(null);
    onSelected(null);
    clearInputs();
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
          onClick={() => setSourcePickerOpen(true)}
          className="w-full rounded-xl border border-dashed border-[var(--border)] bg-[var(--surface-glass)] px-4 py-3 text-left transition-colors hover:border-[var(--accent)] disabled:cursor-not-allowed disabled:opacity-50"
        >
          <span className="block text-sm font-medium text-[var(--text-primary)]">{existing && !removeExisting ? "Replace attachment" : "Choose attachment"}</span>
          <span className="block text-xs text-[var(--text-muted)]">Take a photo or choose from gallery and files</span>
        </button>
      )}

      <input
        ref={galleryInputRef}
        type="file"
        accept={IMAGE_ACCEPT}
        disabled={disabled}
        className="sr-only"
        onChange={(event) => choose(event.target.files?.[0])}
      />
      <input
        ref={cameraInputRef}
        type="file"
        accept={IMAGE_ACCEPT}
        capture="environment"
        disabled={disabled}
        className="sr-only"
        onChange={(event) => choose(event.target.files?.[0])}
      />
      <input
        ref={documentInputRef}
        type="file"
        accept={DOCUMENT_ACCEPT}
        disabled={disabled}
        className="sr-only"
        onChange={(event) => choose(event.target.files?.[0])}
      />
      {error && <p role="alert" className="text-xs text-rose-300">{error}</p>}

      {sourcePickerOpen && (
        <ModalPortal>
          <div className="fixed inset-0 z-[100] flex items-end justify-center sm:items-center sm:p-4">
            <button
              type="button"
              aria-label="Close attachment options"
              className="absolute inset-0 bg-black/60 backdrop-blur-sm"
              onClick={() => setSourcePickerOpen(false)}
            />
            <div role="dialog" aria-modal="true" aria-labelledby="attachment-source-title" className="relative z-10 w-full rounded-t-3xl border border-[var(--border)] bg-[var(--modal-bg)] p-5 shadow-2xl sm:max-w-sm sm:rounded-2xl">
              <div className="mx-auto mb-4 h-1 w-10 rounded-full bg-[var(--border)] sm:hidden" />
              <h3 id="attachment-source-title" className="text-base font-semibold text-[var(--text-heading)]">Add supporting evidence</h3>
              <p className="mt-1 text-xs text-[var(--text-muted)]">Choose where you want to get the attachment from.</p>

              <div className="mt-4 space-y-2">
                <SourceOption
                  title="Take photo"
                  description="Open your phone camera"
                  icon="camera"
                  onClick={() => { setSourcePickerOpen(false); cameraInputRef.current?.click(); }}
                />
                <SourceOption
                  title="Choose from gallery"
                  description="Select a photo already on your phone"
                  icon="gallery"
                  onClick={() => { setSourcePickerOpen(false); galleryInputRef.current?.click(); }}
                />
                <SourceOption
                  title="Choose document"
                  description="Select a PDF, Word, or Excel file"
                  icon="document"
                  onClick={() => { setSourcePickerOpen(false); documentInputRef.current?.click(); }}
                />
              </div>

              <button type="button" onClick={() => setSourcePickerOpen(false)} className="mt-3 w-full rounded-xl px-4 py-3 text-sm font-semibold text-[var(--text-secondary)] hover:bg-[var(--surface-glass-hover)]">Cancel</button>
            </div>
          </div>
        </ModalPortal>
      )}
    </div>
  );
}

function SourceOption({ title, description, icon, onClick }: { title: string; description: string; icon: "camera" | "gallery" | "document"; onClick: () => void }) {
  return (
    <button type="button" onClick={onClick} className="flex w-full items-center gap-3 rounded-xl border border-[var(--border)] bg-[var(--surface-glass)] p-3 text-left transition-colors hover:border-[var(--accent)] hover:bg-[var(--surface-glass-hover)]">
      <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-[var(--accent-glow)] text-[var(--accent)]" aria-hidden="true">
        {icon === "camera" ? "●" : icon === "gallery" ? "▧" : "▤"}
      </span>
      <span className="min-w-0">
        <span className="block text-sm font-semibold text-[var(--text-primary)]">{title}</span>
        <span className="block text-xs text-[var(--text-muted)]">{description}</span>
      </span>
    </button>
  );
}
