import { useCallback, useEffect, useMemo, useState } from "react";
import { api } from "../../api/api.ts";
import Button from "../../lib/Button.tsx";
import DateRangeFilter, { defaultRange, todayIso, type DateRange } from "./DateRangeFilter.tsx";
import {
  ATTENDANCE_STATUSES,
  attendanceMeta,
  parseAttendanceStatus,
  requiresReason,
  type AttendanceStatusNum,
} from "../../utils/attendanceStatus.ts";

interface BatchRow {
  employeeId: number;
  employeeName: string;
  department: string;
  jobTitle: string;
  attendanceId: number | null;
  status: AttendanceStatusNum | null;
  notes: string | null;
  reason: string;
  saving: boolean;
}

interface HistoryRow {
  id: number;
  employeeId: number;
  employeeName: string;
  department: string;
  date: string;
  status: AttendanceStatusNum;
  notes: string | null;
}

function formatDate(iso: string) {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return d.toLocaleDateString("en-US", { weekday: "short", day: "numeric", month: "short", year: "numeric" });
}

// ─── Mark Attendance ────────────────────────────────────────────────────────

function MarkAttendance() {
  const [selectedDate, setSelectedDate] = useState(todayIso());
  const [rows, setRows] = useState<BatchRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await api(`/api/Employee/attendance/batch?date=${selectedDate}`);
      if (!res.ok) { setError("Failed to load attendance."); return; }
      const raw = await res.json() as Array<Record<string, unknown>>;
      setRows(raw.map(r => {
        const marked = r.status != null ? parseAttendanceStatus(r.status as number | string) : null;
        const notes = r.notes != null ? String(r.notes) : null;
        return {
          employeeId: Number(r.employeeId),
          employeeName: String(r.employeeName ?? ""),
          department: String(r.department ?? ""),
          jobTitle: String(r.jobTitle ?? ""),
          attendanceId: r.attendanceId != null ? Number(r.attendanceId) : null,
          status: marked,
          notes,
          reason: notes ?? "",
          saving: false,
        };
      }));
    } catch { setError("Could not load attendance."); }
    finally { setLoading(false); }
  }, [selectedDate]);

  useEffect(() => { load(); }, [load]);

  const setRowStatus = (employeeId: number, status: AttendanceStatusNum) => {
    setRows(prev => prev.map(r =>
      r.employeeId === employeeId
        ? { ...r, status, reason: requiresReason(status) ? r.reason : "" }
        : r
    ));
  };

  const saveRow = async (employeeId: number) => {
    const row = rows.find(r => r.employeeId === employeeId);
    if (!row || row.status == null) return;
    if (requiresReason(row.status) && !row.reason.trim()) {
      setError(`Please enter a reason for ${row.employeeName}.`);
      return;
    }

    setError(null);
    setRows(prev => prev.map(r => r.employeeId === employeeId ? { ...r, saving: true } : r));
    try {
      const meta = attendanceMeta(row.status);
      const res = await api(`/api/Employee/${employeeId}/attendance`, {
        method: "POST",
        body: JSON.stringify({
          date: selectedDate,
          status: meta.api,
          notes: requiresReason(row.status) ? row.reason.trim() : (row.reason.trim() || null),
        }),
      });
      if (!res.ok) {
        const d = await res.json().catch(() => ({}));
        setError((d as { message?: string }).message ?? `Failed to save attendance for ${row.employeeName}.`);
        return;
      }
      await load();
    } catch { setError("Something went wrong."); }
    finally {
      setRows(prev => prev.map(r => r.employeeId === employeeId ? { ...r, saving: false } : r));
    }
  };

  const saveAll = async () => {
    const pending = rows.filter(r => r.status != null);
    for (const row of pending) {
      if (row.status != null && requiresReason(row.status) && !row.reason.trim()) {
        setError(`Please enter a reason for ${row.employeeName}.`);
        return;
      }
    }
    setError(null);
    for (const row of pending) {
      await saveRow(row.employeeId);
    }
  };

  return (
    <div>
      <div className="mb-6 flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <h2 className="section-title mb-1">Mark Attendance</h2>
          <p className="text-sm text-[var(--text-muted)]">Record the daily status for every active employee</p>
        </div>
        <div className="flex flex-wrap items-end gap-3">
          <div>
            <label className="mb-1.5 block text-xs font-medium text-[var(--text-muted)]">Date</label>
            <input
              type="date"
              value={selectedDate}
              max={todayIso()}
              onChange={e => setSelectedDate(e.target.value)}
              className="rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-3 py-2 text-sm text-[var(--text-primary)] outline-none focus:border-[var(--accent)]"
            />
          </div>
          <Button onClick={saveAll} disabled={loading || rows.every(r => r.status == null)}>
            Save All
          </Button>
        </div>
      </div>

      {error && (
        <div className="mb-4 rounded-xl border border-rose-500/20 bg-rose-500/[0.06] px-4 py-3 text-sm text-rose-300">{error}</div>
      )}

      {loading ? (
        <div className="space-y-3">
          {[...Array(6)].map((_, i) => <div key={i} className="skeleton h-14 rounded-xl" />)}
        </div>
      ) : rows.length === 0 ? (
        <div className="py-12 text-center text-sm text-[var(--text-muted)]">No active employees found.</div>
      ) : (
        <div className="overflow-x-auto rounded-xl border border-[var(--border)] bg-[var(--bg-card)] shadow-sm">
          <table className="data-table min-w-[920px] w-full border-collapse text-left">
            <thead>
              <tr className="border-b border-[var(--border)] bg-[var(--surface-glass)]">
                {["Employee", "Department", "Status", "Reason / Notes", ""].map(h => (
                  <th key={h} className="px-5 py-3 text-[10px] font-bold uppercase tracking-wider text-[var(--text-muted)]">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {rows.map(row => (
                <tr key={row.employeeId} className="border-b border-[var(--border)] transition-colors hover:bg-[var(--surface-glass-hover)] last:border-b-0">
                  <td className="px-5 py-4">
                    <p className="text-sm font-semibold text-[var(--text-primary)]">{row.employeeName}</p>
                    <p className="text-xs text-[var(--text-muted)]">{row.jobTitle}</p>
                  </td>
                  <td className="px-5 py-4 text-sm text-[var(--text-secondary)]">{row.department}</td>
                  <td className="px-5 py-4">
                    <div className="inline-flex flex-wrap gap-1.5">
                      {ATTENDANCE_STATUSES.map(s => {
                        const active = row.status === s.num;
                        return (
                          <button
                            key={s.num}
                            type="button"
                            onClick={() => setRowStatus(row.employeeId, s.num)}
                            className={`rounded-lg border px-2.5 py-1 text-[11px] font-semibold transition-colors ${
                              active ? s.badge : "border-[var(--border)] text-[var(--text-muted)] hover:text-[var(--text-primary)]"
                            }`}
                          >
                            {s.label}
                          </button>
                        );
                      })}
                    </div>
                  </td>
                  <td className="px-5 py-4">
                    {row.status != null && requiresReason(row.status) ? (
                      <input
                        type="text"
                        value={row.reason}
                        onChange={e => setRows(prev => prev.map(r =>
                          r.employeeId === row.employeeId ? { ...r, reason: e.target.value } : r
                        ))}
                        placeholder="Reason (required)…"
                        className="w-full min-w-[180px] rounded-lg border border-[var(--border)] bg-[var(--input-bg)] px-3 py-1.5 text-sm text-[var(--text-primary)] outline-none focus:border-[var(--accent)]"
                      />
                    ) : (
                      <input
                        type="text"
                        value={row.reason}
                        onChange={e => setRows(prev => prev.map(r =>
                          r.employeeId === row.employeeId ? { ...r, reason: e.target.value } : r
                        ))}
                        placeholder="Optional note…"
                        className="w-full min-w-[180px] rounded-lg border border-[var(--border)] bg-[var(--input-bg)] px-3 py-1.5 text-sm text-[var(--text-primary)] outline-none focus:border-[var(--accent)]"
                      />
                    )}
                  </td>
                  <td className="px-5 py-4">
                    <Button size="sm" variant="outline" onClick={() => saveRow(row.employeeId)} disabled={row.status == null || row.saving}>
                      {row.saving ? "…" : "Save"}
                    </Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

// ─── Attendance History ─────────────────────────────────────────────────────

function StatCard({ label, value, tone }: { label: string; value: string | number; tone: string }) {
  return (
    <div className="rounded-xl border border-[var(--border)] bg-[var(--bg-card)] px-4 py-3 shadow-sm">
      <p className={`text-2xl font-bold ${tone}`}>{value}</p>
      <p className="mt-0.5 text-[11px] font-semibold uppercase tracking-wider text-[var(--text-muted)]">{label}</p>
    </div>
  );
}

function AttendanceHistory() {
  const [range, setRange] = useState<DateRange>(defaultRange);
  const [statusFilter, setStatusFilter] = useState<string>("all");
  const [employeeFilter, setEmployeeFilter] = useState<string>("all");
  const [records, setRecords] = useState<HistoryRow[]>([]);
  const [employees, setEmployees] = useState<{ id: number; name: string }[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Employee dropdown options (active + on-leave team)
  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const res = await api("/api/Employee");
        if (!res.ok) return;
        const raw = await res.json() as Array<Record<string, unknown>>;
        if (cancelled) return;
        setEmployees(raw.map(e => ({ id: Number(e.id), name: String(e.fullName ?? e.FullName ?? "") }))
          .sort((a, b) => a.name.localeCompare(b.name)));
      } catch { /* ignore */ }
    })();
    return () => { cancelled = true; };
  }, []);

  const load = useCallback(async () => {
    if (!range.from || !range.to) return;
    setLoading(true);
    setError(null);
    try {
      const params = new URLSearchParams({ from: range.from, to: range.to });
      if (statusFilter !== "all") params.set("status", statusFilter);
      if (employeeFilter !== "all") params.set("employeeId", employeeFilter);
      const res = await api(`/api/Employee/attendance/history?${params.toString()}`);
      if (!res.ok) { setError("Failed to load attendance history."); return; }
      const raw = await res.json() as Array<Record<string, unknown>>;
      setRecords(raw.map(r => ({
        id: Number(r.id),
        employeeId: Number(r.employeeId),
        employeeName: String(r.employeeName ?? ""),
        department: String(r.department ?? ""),
        date: String(r.date ?? ""),
        status: parseAttendanceStatus(r.status as number | string),
        notes: r.notes != null ? String(r.notes) : null,
      })));
    } catch { setError("Could not load attendance history."); }
    finally { setLoading(false); }
  }, [range.from, range.to, statusFilter, employeeFilter]);

  useEffect(() => { load(); }, [load]);

  const summary = useMemo(() => {
    const counts = { 0: 0, 1: 0, 2: 0, 3: 0, 4: 0 } as Record<AttendanceStatusNum, number>;
    for (const r of records) counts[r.status]++;
    const total = records.length;
    const rate = total === 0 ? 0 : Math.round(((counts[0] + counts[2] + counts[3] * 0.5) / total) * 100);
    return { counts, total, rate };
  }, [records]);

  return (
    <div>
      <div className="mb-5">
        <h2 className="section-title mb-1">Attendance History</h2>
        <p className="text-sm text-[var(--text-muted)]">Team attendance register for the selected period</p>
      </div>

      <div className="mb-5 flex flex-wrap items-end gap-3">
        <DateRangeFilter value={range} onChange={setRange} />
        <div>
          <label className="mb-1.5 block text-xs font-medium text-[var(--text-muted)]">Employee</label>
          <select
            value={employeeFilter}
            onChange={e => setEmployeeFilter(e.target.value)}
            className="rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-3 py-2 text-sm text-[var(--text-primary)] outline-none focus:border-[var(--accent)]"
          >
            <option value="all">All employees</option>
            {employees.map(e => <option key={e.id} value={e.id}>{e.name}</option>)}
          </select>
        </div>
        <div>
          <label className="mb-1.5 block text-xs font-medium text-[var(--text-muted)]">Status</label>
          <select
            value={statusFilter}
            onChange={e => setStatusFilter(e.target.value)}
            className="rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-3 py-2 text-sm text-[var(--text-primary)] outline-none focus:border-[var(--accent)]"
          >
            <option value="all">All statuses</option>
            {ATTENDANCE_STATUSES.map(s => <option key={s.num} value={s.api}>{s.label}</option>)}
          </select>
        </div>
      </div>

      {error && (
        <div className="mb-4 rounded-xl border border-rose-500/20 bg-rose-500/[0.06] px-4 py-3 text-sm text-rose-300">{error}</div>
      )}

      <div className="mb-6 grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-6">
        <StatCard label="Present"   value={summary.counts[0]} tone="text-emerald-400" />
        <StatCard label="Late"      value={summary.counts[2]} tone="text-amber-400" />
        <StatCard label="Half-day"  value={summary.counts[3]} tone="text-blue-400" />
        <StatCard label="Leave"     value={summary.counts[4]} tone="text-violet-400" />
        <StatCard label="Absent"    value={summary.counts[1]} tone="text-rose-400" />
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
          <table className="data-table min-w-[760px] w-full border-collapse text-left">
            <thead>
              <tr className="border-b border-[var(--border)] bg-[var(--surface-glass)]">
                {["Employee", "Department", "Date", "Status", "Reason / Notes"].map(h => (
                  <th key={h} className="px-5 py-3 text-[10px] font-bold uppercase tracking-wider text-[var(--text-muted)]">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {records.map(r => {
                const meta = attendanceMeta(r.status);
                return (
                  <tr key={r.id} className="border-b border-[var(--border)] transition-colors hover:bg-[var(--surface-glass-hover)] last:border-b-0">
                    <td className="px-5 py-4 text-sm font-semibold text-[var(--text-primary)]">{r.employeeName}</td>
                    <td className="px-5 py-4 text-sm text-[var(--text-secondary)]">{r.department}</td>
                    <td className="px-5 py-4 text-sm text-[var(--text-secondary)]">{formatDate(r.date)}</td>
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
  );
}

// ─── Panel shell with sub-view toggle ───────────────────────────────────────

export default function EmployeesAttendancePanel() {
  const [view, setView] = useState<"mark" | "history">("mark");

  return (
    <div>
      <div className="mb-6 inline-flex rounded-xl border border-[var(--border)] bg-[var(--surface-glass)] p-1">
        {([["mark", "Mark Attendance"], ["history", "History"]] as const).map(([id, label]) => (
          <button
            key={id}
            type="button"
            onClick={() => setView(id)}
            className={`rounded-lg px-4 py-2 text-sm font-semibold transition-colors ${
              view === id ? "bg-[var(--accent)] text-white shadow-sm" : "text-[var(--text-muted)] hover:text-[var(--text-primary)]"
            }`}
          >
            {label}
          </button>
        ))}
      </div>

      {view === "mark" ? <MarkAttendance /> : <AttendanceHistory />}
    </div>
  );
}
