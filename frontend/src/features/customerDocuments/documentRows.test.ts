import { describe, expect, it } from "vitest";
import { DOCUMENT_RULE } from "./documentFile.ts";
import { documentDetail, fileLine, splitDocuments } from "./documentRows.ts";
import { fileRuleError } from "../../components/ui";
import type { DocumentRequirement } from "./types.ts";

const row = (over: Partial<DocumentRequirement>): DocumentRequirement => ({
  id: 1, name: "CNIC front", isRequired: true, displayOrder: 1, status: "Needed", updatedAt: "2026-04-20T07:00:00",
  concurrencyToken: "t", versions: [], hasMoreVersions: false, ...over,
});
const file = { id: 3, versionNumber: 1, isCurrent: true, originalFileName: "cnic-front.jpg", contentType: "image/jpeg", fileSize: 240 * 1024, uploadedAt: "2026-04-20T07:00:00", uploadedByName: "admin" };

describe("the document lists", () => {
  it("puts Needed in Still needed and Uploaded and Not needed in Done", () => {
    const rows = [row({ id: 1 }), row({ id: 2, status: "Uploaded" }), row({ id: 3, status: "NotNeeded" })];
    const { needed, done } = splitDocuments(rows);
    expect(needed.map((r) => r.id)).toEqual([1]);
    expect(done.map((r) => r.id)).toEqual([2, 3]);
  });

  it("describes a done row by its file and day, or by the reason", () => {
    expect(documentDetail(row({ status: "Uploaded", latestVersion: file }))).toBe("cnic-front.jpg · Apr 20, 2026");
    expect(documentDetail(row({ status: "NotNeeded", notNeededReason: "Lives with parents" }))).toBe("Lives with parents");
    expect(documentDetail(row({}))).toBeNull();
  });

  it("writes the size, day and person under the file name", () => {
    expect(fileLine(file)).toBe("240 KB · Uploaded Apr 20, 2026 by admin");
  });
});

describe("the one document file rule", () => {
  it("accepts PDF, JPG, JPEG and PNG up to 10 MB", () => {
    for (const name of ["a.pdf", "b.JPG", "c.jpeg", "d.png"]) expect(fileRuleError({ name, size: 1000 }, DOCUMENT_RULE)).toBeNull();
    expect(fileRuleError({ name: "a.pdf", size: 10 * 1024 * 1024 }, DOCUMENT_RULE)).toBeNull();
  });
  it("stops a file over 10 MB, other types and an empty file", () => {
    expect(fileRuleError({ name: "a.pdf", size: 10 * 1024 * 1024 + 1 }, DOCUMENT_RULE)).toBe("That file is over 10 MB. Choose a smaller one.");
    expect(fileRuleError({ name: "a.docx", size: 10 }, DOCUMENT_RULE)).toBe("This file type isn't allowed. Use PDF, JPG or PNG.");
    expect(fileRuleError({ name: "a.webp", size: 10 }, DOCUMENT_RULE)).not.toBeNull();
    expect(fileRuleError({ name: "a.pdf", size: 0 }, DOCUMENT_RULE)).toBe("That file is empty. Choose another one.");
  });
});
