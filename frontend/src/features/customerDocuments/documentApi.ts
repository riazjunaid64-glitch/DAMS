import { api } from "../../api/api.ts";

async function failure(response: Response, fallback: string): Promise<Error> {
  let message = fallback;
  try {
    const body = await response.json() as { message?: string };
    if (body.message) message = body.message;
  } catch {
    // Keep the safe fallback when a proxy or server returns a non-JSON error.
  }
  return new Error(message);
}

export async function documentJson<T>(endpoint: string, options?: RequestInit): Promise<T> {
  const response = await api(endpoint, options);
  if (!response.ok) throw await failure(response, "The document operation could not be completed.");
  return response.json() as Promise<T>;
}

export function jsonBody(method: "POST" | "PUT", body: unknown): RequestInit {
  return { method, body: JSON.stringify(body) };
}

const fileUrl = (customerId: number, requirementId: number, versionId: number, call: "view" | "file") =>
  `/api/customer-documents/customers/${customerId}/requirements/${requirementId}/versions/${versionId}/${call}`;

/** The private file for the preview. The server records that it was viewed. */
export async function fetchDocumentForView(customerId: number, requirementId: number, versionId: number): Promise<Blob> {
  const response = await api(fileUrl(customerId, requirementId, versionId, "view"));
  if (!response.ok) throw await failure(response, "The document could not be opened.");
  return response.blob();
}

/** Saves the private file to the device. The server records that it was downloaded. */
export async function downloadDocument(customerId: number, requirementId: number, versionId: number, fileName: string): Promise<void> {
  const response = await api(fileUrl(customerId, requirementId, versionId, "file"));
  if (!response.ok) throw await failure(response, "The document could not be downloaded.");
  const url = URL.createObjectURL(await response.blob());
  const link = document.createElement("a");
  link.href = url;
  link.rel = "noopener noreferrer";
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), 60_000);
}

/** The message from a failed upload answer. */
export async function uploadFailure(response: Response): Promise<string> {
  return (await failure(response, "The document could not be uploaded. Try again.")).message;
}
