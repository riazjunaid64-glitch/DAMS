import { useId, useRef, useState, type DragEvent, type ReactNode } from "react";
import { cx } from "./cx.ts";
import { FieldShell } from "./FieldShell.tsx";
import { IconClose, IconCheck, IconFile, IconImage, IconUpload } from "./icons.tsx";
import { PROOF_RULE, fileRuleError, formatFileSize, isImageName, type FileRule } from "./proofFile.ts";
import { useIsPhone } from "./useMediaQuery.ts";

export type AttachProofFile = {
  name: string;
  size?: number;
  /** True once the file is stored on the server; false for a file picked but not sent yet. */
  uploaded?: boolean;
  /** Makes the name a link that opens the file. */
  onOpen?: () => void;
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

/**
 * The one "Attach proof" upload field: a dashed drop zone (tap-to-upload on a phone), then a file
 * card while uploading and once uploaded, or a red zone when the file is refused. One file. The type
 * and size are checked here, before anything is sent: PDF, image, Word or Excel, up to 15 MB by
 * default, or whatever a `rule` says (customer documents). A phone's own picker offers the camera, the gallery and files.
 */
export function AttachProof({ label = "Proof", required = false, rule = PROOF_RULE, file, onPick, onRemove, progress, error, disabled = false, id, className }: AttachProofProps) {
  const autoId = useId();
  const inputId = id ?? `ap-${autoId}`;
  const inputRef = useRef<HTMLInputElement>(null);
  const [refusal, setRefusal] = useState<string | null>(null);
  const [dragging, setDragging] = useState(false);
  const isPhone = useIsPhone();
  const problem = refusal ?? error ?? null;
  const uploading = file !== null && progress !== undefined && progress !== null;

  const choose = (picked: File | undefined) => {
    if (!picked) return;
    const message = fileRuleError(picked, rule);
    if (inputRef.current) inputRef.current.value = "";
    if (message) {
      setRefusal(message);
      return;
    }
    setRefusal(null);
    onPick(picked);
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
      {file ? (
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
    </FieldShell>
  );
}
