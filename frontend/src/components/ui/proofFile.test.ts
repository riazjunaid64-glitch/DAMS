import { describe, expect, it } from "vitest";
import { PROOF_EMPTY, PROOF_MAX_BYTES, PROOF_TOO_LARGE, PROOF_WRONG_TYPE, formatFileSize, isImageName, proofFileError } from "./proofFile.ts";

describe("proofFileError", () => {
  it("accepts PDF, image, Word and Excel up to 15 MB", () => {
    for (const name of ["slip.pdf", "photo.JPG", "shot.png", "scan.webp", "note.doc", "note.docx", "sheet.xls", "sheet.XLSX"]) {
      expect(proofFileError({ name, size: 1000 })).toBeNull();
    }
    expect(proofFileError({ name: "big.pdf", size: PROOF_MAX_BYTES })).toBeNull();
  });
  it("stops a file over 15 MB", () => {
    expect(proofFileError({ name: "big.pdf", size: PROOF_MAX_BYTES + 1 })).toBe(PROOF_TOO_LARGE);
  });
  it("stops other types, and names with no type", () => {
    expect(proofFileError({ name: "run.exe", size: 10 })).toBe(PROOF_WRONG_TYPE);
    expect(proofFileError({ name: "archive.zip", size: 10 })).toBe(PROOF_WRONG_TYPE);
    expect(proofFileError({ name: "pdf", size: 10 })).toBe(PROOF_WRONG_TYPE);
    expect(proofFileError({ name: "gif.gif", size: 10 })).toBe(PROOF_WRONG_TYPE);
  });
  it("stops an empty file", () => {
    expect(proofFileError({ name: "slip.pdf", size: 0 })).toBe(PROOF_EMPTY);
  });
  it("judges the type before the size, as the server does", () => {
    expect(proofFileError({ name: "big.exe", size: PROOF_MAX_BYTES + 1 })).toBe(PROOF_WRONG_TYPE);
  });
});

describe("file names and sizes", () => {
  it("tells images from documents", () => {
    expect(isImageName("transfer-slip.JPG")).toBe(true);
    expect(isImageName("cheque.pdf")).toBe(false);
  });
  it("writes sizes the way the design does", () => {
    expect(formatFileSize(512)).toBe("512 B");
    expect(formatFileSize(240 * 1024)).toBe("240 KB");
    expect(formatFileSize(1.5 * 1024 * 1024)).toBe("1.5 MB");
  });
});
