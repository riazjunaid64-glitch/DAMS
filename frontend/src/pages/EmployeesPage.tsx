import { useEffect, useState, type FormEvent } from "react";
import { api } from "../api/api.ts";
import type { User } from "../App.tsx";
import Button from "../lib/Button.tsx";
import Container from "../lib/Container.tsx";
import Field from "../lib/Field.tsx";
import { parseEmployeesPayload } from "../utils/parseEmployee.ts";

type Props = { user: User | null };

const statusLabels: Record<number, string> = {
  0: "Active",
  1: "Inactive",
  2: "On leave",
  3: "Terminated",
};

/** Must match backend enum names (JSON string enum converter). */
const STATUS_API_VALUES = ["Active", "Inactive", "OnLeave", "Terminated"] as const;

async function errorTextFromResponse(res: Response): Promise<string> {
  const raw = await res.text();
  if (!raw.trim()) return `Request failed (HTTP ${res.status}).`;
  try {
    const j = JSON.parse(raw) as Record<string, unknown>;
    if (typeof j.message === "string" && j.message) return j.message;
    if (j.errors && typeof j.errors === "object" && j.errors !== null) {
      const errs = j.errors as Record<string, unknown>;
      for (const msgs of Object.values(errs)) {
        if (Array.isArray(msgs) && msgs.length > 0 && typeof msgs[0] === "string") return msgs[0];
        if (typeof msgs === "string") return msgs;
      }
    }
    if (typeof j.title === "string" && j.title) return j.title;
  } catch {
    /* not JSON */
  }
  return raw;
}

function statusNum(s: number | string): number {
  if (typeof s === "number" && Number.isFinite(s)) return s;
  return 0;
}

