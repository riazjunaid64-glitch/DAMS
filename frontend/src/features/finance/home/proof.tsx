import type { FinanceAttachmentInfo } from "../../../api/financeAttachments.ts";
import { AttachProof } from "../../../components/ui";

export function RecordProof({
  saved,
  selected,
  disabled,
  onSelected,
  onRemoveSaved,
  onOpen,
  onDownload,
}: {
  saved: FinanceAttachmentInfo | null;
  selected: File | null;
  disabled?: boolean;
  onSelected: (file: File | null) => void;
  onRemoveSaved: (pending: boolean) => void;
  onOpen?: () => void;
  onDownload?: () => void;
}) {
  const file = selected
    ? { name: selected.name, size: selected.size, uploaded: false }
    : saved
      ? { name: saved.fileName, size: saved.fileSize, uploaded: true, onOpen, onDownload }
      : null;
  return (
    <AttachProof
      label="Attachment"
      disabled={disabled}
      file={file}
      onPick={(picked) => onSelected(picked)}
      onReplace={(picked) => {
        onSelected(picked);
        if (picked) onRemoveSaved(false);
      }}
      onPendingRemove={(pending) => {
        onRemoveSaved(pending);
        if (pending) onSelected(null);
      }}
      onRemove={() => onSelected(null)}
    />
  );
}
