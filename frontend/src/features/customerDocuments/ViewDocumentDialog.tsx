import { useRef, useState } from "react";
import { Button, IconDownload, IconUpload, Modal, useToast } from "../../components/ui";
import { formatDay } from "../../lib/dates.ts";
import { DOCUMENT_RULE } from "./documentFile.ts";
import { downloadDocument } from "./documentApi.ts";
import { fileLine } from "./documentRows.ts";
import { FilePreview } from "./FilePreview.tsx";
import type { DocumentRequirement } from "./types.ts";

type Props = {
  customerId: number;
  requirement: DocumentRequirement;
  onClose: () => void;
  /** Sends the new file for this document; the popup closes and the row shows the progress. */
  onReplace: (file: File) => void;
};

/** The View popup: the file inside the popup, Replace file (the old file moves to Older files) and Download. */
export function ViewDocumentDialog({ customerId, requirement, onClose, onReplace }: Props) {
  const toast = useToast();
  const input = useRef<HTMLInputElement>(null);
  const [picked, setPicked] = useState<number | null>(null);
  const [downloading, setDownloading] = useState(false);
  const current = requirement.latestVersion ?? requirement.versions[0];
  const shown = requirement.versions.find((v) => v.id === picked) ?? current;
  const older = requirement.versions.filter((v) => v.id !== current?.id);
  if (!current || !shown) return null;

  const download = async () => {
    setDownloading(true);
    try {
      await downloadDocument(customerId, requirement.id, shown.id, shown.originalFileName);
    } catch (caught) {
      toast.error(caught instanceof Error ? caught.message : "The document could not be downloaded.");
    } finally {
      setDownloading(false);
    }
  };

  return (
    <Modal
      open
      onClose={onClose}
      title={requirement.name}
      size="lg"
      phoneLayout="fullscreen"
      cancelLabel={null}
      footer={
        <div className="flex gap-3 max-md:grid max-md:grid-cols-2 md:justify-end">
          <Button variant="outline" size="md" icon={<IconUpload size={16} />} onClick={() => input.current?.click()}>Replace file</Button>
          <Button size="md" icon={<IconDownload size={16} />} loading={downloading} onClick={() => void download()}>Download</Button>
        </div>
      }
    >
      <input
        ref={input}
        type="file"
        accept={DOCUMENT_RULE.accept}
        aria-label="Choose the new file"
        className="sr-only"
        tabIndex={-1}
        onChange={(event) => {
          const file = event.target.files?.[0];
          event.target.value = "";
          if (file) onReplace(file);
        }}
      />
      <FilePreview customerId={customerId} requirementId={requirement.id} version={shown} />
      <p className="m-0 mt-3 break-words text-body font-extrabold text-ink">{shown.originalFileName}</p>
      <p className="m-0 text-small text-ink-muted">{fileLine(shown)}</p>
      {older.length > 0 && (
        <section className="mt-4 border-t border-line-soft pt-3" aria-label="Older files">
          <h3 className="m-0 mb-1 text-caption font-bold uppercase tracking-[0.4px] text-ink-muted">Older files</h3>
          <ul className="m-0 flex list-none flex-col p-0">
            {older.map((file) => (
              <li key={file.id} className="flex items-center justify-between gap-3 py-1.5">
                <span className="min-w-0 truncate text-small text-ink-2">{file.originalFileName} · {formatDay(file.uploadedAt)}</span>
                <Button variant="link" className="shrink-0 text-small font-extrabold" onClick={() => setPicked(file.id)}>View</Button>
              </li>
            ))}
          </ul>
        </section>
      )}
    </Modal>
  );
}
