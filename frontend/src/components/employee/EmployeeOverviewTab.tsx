import { useState } from "react";
import { api } from "../../api/api.ts";
import Button from "../../lib/Button.tsx";
import Field from "../../lib/Field.tsx";
import type { EmployeeFromApi } from "../../utils/parseEmployee.ts";
import { formatPkr } from "../../utils/currency.ts";

const STATUS_CONFIG: Record<number, { label: string; color: string }> = {
  0: { label: "Active",      color: "text-emerald-400 bg-emerald-500/10 border-emerald-500/20" },
  1: { label: "Inactive",    color: "text-[var(--text-muted)] bg-[var(--surface-glass)] border-[var(--border)]" },
  2: { label: "On Leave",    color: "text-amber-400 bg-amber-500/10 border-amber-500/20" },
  3: { label: "Terminated",  color: "text-rose-400 bg-rose-500/10 border-rose-500/20" },
};

const STATUS_API_VALUES = ["Active", "Inactive", "OnLeave", "Terminated"] as const;

function statusNum(s: number | string): number {
  if (typeof s === "number" && Number.isFinite(s)) return s;
  const map: Record<string, number> = { Active: 0, Inactive: 1, OnLeave: 2, Terminated: 3 };
  return typeof s === "string" && map[s] !== undefined ? map[s] : 0;
}

const AVATAR_COLORS = [
  "from-indigo-500 to-violet-600", "from-blue-500 to-cyan-600",
  "from-emerald-500 to-teal-600",  "from-rose-500 to-pink-600",
  "from-amber-500 to-orange-600",  "from-purple-500 to-indigo-600",
];

interface Props {
  employee: EmployeeFromApi;
  onEmployeeUpdate: (updated: EmployeeFromApi) => void;
}

