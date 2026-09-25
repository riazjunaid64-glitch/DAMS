import { api } from "../../api/api.ts";
import type {
  ClosureReason,
  LeadSource,
  ProjectLookup,
  StaffMember,
  Team,
  UnitLookup,
} from "./types.ts";

/** The record changed since it was read (HTTP 409), so the write was refused rather than overwriting it. */
export class LeadConflictError extends Error {}

export async function apiJson<T>(endpoint: string, options?: RequestInit): Promise<T> {
  const response = await api(endpoint, options);
  if (!response.ok) {
    const body = await response.json().catch(() => ({})) as { message?: string; title?: string };
    if (response.status === 403) throw new Error("You do not have permission to perform this action.");
    if (response.status === 404) throw new Error(body.message ?? "The requested record was not found.");
    if (response.status === 409) throw new LeadConflictError(body.message ?? "This record changed. Reload and try again.");
    throw new Error(body.message ?? body.title ?? `Request failed (HTTP ${response.status}).`);
  }
  if (response.status === 204) return undefined as T;
  return response.json() as Promise<T>;
}

export async function loadCrmLookups() {
  const [sources, reasons, teams, staff, projectsRaw] = await Promise.all([
    apiJson<LeadSource[]>("/api/lead-config/sources"),
    apiJson<ClosureReason[]>("/api/lead-config/closure-reasons"),
    apiJson<Team[]>("/api/lead-config/teams"),
    apiJson<StaffMember[]>("/api/staff/directory"),
    apiJson<unknown>("/api/Project"),
  ]);

  return {
    sources,
    reasons,
    teams,
    staff,
    projects: normalizeProjects(projectsRaw),
  };
}

export async function loadUnits(projectId: number): Promise<UnitLookup[]> {
  const raw = await apiJson<unknown>(`/api/Unit/project/${projectId}`);
  const rows = Array.isArray(raw) ? raw : [];
  return rows.map((value) => {
    const row = value as Record<string, unknown>;
    return {
      id: Number(row.id),
      number: String(row.unitNumber ?? row.number ?? `Unit ${row.id}`),
      projectId: Number(row.projectId ?? projectId),
    };
  }).filter((row) => Number.isFinite(row.id));
}

function normalizeProjects(raw: unknown): ProjectLookup[] {
  const container = raw as { items?: unknown[]; data?: unknown[] } | null;
  const values = Array.isArray(raw)
    ? raw
    : Array.isArray(container?.items)
      ? container.items
      : Array.isArray(container?.data)
        ? container.data
        : [];

  return values.map((value) => {
    const row = value as Record<string, unknown>;
    return {
      id: Number(row.id),
      name: String(row.projectName ?? row.name ?? `Project ${row.id}`),
    };
  }).filter((row) => Number.isFinite(row.id));
}

export function jsonRequest(method: string, body: unknown): RequestInit {
  return { method, body: JSON.stringify(body) };
}

export async function downloadLeadDocument(documentId: number, fileName: string) {
  const response = await api(`/api/leads/documents/${documentId}/download?download=true`);
  if (!response.ok) throw new Error("The document could not be downloaded.");
  const url = URL.createObjectURL(await response.blob());
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = fileName;
  anchor.click();
  URL.revokeObjectURL(url);
}
