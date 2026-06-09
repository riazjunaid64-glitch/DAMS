import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { api } from "../../api/api.ts";
import Button from "../../lib/Button.tsx";
import Field from "../../lib/Field.tsx";
import Modal from "../../lib/Modal.tsx";

interface Task {
  id: number;
  employeeId: number;
  employeeName: string;
  projectId: number | null;
  projectName: string | null;
  title: string;
  description: string | null;
  priority: string;
  status: string;
  dueDate: string | null;
  completedAt: string | null;
  createdAt: string;
}

interface Project {
  id: number;
  projectName: string;
}

const PRIORITY_CONFIG: Record<string, { label: string; color: string; dot: string }> = {
  Low:    { label: "Low",    color: "text-sky-400 bg-sky-500/10 border-sky-500/20",         dot: "bg-sky-400" },
  Medium: { label: "Medium", color: "text-amber-400 bg-amber-500/10 border-amber-500/20",   dot: "bg-amber-400" },
  High:   { label: "High",   color: "text-orange-400 bg-orange-500/10 border-orange-500/20",dot: "bg-orange-400" },
  Urgent: { label: "Urgent", color: "text-rose-400 bg-rose-500/10 border-rose-500/20",      dot: "bg-rose-400" },
};

const STATUS_CONFIG: Record<string, { label: string; color: string }> = {
  Pending:    { label: "Pending",     color: "text-[var(--text-muted)] bg-[var(--surface-glass)] border-[var(--border)]" },
  InProgress: { label: "In Progress", color: "text-blue-400 bg-blue-500/10 border-blue-500/20" },
  Completed:  { label: "Completed",   color: "text-emerald-400 bg-emerald-500/10 border-emerald-500/20" },
  Cancelled:  { label: "Cancelled",   color: "text-rose-400 bg-rose-500/10 border-rose-500/20" },
};

const STATUS_TRANSITIONS: Record<string, string[]> = {
  Pending:    ["InProgress", "Cancelled"],
  InProgress: ["Completed", "Cancelled"],
  Completed:  [],
  Cancelled:  [],
};

function normalizeTask(raw: unknown): Task | null {
  if (!raw || typeof raw !== "object") return null;
  const o = raw as Record<string, unknown>;
  return {
    id:           Number(o.id ?? 0),
    employeeId:   Number(o.employeeId ?? 0),
    employeeName: String(o.employeeName ?? ""),
    projectId:    o.projectId != null ? Number(o.projectId) : null,
    projectName:  o.projectName ? String(o.projectName) : null,
    title:        String(o.title ?? ""),
    description:  o.description ? String(o.description) : null,
    priority:     String(o.priority ?? "Medium"),
    status:       String(o.status ?? "Pending"),
    dueDate:      o.dueDate ? String(o.dueDate) : null,
    completedAt:  o.completedAt ? String(o.completedAt) : null,
    createdAt:    String(o.createdAt ?? ""),
  };
}

function isOverdue(task: Task): boolean {
  if (!task.dueDate || task.status === "Completed" || task.status === "Cancelled") return false;
  return new Date(task.dueDate) < new Date();
}

interface Props {
  employeeId: number;
}

