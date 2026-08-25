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

async function responseMessage(response: Response, fallback: string): Promise<string> {
  try {
    const body = (await response.json()) as { message?: string };
    return body.message || fallback;
  } catch {
    return fallback;
  }
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
