import { useEffect, useMemo, useState, type FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import { api } from "../api/api.ts";
import type { User } from "../App.tsx";
import Button from "../lib/Button.tsx";
import Container from "../lib/Container.tsx";
import Field from "../lib/Field.tsx";
import TabLayout from "../lib/TabLayout.tsx";
import EmployeesAttendancePanel from "../components/employee/EmployeesAttendancePanel.tsx";
import EmployeesSalaryPanel from "../components/employee/EmployeesSalaryPanel.tsx";
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

function formatJoinDate(date: string) {
  const parsed = new Date(date);
  if (Number.isNaN(parsed.getTime())) return "Not set";
  return parsed.toLocaleDateString("en-US", { month: "short", day: "numeric", year: "numeric" });
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
  const [activeTab, setActiveTab] = useState("team");
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

  const departments = useMemo(() => [...new Set(employees.map(e => e.department))].sort(), [employees]);

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    return employees.filter(emp => {
      const sn = statusNum(emp.status);
      if (statusFilter !== "all" && String(sn) !== statusFilter) return false;
      if (deptFilter !== "all" && emp.department !== deptFilter) return false;
      if (q &&
          !emp.fullName.toLowerCase().includes(q) &&
          !emp.jobTitle.toLowerCase().includes(q) &&
          !emp.department.toLowerCase().includes(q) &&
          !(emp.email ?? "").toLowerCase().includes(q)) return false;
      return true;
    });
  }, [deptFilter, employees, search, statusFilter]);

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

  const activeCount = useMemo(() => employees.filter(e => statusNum(e.status) === 0).length, [employees]);

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
                {employees.length} team member{employees.length !== 1 ? "s" : ""} - {activeCount} active
              </p>
            </div>
            {activeTab === "team" && (
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
            )}
          </div>
        </Container>
      </div>

      <TabLayout
        tabs={[
          {
            id: "team", label: "Team",
            icon: <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M17 21v-2a4 4 0 00-4-4H5a4 4 0 00-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 00-3-3.87M16 3.13a4 4 0 010 7.75"/></svg>,
          },
          {
            id: "attendance", label: "Attendance",
            icon: <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><rect x="3" y="4" width="18" height="18" rx="2"/><line x1="16" y1="2" x2="16" y2="6"/><line x1="8" y1="2" x2="8" y2="6"/><line x1="3" y1="10" x2="21" y2="10"/><path d="M9 16l2 2 4-4"/></svg>,
          },
          {
            id: "salary", label: "Salaries",
            icon: <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><circle cx="12" cy="12" r="10"/><path d="M16 8h-6a2 2 0 100 4h4a2 2 0 110 4H8"/><path d="M12 18V6"/></svg>,
          },
        ]}
        activeTab={activeTab}
        onTabChange={setActiveTab}
      >
      <Container className="py-8">
        {activeTab === "attendance" && <EmployeesAttendancePanel />}
        {activeTab === "salary" && <EmployeesSalaryPanel />}

        {activeTab === "team" && (<>
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
              placeholder="Search by name, email, department, position..."
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
          <div className="overflow-x-auto rounded-xl border border-[var(--border)] bg-[var(--bg-card)] shadow-sm">
            <div className="min-w-[1080px]">
              <div className="grid grid-cols-[1.35fr_1fr_1fr_0.8fr_1.15fr_1fr_0.85fr_0.85fr_0.7fr] gap-4 border-b border-[var(--border)] bg-[var(--surface-glass)] px-5 py-4">
                {[...Array(9)].map((_, i) => <div key={i} className="skeleton h-3" />)}
              </div>
              {[...Array(5)].map((_, row) => (
                <div key={row} className="grid grid-cols-[1.35fr_1fr_1fr_0.8fr_1.15fr_1fr_0.85fr_0.85fr_0.7fr] gap-4 border-b border-[var(--border)] px-5 py-5 last:border-b-0">
                  {[...Array(9)].map((_, cell) => <div key={cell} className="skeleton h-4" />)}
                </div>
              ))}
            </div>
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

        {/* Employees Table */}
        {!loading && filtered.length > 0 && (
          <div className="overflow-x-auto rounded-xl border border-[var(--border)] bg-[var(--bg-card)] shadow-sm">
            <table className="min-w-[1080px] w-full border-collapse text-left">
              <thead>
                <tr className="border-b border-[var(--border)] bg-[var(--surface-glass)]">
                  {["Employee", "Position", "Department", "Status", "Assigned Project", "Assigned Task", "Phone", "Joined", "Actions"].map(label => (
                    <th key={label} className="px-5 py-3 text-[10px] font-bold uppercase tracking-wider text-[var(--text-muted)]">
                      {label}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {filtered.map(emp => {
                  const sn = statusNum(emp.status);
                  const statusCfg = STATUS_CONFIG[sn] ?? STATUS_CONFIG[0];
                  const initials = getInitials(emp.fullName);
                  const gradient = avatarColor(emp.id);
                  return (
                    <tr
                      key={emp.id}
                      className="group border-b border-[var(--border)] transition-colors hover:bg-[var(--surface-glass-hover)] last:border-b-0"
                    >
                      <td className="px-5 py-4">
                        <button
                          type="button"
                          onClick={() => navigate(`/employees/${emp.id}`)}
                          className="flex min-w-0 items-center gap-3 text-left"
                        >
                          <span className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-gradient-to-br ${gradient} text-xs font-bold text-white shadow-sm`}>
                            {initials}
                          </span>
                          <span className="min-w-0">
                            <span className="block truncate text-sm font-semibold text-[var(--accent)] group-hover:text-[var(--accent-light)]">
                              {emp.fullName}
                            </span>
                            <span className="block truncate text-xs text-[var(--text-muted)]">{emp.email ?? "No email"}</span>
                          </span>
                        </button>
                      </td>
                      <td className="px-5 py-4 text-sm text-[var(--text-secondary)]">{emp.jobTitle || "Unassigned"}</td>
                      <td className="px-5 py-4 text-sm text-[var(--text-secondary)]">{emp.department || "Unassigned"}</td>
                      <td className="px-5 py-4">
                        <span className={`inline-flex rounded-full border px-2.5 py-1 text-[10px] font-semibold ${statusCfg.color}`}>
                          {statusCfg.label}
                        </span>
                      </td>
                      <td className="px-5 py-4 text-sm font-medium text-[var(--accent)]">Unassigned</td>
                      <td className="px-5 py-4 text-sm text-[var(--text-secondary)]">Unassigned</td>
                      <td className="px-5 py-4 text-sm text-[var(--text-secondary)]">{emp.phone || "Not set"}</td>
                      <td className="px-5 py-4 text-sm text-[var(--text-secondary)]">{formatJoinDate(emp.joinDate)}</td>
                      <td className="px-5 py-4">
                        <button
                          type="button"
                          onClick={() => navigate(`/employees/${emp.id}`)}
                          className="inline-flex items-center gap-1.5 rounded-lg border border-[var(--accent-glow-strong)] bg-[var(--accent-glow)] px-3 py-1.5 text-xs font-semibold text-[var(--accent)] transition-colors hover:border-[var(--accent)] hover:bg-[var(--surface-glass-hover)]"
                        >
                          View Details
                          <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.25" strokeLinecap="round" strokeLinejoin="round"><path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7-10-7-10-7z"/><circle cx="12" cy="12" r="3"/></svg>
                        </button>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
        </>)}
      </Container>
      </TabLayout>
    </>
  );
}
