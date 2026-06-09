import { useCallback, useEffect, useState } from "react";
import { api } from "../../api/api.ts";
import Button from "../../lib/Button.tsx";

interface BatchRow {
  employeeId: number;
  employeeName: string;
  department: string;
  jobTitle: string;
  attendanceId: number | null;
  status: "present" | "absent" | null;
  notes: string | null;
  absentReason: string;
  saving: boolean;
}

function todayIso() {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

function statusFromApi(v: unknown): "present" | "absent" | null {
  if (v === 0 || v === "Present" || v === "present") return "present";
  if (v === 1 || v === "Absent" || v === "absent") return "absent";
  return null;
}

export default function EmployeesAttendancePanel() {
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
      setRows(raw.map(r => ({
        employeeId: Number(r.employeeId),
        employeeName: String(r.employeeName ?? ""),
        department: String(r.department ?? ""),
        jobTitle: String(r.jobTitle ?? ""),
        attendanceId: r.attendanceId != null ? Number(r.attendanceId) : null,
        status: statusFromApi(r.status),
        notes: r.notes != null ? String(r.notes) : null,
        absentReason: r.notes != null ? String(r.notes) : "",
        saving: false,
      })));
    } catch { setError("Could not load attendance."); }
    finally { setLoading(false); }
  }, [selectedDate]);

  useEffect(() => { load(); }, [load]);

  const setRowStatus = (employeeId: number, status: "present" | "absent") => {
    setRows(prev => prev.map(r =>
      r.employeeId === employeeId
        ? { ...r, status, absentReason: status === "present" ? "" : r.absentReason }
        : r
    ));
  };

  const saveRow = async (employeeId: number) => {
    const row = rows.find(r => r.employeeId === employeeId);
    if (!row || !row.status) return;
    if (row.status === "absent" && !row.absentReason.trim()) {
      setError(`Please enter absence reason for ${row.employeeName}.`);
      return;
    }

    setError(null);
    setRows(prev => prev.map(r => r.employeeId === employeeId ? { ...r, saving: true } : r));
    try {
      const res = await api(`/api/Employee/${employeeId}/attendance`, {
        method: "POST",
        body: JSON.stringify({
          date: selectedDate,
          status: row.status === "present" ? "Present" : "Absent",
          notes: row.status === "absent" ? row.absentReason.trim() : null,
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
      if (row.status === "absent" && !row.absentReason.trim()) {
        setError(`Please enter absence reason for ${row.employeeName}.`);
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
          <p className="text-sm text-[var(--text-muted)]">Record present or absent for all active employees</p>
        </div>
        <div className="flex flex-wrap items-end gap-3">
          <div>
            <label className="mb-1.5 block text-xs font-medium text-[var(--text-muted)]">Date</label>
            <input
              type="date"
              value={selectedDate}
              onChange={e => setSelectedDate(e.target.value)}
              className="rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-3 py-2 text-sm text-[var(--text-primary)] outline-none focus:border-[var(--accent)]"
            />
          </div>
          <Button onClick={saveAll} disabled={loading || rows.every(r => !r.status)}>
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
          <table className="min-w-[900px] w-full border-collapse text-left">
            <thead>
              <tr className="border-b border-[var(--border)] bg-[var(--surface-glass)]">
                {["Employee", "Department", "Present", "Absent", "Reason", ""].map(h => (
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
                    <button
                      type="button"
                      onClick={() => setRowStatus(row.employeeId, "present")}
                      className={`flex h-5 w-5 items-center justify-center rounded border text-[10px] font-bold transition-colors ${
                        row.status === "present"
                          ? "border-emerald-400 bg-emerald-400 text-white"
                          : "border-[var(--border)] hover:border-emerald-400/50"
                      }`}
                    >
                      {row.status === "present" ? "✓" : ""}
                    </button>
                  </td>
                  <td className="px-5 py-4">
                    <button
                      type="button"
                      onClick={() => setRowStatus(row.employeeId, "absent")}
                      className={`flex h-5 w-5 items-center justify-center rounded border text-[10px] font-bold transition-colors ${
                        row.status === "absent"
                          ? "border-rose-400 bg-rose-400 text-white"
                          : "border-[var(--border)] hover:border-rose-400/50"
                      }`}
                    >
                      {row.status === "absent" ? "✓" : ""}
                    </button>
                  </td>
                  <td className="px-5 py-4">
                    {row.status === "absent" ? (
                      <input
                        type="text"
                        value={row.absentReason}
                        onChange={e => setRows(prev => prev.map(r =>
                          r.employeeId === row.employeeId ? { ...r, absentReason: e.target.value } : r
                        ))}
                        placeholder="Reason for absence..."
                        className="w-full min-w-[160px] rounded-lg border border-[var(--border)] bg-[var(--input-bg)] px-3 py-1.5 text-sm text-[var(--text-primary)] outline-none focus:border-[var(--accent)]"
                      />
                    ) : (
                      <span className="text-sm text-[var(--text-muted)]">{row.notes ?? "—"}</span>
                    )}
                  </td>
                  <td className="px-5 py-4">
                    <Button size="sm" variant="outline" onClick={() => saveRow(row.employeeId)} disabled={!row.status || row.saving}>
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
