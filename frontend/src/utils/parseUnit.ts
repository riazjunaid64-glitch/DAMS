/** Normalizes unit JSON (camelCase or PascalCase). */

export interface UnitFromApi {
  id: number;
  projectId: number;
  unitNumber: string;
  unitType: string;
  floorNumber: number;
  size: number;
  price: number;
  status: string;
}

function num(v: unknown): number {
  const n = Number(v);
  return Number.isFinite(n) ? n : 0;
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

export function parseUnitRow(raw: unknown): UnitFromApi | null {
  if (!raw || typeof raw !== "object") return null;
  const o = raw as Record<string, unknown>;
  const id = pickId(o);
  if (id === null) return null;

  const projectIdRaw = o.projectId ?? o.ProjectId;
  const projectId = num(projectIdRaw);
  if (!Number.isFinite(projectId)) return null;

  return {
    id,
    projectId,
    unitNumber: String(o.unitNumber ?? o.UnitNumber ?? ""),
    unitType: String(o.unitType ?? o.UnitType ?? ""),
    floorNumber: num(o.floorNumber ?? o.FloorNumber),
    size: num(o.size ?? o.Size),
    price: num(o.price ?? o.Price),
    status: String(o.status ?? o.Status ?? ""),
  };
}

export function parseUnitsPayload(raw: unknown): UnitFromApi[] {
  if (!Array.isArray(raw)) return [];
  const out: UnitFromApi[] = [];
  for (const row of raw) {
    const u = parseUnitRow(row);
    if (u) out.push(u);
  }
  return out;
}
