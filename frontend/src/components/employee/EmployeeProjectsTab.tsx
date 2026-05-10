import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { api } from "../../api/api.ts";

interface Task {
  id: number;
  projectId: number | null;
  projectName: string | null;
  title: string;
  status: string;
  priority: string;
}

interface ProjectSummary {
  projectId: number;
  projectName: string;
  tasks: Task[];
  completedCount: number;
  totalCount: number;
  latestStatus: string;
}

const STATUS_COLOR: Record<string, string> = {
  Pending:    "text-[var(--text-muted)] bg-[var(--surface-glass)] border-[var(--border)]",
  InProgress: "text-blue-400 bg-blue-500/10 border-blue-500/20",
  Completed:  "text-emerald-400 bg-emerald-500/10 border-emerald-500/20",
  Cancelled:  "text-rose-400 bg-rose-500/10 border-rose-500/20",
};

interface Props {
  employeeId: number;
}

export default function EmployeeProjectsTab({ employeeId }: Props) {
  const navigate = useNavigate();
  const [projects, setProjects] = useState<ProjectSummary[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const load = async () => {
      setLoading(true);
      try {
        const res = await api(`/api/Employee/${employeeId}/tasks`);
        if (!res.ok) return;
        const raw = await res.json() as Array<Record<string, unknown>>;

        // Derive project involvement from tasks
        const map = new Map<number, ProjectSummary>();
        for (const t of raw) {
          const pid = t.projectId != null ? Number(t.projectId) : null;
          const pname = t.projectName ? String(t.projectName) : null;
          if (!pid || !pname) continue;

          if (!map.has(pid)) {
            map.set(pid, { projectId: pid, projectName: pname, tasks: [], completedCount: 0, totalCount: 0, latestStatus: "Pending" });
          }
          const entry = map.get(pid)!;
          const task: Task = {
            id:          Number(t.id),
            projectId:   pid,
            projectName: pname,
            title:       String(t.title ?? ""),
            status:      String(t.status ?? "Pending"),
            priority:    String(t.priority ?? "Medium"),
          };
          entry.tasks.push(task);
          entry.totalCount++;
          if (task.status === "Completed") entry.completedCount++;
        }

        // Determine overall status per project
        for (const entry of map.values()) {
          const statuses = entry.tasks.map(t => t.status);
          if (statuses.some(s => s === "InProgress")) entry.latestStatus = "InProgress";
          else if (statuses.every(s => s === "Completed")) entry.latestStatus = "Completed";
          else if (statuses.every(s => s === "Cancelled")) entry.latestStatus = "Cancelled";
          else entry.latestStatus = "Pending";
        }

        setProjects([...map.values()].sort((a, b) => {
          const order: Record<string, number> = { InProgress: 0, Pending: 1, Completed: 2, Cancelled: 3 };
          return (order[a.latestStatus] ?? 9) - (order[b.latestStatus] ?? 9);
        }));
      } catch { /* ignore */ }
      finally { setLoading(false); }
    };
    load();
  }, [employeeId]);

  return (
    <div className="py-8 sm:py-10">
      <div className="mx-auto w-full max-w-7xl px-5 sm:px-8 lg:px-10">
        <div className="mb-6">
          <h2 className="section-title mb-1">Project Assignments</h2>
          <p className="text-sm text-[var(--text-muted)]">Projects this employee is involved in via task assignments</p>
        </div>

        {loading && (
          <div className="grid gap-4 sm:grid-cols-2">
            {[...Array(4)].map((_, i) => (
              <div key={i} className="rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] p-5">
                <div className="skeleton h-5 w-1/2 mb-3" />
                <div className="skeleton h-3 w-3/4 mb-2" />
                <div className="skeleton h-3 w-1/2" />
              </div>
            ))}
          </div>
        )}

        {!loading && projects.length === 0 && (
          <div className="py-16 text-center">
            <div className="mx-auto mb-4 flex h-14 w-14 items-center justify-center rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)]">
              <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" className="text-[var(--text-muted)]" strokeLinecap="round">
                <path d="M3 9l9-7 9 7v11a2 2 0 01-2 2H5a2 2 0 01-2-2z"/>
                <polyline points="9 22 9 12 15 12 15 22"/>
              </svg>
            </div>
            <p className="text-sm font-medium text-[var(--text-secondary)]">No project assignments yet</p>
            <p className="mt-1 text-xs text-[var(--text-muted)]">Assign tasks linked to a project to see involvement here</p>
          </div>
        )}

        {!loading && projects.length > 0 && (
          <div className="grid gap-4 sm:grid-cols-2">
            {projects.map(proj => {
              const progress = proj.totalCount > 0 ? Math.round((proj.completedCount / proj.totalCount) * 100) : 0;
              const statusColor = STATUS_COLOR[proj.latestStatus] ?? STATUS_COLOR.Pending;

              return (
                <div
                  key={proj.projectId}
                  className="rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] p-5 transition-all hover:border-[var(--border-hover)] hover:shadow-md"
                >
                  {/* Project header */}
                  <div className="mb-4 flex items-start justify-between gap-3">
                    <div className="flex items-center gap-3">
                      <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-indigo-500/10 border border-indigo-500/20">
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" className="text-indigo-400" strokeLinecap="round">
                          <path d="M3 9l9-7 9 7v11a2 2 0 01-2 2H5a2 2 0 01-2-2z"/>
                          <polyline points="9 22 9 12 15 12 15 22"/>
                        </svg>
                      </div>
                      <div>
                        <p className="font-semibold text-[var(--text-heading)]">{proj.projectName}</p>
                        <p className="text-xs text-[var(--text-muted)]">{proj.totalCount} task{proj.totalCount !== 1 ? "s" : ""} assigned</p>
                      </div>
                    </div>
                    <span className={`shrink-0 rounded-full border px-2.5 py-0.5 text-[10px] font-semibold ${statusColor}`}>
                      {proj.latestStatus === "InProgress" ? "Active" : proj.latestStatus}
                    </span>
                  </div>

                  {/* Progress bar */}
                  <div className="mb-4">
                    <div className="mb-1 flex items-center justify-between text-xs">
                      <span className="text-[var(--text-muted)]">Task completion</span>
                      <span className="font-medium text-[var(--text-secondary)]">{proj.completedCount}/{proj.totalCount}</span>
                    </div>
                    <div className="h-1.5 rounded-full bg-[var(--border)] overflow-hidden">
                      <div
                        className="h-full rounded-full bg-gradient-to-r from-indigo-500 to-violet-600 transition-all duration-500"
                        style={{ width: `${progress}%` }}
                      />
                    </div>
                  </div>

                  {/* Recent tasks */}
                  <div className="space-y-1.5 mb-4">
                    {proj.tasks.slice(0, 3).map(t => (
                      <div key={t.id} className="flex items-center gap-2 text-xs">
                        <div className={`h-1.5 w-1.5 rounded-full shrink-0 ${
                          t.status === "Completed" ? "bg-emerald-400" :
                          t.status === "InProgress" ? "bg-blue-400" :
                          t.status === "Cancelled" ? "bg-rose-400" : "bg-[var(--text-muted)]"
                        }`} />
                        <span className="truncate text-[var(--text-secondary)]">{t.title}</span>
                      </div>
                    ))}
                    {proj.tasks.length > 3 && (
                      <p className="text-[10px] text-[var(--text-muted)] pl-3.5">+{proj.tasks.length - 3} more tasks</p>
                    )}
                  </div>

                  {/* View project link */}
                  <button
                    onClick={() => navigate(`/projects/${proj.projectId}`)}
                    className="flex items-center gap-1.5 text-xs font-medium text-[var(--accent)] hover:underline transition-colors"
                  >
                    Open Project
                    <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round"><polyline points="9 18 15 12 9 6"/></svg>
                  </button>
                </div>
              );
            })}
          </div>
        )}
      </div>
    </div>
  );
}
