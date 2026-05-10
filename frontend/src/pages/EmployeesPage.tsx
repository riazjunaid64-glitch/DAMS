import { useEffect, useState, type FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import { api } from "../api/api.ts";
import type { User } from "../App.tsx";
import Button from "../lib/Button.tsx";
import Container from "../lib/Container.tsx";
import Field from "../lib/Field.tsx";
import { parseEmployeesPayload, type EmployeeFromApi } from "../utils/parseEmployee.ts";

type Props = { user: User | null };

const STATUS_CONFIG: Record<number, { label: string; color: string }> = {
  0: { label: "Active",      color: "text-emerald-400 bg-emerald-500/10 border-emerald-500/20" },
  1: { label: "Inactive",    color: "text-[var(--text-muted)] bg-[var(--surface-glass)] border-[var(--border)]" },
  2: { label: "On Leave",    color: "text-amber-400 bg-amber-500/10 border-amber-500/20" },
  3: { label: "Terminated",  color: "text-rose-400 bg-rose-500/10 border-rose-500/20" },
};

const STATUS_API_VALUES = ["Active", "Inactive", "OnLeave", "Terminated"] as const;

async function errorTextFromResponse(res: Response): Promise<string> {
  const raw = await res.text();
  if (!raw.trim()) return `Request failed (HTTP ${res.status}).`;
  try {
    const j = JSON.parse(raw) as Record<string, unknown>;
    if (typeof j.message === "string" && j.message) return j.message;
    if (j.errors && typeof j.errors === "object") {
      for (const msgs of Object.values(j.errors as Record<string, unknown>)) {
        if (Array.isArray(msgs) && msgs.length > 0 && typeof msgs[0] === "string") return msgs[0];
        if (typeof msgs === "string") return msgs;
      }
    }
    if (typeof j.title === "string" && j.title) return j.title;
  } catch { /* not JSON */ }
  return raw;
}

function statusNum(s: number | string): number {
  if (typeof s === "number" && Number.isFinite(s)) return s;
  const map: Record<string, number> = { Active: 0, Inactive: 1, OnLeave: 2, Terminated: 3 };
  if (typeof s === "string" && map[s] !== undefined) return map[s];
  return 0;
}

function getInitials(name: string) {
  return name.split(" ").slice(0, 2).map(w => w[0]?.toUpperCase() ?? "").join("");
}

const AVATAR_COLORS = [
  "from-indigo-500 to-violet-600",
  "from-blue-500 to-cyan-600",
  "from-emerald-500 to-teal-600",
  "from-rose-500 to-pink-600",
  "from-amber-500 to-orange-600",
  "from-purple-500 to-indigo-600",
];

function avatarColor(id: number) {
  return AVATAR_COLORS[id % AVATAR_COLORS.length];
}

export default function EmployeesPage({ user }: Props) {
  const navigate = useNavigate();
  const [employees, setEmployees] = useState<EmployeeFromApi[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [showForm, setShowForm] = useState(false);
  const [search, setSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState<string>("all");
  const [deptFilter, setDeptFilter] = useState<string>("all");
  const [form, setForm] = useState({
    fullName: "", jobTitle: "", department: "", phone: "",
    email: "", address: "", salary: "", joinDate: "", status: 0,
  });

  const isAdmin = user?.role === "Admin";

  const load = async () => {
    if (!isAdmin) return;
    setLoading(true);
    setError(null);
    try {
      const res = await api("/api/Employee");
      if (!res.ok) {
        setError(res.status === 401 || res.status === 403 ? "Admin access required." : `Could not load employees (HTTP ${res.status}).`);
        setEmployees([]);
        return;
      }
      const raw: unknown = await res.json();
      setEmployees(Array.isArray(raw) ? parseEmployeesPayload(raw) : []);
    } catch {
      setError("Could not load employees.");
      setEmployees([]);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { load(); }, [user, isAdmin]);

  const submitCreate = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    if (!form.fullName.trim() || !form.jobTitle.trim() || !form.department.trim() || !form.phone.trim() || !form.joinDate) {
      setError("Full name, job title, department, phone, and join date are required.");
      return;
    }
    const salary = form.salary.trim() === "" ? 0 : Number(form.salary.trim());
    if (!Number.isFinite(salary) || salary < 0) { setError("Salary must be a valid non-negative number."); return; }
    const join = new Date(`${form.joinDate}T12:00:00`);
    if (Number.isNaN(join.getTime())) { setError("Please choose a valid join date."); return; }
    if (form.phone.trim().length > 50) { setError("Phone cannot exceed 50 characters."); return; }
    try {
      const emailTrim = form.email.trim();
      const res = await api("/api/Employee", {
        method: "POST",
        body: JSON.stringify({
          fullName: form.fullName.trim(), jobTitle: form.jobTitle.trim(),
          department: form.department.trim(), phone: form.phone.trim(),
          email: emailTrim === "" ? null : emailTrim,
          address: form.address.trim() || null, salary,
          joinDate: join.toISOString(),
          status: STATUS_API_VALUES[form.status] ?? "Active",
        }),
      });
      if (!res.ok) { setError(await errorTextFromResponse(res)); return; }
      setShowForm(false);
      setForm({ fullName: "", jobTitle: "", department: "", phone: "", email: "", address: "", salary: "", joinDate: "", status: 0 });
      await load();
    } catch { setError("Could not create employee."); }
  };

  const departments = [...new Set(employees.map(e => e.department))].sort();

  const filtered = employees.filter(emp => {
    const sn = statusNum(emp.status);
    if (statusFilter !== "all" && String(sn) !== statusFilter) return false;
    if (deptFilter !== "all" && emp.department !== deptFilter) return false;
    if (search.trim()) {
      const q = search.toLowerCase();
      if (!emp.fullName.toLowerCase().includes(q) &&
          !emp.jobTitle.toLowerCase().includes(q) &&
          !emp.department.toLowerCase().includes(q) &&
          !(emp.email ?? "").toLowerCase().includes(q)) return false;
    }
    return true;
  });

  if (!isAdmin) {
    return (
      <div className="py-16 sm:py-24">
        <Container>
          <div className="mx-auto max-w-lg rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] px-6 py-10 text-center">
            <h1 className="text-xl font-semibold text-[var(--text-heading)]">Employees</h1>
            <p className="mt-3 text-sm text-[var(--text-muted)]">
              Employee management is available to administrators only.
            </p>
          </div>
        </Container>
      </div>
    );
  }

  const activeCount = employees.filter(e => statusNum(e.status) === 0).length;

  return (
    <>
      {/* Page Header */}
      <div className="relative overflow-hidden border-b border-[var(--border)]">
        <div className="absolute inset-0 mesh-gradient-subtle" />
        <Container className="relative py-8 sm:py-10">
          <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
            <div>
              <div className="mb-2 inline-flex items-center gap-2 rounded-full bg-indigo-500/[0.08] px-3 py-1">
                <span className="h-1.5 w-1.5 rounded-full bg-indigo-500" />
                <span className="text-[10px] font-semibold uppercase tracking-wider text-indigo-500">Admin Module</span>
              </div>
              <h1 className="text-2xl font-bold text-[var(--text-heading)] sm:text-3xl">Employees</h1>
              <p className="mt-1 text-sm text-[var(--text-muted)]">
                {employees.length} team member{employees.length !== 1 ? "s" : ""} · {activeCount} active
              </p>
            </div>
            <Button onClick={() => setShowForm(v => !v)}>
              {showForm ? (
                <>
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
                  Cancel
                </>
              ) : (
                <>
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
                  Add Employee
                </>
              )}
            </Button>
          </div>
        </Container>
      </div>

      <Container className="py-8">
        {/* Add Employee Form */}
        {showForm && (
          <form onSubmit={submitCreate} className="mb-10 animate-scale-in rounded-2xl border border-[var(--accent-glow-strong)] bg-[var(--surface-glass)] p-6 shadow-sm">
            <div className="mb-4 flex items-center justify-between">
              <h2 className="text-base font-semibold text-[var(--text-heading)]">New Employee</h2>
              <button type="button" onClick={() => setShowForm(false)} className="flex h-7 w-7 items-center justify-center rounded-lg text-[var(--text-muted)] hover:bg-[var(--surface-glass-hover)] hover:text-[var(--text-primary)]">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
              </button>
            </div>
            <div className="grid gap-4 sm:grid-cols-2">
              <Field label="Full Name" value={form.fullName} onChange={e => setForm(f => ({ ...f, fullName: e.target.value }))} required />
              <Field label="Job Title" value={form.jobTitle} onChange={e => setForm(f => ({ ...f, jobTitle: e.target.value }))} required />
              <Field label="Department" value={form.department} onChange={e => setForm(f => ({ ...f, department: e.target.value }))} required />
              <Field label="Phone" value={form.phone} maxLength={50} onChange={e => setForm(f => ({ ...f, phone: e.target.value }))} required />
              <Field label="Email" type="email" value={form.email} onChange={e => setForm(f => ({ ...f, email: e.target.value }))} hint="Optional" />
              <Field label="Address" value={form.address} onChange={e => setForm(f => ({ ...f, address: e.target.value }))} hint="Optional" />
              <Field label="Salary" value={form.salary} onChange={e => setForm(f => ({ ...f, salary: e.target.value }))} placeholder="0" hint="Optional" />
              <Field label="Join Date" type="date" value={form.joinDate} onChange={e => setForm(f => ({ ...f, joinDate: e.target.value }))} required />
              <label className="flex flex-col gap-1.5 text-sm font-medium text-[var(--text-secondary)] sm:col-span-2">
                Status
                <select className="rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-4 py-3 text-sm text-[var(--text-primary)] transition-all focus:border-[var(--accent)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-glow)]" value={form.status} onChange={e => setForm(f => ({ ...f, status: Number(e.target.value) }))}>
                  {Object.entries(STATUS_CONFIG).map(([v, { label }]) => <option key={v} value={v}>{label}</option>)}
                </select>
              </label>
            </div>
            {error && (
              <div className="mt-4 rounded-xl border border-rose-500/20 bg-rose-500/[0.06] px-4 py-3 text-sm text-rose-300 flex items-center gap-2">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>
                {error}
              </div>
            )}
            <div className="mt-6 flex justify-end gap-3">
              <Button type="button" variant="ghost" onClick={() => setShowForm(false)}>Cancel</Button>
              <Button type="submit">Save Employee</Button>
            </div>
          </form>
        )}

        {/* Error (non-form) */}
        {error && !showForm && (
          <div className="mb-6 rounded-2xl border border-rose-500/20 bg-rose-500/[0.06] px-4 py-3 text-sm text-rose-300 flex items-center gap-2">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><circle cx="12" cy="12" r="10"/><line x1="15" y1="9" x2="9" y2="15"/><line x1="9" y1="9" x2="15" y2="15"/></svg>
            {error}
          </div>
        )}

        {/* Search & Filters */}
        <div className="mb-6 flex flex-col gap-3 sm:flex-row sm:items-center">
          <div className="relative flex-1">
            <svg className="absolute left-3 top-1/2 -translate-y-1/2 text-[var(--text-muted)]" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/></svg>
            <input
              type="text"
              value={search}
              onChange={e => setSearch(e.target.value)}
              placeholder="Search by name, title, department, email…"
              className="w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] pl-10 pr-4 py-2.5 text-sm text-[var(--text-primary)] placeholder:text-[var(--text-muted)] transition-all focus:border-[var(--accent)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-glow)]"
            />
          </div>
          <select
            value={statusFilter}
            onChange={e => setStatusFilter(e.target.value)}
            className="rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-4 py-2.5 text-sm text-[var(--text-primary)] transition-all focus:border-[var(--accent)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-glow)]"
          >
            <option value="all">All Statuses</option>
            {Object.entries(STATUS_CONFIG).map(([v, { label }]) => <option key={v} value={v}>{label}</option>)}
          </select>
          {departments.length > 0 && (
            <select
              value={deptFilter}
              onChange={e => setDeptFilter(e.target.value)}
              className="rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-4 py-2.5 text-sm text-[var(--text-primary)] transition-all focus:border-[var(--accent)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-glow)]"
            >
              <option value="all">All Departments</option>
              {departments.map(d => <option key={d} value={d}>{d}</option>)}
            </select>
          )}
        </div>

        {/* Loading */}
        {loading && (
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {[...Array(6)].map((_, i) => (
              <div key={i} className="rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] p-5">
                <div className="flex items-center gap-3 mb-3">
                  <div className="skeleton h-10 w-10 rounded-xl" />
                  <div className="flex-1"><div className="skeleton h-4 w-3/4 mb-1.5" /><div className="skeleton h-3 w-1/2" /></div>
                </div>
                <div className="skeleton h-3 w-full mb-2" /><div className="skeleton h-3 w-2/3" />
              </div>
            ))}
          </div>
        )}

        {/* Empty state */}
        {!loading && filtered.length === 0 && (
          <div className="py-20 text-center">
            <div className="mx-auto mb-4 flex h-16 w-16 items-center justify-center rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)]">
              <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" className="text-[var(--text-muted)]" strokeLinecap="round"><path d="M17 21v-2a4 4 0 00-4-4H5a4 4 0 00-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 00-3-3.87M16 3.13a4 4 0 010 7.75"/></svg>
            </div>
            <h3 className="text-lg font-semibold text-[var(--text-heading)]">
              {employees.length === 0 ? "No Employees Yet" : "No Results Found"}
            </h3>
            <p className="mt-1 text-sm text-[var(--text-muted)]">
              {employees.length === 0
                ? "Add your first employee to get started."
                : "Try adjusting your search or filter criteria."}
            </p>
          </div>
        )}

        {/* Employee Cards Grid */}
        {!loading && filtered.length > 0 && (
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {filtered.map(emp => {
              const sn = statusNum(emp.status);
              const statusCfg = STATUS_CONFIG[sn] ?? STATUS_CONFIG[0];
              const initials = getInitials(emp.fullName);
              const gradient = avatarColor(emp.id);
              return (
                <div
                  key={emp.id}
                  className="group relative rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] p-5 transition-all hover:border-[var(--border-hover)] hover:shadow-lg hover:bg-[var(--surface-glass-hover)] cursor-pointer"
                  onClick={() => navigate(`/employees/${emp.id}`)}
                >
                  {/* Card Header */}
                  <div className="flex items-start gap-3 mb-4">
                    <div className={`flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-gradient-to-br ${gradient} text-sm font-bold text-white shadow-sm`}>
                      {initials}
                    </div>
                    <div className="flex-1 min-w-0">
                      <p className="font-semibold text-[var(--text-heading)] truncate group-hover:text-[var(--accent)] transition-colors">
                        {emp.fullName}
                      </p>
                      <p className="text-xs text-[var(--text-muted)] truncate">{emp.jobTitle}</p>
                    </div>
                    <span className={`shrink-0 rounded-full border px-2.5 py-0.5 text-[10px] font-semibold ${statusCfg.color}`}>
                      {statusCfg.label}
                    </span>
                  </div>

                  {/* Details */}
                  <div className="space-y-1.5">
                    <div className="flex items-center gap-2 text-xs text-[var(--text-muted)]">
                      <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><rect x="2" y="7" width="20" height="14" rx="2"/><path d="M16 21V5a2 2 0 00-2-2h-4a2 2 0 00-2 2v16"/></svg>
                      <span className="truncate">{emp.department}</span>
                    </div>
                    <div className="flex items-center gap-2 text-xs text-[var(--text-muted)]">
                      <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M22 16.92v3a2 2 0 01-2.18 2 19.79 19.79 0 01-8.63-3.07A19.5 19.5 0 013.07 9.63a19.79 19.79 0 01-3.07-8.67A2 2 0 012 .84h3a2 2 0 012 1.72c.127.96.361 1.903.7 2.81a2 2 0 01-.45 2.11L6.09 8.49a16 16 0 006.29 6.29l1.42-1.42a2 2 0 012.11-.45c.907.339 1.85.573 2.81.7A2 2 0 0122 15.92z"/></svg>
                      <span>{emp.phone}</span>
                    </div>
                    {emp.email && (
                      <div className="flex items-center gap-2 text-xs text-[var(--text-muted)]">
                        <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M4 4h16c1.1 0 2 .9 2 2v12c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2V6c0-1.1.9-2 2-2z"/><polyline points="22,6 12,13 2,6"/></svg>
                        <span className="truncate">{emp.email}</span>
                      </div>
                    )}
                  </div>

                  {/* Footer */}
                  <div className="mt-4 flex items-center justify-between border-t border-[var(--border)] pt-3">
                    <span className="text-[10px] text-[var(--text-muted)]">
                      Joined {new Date(emp.joinDate).toLocaleDateString("en-US", { month: "short", year: "numeric" })}
                    </span>
                    <span className="flex items-center gap-1 text-[10px] font-medium text-[var(--accent)] opacity-0 group-hover:opacity-100 transition-opacity">
                      View Profile
                      <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round"><polyline points="9 18 15 12 9 6"/></svg>
                    </span>
                  </div>
                </div>
              );
            })}
          </div>
        )}
      </Container>
    </>
  );
}
