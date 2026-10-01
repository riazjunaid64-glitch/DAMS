import type { FileRule } from "../../components/ui";

export const DOCUMENT_MAX_BYTES = 10 * 1024 * 1024;

/** The one file rule for every customer document: PDF, JPG or PNG, up to 10 MB. Mirrors the server. */
export const DOCUMENT_RULE: FileRule = {
  extensions: [".pdf", ".jpg", ".jpeg", ".png"],
  maxBytes: DOCUMENT_MAX_BYTES,
  accept: "image/jpeg,image/png,.jpg,.jpeg,.png,.pdf",
  hint: "PDF, JPG or PNG · up to 10 MB",
  tooLarge: "That file is over 10 MB. Choose a smaller one.",
  wrongType: "This file type isn't allowed. Use PDF, JPG or PNG.",
  empty: "That file is empty. Choose another one.",
};
