import { api } from "./api";

export type FinanceRecordKind = "revenue" | "expense" | "assetPurchase";

const ATTACHMENT_RESOURCE: Record<FinanceRecordKind, string> = {
  revenue: "revenue",
  expense: "expenses",
  assetPurchase: "asset-purchases",
};

export interface FinanceAttachmentInfo {
  fileName: string;
  contentType: string;
  fileSize: number;
  uploadedAt: string;
}

type ApiErrorBody = {
  message?: string;
  title?: string;
  detail?: string;
  errors?: Record<string, string[] | string>;
};

/**
 * The reason the server gave, in whichever shape it gave it.
 *
 * Controllers answer with `{ message }`, and that stays the preferred reading. But two failures
 * never reach a controller: a request the model binder rejects, and an exception the controller
 * does not catch. ASP.NET answers both with ProblemDetails — `title`, `detail`, `errors` — and
 * reading only `message` turned every one of them into a bare "could not do this", which tells the
 * operator nothing and cannot be diagnosed afterwards from a screenshot.
 *
 * When there is genuinely no reason to read, the status goes on the end. "(HTTP 500)" is not
 * pretty, but it separates a server fault from a rejected entry at a glance, and a message that
 * admits it knows nothing beats one that quietly implies the entry was at fault.
 */
async function responseMessage(response: Response, fallback: string): Promise<string> {
  const stamped = `${fallback} (HTTP ${response.status})`;
  let body: ApiErrorBody | null = null;
  try {
    body = (await response.json()) as ApiErrorBody;
  } catch {
    return stamped;
  }
  if (!body || typeof body !== "object") return stamped;
  if (body.message) return body.message;

  // Validation failures carry the useful text per field, not at the top level.
  const fieldErrors = Object.values(body.errors ?? {})
    .flatMap((entry) => (Array.isArray(entry) ? entry : [entry]))
    .filter((entry): entry is string => typeof entry === "string" && entry.trim() !== "");
  if (fieldErrors.length) return fieldErrors.join(" ");

  return body.detail || body.title || stamped;
}

export async function openFinanceAttachment(
  kind: FinanceRecordKind,
  recordId: number,
  fileName: string,
  download: boolean,
): Promise<void> {
  return openAttachmentAt(`/api/Finance/${ATTACHMENT_RESOURCE[kind]}/${recordId}/attachment`, fileName, download);
}

/**
 * Same viewer, for records the one-id route above cannot address — a loan movement hangs off both
 * its loan and its own id. Everything after fetching the bytes is identical, and duplicating it
 * per record type is how one copy quietly loses the popup-blocker handling or the revoke.
 */
export async function openAttachmentAt(
  path: string,
  fileName: string,
  download: boolean,
): Promise<void> {
  // Opening the placeholder synchronously avoids mobile popup blockers while the
  // authenticated request is in flight. Downloads do not need a new window.
  const previewWindow = download ? null : window.open("", "_blank");
  if (previewWindow) previewWindow.opener = null;
  try {
    const response = await api(`${path}?download=${download}`);
    if (!response.ok) {
      previewWindow?.close();
      throw new Error(await responseMessage(response, "The attachment could not be opened."));
    }

    const blobUrl = URL.createObjectURL(await response.blob());
    if (download) {
      const link = document.createElement("a");
      link.href = blobUrl;
      link.download = fileName;
      document.body.appendChild(link);
      link.click();
      link.remove();
    } else if (previewWindow) {
      previewWindow.location.replace(blobUrl);
    } else {
      // Some browsers block new windows even when pre-opened. Navigating the current
      // tab still gives the admin access to the evidence instead of silently failing.
      window.location.assign(blobUrl);
    }

    window.setTimeout(() => URL.revokeObjectURL(blobUrl), 60_000);
  } catch (error) {
    previewWindow?.close();
    throw error;
  }
}

export async function financeApiError(response: Response, fallback: string): Promise<string> {
  if (response.status === 413) return "The attachment is too large. The maximum allowed size is 15 MB.";
  return responseMessage(response, fallback);
}
