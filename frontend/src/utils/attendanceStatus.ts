// Single source of truth for attendance status across the app.
// DB enum (DAMS.Domain.Enums.AttendanceStatus):
//   Present = 0, Absent = 1, Late = 2, HalfDay = 3, Leave = 4

export type AttendanceStatusNum = 0 | 1 | 2 | 3 | 4;

export interface AttendanceStatusMeta {
  num: AttendanceStatusNum;
  /** Enum name sent to / received from the API (e.g. POST body, query string). */
  api: string;
  label: string;
  /** Tailwind classes for a pill/badge. */
  badge: string;
  /** Single accent color class (for buttons / dots). */
  accent: string;
}

export const ATTENDANCE_STATUSES: AttendanceStatusMeta[] = [
  { num: 0, api: "Present",  label: "Present",   badge: "text-emerald-400 bg-emerald-500/10 border-emerald-500/20", accent: "emerald" },
  { num: 2, api: "Late",     label: "Late",      badge: "text-amber-400 bg-amber-500/10 border-amber-500/20",       accent: "amber" },
  { num: 3, api: "HalfDay",  label: "Half-day",  badge: "text-blue-400 bg-blue-500/10 border-blue-500/20",          accent: "blue" },
  { num: 4, api: "Leave",    label: "Leave",     badge: "text-violet-400 bg-violet-500/10 border-violet-500/20",    accent: "violet" },
  { num: 1, api: "Absent",   label: "Absent",    badge: "text-rose-400 bg-rose-500/10 border-rose-500/20",          accent: "rose" },
];

const BY_NUM = new Map<number, AttendanceStatusMeta>(ATTENDANCE_STATUSES.map(s => [s.num, s]));
const BY_API = new Map<string, AttendanceStatusMeta>(ATTENDANCE_STATUSES.map(s => [s.api.toLowerCase(), s]));

/** Normalize an API value (number or enum-name string) to a 0–4 status number. */
export function parseAttendanceStatus(value: number | string | null | undefined): AttendanceStatusNum {
  if (typeof value === "number" && BY_NUM.has(value)) return value as AttendanceStatusNum;
  if (typeof value === "string") {
    const byName = BY_API.get(value.trim().toLowerCase());
    if (byName) return byName.num;
    const asNum = Number(value);
    if (BY_NUM.has(asNum)) return asNum as AttendanceStatusNum;
  }
  return 0;
}

export function attendanceMeta(value: number | string | null | undefined): AttendanceStatusMeta {
  return BY_NUM.get(parseAttendanceStatus(value)) ?? ATTENDANCE_STATUSES[0];
}

/** Statuses that require a reason/notes from the user when marking. */
export function requiresReason(num: AttendanceStatusNum): boolean {
  return num === 1 || num === 4; // Absent, Leave
}
