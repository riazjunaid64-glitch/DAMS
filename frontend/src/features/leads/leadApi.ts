import { api } from "../../api/api.ts";
import type {
  ClosureReason,
  LeadSource,
  ProjectLookup,
  StaffMember,
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

/** What the CRM screens pick from. Projects are not here: they come from ProjectsContext. */
export type CrmLookups = {
  sources: LeadSource[];
  reasons: ClosureReason[];
  staff: StaffMember[];
  apartmentTypes: string[];
};

let lookups: { account: string; promise: Promise<CrmLookups> } | null = null;

/**
 * Sources, closure reasons, the staff directory and apartment types, fetched once per signed-in
 * account and shared by every CRM screen, so a filter or page change never fetches them again.
 */
export function loadCrmLookups(account: string): Promise<CrmLookups> {
  if (lookups?.account !== account) {
    const promise = Promise.all([
      apiJson<LeadSource[]>("/api/lead-config/sources"),
      apiJson<ClosureReason[]>("/api/lead-config/closure-reasons"),
      apiJson<StaffMember[]>("/api/staff/directory"),
      apiJson<string[]>("/api/lead-config/apartment-types"),
    ]).then(([sources, reasons, staff, apartmentTypes]) => ({ sources, reasons, staff, apartmentTypes }));
    // A failed load is not kept: the next screen that needs the lookups asks again.
    promise.catch(() => { if (lookups?.promise === promise) lookups = null; });
    lookups = { account, promise };
  }
  return lookups.promise;
}

/** Drops the shared lookups, so the next CRM screen reads what CRM settings just changed. */
export function forgetCrmLookups() {
  lookups = null;
}

export async function loadProjects(): Promise<ProjectLookup[]> {
  return normalizeProjects(await apiJson<unknown>("/api/Project"));
}

/** Every unit of a project, with what the convert popup needs to pick one. */
export async function loadUnits(projectId: number, signal?: AbortSignal): Promise<UnitLookup[]> {
  const raw = await apiJson<unknown>(`/api/Unit/project/${projectId}`, { signal });
  const rows = Array.isArray(raw) ? raw : [];
  return rows.map((value) => {
    const row = value as Record<string, unknown>;
    return {
      id: Number(row.id),
      projectId: Number(row.projectId ?? projectId),
      number: String(row.unitNumber ?? row.id),
      type: String(row.unitType ?? ""),
      floor: Number(row.floorNumber ?? 0),
      status: String(row.status ?? ""),
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
