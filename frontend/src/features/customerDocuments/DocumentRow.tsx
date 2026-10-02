import { useRef } from "react";
import { Button, IconClose, IconFile, IconUpload, StatusBadge, cx } from "../../components/ui";
import { DOCUMENT_RULE } from "./documentFile.ts";
import { documentDetail } from "./documentRows.ts";
import type { DocumentRequirement } from "./types.ts";

/** What the row is doing about an upload: sending (with how far), or stopped by a problem. */
export type RowUpload = { fileName: string; progress: number | null; error: string | null };

type Props = {
  requirement: DocumentRequirement;
  upload?: RowUpload;
  onPick: (file: File) => void;
  onStop: () => void;
  onView: () => void;
  onNotNeeded: () => void;
};

const tile = {
  Needed: "bg-warning-soft text-warning",
  Uploaded: "bg-success-soft text-success",
  NotNeeded: "bg-steel-soft text-steel",
} as const;

/**
 * One document on the Documents tab. The buttons depend on the status: Needed has Not needed and
 * Upload, Uploaded has View, Not needed has Upload. While a file is going up the row shows the
 * progress and a × to stop it; a refused file shows its red message and keeps the buttons.
 */
export function DocumentRow({ requirement, upload, onPick, onStop, onView, onNotNeeded }: Props) {
  const input = useRef<HTMLInputElement>(null);
  const sending = upload !== undefined && upload.progress !== null;
  const done = requirement.status !== "Needed";
  const detail = documentDetail(requirement);
  const choose = () => input.current?.click();

  const uploadButton = (
    <Button size="sm" variant={requirement.status === "Needed" ? "primary" : "outline"} icon={<IconUpload size={15} />} className="max-md:flex-1" onClick={choose}>
      Upload
    </Button>
  );

  return (
    <li className="flex flex-col gap-2 px-4 py-3.5 md:flex-row md:items-center md:gap-4 md:px-5">
      <div className="flex min-w-0 flex-1 items-start gap-3">
        <span className={cx("flex size-9 shrink-0 items-center justify-center rounded-lg", tile[requirement.status])}>
          <IconFile size={17} />
        </span>
        <div className="min-w-0 flex-1">
          <p className="m-0 flex flex-wrap items-center gap-x-2 gap-y-1 text-body font-extrabold text-ink">
            {requirement.name}
            {done && <StatusBadge status={requirement.status} />}
          </p>
          {upload?.error ? (
            <p role="alert" className="m-0 mt-0.5 text-small font-bold text-danger">{upload.error}</p>
          ) : sending ? (
            <>
              <p className="m-0 mt-0.5 text-small text-ink-muted">Uploading · {upload.progress}%</p>
              <div role="progressbar" aria-label={`Uploading ${requirement.name}`} aria-valuemin={0} aria-valuemax={100} aria-valuenow={upload.progress ?? 0} className="mt-1 h-1 max-w-[180px] overflow-hidden rounded-full bg-line-soft">
                <div className="h-full rounded-full bg-primary transition-[width]" style={{ width: `${upload.progress}%` }} />
              </div>
            </>
          ) : (
            detail && <p title={detail} className="m-0 mt-0.5 truncate text-small text-ink-muted">{detail}</p>
          )}
        </div>
      </div>

      <input
        ref={input}
        type="file"
        accept={DOCUMENT_RULE.accept}
        aria-label={`Choose a file for ${requirement.name}`}
        className="sr-only"
        tabIndex={-1}
        onChange={(event) => {
          const file = event.target.files?.[0];
          event.target.value = "";
          if (file) onPick(file);
        }}
      />

      {sending ? (
        <Button size="sm" variant="outline" iconOnly icon={<IconClose size={16} />} aria-label={`Stop uploading ${requirement.name}`} onClick={onStop} />
      ) : (
        <div className="flex items-center gap-2 max-md:pl-12 md:shrink-0 md:justify-end">
          {requirement.status === "Needed" && (
            <>
              <Button size="sm" variant="ghost" className="max-md:flex-1 max-md:border max-md:border-line-input max-md:bg-card" onClick={onNotNeeded}>
                Not needed
              </Button>
              {uploadButton}
            </>
          )}
          {requirement.status === "Uploaded" && (
            <Button size="sm" variant="outline" className="max-md:flex-1" onClick={onView}>View</Button>
          )}
          {requirement.status === "NotNeeded" && uploadButton}
        </div>
      )}
    </li>
  );
}
