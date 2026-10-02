import { useId, useRef, useState, type DragEvent, type ReactNode } from "react";
import { cx } from "./cx.ts";
import { FieldShell } from "./FieldShell.tsx";
import { IconClose, IconCheck, IconFile, IconImage, IconUpload } from "./icons.tsx";
import { PROOF_RULE, fileRuleError, formatFileSize, isImageName, proofFileDetail, type FileRule } from "./proofFile.ts";
import { useIsPhone } from "./useMediaQuery.ts";

export type AttachProofFile = {
  name: string;
  size?: number;
  /** True once the file is stored on the server; false for a file picked but not sent yet. */
  uploaded?: boolean;
  /** Makes the name a link that opens the file. */
  onOpen?: () => void;
  /** Downloads the saved file. Together with `onReplace`, this turns on the saved-file card. */
  onDownload?: () => void;
};

export type AttachProofProps = {
  /** Field label; "(optional)" is added unless the field is `required` — proof never blocks saving. */
  label?: ReactNode;
  /** Marks the field required (a * instead of "(optional)"), for a document that must be chosen. */
  required?: boolean;
  /** The types and size accepted; proof by default (PDF, image, Word or Excel, up to 15 MB). */
  rule?: FileRule;
  /** The current file, if any. */
  file: AttachProofFile | null;
  /** A file that passed the type and size check. */
  onPick: (file: File) => void;
  /**
   * Saved-file card: a file chosen to replace the one already stored. `null` clears that choice
   * (the user switched to Remove). The saved file stays until the page saves.
   */
  onReplace?: (file: File | null) => void;
  /**
   * Saved-file card: true while Remove is waiting for Save, false on Undo or when a replacement
   * is picked. Never true at the same time as a replacement file.
   */
  onPendingRemove?: (pending: boolean) => void;
  /** The × on the file card: stops an upload or removes the file before saving. */
  onRemove?: () => void;
  /** 0–100 while the file is uploading; leave it out otherwise. */
  progress?: number | null;
  /** A problem to show in the zone, e.g. the upload failed. A file that fails the check shows its own message. */
  error?: string | null;
  disabled?: boolean;
  id?: string;
  className?: string;
};

const cardIcon = "flex size-10 shrink-0 items-center justify-center rounded-lg bg-gold-soft text-gold-text";
const textButton = "min-h-11 cursor-pointer border-0 bg-transparent p-0 text-left text-small font-bold underline-offset-4 hover:underline focus-visible:underline focus-visible:outline-none disabled:cursor-not-allowed disabled:text-ink-faint disabled:no-underline md:min-h-0";

/**
 * The one "Attach proof" upload field: a dashed drop zone (tap-to-upload on a phone), then a file
 * card while uploading and once uploaded, or a red zone when the file is refused. One file. The type
 * and size are checked here, before anything is sent: PDF, image, Word or Excel, up to 15 MB by
 * default, or whatever a `rule` says (customer documents). A phone's own picker offers the camera, the gallery and files.
 */
