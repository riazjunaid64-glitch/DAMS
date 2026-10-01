import { useEffect, useState } from "react";
import { IconFile } from "../../components/ui";
import { fetchDocumentForView } from "./documentApi.ts";
import type { DocumentVersion } from "./types.ts";

type Props = { customerId: number; requirementId: number; version: DocumentVersion };

/**
 * The preview box in the View popup: the file itself (an image, or a PDF page) read through the
 * server's view call, so each opening is recorded. The bytes live only in this browser tab.
 */
export function FilePreview({ customerId, requirementId, version }: Props) {
  const [source, setSource] = useState<{ versionId: number; url: string } | null>(null);
  const [failure, setFailure] = useState<{ versionId: number; message: string } | null>(null);

  useEffect(() => {
    let url: string | null = null;
    let cancelled = false;
    fetchDocumentForView(customerId, requirementId, version.id)
      .then((blob) => {
        if (cancelled) return;
        url = URL.createObjectURL(blob);
        setSource({ versionId: version.id, url });
      })
      .catch((caught: unknown) => {
        if (!cancelled) setFailure({ versionId: version.id, message: caught instanceof Error ? caught.message : "The document could not be opened." });
      });
    return () => {
      cancelled = true;
      if (url) URL.revokeObjectURL(url);
    };
  }, [customerId, requirementId, version.id]);

  const ready = source?.versionId === version.id ? source.url : null;
  const problem = failure?.versionId === version.id ? failure.message : null;
  const isImage = version.contentType.startsWith("image/");

  return (
    <div className="flex h-[240px] items-center justify-center overflow-hidden rounded-field bg-page p-3 md:h-[200px]" data-testid="file-preview">
      {problem ? (
        <p role="alert" className="m-0 text-center text-small font-bold text-danger">{problem}</p>
      ) : !ready ? (
        <p className="m-0 flex items-center gap-2 text-small text-ink-muted"><IconFile size={16} /> Opening…</p>
      ) : isImage ? (
        <img src={ready} alt={version.originalFileName} className="max-h-full max-w-full object-contain" />
      ) : (
        <iframe src={ready} title={version.originalFileName} className="h-full w-full border-0 bg-white" />
      )}
    </div>
  );
}
