import { formatDay } from "../../lib/dates.ts";
import { formatFileSize } from "../../components/ui";
import type { DocumentRequirement } from "./types.ts";

/** The two lists on the tab: documents still needed, and documents that are done (uploaded or not needed). */
export function splitDocuments(requirements: DocumentRequirement[]) {
  return {
    needed: requirements.filter((r) => r.status === "Needed"),
    done: requirements.filter((r) => r.status !== "Needed"),
  };
}

/** The grey line under a done row: the file and its day, or the reason it is not needed. */
export function documentDetail(requirement: DocumentRequirement): string | null {
  if (requirement.status === "NotNeeded") return requirement.notNeededReason ?? null;
  const file = requirement.latestVersion;
  if (!file) return null;
  return `${file.originalFileName} · ${formatDay(file.uploadedAt)}`;
}

/** "240 KB · Uploaded Apr 20, 2026 by admin" under the file name in the View popup. */
export function fileLine(file: { fileSize: number; uploadedAt: string; uploadedByName?: string | null }): string {
  const by = file.uploadedByName ? ` by ${file.uploadedByName}` : "";
  return `${formatFileSize(file.fileSize)} · Uploaded ${formatDay(file.uploadedAt)}${by}`;
}