export default function EmployeesPage({ user }: Props) {
  const [employees, setEmployees] = useState<ReturnType<typeof parseEmployeesPayload>>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [showForm, setShowForm] = useState(false);
  const [form, setForm] = useState({
    fullName: "",
    jobTitle: "",
    department: "",
    phone: "",
    email: "",
    address: "",
    salary: "",
    joinDate: "",
    status: 0,
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
      if (!Array.isArray(raw)) {
        setError("Unexpected API response.");
        setEmployees([]);
        return;
      }
      setEmployees(parseEmployeesPayload(raw));
    } catch {
      setError("Could not load employees.");
      setEmployees([]);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    load();
  }, [user, isAdmin]);

  const submitCreate = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    if (!form.fullName.trim() || !form.jobTitle.trim() || !form.department.trim() || !form.phone.trim() || !form.joinDate) {
      setError("Full name, job title, department, phone, and join date are required.");
      return;
    }
    const salaryRaw = form.salary.trim();
    const salary = salaryRaw === "" ? 0 : Number(salaryRaw);
    if (!Number.isFinite(salary) || salary < 0) {
      setError("Salary must be a valid non-negative number (or leave blank for 0).");
      return;
    }
    const join = new Date(`${form.joinDate}T12:00:00`);
    if (Number.isNaN(join.getTime())) {
      setError("Please choose a valid join date.");
      return;
    }
    const phone = form.phone.trim();
    if (phone.length > 50) {
      setError("Phone cannot exceed 50 characters.");
      return;
    }
    try {
      const emailTrim = form.email.trim();
      const res = await api("/api/Employee", {
        method: "POST",
        body: JSON.stringify({
          fullName: form.fullName.trim(),
          jobTitle: form.jobTitle.trim(),
          department: form.department.trim(),
          phone,
          email: emailTrim === "" ? null : emailTrim,
          address: form.address.trim() || null,
          salary,
          joinDate: join.toISOString(),
          status: STATUS_API_VALUES[form.status] ?? "Active",
        }),
      });
      if (!res.ok) {
        setError(await errorTextFromResponse(res));
        return;
      }
      setShowForm(false);
      setForm({
        fullName: "",
        jobTitle: "",
        department: "",
        phone: "",
        email: "",
        address: "",
        salary: "",
        joinDate: "",
        status: 0,
      });
      await load();
    } catch {
      setError("Could not create employee.");
    }
  };

  if (!isAdmin) {
    return (
      <div className="py-16 sm:py-24">
        <Container>
          <div className="mx-auto max-w-lg rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] px-6 py-10 text-center">
            <h1 className="text-xl font-semibold text-[var(--text-heading)]">Employees</h1>
            <p className="mt-3 text-sm text-[var(--text-muted)]">
              Employee management is available to administrators only. Sign in with an Admin account to view this module.
            </p>
          </div>
        </Container>
      </div>
    );
  }

  return (
    <div className="py-10 sm:py-14">
      <Container>
        <div className="mb-8 flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
          <div>
            <h1 className="text-2xl font-bold text-[var(--text-heading)]">Employees</h1>
            <p className="mt-1 text-sm text-[var(--text-muted)]">{employees.length} team member{employees.length !== 1 ? "s" : ""}</p>
          </div>
          <Button onClick={() => setShowForm((v) => !v)}>{showForm ? "Cancel" : "Add employee"}</Button>
        </div>

        {error && (
          <div className="mb-6 rounded-2xl border border-rose-500/20 bg-rose-500/[0.06] px-4 py-3 text-sm text-rose-300">
            {error}
          </div>
        )}

        {showForm && (
          <form
            onSubmit={submitCreate}
            className="mb-10 rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] p-6"
          >
            <h2 className="mb-4 text-lg font-semibold text-[var(--text-heading)]">New employee</h2>
            <div className="grid gap-4 sm:grid-cols-2">
              <Field label="Full name" value={form.fullName} onChange={(e) => setForm((f) => ({ ...f, fullName: e.target.value }))} required />
              <Field label="Job title" value={form.jobTitle} onChange={(e) => setForm((f) => ({ ...f, jobTitle: e.target.value }))} required />
              <Field label="Department" value={form.department} onChange={(e) => setForm((f) => ({ ...f, department: e.target.value }))} required />
              <Field label="Phone" value={form.phone} maxLength={50} onChange={(e) => setForm((f) => ({ ...f, phone: e.target.value }))} required />
              <Field label="Email" type="email" value={form.email} onChange={(e) => setForm((f) => ({ ...f, email: e.target.value }))} hint="Optional" />
              <Field label="Address" value={form.address} onChange={(e) => setForm((f) => ({ ...f, address: e.target.value }))} hint="Optional" />
              <Field label="Salary" value={form.salary} onChange={(e) => setForm((f) => ({ ...f, salary: e.target.value }))} placeholder="0" />
              <Field label="Join date" type="date" value={form.joinDate} onChange={(e) => setForm((f) => ({ ...f, joinDate: e.target.value }))} required />
              <label className="flex flex-col gap-1.5 text-sm font-medium text-[var(--text-secondary)] sm:col-span-2">
                Status
                <select
                  className="rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-4 py-3 text-sm text-[var(--text-primary)]"
                  value={form.status}
                  onChange={(e) => setForm((f) => ({ ...f, status: Number(e.target.value) }))}
                >
                  {Object.entries(statusLabels).map(([v, label]) => (
                    <option key={v} value={v}>
                      {label}
                    </option>
                  ))}
                </select>
              </label>
            </div>
            <div className="mt-6 flex justify-end gap-3">
              <Button type="button" variant="ghost" onClick={() => setShowForm(false)}>
                Close
              </Button>
              <Button type="submit">Save</Button>
            </div>
          </form>
        )}

        {loading ? (
          <p className="text-sm text-[var(--text-muted)]">Loading…</p>
        ) : employees.length === 0 ? (
          <p className="text-sm text-[var(--text-muted)]">No employees yet. Add one to get started.</p>
        ) : (
          <div className="overflow-x-auto rounded-2xl border border-[var(--border)]">
            <table className="w-full min-w-[640px] text-left text-sm">
              <thead className="border-b border-[var(--border)] bg-[var(--surface-glass)] text-xs uppercase tracking-wide text-[var(--text-muted)]">
                <tr>
                  <th className="px-4 py-3 font-medium">Name</th>
                  <th className="px-4 py-3 font-medium">Role</th>
                  <th className="px-4 py-3 font-medium">Department</th>
                  <th className="px-4 py-3 font-medium">Phone</th>
                  <th className="px-4 py-3 font-medium">Status</th>
                </tr>
              </thead>
              <tbody>
                {employees.map((emp) => {
                  const sn = statusNum(emp.status);
                  return (
                    <tr key={emp.id} className="border-b border-[var(--border)] last:border-0">
                      <td className="px-4 py-3 font-medium text-[var(--text-heading)]">{emp.fullName}</td>
                      <td className="px-4 py-3 text-[var(--text-secondary)]">{emp.jobTitle}</td>
                      <td className="px-4 py-3 text-[var(--text-secondary)]">{emp.department}</td>
                      <td className="px-4 py-3 text-[var(--text-secondary)]">{emp.phone}</td>
                      <td className="px-4 py-3 text-[var(--text-secondary)]">{statusLabels[sn] ?? String(emp.status)}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </Container>
    </div>
  );
}
