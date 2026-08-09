import type { DocumentRequirement, DocumentStatus } from "./types.ts";

export type DocumentFilter = "all" | "missing" | "review" | "approved" | "rejected" |
  "replacement" | "postponed" | "waived" | "notApplicable" | "expired";

export function statusLabel(status: string): string {
  return status.replace(/([a-z])([A-Z])/g, "$1 $2");
}

export function matchesDocumentFilter(requirement: DocumentRequirement, filter: DocumentFilter): boolean {
  const status = requirement.status;
  if (filter === "all") return true;
  if (filter === "missing") return status === "Missing" || status === "Requested";
  if (filter === "review") return status === "Received" || status === "UnderReview";
  if (filter === "replacement") return status === "ReplacementRequired";
  if (filter === "notApplicable") return status === "NotApplicable";
  return status.toLowerCase() === filter.toLowerCase();
}

export function uploadAllowed(status: DocumentStatus): boolean {
  return ["Missing", "Requested", "Rejected", "ReplacementRequired", "Postponed", "Expired", "Approved"].includes(status);
}

export function canOverride(status: DocumentStatus): boolean {
  return !["Approved", "Waived", "NotApplicable"].includes(status);
}

export function statusTone(status: DocumentStatus): string {
  if (["Approved", "Waived", "NotApplicable"].includes(status)) return "emerald";
  if (["Rejected", "ReplacementRequired", "Expired"].includes(status)) return "rose";
  if (["UnderReview", "Received"].includes(status)) return "amber";
  if (status === "Postponed") return "violet";
  return "slate";
}
