import { useCallback, useEffect, useMemo, useState } from "react";
import { api } from "../../api/api.ts";
import DateRangeFilter, { defaultRange, type DateRange } from "./DateRangeFilter.tsx";
import {
  attendanceMeta,
  parseAttendanceStatus,
  type AttendanceStatusNum,
} from "../../utils/attendanceStatus.ts";

interface AttendanceRecord {
  id: number;
  date: string;
  status: AttendanceStatusNum;
  notes: string | null;
}

function formatDate(iso: string) {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return d.toLocaleDateString("en-US", { weekday: "short", day: "numeric", month: "short", year: "numeric" });
}

function StatCard({ label, value, tone }: { label: string; value: string | number; tone: string }) {
  return (
    <div className="rounded-xl border border-[var(--border)] bg-[var(--bg-card)] px-4 py-3 shadow-sm">
      <p className={`text-2xl font-bold ${tone}`}>{value}</p>
      <p className="mt-0.5 text-[11px] font-semibold uppercase tracking-wider text-[var(--text-muted)]">{label}</p>
    </div>
  );
}

interface Props {
  employeeId: number;
}

export default function EmployeeAttendanceHistoryTab({ employeeId }: Props) {
  const [range, setRange] = useState<DateRange>(defaultRange);
  const [records, setRecords] = useState<AttendanceRecord[]>([]);
  const [loading, setLoading] = useState(true);

  const load = useCallback(async () => {
    if (!range.from || !range.to) return;
    setLoading(true);
    try {
      const params = new URLSearchParams({ from: range.from, to: range.to });
      const res = await api(`/api/Employee/${employeeId}/attendance?${params.toString()}`);
      if (!res.ok) return;
      const raw = await res.json() as Array<Record<string, unknown>>;
      setRecords(raw.map(r => ({
        id: Number(r.id),
        date: String(r.date ?? ""),
        status: parseAttendanceStatus(r.status as number | string),
        notes: r.notes != null ? String(r.notes) : null,
      })));
    } catch { /* ignore */ }
    finally { setLoading(false); }
  }, [employeeId, range.from, range.to]);

  useEffect(() => { load(); }, [load]);

  const summary = useMemo(() => {
    const counts = { 0: 0, 1: 0, 2: 0, 3: 0, 4: 0 } as Record<AttendanceStatusNum, number>;
    for (const r of records) counts[r.status]++;
    const total = records.length;
    const rate = total === 0 ? 0 : Math.round(((counts[0] + counts[2] + counts[3] * 0.5) / total) * 100);
    return { counts, rate };
  }, [records]);

  return (
    <div className="py-8 sm:py-10">
      <div className="mx-auto w-full max-w-7xl px-5 sm:px-8 lg:px-10">
        <div className="mb-5">
          <h2 className="section-title mb-1">Attendance History</h2>
          <p className="text-sm text-[var(--text-muted)]">Attendance records for the selected period</p>
        </div>

        <div className="mb-5">
          <DateRangeFilter value={range} onChange={setRange} />
        </div>

        <div className="mb-6 grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-6">
          <StatCard label="Present"    value={summary.counts[0]} tone="text-emerald-400" />
          <StatCard label="Late"       value={summary.counts[2]} tone="text-amber-400" />
          <StatCard label="Half-day"   value={summary.counts[3]} tone="text-blue-400" />
          <StatCard label="Leave"      value={summary.counts[4]} tone="text-violet-400" />
          <StatCard label="Absent"     value={summary.counts[1]} tone="text-rose-400" />
          <StatCard label="Attendance" value={`${summary.rate}%`} tone="text-[var(--text-primary)]" />
        </div>

        {loading ? (
          <div className="space-y-3">
            {[...Array(5)].map((_, i) => <div key={i} className="skeleton h-12 rounded-xl" />)}
          </div>
        ) : records.length === 0 ? (
          <div className="py-12 text-center text-sm text-[var(--text-muted)]">No attendance records for this period.</div>
        ) : (
          <div className="overflow-x-auto rounded-xl border border-[var(--border)] bg-[var(--bg-card)] shadow-sm">
            <table className="data-table min-w-[600px] w-full border-collapse text-left">
              <thead>
                <tr className="border-b border-[var(--border)] bg-[var(--surface-glass)]">
                  {["Date", "Status", "Reason / Notes"].map(h => (
                    <th key={h} className="px-5 py-3 text-[10px] font-bold uppercase tracking-wider text-[var(--text-muted)]">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {records.map(r => {
                  const meta = attendanceMeta(r.status);
                  return (
                    <tr key={r.id} className="border-b border-[var(--border)] transition-colors hover:bg-[var(--surface-glass-hover)] last:border-b-0">
                      <td className="px-5 py-4 text-sm font-medium text-[var(--text-primary)]">{formatDate(r.date)}</td>
                      <td className="px-5 py-4">
                        <span className={`inline-flex rounded-full border px-2.5 py-1 text-[10px] font-semibold ${meta.badge}`}>{meta.label}</span>
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
