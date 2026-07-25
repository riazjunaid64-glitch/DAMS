import { useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { api } from "../api/api.ts";
import type { User } from "../App.tsx";
import { parseEmployeeRow, type EmployeeFromApi } from "../utils/parseEmployee.ts";
import Container from "../lib/Container.tsx";
import Button from "../lib/Button.tsx";
import Modal from "../lib/Modal.tsx";
import TabLayout from "../lib/TabLayout.tsx";
import EmployeeOverviewTab from "../components/employee/EmployeeOverviewTab.tsx";
import EmployeeTasksTab from "../components/employee/EmployeeTasksTab.tsx";
import EmployeeProjectsTab from "../components/employee/EmployeeProjectsTab.tsx";
import EmployeeAttendanceHistoryTab from "../components/employee/EmployeeAttendanceHistoryTab.tsx";
import EmployeeSalaryHistoryTab from "../components/employee/EmployeeSalaryHistoryTab.tsx";

const STATUS_CONFIG: Record<number, { label: string; color: string }> = {
  0: { label: "Active",     color: "text-emerald-400 bg-emerald-500/10 border-emerald-500/20" },
  1: { label: "Inactive",   color: "text-[var(--text-muted)] bg-[var(--surface-glass)] border-[var(--border)]" },
  2: { label: "On Leave",   color: "text-amber-400 bg-amber-500/10 border-amber-500/20" },
  3: { label: "Terminated", color: "text-rose-400 bg-rose-500/10 border-rose-500/20" },
};

const AVATAR_COLORS = [
  "from-indigo-500 to-violet-600", "from-blue-500 to-cyan-600",
  "from-emerald-500 to-teal-600",  "from-rose-500 to-pink-600",
  "from-amber-500 to-orange-600",  "from-purple-500 to-indigo-600",
];

function statusNum(s: number | string): number {
  if (typeof s === "number" && Number.isFinite(s)) return s;
  const map: Record<string, number> = { Active: 0, Inactive: 1, OnLeave: 2, Terminated: 3 };
  return typeof s === "string" && map[s] !== undefined ? map[s] : 0;
}

type Props = { user: User | null };

const TABS = [
  {
    id: "overview", label: "Overview",
    icon: <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M20 21v-2a4 4 0 00-4-4H8a4 4 0 00-4 4v2"/><circle cx="12" cy="7" r="4"/></svg>,
  },
  {
    id: "tasks", label: "Tasks",
    icon: <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><rect x="9" y="11" width="6" height="6"/><path d="M13 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V9z"/><polyline points="13 2 13 9 20 9"/></svg>,
  },
  {
    id: "projects", label: "Projects",
    icon: <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M3 9l9-7 9 7v11a2 2 0 01-2 2H5a2 2 0 01-2-2z"/><polyline points="9 22 9 12 15 12 15 22"/></svg>,
  },
  {
    id: "attendance", label: "Attendance History",
    icon: <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><rect x="3" y="4" width="18" height="18" rx="2"/><line x1="16" y1="2" x2="16" y2="6"/><line x1="8" y1="2" x2="8" y2="6"/><line x1="3" y1="10" x2="21" y2="10"/><path d="M9 16l2 2 4-4"/></svg>,
  },
  {
    id: "salary", label: "Salary History",
    icon: <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><circle cx="12" cy="12" r="10"/><path d="M16 8h-6a2 2 0 100 4h4a2 2 0 110 4H8"/><path d="M12 18V6"/></svg>,
  },
];

export default function EmployeeDetailPage({ user }: Props) {
  const { id } = useParams<{ id?: string }>();
  const navigate = useNavigate();
  const employeeId = Number(id);

  const [employee, setEmployee] = useState<EmployeeFromApi | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState("overview");
  const [showDeleteConfirm, setShowDeleteConfirm] = useState(false);
  const [deleting, setDeleting] = useState(false);

  const isAdmin = user?.role === "Admin";

  useEffect(() => {
    if (!isAdmin) { navigate("/employees"); return; }
    if (!employeeId || Number.isNaN(employeeId)) { setError("Invalid employee ID."); setLoading(false); return; }

    const load = async () => {
      setLoading(true);
      setError(null);
      try {
        const res = await api(`/api/Employee/${employeeId}`);
        if (!res.ok) { setError(res.status === 404 ? "Employee not found." : "Failed to load employee."); return; }
        const raw = await res.json() as unknown;
        const parsed = parseEmployeeRow(raw);
        if (!parsed) { setError("Invalid employee data."); return; }
        setEmployee(parsed);
      } catch { setError("Unable to load employee details."); }
      finally { setLoading(false); }
    };
    load();
  }, [employeeId, isAdmin, navigate]);

  const handleDelete = async () => {
    setDeleting(true);
    try {
      const res = await api(`/api/Employee/${employeeId}`, { method: "DELETE" });
      if (res.ok) { navigate("/employees"); return; }
      const d = await res.json().catch(() => ({}));
      alert((d as { message?: string }).message ?? "Failed to delete employee.");
    } catch { alert("Something went wrong."); }
    finally { setDeleting(false); setShowDeleteConfirm(false); }
  };

  if (!isAdmin) return null;

  const sn = employee ? statusNum(employee.status) : 0;
  const statusCfg = STATUS_CONFIG[sn] ?? STATUS_CONFIG[0];
  const gradient = employee ? AVATAR_COLORS[employee.id % AVATAR_COLORS.length] : AVATAR_COLORS[0];
  const initials = employee ? employee.fullName.split(" ").slice(0, 2).map(w => w[0]?.toUpperCase() ?? "").join("") : "?";

  return (
    <>
      {/* Workspace Header */}
      <Container className="border-b border-[var(--border)] pt-8 pb-6 sm:pt-10">
          {/* Breadcrumb */}
          <div className="mb-4 flex items-center gap-2 text-sm text-[var(--text-muted)]">
            <button onClick={() => navigate("/employees")} className="hover:text-[var(--text-primary)] transition-colors">Employees</button>
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><polyline points="9 18 15 12 9 6"/></svg>
            <span className="text-[var(--text-secondary)]">{employee?.fullName ?? "Loading…"}</span>
          </div>

          <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
            <div className="flex items-center gap-4">
              {/* Avatar */}
              {!loading && employee && (
                <div className={`flex h-14 w-14 shrink-0 items-center justify-center rounded-2xl bg-gradient-to-br ${gradient} text-xl font-bold text-white shadow-md`}>
                  {initials}
                </div>
              )}
              {loading && <div className="skeleton h-14 w-14 rounded-2xl" />}

              <div>
                {loading ? (
                  <><div className="skeleton h-7 w-48 mb-2" /><div className="skeleton h-4 w-32" /></>
                ) : employee ? (
                  <>
                    <div className="flex flex-wrap items-center gap-2">
                      <h1 className="text-2xl font-bold text-[var(--text-heading)] sm:text-3xl">{employee.fullName}</h1>
                      <span className={`rounded-full border px-3 py-0.5 text-xs font-semibold flex items-center gap-1.5 ${statusCfg.color}`}>
                        <span className={`h-1.5 w-1.5 rounded-full ${sn === 0 ? "bg-emerald-400 animate-pulse" : "bg-current"}`} />
                        {statusCfg.label}
                      </span>
                    </div>
                    <p className="mt-0.5 text-sm text-[var(--text-muted)]">
                      {employee.jobTitle}
                      {employee.department && <> · <span className="text-[var(--text-secondary)]">{employee.department}</span></>}
                    </p>
                  </>
                ) : null}
              </div>
            </div>

            <div className="flex items-center gap-2">
              <Button variant="outline" size="sm" onClick={() => navigate(-1)}>
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><polyline points="15 18 9 12 15 6"/></svg>
                Back
              </Button>
              {employee && (
                <Button variant="ghost" size="sm" onClick={() => setShowDeleteConfirm(true)} className="text-rose-400 hover:text-rose-300 hover:bg-rose-500/10">
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><polyline points="3 6 5 6 21 6"/><path d="M19 6l-1 14a2 2 0 01-2 2H8a2 2 0 01-2-2L5 6"/><path d="M10 11v6M14 11v6"/><path d="M9 6V4h6v2"/></svg>
                  Delete
                </Button>
              )}
            </div>
          </div>
      </Container>

      {/* Error */}
      {error && (
        <Container className="py-10">
          <div className="rounded-2xl border border-rose-500/20 bg-rose-500/[0.06] px-5 py-4 text-sm text-rose-300 flex items-center gap-3">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><circle cx="12" cy="12" r="10"/><line x1="15" y1="9" x2="9" y2="15"/><line x1="9" y1="9" x2="15" y2="15"/></svg>
            {error}
          </div>
        </Container>
      )}

      {/* Loading skeleton */}
      {loading && !error && (
        <Container className="py-10">
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {[...Array(6)].map((_, i) => (
              <div key={i} className="rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] p-5">
                <div className="skeleton mb-3 h-4 w-1/2" /><div className="skeleton h-3 w-3/4" />
              </div>
            ))}
          </div>
        </Container>
      )}

      {/* Tab Workspace */}
      {!loading && !error && employee && (
        <TabLayout tabs={TABS} activeTab={activeTab} onTabChange={setActiveTab}>
          {activeTab === "overview" && (
            <EmployeeOverviewTab employee={employee} onEmployeeUpdate={setEmployee} />
          )}
          {activeTab === "tasks" && (
            <EmployeeTasksTab employeeId={employeeId} />
          )}
          {activeTab === "projects" && (
            <EmployeeProjectsTab employeeId={employeeId} />
          )}
          {activeTab === "attendance" && (
            <EmployeeAttendanceHistoryTab employeeId={employeeId} />
          )}
          {activeTab === "salary" && (
            <EmployeeSalaryHistoryTab employee={employee} />
          )}
        </TabLayout>
      )}

      {/* Delete confirmation modal */}
      <Modal open={showDeleteConfirm && employee != null} onClose={() => setShowDeleteConfirm(false)}>
        {employee && (
          <div className="relative z-10 w-[420px] max-w-[92vw] animate-scale-in rounded-2xl border border-[var(--border)] bg-[var(--modal-bg)] shadow-2xl">
            <div className="p-6 text-center">
              <div className="mx-auto mb-4 flex h-12 w-12 items-center justify-center rounded-xl bg-rose-500/10 border border-rose-500/20">
                <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" className="text-rose-400">
                  <polyline points="3 6 5 6 21 6"/><path d="M19 6l-1 14a2 2 0 01-2 2H8a2 2 0 01-2-2L5 6"/><path d="M10 11v6M14 11v6"/><path d="M9 6V4h6v2"/>
                </svg>
              </div>
              <h3 className="text-base font-semibold text-[var(--text-heading)] mb-1">Delete Employee</h3>
              <p className="text-sm text-[var(--text-muted)] mb-5">
                Are you sure you want to permanently delete <span className="font-medium text-[var(--text-secondary)]">{employee.fullName}</span>? This action cannot be undone.
              </p>
              <div className="flex gap-3">
                <Button variant="ghost" className="flex-1" onClick={() => setShowDeleteConfirm(false)} disabled={deleting}>Cancel</Button>
                <Button variant="danger" className="flex-1" onClick={handleDelete} disabled={deleting}>
                  {deleting ? "Deleting…" : "Delete Employee"}
                </Button>
              </div>
            </div>
          </div>
        )}
      </Modal>
    </>
  );
}