export default function EmployeeTasksTab({ employeeId }: Props) {
  const navigate = useNavigate();
  const [tasks, setTasks] = useState<Task[]>([]);
  const [projects, setProjects] = useState<Project[]>([]);
  const [loading, setLoading] = useState(true);
  const [statusFilter, setStatusFilter] = useState<string>("all");
  const [priorityFilter, setPriorityFilter] = useState<string>("all");
  const [showAssignModal, setShowAssignModal] = useState(false);
  const [assigning, setAssigning] = useState(false);
  const [assignError, setAssignError] = useState<string | null>(null);
  const [updatingTaskId, setUpdatingTaskId] = useState<number | null>(null);

  const [assignForm, setAssignForm] = useState({
    title: "", description: "", priority: "Medium",
    projectId: "", dueDate: "",
  });

  const loadTasks = async () => {
    setLoading(true);
    try {
      const res = await api(`/api/Employee/${employeeId}/tasks`);
      if (res.ok) {
        const raw = await res.json() as unknown[];
        setTasks(raw.map(normalizeTask).filter(Boolean) as Task[]);
      }
    } catch { /* ignore */ }
    finally { setLoading(false); }
  };

  const loadProjects = async () => {
    try {
      const res = await api("/api/Project", undefined, false);
      if (res.ok) {
        const raw = await res.json() as Array<{ id: number; projectName: string }>;
        setProjects(raw.map(p => ({ id: p.id, projectName: p.projectName })));
      }
    } catch { /* ignore */ }
  };

  useEffect(() => {
    loadTasks();
    loadProjects();
  }, [employeeId]);

  const handleAssign = async () => {
    setAssignError(null);
    if (!assignForm.title.trim()) { setAssignError("Title is required."); return; }
    setAssigning(true);
    try {
      const res = await api("/api/Employee/tasks", {
        method: "POST",
        body: JSON.stringify({
          employeeId,
          title: assignForm.title.trim(),
          description: assignForm.description.trim() || null,
          priority: assignForm.priority,
          projectId: assignForm.projectId ? Number(assignForm.projectId) : null,
          dueDate: assignForm.dueDate ? new Date(`${assignForm.dueDate}T12:00:00`).toISOString() : null,
        }),
      });
      if (!res.ok) {
        const d = await res.json().catch(() => ({}));
        setAssignError((d as { message?: string }).message ?? "Failed to assign task.");
        return;
      }
      setShowAssignModal(false);
      setAssignForm({ title: "", description: "", priority: "Medium", projectId: "", dueDate: "" });
      await loadTasks();
    } catch { setAssignError("Something went wrong."); }
    finally { setAssigning(false); }
  };

  const updateStatus = async (taskId: number, newStatus: string) => {
    setUpdatingTaskId(taskId);
    try {
      const res = await api(`/api/Employee/tasks/${taskId}/status`, {
        method: "PUT",
        body: JSON.stringify({ status: newStatus }),
      });
      if (res.ok) {
        setTasks(prev => prev.map(t =>
          t.id === taskId ? { ...t, status: newStatus, completedAt: newStatus === "Completed" ? new Date().toISOString() : t.completedAt } : t
        ));
      }
    } catch { /* ignore */ }
    finally { setUpdatingTaskId(null); }
  };

  const filtered = tasks.filter(t => {
    if (statusFilter !== "all" && t.status !== statusFilter) return false;
    if (priorityFilter !== "all" && t.priority !== priorityFilter) return false;
    return true;
  });

  const counts = {
    total: tasks.length,
    pending: tasks.filter(t => t.status === "Pending").length,
    inProgress: tasks.filter(t => t.status === "InProgress").length,
    completed: tasks.filter(t => t.status === "Completed").length,
    overdue: tasks.filter(isOverdue).length,
  };

  return (
    <div className="py-8 sm:py-10">
      <div className="mx-auto w-full max-w-7xl px-5 sm:px-8 lg:px-10">

        {/* Header */}
        <div className="mb-6 flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
          <div>
            <h2 className="section-title mb-1">Task Management</h2>
            <p className="text-sm text-[var(--text-muted)]">Assign and track tasks for this employee</p>
          </div>
          <Button size="sm" onClick={() => setShowAssignModal(true)}>
            <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
            Assign Task
          </Button>
        </div>

        {/* Stats Row */}
        <div className="mb-6 grid grid-cols-2 gap-3 sm:grid-cols-4">
          {[
            { label: "Pending",     value: counts.pending,    color: "text-[var(--text-muted)]" },
            { label: "In Progress", value: counts.inProgress, color: "text-blue-400" },
            { label: "Completed",   value: counts.completed,  color: "text-emerald-400" },
            { label: "Overdue",     value: counts.overdue,    color: counts.overdue > 0 ? "text-rose-400" : "text-[var(--text-muted)]" },
          ].map(s => (
            <div key={s.label} className="rounded-xl border border-[var(--border)] bg-[var(--surface-glass)] p-4">
              <p className={`text-2xl font-bold ${s.color}`}>{s.value}</p>
              <p className="text-xs text-[var(--text-muted)] mt-0.5">{s.label}</p>
            </div>
          ))}
        </div>

        {/* Filters */}
        <div className="mb-5 flex flex-wrap gap-2">
          {["all", "Pending", "InProgress", "Completed", "Cancelled"].map(s => (
            <button
              key={s}
              onClick={() => setStatusFilter(s)}
              className={`rounded-lg px-3 py-1.5 text-xs font-medium transition-all ${
                statusFilter === s
                  ? "bg-[var(--accent-glow)] text-[var(--accent)] border border-[var(--accent)]/20"
                  : "text-[var(--text-muted)] hover:text-[var(--text-primary)] hover:bg-[var(--surface-glass-hover)]"
              }`}
            >
              {s === "all" ? "All Tasks" : STATUS_CONFIG[s]?.label ?? s}
            </button>
          ))}
          <div className="ml-auto">
            <select
              value={priorityFilter}
              onChange={e => setPriorityFilter(e.target.value)}
              className="rounded-lg border border-[var(--border)] bg-[var(--input-bg)] px-3 py-1.5 text-xs text-[var(--text-primary)] focus:border-[var(--accent)] focus:outline-none"
            >
              <option value="all">All Priorities</option>
              {["Urgent", "High", "Medium", "Low"].map(p => <option key={p} value={p}>{p}</option>)}
            </select>
          </div>
        </div>

        {/* Loading */}
        {loading && (
          <div className="space-y-3">
            {[...Array(3)].map((_, i) => (
              <div key={i} className="rounded-xl border border-[var(--border)] bg-[var(--surface-glass)] p-4">
                <div className="skeleton h-4 w-1/2 mb-2" />
                <div className="skeleton h-3 w-3/4" />
              </div>
            ))}
          </div>
        )}

        {/* Empty */}
        {!loading && filtered.length === 0 && (
          <div className="py-16 text-center">
            <div className="mx-auto mb-4 flex h-14 w-14 items-center justify-center rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)]">
              <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" className="text-[var(--text-muted)]" strokeLinecap="round"><rect x="9" y="11" width="6" height="6"/><path d="M13 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V9z"/><polyline points="13 2 13 9 20 9"/></svg>
            </div>
            <p className="text-sm font-medium text-[var(--text-secondary)]">
              {tasks.length === 0 ? "No tasks assigned yet" : "No tasks match the current filter"}
            </p>
            {tasks.length === 0 && (
              <Button size="sm" className="mt-4" onClick={() => setShowAssignModal(true)}>Assign First Task</Button>
            )}
          </div>
        )}

        {/* Task List */}
        {!loading && filtered.length > 0 && (
          <div className="space-y-3">
            {filtered.map(task => {
              const prio = PRIORITY_CONFIG[task.priority] ?? PRIORITY_CONFIG.Medium;
              const stat = STATUS_CONFIG[task.status] ?? STATUS_CONFIG.Pending;
              const overdue = isOverdue(task);
              const transitions = STATUS_TRANSITIONS[task.status] ?? [];

              return (
                <div
                  key={task.id}
                  className={`rounded-2xl border bg-[var(--surface-glass)] p-5 transition-all ${
                    overdue ? "border-rose-500/30" : "border-[var(--border)] hover:border-[var(--border-hover)]"
                  }`}
                >
                  <div className="flex items-start gap-4">
                    <div className="flex-1 min-w-0">
                      <div className="flex flex-wrap items-center gap-2 mb-1">
                        <span className="font-medium text-[var(--text-heading)]">{task.title}</span>
                        {overdue && (
                          <span className="flex items-center gap-1 rounded-full bg-rose-500/10 border border-rose-500/20 px-2 py-0.5 text-[10px] font-semibold text-rose-400">
                            <svg width="9" height="9" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>
                            Overdue
                          </span>
                        )}
                      </div>
                      {task.description && (
                        <p className="text-xs text-[var(--text-muted)] mb-2 line-clamp-2">{task.description}</p>
                      )}
                      <div className="flex flex-wrap items-center gap-3 text-xs text-[var(--text-muted)]">
                        {task.projectName && (
                          <button
                            onClick={() => navigate(`/projects/${task.projectId}`)}
                            className="flex items-center gap-1 hover:text-[var(--accent)] transition-colors"
                          >
                            <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M3 9l9-7 9 7v11a2 2 0 01-2 2H5a2 2 0 01-2-2z"/></svg>
                            {task.projectName}
                          </button>
                        )}
                        {task.dueDate && (
                          <span className={`flex items-center gap-1 ${overdue ? "text-rose-400" : ""}`}>
                            <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><rect x="3" y="4" width="18" height="18" rx="2"/><line x1="16" y1="2" x2="16" y2="6"/><line x1="8" y1="2" x2="8" y2="6"/><line x1="3" y1="10" x2="21" y2="10"/></svg>
                            Due {new Date(task.dueDate).toLocaleDateString("en-US", { month: "short", day: "numeric", year: "numeric" })}
                          </span>
                        )}
                        {task.completedAt && (
                          <span className="flex items-center gap-1 text-emerald-400">
                            <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M22 11.08V12a10 10 0 11-5.93-9.14"/><polyline points="22 4 12 14.01 9 11.01"/></svg>
                            Completed {new Date(task.completedAt).toLocaleDateString("en-US", { month: "short", day: "numeric" })}
                          </span>
                        )}
                      </div>
                    </div>

                    {/* Right side badges + actions */}
                    <div className="flex flex-col items-end gap-2 shrink-0">
                      <div className="flex items-center gap-2">
                        <span className={`rounded-full border px-2.5 py-0.5 text-[10px] font-semibold flex items-center gap-1 ${prio.color}`}>
                          <span className={`h-1.5 w-1.5 rounded-full ${prio.dot}`} />{prio.label}
                        </span>
                        <span className={`rounded-full border px-2.5 py-0.5 text-[10px] font-semibold ${stat.color}`}>
                          {stat.label}
                        </span>
                      </div>
                      {transitions.length > 0 && (
                        <div className="flex gap-1.5">
                          {transitions.map(next => (
                            <button
                              key={next}
                              onClick={() => updateStatus(task.id, next)}
                              disabled={updatingTaskId === task.id}
                              className={`rounded-lg px-2.5 py-1 text-[10px] font-semibold transition-all disabled:opacity-40 ${
                                next === "Completed"
                                  ? "bg-emerald-500/10 text-emerald-400 border border-emerald-500/20 hover:bg-emerald-500/20"
                                  : next === "InProgress"
                                    ? "bg-blue-500/10 text-blue-400 border border-blue-500/20 hover:bg-blue-500/20"
                                    : "bg-rose-500/10 text-rose-400 border border-rose-500/20 hover:bg-rose-500/20"
                              }`}
                            >
                              {updatingTaskId === task.id ? "…" : `→ ${STATUS_CONFIG[next]?.label ?? next}`}
                            </button>
                          ))}
                        </div>
                      )}
                    </div>
                  </div>
                </div>
              );
            })}
          </div>
        )}
      </div>

      <Modal open={showAssignModal} onClose={() => setShowAssignModal(false)}>
          <div className="relative z-10 w-[520px] max-w-[92vw] animate-scale-in rounded-2xl border border-[var(--border)] bg-[var(--modal-bg)] shadow-2xl">
            <div className="absolute right-0 top-0 h-28 w-28 rounded-full bg-indigo-500/[0.08] blur-[50px]" />

            <div className="border-b border-[var(--border)] px-6 py-5">
              <div className="flex items-center justify-between">
                <div>
                  <div className="mb-1 inline-flex items-center gap-2 rounded-full bg-indigo-500/[0.08] px-2.5 py-0.5">
                    <span className="h-1.5 w-1.5 rounded-full bg-indigo-500" />
                    <span className="text-[10px] font-semibold uppercase tracking-wider text-indigo-500">New Task</span>
                  </div>
                  <h3 className="text-base font-semibold text-[var(--text-heading)]">Assign Task</h3>
                </div>
                <button onClick={() => setShowAssignModal(false)} className="flex h-8 w-8 items-center justify-center rounded-lg text-[var(--text-muted)] hover:bg-[var(--surface-glass-hover)] hover:text-[var(--text-primary)]">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
                </button>
              </div>
            </div>

            <div className="space-y-4 p-6">
              <Field label="Task Title" value={assignForm.title} onChange={e => setAssignForm(f => ({ ...f, title: e.target.value }))} placeholder="Describe the task…" required />
              <Field as="textarea" label="Description" hint="Optional" value={assignForm.description} onChange={e => setAssignForm(f => ({ ...f, description: e.target.value }))} placeholder="Add more details…" />
              <div className="grid gap-4 sm:grid-cols-2">
                <div>
                  <label className="text-xs font-medium text-[var(--text-muted)] mb-2 block">Priority</label>
                  <select
                    value={assignForm.priority}
                    onChange={e => setAssignForm(f => ({ ...f, priority: e.target.value }))}
                    className="w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-4 py-2.5 text-sm text-[var(--text-primary)] focus:border-[var(--accent)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-glow)]"
                  >
                    {["Low", "Medium", "High", "Urgent"].map(p => <option key={p} value={p}>{p}</option>)}
                  </select>
                </div>
                <div>
                  <label className="text-xs font-medium text-[var(--text-muted)] mb-2 block">Project <span className="font-normal opacity-60">(optional)</span></label>
                  <select
                    value={assignForm.projectId}
                    onChange={e => setAssignForm(f => ({ ...f, projectId: e.target.value }))}
                    className="w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-4 py-2.5 text-sm text-[var(--text-primary)] focus:border-[var(--accent)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-glow)]"
                  >
                    <option value="">No project</option>
                    {projects.map(p => <option key={p.id} value={p.id}>{p.projectName}</option>)}
                  </select>
                </div>
              </div>
              <Field label="Due Date" type="date" value={assignForm.dueDate} onChange={e => setAssignForm(f => ({ ...f, dueDate: e.target.value }))} hint="Optional" />
              {assignError && (
                <div className="rounded-xl border border-rose-500/20 bg-rose-500/[0.06] px-4 py-3 text-sm text-rose-300 flex items-center gap-2">
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>
                  {assignError}
                </div>
              )}
            </div>

            <div className="flex items-center justify-between border-t border-[var(--border)] px-6 py-4 bg-[var(--surface-glass)]">
              <Button variant="ghost" onClick={() => setShowAssignModal(false)} disabled={assigning}>Cancel</Button>
              <Button onClick={handleAssign} disabled={assigning}>
                {assigning ? (
                  <><svg className="animate-spin" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M21 12a9 9 0 11-6.219-8.56"/></svg>Assigning…</>
                ) : (
                  <><svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>Assign Task</>
                )}
              </Button>
            </div>
          </div>
      </Modal>
    </div>
  );
}
