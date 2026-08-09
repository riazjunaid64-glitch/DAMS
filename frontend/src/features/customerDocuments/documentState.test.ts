import { describe, expect, it } from "vitest";
import { canOverride, matchesDocumentFilter, statusLabel, uploadAllowed } from "./documentState.ts";
import type { DocumentRequirement } from "./types.ts";

const requirement = (status: DocumentRequirement["status"]): DocumentRequirement => ({
  id: 1, categoryIsActive: true, name: "CNIC Front", isRequired: true, displayOrder: 1,
  allowedFileTypes: [".pdf"], maxFileSizeBytes: 1024, status, updatedAt: "2026-08-04",
  concurrencyToken: "token", versions: [], hasMoreVersions: false,
});

describe("customer document UI state", () => {
  it("groups requested with missing and received with review", () => {
    expect(matchesDocumentFilter(requirement("Requested"), "missing")).toBe(true);
    expect(matchesDocumentFilter(requirement("Received"), "review")).toBe(true);
    expect(matchesDocumentFilter(requirement("Approved"), "missing")).toBe(false);
  });

  it("offers replacement uploads without allowing invalid under-review uploads", () => {
    expect(uploadAllowed("ReplacementRequired")).toBe(true);
    expect(uploadAllowed("Approved")).toBe(true);
    expect(uploadAllowed("UnderReview")).toBe(false);
  });

  it("keeps terminal completion overrides controlled", () => {
    expect(canOverride("Missing")).toBe(true);
    expect(canOverride("Approved")).toBe(false);
    expect(statusLabel("NotApplicable")).toBe("Not Applicable");
  });
});