export function AttachProof({ label = "Proof", required = false, rule = PROOF_RULE, file, onPick, onReplace, onPendingRemove, onRemove, progress, error, disabled = false, id, className }: AttachProofProps) {
  const autoId = useId();
  const inputId = id ?? `ap-${autoId}`;
  const inputRef = useRef<HTMLInputElement>(null);
  const [refusal, setRefusal] = useState<string | null>(null);
  const [dragging, setDragging] = useState(false);
  const [replacement, setReplacement] = useState<File | null>(null);
  const [pendingRemove, setPendingRemove] = useState(false);
  const isPhone = useIsPhone();
  const problem = refusal ?? error ?? null;
  const uploading = file !== null && progress !== undefined && progress !== null;
  const savedKey = file?.uploaded ? file.name : "";
  const [seenSaved, setSeenSaved] = useState(savedKey);
  if (seenSaved !== savedKey) {
    setSeenSaved(savedKey);
    setReplacement(null);
    setPendingRemove(false);
  }
  const savedActions = Boolean(file?.uploaded && !uploading && file.onDownload && onReplace);

  const choose = (picked: File | undefined) => {
    if (!picked) return;
    const message = fileRuleError(picked, rule);
    if (inputRef.current) inputRef.current.value = "";
    if (message) {
      setRefusal(message);
      return;
    }
    setRefusal(null);
    if (savedActions) {
      setPendingRemove(false);
      setReplacement(picked);
      onPendingRemove?.(false);
      onReplace?.(picked);
    }
    onPick(picked);
  };

  const markRemove = () => {
    setReplacement(null);
    setPendingRemove(true);
    setRefusal(null);
    onReplace?.(null);
    onPendingRemove?.(true);
  };

  const undoRemove = () => {
    setPendingRemove(false);
    onPendingRemove?.(false);
  };

  const onDragOver = (event: DragEvent<HTMLElement>) => {
    if (disabled || isPhone) return;
    event.preventDefault();
    setDragging(true);
  };
  const onDragLeave = (event: DragEvent<HTMLElement>) => {
    if (!event.currentTarget.contains(event.relatedTarget as Node | null)) setDragging(false);
  };
  const onDrop = (event: DragEvent<HTMLElement>) => {
    event.preventDefault();
    setDragging(false);
    if (!disabled) choose(event.dataTransfer.files[0]);
  };

  const fieldLabel = required ? label : (
    <>
      {label} <span className="font-normal normal-case tracking-normal text-ink-faint">(optional)</span>
    </>
  );

  const percent = Math.min(100, Math.max(0, Math.round(progress ?? 0)));

  return (
    <FieldShell as="fieldset" label={fieldLabel} required={required} className={className}>
      <input
        ref={inputRef}
        id={inputId}
        type="file"
        accept={rule.accept}
        disabled={disabled}
        className="sr-only"
        tabIndex={-1}
        onChange={(event) => choose(event.target.files?.[0])}
      />
      {file && pendingRemove && savedActions ? (
        <div className="flex min-w-0 items-center justify-between gap-3 rounded-field border border-line-input bg-card px-3 py-2.5">
          <p className="m-0 min-w-0 truncate text-small text-ink" title={`${file.name} will be removed when you save.`}>
            {file.name} will be removed when you save.
          </p>
          <button type="button" disabled={disabled} onClick={undoRemove} className={cx(textButton, "shrink-0 text-gold-text")}>Undo</button>
        </div>
      ) : file && replacement && savedActions ? (
        <div className="flex min-w-0 items-center gap-3 rounded-field border border-line-input bg-card p-2.5">
          <span className={cardIcon}>{isImageName(replacement.name) ? <IconImage size={20} /> : <IconFile size={20} />}</span>
          <div className="min-w-0 flex-1">
            <p title={replacement.name} className="m-0 truncate text-body font-extrabold text-ink">{replacement.name}</p>
            <p className="m-0 text-small text-ink-muted">{proofFileDetail(replacement.name, replacement.size)}</p>
            <p className="m-0 text-small font-bold text-ink-2">Will replace the saved file</p>
          </div>
          <div className="flex shrink-0 flex-col items-end">
            <button type="button" disabled={disabled} onClick={() => inputRef.current?.click()} className={cx(textButton, "text-gold-text")}>Replace</button>
            <button type="button" disabled={disabled} onClick={markRemove} className={cx(textButton, "text-danger")}>Remove</button>
          </div>
        </div>
      ) : file && savedActions ? (
        <div className="flex min-w-0 items-center gap-3 rounded-field border border-line-input bg-card p-2.5">
          <span className={cardIcon}>{isImageName(file.name) ? <IconImage size={20} /> : <IconFile size={20} />}</span>
          <div className="min-w-0 flex-1">
            <p title={file.name} className="m-0 truncate text-body font-extrabold text-ink">{file.name}</p>
            <p className="m-0 text-small text-ink-muted">{proofFileDetail(file.name, file.size)}</p>
          </div>
          <div className="flex shrink-0 flex-col items-end">
            <button type="button" disabled={disabled || !file.onOpen} title={file.name} aria-label={`View ${file.name}`} onClick={file.onOpen} className={cx(textButton, "text-gold-text")}>View</button>
            <button type="button" disabled={disabled || !file.onDownload} onClick={file.onDownload} className={cx(textButton, "text-gold-text")}>Download</button>
            <button type="button" disabled={disabled} onClick={() => inputRef.current?.click()} className={cx(textButton, "text-gold-text")}>Replace</button>
            <button type="button" disabled={disabled} onClick={markRemove} className={cx(textButton, "text-danger")}>Remove</button>
          </div>
        </div>
      ) : file ? (
        <div className="flex min-w-0 items-center gap-3 rounded-field border border-line-input bg-card p-2.5">
          <span className={cardIcon}>{isImageName(file.name) ? <IconImage size={20} /> : <IconFile size={20} />}</span>
          <div className="min-w-0 flex-1">
            {file.onOpen ? (
              <button type="button" onClick={file.onOpen} title={file.name} className="block max-w-full cursor-pointer truncate border-0 bg-transparent p-0 text-left text-body font-extrabold text-ink outline-none hover:underline focus-visible:underline">
                {file.name}
              </button>
            ) : (
              <p title={file.name} className="m-0 truncate text-body font-extrabold text-ink">{file.name}</p>
            )}
            {uploading ? (
              <>
                <p className="m-0 text-small text-ink-muted">Uploading · {percent}%</p>
                <div role="progressbar" aria-label="Upload progress" aria-valuemin={0} aria-valuemax={100} aria-valuenow={percent} className="mt-1 h-1 overflow-hidden rounded-full bg-line-soft">
                  <div className="h-full rounded-full bg-primary transition-[width]" style={{ width: `${percent}%` }} />
                </div>
              </>
            ) : file.uploaded === false ? (
              <p className="m-0 text-small font-bold text-ink-muted">Ready to upload{file.size !== undefined && ` · ${formatFileSize(file.size)}`}</p>
            ) : (
              <p className="m-0 flex items-center gap-1 whitespace-nowrap text-small font-bold text-success">
                <IconCheck size={14} className="shrink-0" />
                <span className="truncate">Uploaded{file.size !== undefined && ` · ${formatFileSize(file.size)}`}</span>
              </p>
            )}
          </div>
          {onRemove && (
            <button
              type="button"
              aria-label={uploading ? "Stop upload" : "Remove file"}
              disabled={disabled && !uploading}
              onClick={onRemove}
              className="flex size-11 shrink-0 cursor-pointer items-center justify-center rounded-field border border-line-input bg-card text-ink-2 outline-none hover:bg-page focus-visible:ring-3 focus-visible:ring-primary-ring disabled:cursor-not-allowed disabled:opacity-45 md:size-10"
            >
              <IconClose size={16} />
            </button>
          )}
        </div>
      ) : (
        <button
          type="button"
          disabled={disabled}
          onClick={() => inputRef.current?.click()}
          onDragOver={onDragOver}
          onDragEnter={onDragOver}
          onDragLeave={onDragLeave}
          onDrop={onDrop}
          className={cx(
            "flex w-full cursor-pointer flex-col items-center gap-2 rounded-field border px-4 py-5 text-center outline-none transition-colors focus-visible:ring-3 focus-visible:ring-primary-ring disabled:cursor-not-allowed disabled:opacity-60",
            dragging
              ? "border-solid border-primary bg-selected"
              : problem
                ? "border-dashed border-danger bg-danger-soft"
                : "border-dashed border-line-input bg-page hover:border-ink-faint",
          )}
        >
          <span className="flex size-10 items-center justify-center rounded-full bg-gold-soft text-gold-text">
            <IconUpload size={18} />
          </span>
          <span className="text-body font-extrabold text-ink">
            {dragging ? (
              "Drop the file to upload"
            ) : isPhone ? (
              <>Tap to <span className="text-gold-text underline">upload</span> a photo or file</>
            ) : (
              <>Drag a file here or <span className="text-gold-text underline">browse</span></>
            )}
          </span>
          {problem ? (
            <span role="alert" className="text-small font-bold text-danger">{problem}</span>
          ) : (
            <span className="text-small text-ink-muted">{rule.hint}</span>
          )}
        </button>
      )}
      {savedActions && problem && <p role="alert" className="m-0 text-small font-bold text-danger">{problem}</p>}
    </FieldShell>
  );
}