export default function EmployeeOverviewTab({ employee, onEmployeeUpdate }: Props) {
  const [showEdit, setShowEdit] = useState(false);
  const [saving, setSaving] = useState(false);
  const [editError, setEditError] = useState<string | null>(null);
  const sn = statusNum(employee.status);
  const statusCfg = STATUS_CONFIG[sn] ?? STATUS_CONFIG[0];
  const gradient = AVATAR_COLORS[employee.id % AVATAR_COLORS.length];
  const initials = employee.fullName.split(" ").slice(0, 2).map(w => w[0]?.toUpperCase() ?? "").join("");

  const [form, setForm] = useState({
    fullName: employee.fullName,
    jobTitle: employee.jobTitle,
    department: employee.department,
    phone: employee.phone,
    email: employee.email ?? "",
    address: employee.address ?? "",
    salary: String(employee.salary),
    status: sn,
  });

  const saveEdit = async () => {
    setEditError(null);
    if (!form.fullName.trim() || !form.jobTitle.trim() || !form.department.trim() || !form.phone.trim()) {
      setEditError("Name, job title, department, and phone are required.");
      return;
    }
    const salary = form.salary.trim() === "" ? 0 : Number(form.salary);
    if (!Number.isFinite(salary) || salary < 0) { setEditError("Salary must be a valid non-negative number."); return; }
    setSaving(true);
    try {
      const res = await api(`/api/Employee/${employee.id}`, {
        method: "PUT",
        body: JSON.stringify({
          fullName: form.fullName.trim(), jobTitle: form.jobTitle.trim(),
          department: form.department.trim(), phone: form.phone.trim(),
          email: form.email.trim() || null, address: form.address.trim() || null,
          salary, status: STATUS_API_VALUES[form.status],
        }),
      });
      if (!res.ok) {
        const d = await res.json().catch(() => ({}));
        setEditError((d as { message?: string }).message ?? "Failed to update employee.");
        return;
      }
      const updated = await res.json() as EmployeeFromApi;
      onEmployeeUpdate(updated);
      setShowEdit(false);
    } catch { setEditError("Something went wrong."); }
    finally { setSaving(false); }
  };

  const infoItems = [
    { icon: <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M20 21v-2a4 4 0 00-4-4H8a4 4 0 00-4 4v2"/><circle cx="12" cy="7" r="4"/></svg>, label: "Full Name",   value: employee.fullName },
    { icon: <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><rect x="2" y="7" width="20" height="14" rx="2"/><path d="M16 21V5a2 2 0 00-2-2h-4a2 2 0 00-2 2v16"/></svg>, label: "Job Title",   value: employee.jobTitle },
    { icon: <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M3 9l9-7 9 7v11a2 2 0 01-2 2H5a2 2 0 01-2-2z"/></svg>, label: "Department",  value: employee.department },
    { icon: <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M22 16.92v3a2 2 0 01-2.18 2 19.79 19.79 0 01-8.63-3.07A19.5 19.5 0 013.07 9.63a19.79 19.79 0 01-3.07-8.67A2 2 0 012 .84h3a2 2 0 012 1.72c.127.96.361 1.903.7 2.81a2 2 0 01-.45 2.11L6.09 8.49a16 16 0 006.29 6.29l1.42-1.42a2 2 0 012.11-.45c.907.339 1.85.573 2.81.7A2 2 0 0122 15.92z"/></svg>, label: "Phone",       value: employee.phone },
    { icon: <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M4 4h16c1.1 0 2 .9 2 2v12c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2V6c0-1.1.9-2 2-2z"/><polyline points="22,6 12,13 2,6"/></svg>, label: "Email",       value: employee.email ?? "—" },
    { icon: <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0118 0z"/><circle cx="12" cy="10" r="3"/></svg>, label: "Address",     value: employee.address ?? "—" },
    { icon: <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><circle cx="12" cy="12" r="10"/><path d="M16 8h-6a2 2 0 100 4h4a2 2 0 110 4H8"/><path d="M12 18V6"/></svg>, label: "Salary",      value: formatPkr(employee.salary) },
    { icon: <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><rect x="3" y="4" width="18" height="18" rx="2"/><line x1="16" y1="2" x2="16" y2="6"/><line x1="8" y1="2" x2="8" y2="6"/><line x1="3" y1="10" x2="21" y2="10"/></svg>, label: "Joined",       value: new Date(employee.joinDate).toLocaleDateString("en-US", { dateStyle: "long" }) },
    { icon: <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M22 11.08V12a10 10 0 11-5.93-9.14"/><polyline points="22 4 12 14.01 9 11.01"/></svg>, label: "Status",      value: statusCfg.label },
  ];

  return (
    <div className="py-8 sm:py-10">
      <div className="mx-auto w-full max-w-7xl px-5 sm:px-8 lg:px-10">

        {/* Profile Card + Edit CTA */}
        <div className="mb-10 flex flex-col gap-6 lg:flex-row lg:items-start">
          {/* Avatar + Identity */}
          <div className="flex items-center gap-5 lg:w-72 lg:shrink-0">
            <div className={`flex h-20 w-20 shrink-0 items-center justify-center rounded-2xl bg-gradient-to-br ${gradient} text-2xl font-bold text-white shadow-lg`}>
              {initials}
            </div>
            <div>
              <h2 className="text-xl font-bold text-[var(--text-heading)]">{employee.fullName}</h2>
              <p className="text-sm text-[var(--text-muted)]">{employee.jobTitle}</p>
              <span className={`mt-2 inline-flex items-center gap-1.5 rounded-full border px-3 py-1 text-xs font-semibold ${statusCfg.color}`}>
                <span className={`h-1.5 w-1.5 rounded-full ${sn === 0 ? "bg-emerald-400 animate-pulse" : "bg-current"}`} />
                {statusCfg.label}
              </span>
            </div>
          </div>

          {/* Quick Stats */}
          <div className="flex-1 grid grid-cols-2 gap-3 sm:grid-cols-3">
            {[
              { label: "Department", value: employee.department },
              { label: "Salary",     value: formatPkr(employee.salary) },
              { label: "Joined",     value: new Date(employee.joinDate).toLocaleDateString("en-US", { month: "short", year: "numeric" }) },
            ].map(s => (
              <div key={s.label} className="rounded-xl border border-[var(--border)] bg-[var(--surface-glass)] p-4">
                <p className="text-[11px] uppercase tracking-wider text-[var(--text-muted)] mb-1">{s.label}</p>
                <p className="text-sm font-semibold text-[var(--text-heading)]">{s.value}</p>
              </div>
            ))}
          </div>

          {/* Edit Button */}
          <div>
            <Button size="sm" variant="outline" onClick={() => setShowEdit(p => !p)}>
              <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M11 4H4a2 2 0 00-2 2v14a2 2 0 002 2h14a2 2 0 002-2v-7"/><path d="M18.5 2.5a2.121 2.121 0 013 3L12 15l-4 1 1-4 9.5-9.5z"/></svg>
              {showEdit ? "Cancel" : "Edit Profile"}
            </Button>
          </div>
        </div>

        {/* Edit Form */}
        {showEdit && (
          <div className="mb-10 animate-scale-in rounded-2xl border border-[var(--accent-glow-strong)] bg-[var(--accent-glow)] p-6">
            <h4 className="mb-4 text-sm font-semibold text-[var(--text-heading)]">Edit Employee Profile</h4>
            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
              <Field label="Full Name"   value={form.fullName}   onChange={e => setForm(f => ({ ...f, fullName: e.target.value }))} />
              <Field label="Job Title"   value={form.jobTitle}   onChange={e => setForm(f => ({ ...f, jobTitle: e.target.value }))} />
              <Field label="Department"  value={form.department} onChange={e => setForm(f => ({ ...f, department: e.target.value }))} />
              <Field label="Phone"       value={form.phone}      onChange={e => setForm(f => ({ ...f, phone: e.target.value }))} maxLength={50} />
              <Field label="Email"       value={form.email}      onChange={e => setForm(f => ({ ...f, email: e.target.value }))} type="email" hint="Optional" />
              <Field label="Salary"      value={form.salary}     onChange={e => setForm(f => ({ ...f, salary: e.target.value }))} placeholder="0" />
              <div className="sm:col-span-2">
                <Field label="Address"   value={form.address}    onChange={e => setForm(f => ({ ...f, address: e.target.value }))} hint="Optional" />
              </div>
              <div>
                <label className="text-xs font-medium text-[var(--text-muted)] mb-2 block">Status</label>
                <select
                  value={form.status}
                  onChange={e => setForm(f => ({ ...f, status: Number(e.target.value) }))}
                  className="w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-4 py-2.5 text-sm text-[var(--text-primary)] transition-all focus:border-[var(--accent)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-glow)]"
                >
                  {Object.entries(STATUS_CONFIG).map(([v, { label }]) => <option key={v} value={v}>{label}</option>)}
                </select>
              </div>
            </div>
            {editError && (
              <p className="mt-3 flex items-center gap-1.5 text-xs text-rose-400">
                <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>
                {editError}
              </p>
            )}
            <div className="mt-5 flex items-center gap-3">
              <Button size="sm" onClick={saveEdit} disabled={saving}>
                {saving ? "Saving…" : "Save Changes"}
              </Button>
              <Button size="sm" variant="ghost" onClick={() => setShowEdit(false)}>Cancel</Button>
            </div>
          </div>
        )}

        {/* Info Grid */}
        <h2 className="section-title">Employee Information</h2>
        <div className="info-grid">
          {infoItems.map(item => (
            <div key={item.label} className="info-card">
              <div className="info-card__label">{item.icon}<span>{item.label}</span></div>
              <div className="info-card__value">{item.value}</div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
