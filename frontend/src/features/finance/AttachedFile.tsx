import type { FinanceAttachmentInfo } from "../../api/financeAttachments.ts";
import { IconDownload, IconPaperclip } from "../../components/ui";

/**
 * A movement's attachment cell: the gold "Attached" pill that opens the file and a download arrow
 * beside it, or a grey "None".
 */
export function AttachedFile({ attachment, onOpen }: { attachment: FinanceAttachmentInfo | null; onOpen: (download: boolean) => void }) {
  if (!attachment) return <span className="text-small text-ink-faint">None</span>;
  return (
    <span className="inline-flex items-center gap-1.5 whitespace-nowrap">
      <button
        type="button"
        onClick={() => onOpen(false)}
        title={attachment.fileName}
        className="inline-flex h-6 cursor-pointer items-center gap-1 rounded-full border border-gold-line bg-gold-soft px-2.5 text-label font-bold text-gold-text focus-visible:outline-2 focus-visible:outline-primary"
      >
        <IconPaperclip size={12} />
        Attached
      </button>
      <button
        type="button"
        aria-label={`Download ${attachment.fileName}`}
        onClick={() => onOpen(true)}
        className="flex size-8 cursor-pointer items-center justify-center rounded-lg border-0 bg-transparent text-ink-2 hover:bg-page focus-visible:outline-2 focus-visible:outline-primary"
      >
        <IconDownload size={14} />
      </button>
    </span>
  );
}
