/** Normalizes employee JSON from the API (camelCase or PascalCase). */

export interface EmployeeFromApi {
  id: number;
  fullName: string;
  jobTitle: string;
  department: string;
  phone: string;
  email: string | null;
  address: string | null;
  salary: number;
  joinDate: string;
  status: number | string;
  createdAt: string;
  updatedAt: string | null;
}

const STATUS_NAMES: Record<string, number> = {
  Active: 0,
  Inactive: 1,
  OnLeave: 2,
  Terminated: 3,
};

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

function num(v: unknown): number {
  const n = Number(v);
  return Number.isFinite(n) ? n : 0;
}

function normStatus(v: unknown): number | string {
  if (typeof v === "number" && Number.isFinite(v)) return v;
  if (typeof v === "string") {
    if (STATUS_NAMES[v] !== undefined) return STATUS_NAMES[v];
    const n = Number(v);
    if (Number.isFinite(n)) return n;
    return v;
  }
  return 0;
}

export function parseEmployeeRow(raw: unknown): EmployeeFromApi | null {
  if (!raw || typeof raw !== "object") return null;
  const o = raw as Record<string, unknown>;
  const id = pickId(o);
  if (id === null) return null;

  const email = o.email ?? o.Email;
  const address = o.address ?? o.Address;
  const updated = o.updatedAt ?? o.UpdatedAt;

  return {
    id,
    fullName: String(o.fullName ?? o.FullName ?? ""),
    jobTitle: String(o.jobTitle ?? o.JobTitle ?? ""),
    department: String(o.department ?? o.Department ?? ""),
    phone: String(o.phone ?? o.Phone ?? ""),
    email: email == null || email === "" ? null : String(email),
    address: address == null || address === "" ? null : String(address),
    salary: num(o.salary ?? o.Salary),
    joinDate: String(o.joinDate ?? o.JoinDate ?? ""),
    status: normStatus(o.status ?? o.Status),
    createdAt: String(o.createdAt ?? o.CreatedAt ?? ""),
    updatedAt: updated == null || updated === "" ? null : String(updated),
  };
}

export function parseEmployeesPayload(raw: unknown): EmployeeFromApi[] {
  if (!Array.isArray(raw)) return [];
  const out: EmployeeFromApi[] = [];
  for (const row of raw) {
    const e = parseEmployeeRow(row);
    if (e) out.push(e);
  }
  return out;
}
