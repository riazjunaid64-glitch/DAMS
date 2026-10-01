import { useEffect, useRef, useState, type ReactNode } from "react";
import { apiUpload } from "../../api/api.ts";
import { Button, EmptyState, IconFile, IconPlus, Notice, fileRuleError, useToast } from "../../components/ui";
import { AddDocumentDialog } from "./AddDocumentDialog.tsx";
import { DocumentRow, type RowUpload } from "./DocumentRow.tsx";
import { uploadFailure } from "./documentApi.ts";
import { DOCUMENT_RULE } from "./documentFile.ts";
import { splitDocuments } from "./documentRows.ts";
import { NotNeededDialog } from "./NotNeededDialog.tsx";
import type { DocumentChecklist, DocumentRequirement } from "./types.ts";
import { ViewDocumentDialog } from "./ViewDocumentDialog.tsx";

type Props = {
  customerId: number;
  checklist: DocumentChecklist | null;
  loading: boolean;
  error: string | null;
  onRefresh: () => Promise<void>;
};

function Section({ title, count, children }: { title: string; count: number; children: ReactNode }) {
  return (
    <section aria-label={title} className="overflow-hidden rounded-card border border-line bg-card font-ui">
      <h2 className="m-0 border-b border-line-soft px-4 py-3 text-body font-extrabold text-ink md:px-5">
        {title} <span className="font-bold text-ink-muted">{count}</span>
      </h2>
      <ul className="m-0 flex list-none flex-col divide-y divide-line-soft p-0">{children}</ul>
    </section>
  );
}

/** The Documents tab: Still needed and Done, one action per row, and Add document for extras. */
export default function CustomerDocumentsPanel({ customerId, checklist, loading, error, onRefresh }: Props) {
  const toast = useToast();
  const [uploads, setUploads] = useState<Record<number, RowUpload>>({});
  const [adding, setAdding] = useState(false);
  const [viewingId, setViewingId] = useState<number | null>(null);
  const [notNeeded, setNotNeeded] = useState<DocumentRequirement | null>(null);
  const controllers = useRef(new Map<number, AbortController>());

  // Leaving the tab stops anything still going up.
  useEffect(() => {
    const running = controllers.current;
    return () => running.forEach((controller) => controller.abort());
  }, []);

  const setUpload = (id: number, upload: RowUpload | null) =>
    setUploads((current) => {
      const next = { ...current };
      if (upload) next[id] = upload;
      else delete next[id];
      return next;
    });

  const upload = async (requirement: DocumentRequirement, file: File) => {
    const problem = fileRuleError(file, DOCUMENT_RULE);
    if (problem) {
      setUpload(requirement.id, { fileName: file.name, progress: null, error: problem });
      return;
    }
    const controller = new AbortController();
    controllers.current.set(requirement.id, controller);
    setUpload(requirement.id, { fileName: file.name, progress: 0, error: null });
    try {
      const form = new FormData();
      form.append("file", file);
      form.append("concurrencyToken", requirement.concurrencyToken);
      const response = await apiUpload(
        `/api/customer-documents/customers/${customerId}/requirements/${requirement.id}/upload`,
        form,
        (percent) => setUpload(requirement.id, { fileName: file.name, progress: percent, error: null }),
        controller.signal,
      );
      if (!response.ok) {
        setUpload(requirement.id, { fileName: file.name, progress: null, error: await uploadFailure(response) });
        return;
      }
      toast.success("Document uploaded");
      await onRefresh();
      setUpload(requirement.id, null);
    } catch (caught) {
      if (caught instanceof DOMException && caught.name === "AbortError") setUpload(requirement.id, null);
      else setUpload(requirement.id, { fileName: file.name, progress: null, error: "The document could not be uploaded. Try again." });
    } finally {
      controllers.current.delete(requirement.id);
    }
  };

  if (loading && !checklist) return <p className="py-10 text-center text-sm text-ink-muted">Loading documents…</p>;
  if (error && !checklist) return <Notice tone="red" role="alert" title={error} />;
  if (!checklist) return null;

  const { needed, done } = splitDocuments(checklist.requirements);
  const viewing = checklist.requirements.find((r) => r.id === viewingId) ?? null;

  const row = (requirement: DocumentRequirement) => (
    <DocumentRow
      key={requirement.id}
      requirement={requirement}
      upload={uploads[requirement.id]}
      onPick={(file) => void upload(requirement, file)}
      onStop={() => controllers.current.get(requirement.id)?.abort()}
      onView={() => setViewingId(requirement.id)}
      onNotNeeded={() => setNotNeeded(requirement)}
    />
  );

  return (
    <div className="flex flex-col gap-4">
      {error && <Notice tone="red" role="alert" title={error} />}
      <div className="flex md:justify-end">
        <Button variant="outline" icon={<IconPlus size={16} />} className="max-md:w-full" onClick={() => setAdding(true)}>
          Add document
        </Button>
      </div>

      {needed.length === 0 && done.length === 0 && (
        <EmptyState icon={<IconFile size={26} />} title="No documents yet" message="Use Add document to upload one." />
      )}
      {needed.length > 0 && <Section title="Still needed" count={needed.length}>{needed.map(row)}</Section>}
      {done.length > 0 && <Section title="Done" count={done.length}>{done.map(row)}</Section>}

      {adding && (
        <AddDocumentDialog
          customerId={customerId}
          customerName={checklist.customerName}
          types={checklist.availableTypes}
          onClose={() => setAdding(false)}
          onSaved={onRefresh}
        />
      )}
      {viewing && (
        <ViewDocumentDialog
          customerId={customerId}
          requirement={viewing}
          onClose={() => setViewingId(null)}
          onReplace={(file) => {
            setViewingId(null);
            void upload(viewing, file);
          }}
        />
      )}
      {notNeeded && (
        <NotNeededDialog customerId={customerId} requirement={notNeeded} onClose={() => setNotNeeded(null)} onSaved={onRefresh} />
      )}
    </div>
  );
}
