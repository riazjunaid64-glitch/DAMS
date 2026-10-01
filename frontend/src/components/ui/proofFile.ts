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

/** What a file field accepts: the types, the size limit and the words shown when a file is refused. */
export type FileRule = {
  extensions: readonly string[];
  maxBytes: number;
  /** What the file picker offers; on a phone this includes the camera and the gallery. */
  accept: string;
  /** The grey line in the drop zone. */
  hint: string;
  tooLarge: string;
  wrongType: string;
  empty: string;
};

export const PROOF_RULE: FileRule = {
  extensions: PROOF_EXTENSIONS,
  maxBytes: PROOF_MAX_BYTES,
  accept: PROOF_ACCEPT,
  hint: "PDF, image, Word or Excel · up to 15 MB",
  tooLarge: PROOF_TOO_LARGE,
  wrongType: PROOF_WRONG_TYPE,
  empty: PROOF_EMPTY,
};

const IMAGE_EXTENSIONS = [".jpg", ".jpeg", ".png", ".webp"];

function extensionOf(name: string): string {
  const dot = name.lastIndexOf(".");
  return dot < 0 ? "" : name.slice(dot).toLowerCase();
}

/** The message to show when the file breaks the rule, or null when it can be used. */
export function fileRuleError(file: { name: string; size: number }, rule: FileRule): string | null {
  if (!rule.extensions.includes(extensionOf(file.name))) return rule.wrongType;
  if (file.size === 0) return rule.empty;
  if (file.size > rule.maxBytes) return rule.tooLarge;
  return null;
}

/** The message to show when the file cannot be attached, or null when it can. */
export const proofFileError = (file: { name: string; size: number }): string | null => fileRuleError(file, PROOF_RULE);

export function isImageName(name: string): boolean {
  return IMAGE_EXTENSIONS.includes(extensionOf(name));
}

/** "240 KB", "1.5 MB". */
export function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

/** "PDF", "Photo", "Word" or "Excel" — the word shown beside the size on a saved file. */
export function proofKindLabel(name: string): string {
  const ext = extensionOf(name);
  if (ext === ".pdf") return "PDF";
  if (IMAGE_EXTENSIONS.includes(ext)) return "Photo";
  if (ext === ".doc" || ext === ".docx") return "Word";
  if (ext === ".xls" || ext === ".xlsx") return "Excel";
  return "File";
}

/** "PDF · 220 KB", "Photo · 1.2 MB". The kind alone when the size is unknown. */
export function proofFileDetail(name: string, size?: number): string {
  const kind = proofKindLabel(name);
  return size === undefined ? kind : `${kind} · ${formatFileSize(size)}`;
}
