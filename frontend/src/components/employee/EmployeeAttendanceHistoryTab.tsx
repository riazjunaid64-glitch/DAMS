import { useCallback, useEffect, useState } from "react";
import { api } from "../../api/api.ts";

interface AttendanceRecord {
  id: number;
  date: string;
  status: number | string;
  notes: string | null;
}

const STATUS_LABELS: Record<number, string> = {
  0: "Present", 1: "Absent", 2: "Late", 3: "Half Day", 4: "Leave",
};

const STATUS_COLORS: Record<number, string> = {
  0: "text-emerald-400 bg-emerald-500/10 border-emerald-500/20",
  1: "text-rose-400 bg-rose-500/10 border-rose-500/20",
  2: "text-amber-400 bg-amber-500/10 border-amber-500/20",
  3: "text-blue-400 bg-blue-500/10 border-blue-500/20",
  4: "text-violet-400 bg-violet-500/10 border-violet-500/20",
};

function statusNum(s: number | string): number {
  if (typeof s === "number" && Number.isFinite(s)) return s;
  const map: Record<string, number> = { Present: 0, Absent: 1, Late: 2, HalfDay: 3, Leave: 4 };
  return typeof s === "string" && map[s] !== undefined ? map[s] : 0;
}

function formatDate(iso: string) {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return d.toLocaleDateString("en-US", { weekday: "short", day: "numeric", month: "short", year: "numeric" });
}

interface Props {
  employeeId: number;
}

export default function EmployeeAttendanceHistoryTab({ employeeId }: Props) {
  const [records, setRecords] = useState<AttendanceRecord[]>([]);
  const [loading, setLoading] = useState(true);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const res = await api(`/api/Employee/${employeeId}/attendance`);
      if (!res.ok) return;
      const raw = await res.json() as Array<Record<string, unknown>>;
      setRecords(raw.map(r => ({
        id: Number(r.id),
        date: String(r.date ?? ""),
        status: r.status as number | string,
        notes: r.notes != null ? String(r.notes) : null,
      })));
    } catch { /* ignore */ }
    finally { setLoading(false); }
  }, [employeeId]);

  useEffect(() => { load(); }, [load]);

  return (
    <div className="py-8 sm:py-10">
      <div className="mx-auto w-full max-w-7xl px-5 sm:px-8 lg:px-10">
        <div className="mb-6">
          <h2 className="section-title mb-1">Attendance History</h2>
          <p className="text-sm text-[var(--text-muted)]">Past attendance records for this employee</p>
        </div>

        {loading ? (
          <div className="space-y-3">
            {[...Array(5)].map((_, i) => <div key={i} className="skeleton h-12 rounded-xl" />)}
          </div>
        ) : records.length === 0 ? (
          <div className="py-12 text-center text-sm text-[var(--text-muted)]">No attendance records yet.</div>
        ) : (
          <div className="overflow-x-auto rounded-xl border border-[var(--border)] bg-[var(--bg-card)] shadow-sm">
            <table className="min-w-[600px] w-full border-collapse text-left">
              <thead>
                <tr className="border-b border-[var(--border)] bg-[var(--surface-glass)]">
                  {["Date", "Status", "Reason / Notes"].map(h => (
                    <th key={h} className="px-5 py-3 text-[10px] font-bold uppercase tracking-wider text-[var(--text-muted)]">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {records.map(r => {
                  const sn = statusNum(r.status);
                  return (
                    <tr key={r.id} className="border-b border-[var(--border)] transition-colors hover:bg-[var(--surface-glass-hover)] last:border-b-0">
                      <td className="px-5 py-4 text-sm font-medium text-[var(--text-primary)]">{formatDate(r.date)}</td>
                      <td className="px-5 py-4">
                        <span className={`inline-flex rounded-full border px-2.5 py-1 text-[10px] font-semibold ${STATUS_COLORS[sn] ?? STATUS_COLORS[0]}`}>
                          {STATUS_LABELS[sn] ?? "Unknown"}
                        </span>
                      </td>
                      <td className="px-5 py-4 text-sm text-[var(--text-secondary)]">{r.notes ?? "—"}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}
