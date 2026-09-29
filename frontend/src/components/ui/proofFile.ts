/*
 * The rules an "Attach proof" file must pass in the browser before it is sent. They mirror the
 * server's (FinanceAttachmentFileValidator): PDF, JPG, PNG, WebP, Word or Excel, up to 15 MB.
 */

export const PROOF_MAX_BYTES = 15 * 1024 * 1024;
export const PROOF_EXTENSIONS = [".pdf", ".jpg", ".jpeg", ".png", ".webp", ".doc", ".docx", ".xls", ".xlsx"] as const;
/** What the file picker offers; on a phone this includes the camera and the gallery. */
export const PROOF_ACCEPT = "image/jpeg,image/png,image/webp,.jpg,.jpeg,.png,.webp,.pdf,.doc,.docx,.xls,.xlsx";

export const PROOF_TOO_LARGE = "That file is over 15 MB. Choose a smaller one.";
export const PROOF_WRONG_TYPE = "This file type isn't allowed. Use PDF, image, Word or Excel.";
export const PROOF_EMPTY = "That file is empty. Choose another one.";

const IMAGE_EXTENSIONS = [".jpg", ".jpeg", ".png", ".webp"];

function extensionOf(name: string): string {
  const dot = name.lastIndexOf(".");
  return dot < 0 ? "" : name.slice(dot).toLowerCase();
}

/** The message to show when the file cannot be attached, or null when it can. */
export function proofFileError(file: { name: string; size: number }): string | null {
  if (!(PROOF_EXTENSIONS as readonly string[]).includes(extensionOf(file.name))) return PROOF_WRONG_TYPE;
  if (file.size === 0) return PROOF_EMPTY;
  if (file.size > PROOF_MAX_BYTES) return PROOF_TOO_LARGE;
  return null;
}

export function isImageName(name: string): boolean {
  return IMAGE_EXTENSIONS.includes(extensionOf(name));
}

/** "240 KB", "1.5 MB". */
export function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}
