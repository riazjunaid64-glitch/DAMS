import { useEffect, useMemo, useState, type FormEvent } from "react";
import { Link } from "react-router-dom";
import { api } from "../api/api.ts";
import { parseProjectsPayload } from "../utils/parseProject.ts";
import type { User } from "../App.tsx";
import Button from "../lib/Button.tsx";
import Container from "../lib/Container.tsx";
import Field from "../lib/Field.tsx";
import Pagination from "../lib/Pagination.tsx";
import Section from "../lib/Section.tsx";

interface Project {
  id: number;
  projectName: string;
  location: string;
  description?: string | null;
  startingDate: string;
  expectedCompletionDate?: string | null;
  status: number | string;
  createdAt: string;
}

type Props = {
  user: User | null;
};

const statusLabels: Record<number, string> = {
  1: "Planning",
  2: "Ongoing",
  3: "Completed",
  4: "Cancelled",
  5: "Archived",
};

const statusStyles: Record<number, string> = {
  1: "status-planning",
  2: "status-ongoing",
  3: "status-completed",
  4: "status-cancelled",
  5: "status-archived",
};

export default function ProjectsPage({ user }: Props) {
  const PROJECTS_PER_PAGE = 6;
  const [projects, setProjects] = useState<Project[]>([]);
  const [currentPage, setCurrentPage] = useState(1);
  const [projectsLoading, setProjectsLoading] = useState(false);
  const [projectsError, setProjectsError] = useState<string | null>(null);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [showProjectModal, setShowProjectModal] = useState(false);
  const [projectForm, setProjectForm] = useState({
    projectName: "",
    location: "",
    description: "",
    startingDate: "",
    expectedCompletionDate: "",
    status: 1,
  });

  const isAdmin = user?.role === "Admin";

  const dateFormatter = useMemo(
    () => new Intl.DateTimeFormat("en-US", { month: "short", day: "numeric", year: "numeric" }),
    []
  );

  const formatDate = (value?: string | null) => {
    if (!value) return "N/A";
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return "N/A";
    return dateFormatter.format(date);
  };

  const toInputDate = (value?: string | null) => {
    if (!value) return "";
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return "";
    return date.toISOString().slice(0, 10);
  };

  const loadProjects = async () => {
    setProjectsLoading(true);
    setProjectsError(null);
    try {
      const res = await api("/api/Project", undefined, false);
      if (!res.ok) {
        setProjectsError(
          res.status === 0
            ? "Cannot reach the API. Is the backend running (e.g. http://localhost:5219), and is npm dev restarted after vite proxy changes?"
            : `Unable to load projects (HTTP ${res.status}).`
        );
        return;
      }
      const raw: unknown = await res.json();
      if (!Array.isArray(raw)) {
        setProjectsError("Unexpected API response (expected a JSON array of projects).");
        setProjects([]);
        return;
      }
      const data = parseProjectsPayload(raw);
      if (raw.length > 0 && data.length === 0) {
        setProjectsError(
          "The API returned data but no valid project rows were parsed. Open the Network tab and confirm the JSON uses id/projectName (or Id/ProjectName)."
        );
        setProjects([]);
        return;
      }
      setProjects(data);
    } catch {
      setProjectsError("Unable to load projects right now.");
    } finally {
      setProjectsLoading(false);
    }
  };

  useEffect(() => {
    loadProjects();
  }, [user]);

  useEffect(() => {
    const totalPages = Math.max(1, Math.ceil(projects.length / PROJECTS_PER_PAGE));
    if (currentPage > totalPages) {
      setCurrentPage(totalPages);
    }
  }, [currentPage, projects.length]);

  const resetProjectForm = () => {
    setProjectForm({
      projectName: "",
      location: "",
      description: "",
      startingDate: "",
      expectedCompletionDate: "",
      status: 1,
    });
    setEditingId(null);
    setShowProjectModal(false);
  };

  const startEditProject = (project: Project) => {
    setEditingId(project.id);
    setShowProjectModal(true);
    setProjectForm({
      projectName: project.projectName,
      location: project.location,
      description: project.description ?? "",
      startingDate: toInputDate(project.startingDate),
      expectedCompletionDate: toInputDate(project.expectedCompletionDate),
      status: typeof project.status === "number" ? project.status : 1,
    });
  };

  const submitProject = async (event: FormEvent) => {
    event.preventDefault();
    setProjectsError(null);

    if (!projectForm.projectName || !projectForm.location || !projectForm.startingDate) {
      setProjectsError("Project name, location, and starting date are required.");
      return;
    }

    const payload = {
      projectName: projectForm.projectName,
      location: projectForm.location,
      description: projectForm.description || null,
      startingDate: new Date(projectForm.startingDate).toISOString(),
      expectedCompletionDate: projectForm.expectedCompletionDate
        ? new Date(projectForm.expectedCompletionDate).toISOString()
        : null,
      ...(editingId ? { status: projectForm.status } : {}),
    };

    try {
      const res = await api(editingId ? `/api/Project/${editingId}` : "/api/Project", {
        method: editingId ? "PUT" : "POST",
        body: JSON.stringify(payload),
      });

      if (!res.ok) {
        const text = await res.text();
        setProjectsError(text || "Unable to save project.");
        return;
      }

      resetProjectForm();
      await loadProjects();
    } catch {
      setProjectsError("Unable to save project right now.");
    }
  };

  const getStatusNum = (s: number | string) => (typeof s === "number" ? s : 1);
  const totalPages = Math.max(1, Math.ceil(projects.length / PROJECTS_PER_PAGE));
  const paginatedProjects = useMemo(
    () =>
      projects.slice(
        (currentPage - 1) * PROJECTS_PER_PAGE,
        currentPage * PROJECTS_PER_PAGE
      ),
    [currentPage, projects]
  );

  return (
    <>
      {/* Page Header */}
      <div className="relative overflow-hidden border-b border-white/[0.04]">
        <div className="absolute inset-0 mesh-gradient-subtle" />
        <Container className="relative py-4 sm:py-4">
          <Section
           
          >
            <div className="flex flex-wrap items-center justify-center gap-4">
              <div className="flex items-center gap-2 rounded-full border border-[var(--border)] bg-[var(--surface-glass)] px-4 py-2">
                <span className="h-2 w-2 rounded-full bg-indigo-500" />
                <span className="text-sm text-[var(--text-secondary)]">
                  {projects.length} project{projects.length !== 1 ? "s" : ""}
                </span>
              </div>
              {isAdmin && (
                <Button
                  onClick={() => {
                    setEditingId(null);
                    setProjectForm({
                      projectName: "",
                      location: "",
                      description: "",
                      startingDate: "",
                      expectedCompletionDate: "",
                      status: 1,
                    });
                    setShowProjectModal(true);
                  }}
                >
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                    <line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>
                  </svg>
                  New Project
                </Button>
              )}
            </div>
          </Section>
        </Container>
      </div>

      {/* Project Grid */}
      <div className="py-12 sm:py-16">
        <Container>
          {/* Loading State */}
          {projectsLoading && (
            <div className="grid gap-5 md:grid-cols-2 lg:grid-cols-3">
              {[...Array(6)].map((_, i) => (
                <div key={i} className="rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] p-6">
                  <div className="skeleton mb-4 h-5 w-2/3" />
                  <div className="skeleton mb-3 h-4 w-1/3" />
                  <div className="skeleton mb-2 h-3 w-full" />
                  <div className="skeleton h-3 w-4/5" />
                </div>
              ))}
            </div>
          )}

          {/* Error State */}
          {projectsError && (
            <div className="rounded-2xl border border-rose-500/20 bg-rose-500/[0.06] px-5 py-4 text-sm text-rose-300 flex items-center gap-3">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                <circle cx="12" cy="12" r="10"/><line x1="15" y1="9" x2="9" y2="15"/><line x1="9" y1="9" x2="15" y2="15"/>
              </svg>
              {projectsError}
            </div>
          )}

          {/* Empty State */}
          {!projectsLoading && projects.length === 0 && !projectsError && (
            <div className="flex flex-col items-center justify-center py-20 text-center">
              <div className="mb-4 flex h-16 w-16 items-center justify-center rounded-2xl bg-[var(--surface-glass)] border border-[var(--border)]">
                <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" className="text-[var(--text-muted)]" strokeLinecap="round">
                  <rect x="3" y="3" width="7" height="9" rx="1"/><rect x="14" y="3" width="7" height="5" rx="1"/><rect x="14" y="12" width="7" height="9" rx="1"/><rect x="3" y="16" width="7" height="5" rx="1"/>
                </svg>
              </div>
              <h3 className="text-lg font-semibold text-[var(--text-heading)]">No projects yet</h3>
              <p className="mt-2 max-w-sm text-sm text-[var(--text-muted)]">
                Create your first project to get started with tracking and management.
              </p>
            </div>
          )}

          {/* Project Cards */}
          {!projectsLoading && projects.length > 0 && (
            <>
              <div className="grid gap-5 md:grid-cols-2 lg:grid-cols-3">
                {paginatedProjects.map((project) => {
                  const statusNum = getStatusNum(project.status);
                  const statusValue = statusLabels[statusNum] ?? "Unknown";
                  const statusClass = statusStyles[statusNum] ?? "status-archived";
                  return (
                    <div
                      key={project.id}
                      className="glass-card group relative overflow-hidden p-6 animate-fade-in-up"
                    >
                      {/* Hover glow */}
                      <div className="pointer-events-none absolute -right-12 -top-12 h-32 w-32 rounded-full bg-indigo-500/[0.06] opacity-0 blur-2xl transition-opacity group-hover:opacity-100" />

                      <div className="relative">
                        {/* Header */}
                        <div className="flex items-start justify-between gap-3">
                          <div className="min-w-0 flex-1">
                            <h3 className="truncate text-lg font-semibold text-[var(--text-heading)] group-hover:text-[var(--accent-light)] transition-colors">
                              {project.projectName}
                            </h3>
                            <div className="mt-1.5 flex items-center gap-1.5 text-xs text-[var(--text-muted)]">
                              <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                                <path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0118 0z"/><circle cx="12" cy="10" r="3"/>
                              </svg>
                              {project.location}
                            </div>
                          </div>
                          <span className={`shrink-0 rounded-full px-3 py-1 text-xs font-semibold ${statusClass}`}>
                            {statusValue}
                          </span>
                        </div>

                        {/* Description */}
                        <p className="mt-4 text-sm leading-relaxed text-[var(--text-secondary)] line-clamp-2">
                          {project.description || "No description provided."}
                        </p>

                        {/* Dates */}
                        <div className="mt-5 grid grid-cols-2 gap-3 rounded-xl bg-[var(--surface-glass)] border border-[var(--border)] p-3">
                          <div>
                            <p className="text-[10px] uppercase tracking-wider text-[var(--text-muted)]">Start</p>
                            <p className="mt-0.5 text-xs font-medium text-[var(--text-secondary)]">{formatDate(project.startingDate)}</p>
                          </div>
                          <div>
                            <p className="text-[10px] uppercase tracking-wider text-[var(--text-muted)]">Expected End</p>
                            <p className="mt-0.5 text-xs font-medium text-[var(--text-secondary)]">{formatDate(project.expectedCompletionDate)}</p>
                          </div>
                        </div>

                        {/* Actions */}
                        <div className="mt-5 flex items-center gap-2">
                          <Link to={`/projects/${project.id}`} className="flex-1">
                            <Button variant="outline" size="sm" className="w-full">
                              View Details
                              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                                <line x1="5" y1="12" x2="19" y2="12"/><polyline points="12 5 19 12 12 19"/>
                              </svg>
                            </Button>
                          </Link>
                          {isAdmin && (
                            <Button variant="ghost" size="sm" onClick={() => startEditProject(project)}>
                              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                                <path d="M11 4H4a2 2 0 00-2 2v14a2 2 0 002 2h14a2 2 0 002-2v-7"/><path d="M18.5 2.5a2.121 2.121 0 013 3L12 15l-4 1 1-4 9.5-9.5z"/>
                              </svg>
                            </Button>
                          )}
                        </div>
                      </div>
                    </div>
                  );
                })}
              </div>

              <Pagination currentPage={currentPage} totalPages={totalPages} onPageChange={setCurrentPage} />
            </>
          )}
        </Container>
      </div>

      {/* ─── Create / Edit Modal ─── */}
      {isAdmin && showProjectModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
          <div
            className="absolute inset-0 bg-black/60 backdrop-blur-sm animate-fade-in"
            onClick={resetProjectForm}
          />
          <div className="relative z-10 w-full max-w-2xl animate-scale-in overflow-hidden rounded-2xl border border-[var(--border)] bg-[var(--modal-bg)] shadow-2xl">
            {/* Modal Header */}
            <div className="flex items-center justify-between border-b border-[var(--border)] px-6 py-4">
              <div>
                <h3 className="text-lg font-semibold text-[var(--text-heading)]">
                  {editingId ? "Update Project" : "Create Project"}
                </h3>
                <p className="mt-0.5 text-xs text-[var(--text-muted)]">
                  {editingId ? "Edit the project details below" : "Fill in the details to create a new project"}
                </p>
              </div>
              <button
                onClick={resetProjectForm}
                className="flex h-8 w-8 items-center justify-center rounded-lg text-[var(--text-muted)] transition hover:bg-[var(--surface-glass-hover)] hover:text-[var(--text-primary)]"
              >
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                  <line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>
                </svg>
              </button>
            </div>

            {/* Modal Body */}
            <form onSubmit={submitProject} className="p-6">
              <div className="grid gap-4">
                <Field
                  label="Project Name"
                  value={projectForm.projectName}
                  onChange={(e) =>
                    setProjectForm((prev) => ({ ...prev, projectName: e.target.value }))
                  }
                  placeholder="e.g. Phase 2 Commercial Tower"
                />
                <Field
                  label="Location"
                  value={projectForm.location}
                  onChange={(e) => setProjectForm((prev) => ({ ...prev, location: e.target.value }))}
                  placeholder="City, Country"
                />
                <Field
                  label="Description"
                  as="textarea"
                  value={projectForm.description}
                  onChange={(e) =>
                    setProjectForm((prev) => ({ ...prev, description: e.target.value }))
                  }
                  placeholder="Brief description of the project..."
                  hint="Optional"
                />
                <div className="grid gap-4 sm:grid-cols-2">
                  <Field
                    label="Starting Date"
                    type="date"
                    value={projectForm.startingDate}
                    onChange={(e) =>
                      setProjectForm((prev) => ({
                        ...prev,
                        startingDate: e.target.value,
                      }))
                    }
                  />
                  <Field
                    label="Expected Completion"
                    type="date"
                    value={projectForm.expectedCompletionDate}
                    onChange={(e) =>
                      setProjectForm((prev) => ({
                        ...prev,
                        expectedCompletionDate: e.target.value,
                      }))
                    }
                    hint="Optional"
                  />
                </div>
                {editingId && (
                  <label className="flex flex-col gap-1.5 text-sm font-medium text-[var(--text-secondary)]">
                    Status
                    <select
                      className="w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-4 py-3 text-sm text-[var(--text-primary)] transition-all focus:border-[var(--accent)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-glow)]"
                      value={projectForm.status}
                      onChange={(e) =>
                        setProjectForm((prev) => ({
                          ...prev,
                          status: Number(e.target.value),
                        }))
                      }
                    >
                      {Object.entries(statusLabels).map(([value, label]) => (
                        <option key={value} value={value} className="bg-[var(--bg-card)]">
                          {label}
                        </option>
                      ))}
                    </select>
                  </label>
                )}
              </div>

              {/* Modal Footer */}
              <div className="mt-6 flex items-center justify-end gap-3 border-t border-[var(--border)] pt-6">
                <Button type="button" variant="ghost" onClick={resetProjectForm}>
                  Cancel
                </Button>
                <Button type="submit">{editingId ? "Update Project" : "Create Project"}</Button>
              </div>
            </form>
          </div>
        </div>
      )}
    </>
  );
}


