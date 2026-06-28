/** Normalizes project JSON from the API (camelCase or PascalCase). */

const STATUS_FROM_NAME: Record<string, number> = {
  Planning: 1,
  Ongoing: 2,
  Completed: 3,
  Cancelled: 4,
  Archived: 5,
};

export interface ProjectFromApi {
  id: number;
  projectName: string;
  location: string;
  category?: string | null;
  coverImageUrl?: string | null;
  description?: string | null;
  startingDate: string;
  expectedCompletionDate?: string | null;
  status: number | string;
  createdAt: string;
}

function pickId(o: Record<string, unknown>): number | null {
  if (o.id !== undefined && o.id !== null) {
    const n = Number(o.id);
    return Number.isFinite(n) ? n : null;
  }
  if (o.Id !== undefined && o.Id !== null) {
    const n = Number(o.Id);
    return Number.isFinite(n) ? n : null;
  }
  return null;
}

function str(v: unknown): string {
  if (v == null) return "";
  if (typeof v === "string") return v;
  if (typeof v === "number" || typeof v === "boolean") return String(v);
  return "";
}

function strOrNull(v: unknown): string | null {
  if (v == null || v === "") return null;
  return String(v);
}

function dateStr(v: unknown): string {
  if (v == null) return "";
  if (typeof v === "string") return v;
  return String(v);
}

function normStatus(v: unknown): number | string {
  if (typeof v === "number" && Number.isFinite(v)) return v;
  if (typeof v === "string") {
    if (STATUS_FROM_NAME[v] !== undefined) return STATUS_FROM_NAME[v];
    const n = Number(v);
    if (Number.isFinite(n)) return n;
    return v;
  }
  return 1;
}

/** Parse one project object, or null if it is not a valid row. */
export function parseProjectRow(raw: unknown): ProjectFromApi | null {
  if (!raw || typeof raw !== "object") return null;
  const o = raw as Record<string, unknown>;
  const id = pickId(o);
  if (id === null) return null;

  const desc = o.description ?? o.Description;
  const expected = o.expectedCompletionDate ?? o.ExpectedCompletionDate;

  return {
    id,
    projectName: str(o.projectName ?? o.ProjectName),
    location: str(o.location ?? o.Location),
    category: strOrNull(o.category ?? o.Category),
    coverImageUrl: strOrNull(o.coverImageUrl ?? o.CoverImageUrl),
    description: desc == null || desc === "" ? null : String(desc),
    startingDate: dateStr(o.startingDate ?? o.StartingDate),
    expectedCompletionDate:
      expected == null || expected === "" ? null : dateStr(expected),
    status: normStatus(o.status ?? o.Status),
    createdAt: dateStr(o.createdAt ?? o.CreatedAt),
  };
}

/** Parse GET /api/Project body — must be a JSON array. */
export function parseProjectsPayload(raw: unknown): ProjectFromApi[] {
  if (!Array.isArray(raw)) return [];
  const out: ProjectFromApi[] = [];
  for (const row of raw) {
    const p = parseProjectRow(row);
    if (p) out.push(p);
  }
  return out;
}
