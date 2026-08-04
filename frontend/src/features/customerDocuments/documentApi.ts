import { api } from "../../api/api.ts";

export async function documentJson<T>(endpoint: string, options?: RequestInit): Promise<T> {
  const response = await api(endpoint, options);
  if (!response.ok) {
    let message = "The document operation could not be completed.";
    try {
      const body = await response.json() as { message?: string };
      if (body.message) message = body.message;
    } catch {
      // Keep the safe fallback when a proxy or server returns a non-JSON error.
    }
    throw new Error(message);
  }
  return response.json() as Promise<T>;
}

export function jsonBody(method: "POST" | "PUT", body: unknown): RequestInit {
  return { method, body: JSON.stringify(body) };
}

export async function openPrivateDocument(
  customerId: number,
  requirementId: number,
  versionId: number,
  fileName: string,
  download: boolean,
): Promise<void> {
  const response = await api(
    `/api/customer-documents/customers/${customerId}/requirements/${requirementId}/versions/${versionId}/file?download=${download}`,
  );
  if (!response.ok) {
    let message = "The private document could not be opened.";
    try { message = ((await response.json()) as { message?: string }).message ?? message; } catch { /* safe fallback */ }
    throw new Error(message);
  }

  const url = URL.createObjectURL(await response.blob());
  const link = document.createElement("a");
  link.href = url;
  link.rel = "noopener noreferrer";
  if (download) link.download = fileName;
  else link.target = "_blank";
  document.body.appendChild(link);
  link.click();
  link.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), 60_000);
}
