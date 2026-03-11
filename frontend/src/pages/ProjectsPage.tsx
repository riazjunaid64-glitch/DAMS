import { useEffect, useState, type FormEvent } from "react";
import { api } from "../api/api";
import type { User } from "../App";
import Button from "../lib/Button";
import Container from "../lib/Container";
import Field from "../lib/Field";
import Section from "../lib/Section";

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

export default function ProjectsPage({ user }: Props) {
  const [projects, setProjects] = useState<Project[]>([]);
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

  const statusLabels: Record<number, string> = {
    1: "Planning",
    2: "Ongoing",
    3: "Completed",
    4: "Cancelled",
    5: "Archived",
  };

  const formatDate = (value?: string | null) => {
    if (!value) return "-";
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return "-";
    return date.toLocaleDateString();
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
        setProjectsError("Unable to load projects right now.");
        return;
      }
      const data = (await res.json()) as Project[];
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

  return (
    <main>
      <Section
        eyebrow="Projects"
        title="Active Portfolio"
        description="Track every project with timelines, locations, and status updates in one view."
        className="bg-slate-950 py-16 sm:py-20"
      >
        <Container>
          <div className="space-y-6">
            <div className="flex items-center justify-between gap-4">
              <p className="text-sm text-slate-300">
                Projects are visible to all users. Only admins can create or edit projects.
              </p>
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
                  Create Project
                </Button>
              )}
            </div>
            <div className="space-y-6">
              {projectsLoading && (
                <div className="rounded-3xl border border-white/10 bg-white/5 p-6 text-sm text-slate-200">
                  Loading projects...
                </div>
              )}
              {projectsError && (
                <div className="rounded-3xl border border-rose-300/30 bg-rose-500/10 p-6 text-sm text-rose-100">
                  {projectsError}
                </div>
              )}
              {!projectsLoading && projects.length === 0 && (
                <div className="rounded-3xl border border-white/10 bg-white/5 p-6 text-sm text-slate-200">
                  No projects yet. Create your first project to get started.
                </div>
              )}
              <div className="grid gap-6 md:grid-cols-2">
                {projects.map((project) => {
                  const statusValue =
                    typeof project.status === "number"
                      ? statusLabels[project.status] ?? "Unknown"
                      : project.status;
                  return (
                    <div
                      key={project.id}
                      className="rounded-3xl border border-white/10 bg-white/5 p-6 shadow-[0_24px_60px_-50px_rgba(15,23,42,0.9)]"
                    >
                      <div className="flex items-start justify-between gap-3">
                        <div>
                          <h3 className="text-lg font-semibold">{project.projectName}</h3>
                          <p className="mt-1 text-xs uppercase tracking-[0.2em] text-amber-200">
                            {project.location}
                          </p>
                        </div>
                        <span className="rounded-full border border-amber-300/40 bg-amber-400/10 px-3 py-1 text-xs font-semibold text-amber-200">
                          {statusValue}
                        </span>
                      </div>
                      <p className="mt-4 text-sm text-slate-300">
                        {project.description || "No description provided yet."}
                      </p>
                      <div className="mt-6 grid gap-2 text-xs text-slate-300">
                        <div className="flex items-center justify-between">
                          <span>Start Date</span>
                          <span className="text-slate-100">{formatDate(project.startingDate)}</span>
                        </div>
                        <div className="flex items-center justify-between">
                          <span>Expected Completion</span>
                          <span className="text-slate-100">
                            {formatDate(project.expectedCompletionDate)}
                          </span>
                        </div>
                      </div>
                      {isAdmin && (
                        <div className="mt-6">
                          <Button variant="outline" onClick={() => startEditProject(project)}>
                            Edit Project
                          </Button>
                        </div>
                      )}
                    </div>
                  );
                })}
              </div>
            </div>
          </div>
        </Container>
      </Section>
      {isAdmin && showProjectModal && (
        <div className="fixed inset-0 z-40 flex items-center justify-center p-4">
          <div
            className="absolute inset-0 bg-slate-950/80 backdrop-blur-sm"
            onClick={resetProjectForm}
          />
          <div className="relative z-10 w-full max-w-2xl rounded-3xl border border-white/10 bg-slate-900 p-8">
            <div className="flex items-center justify-between gap-4">
              <h3 className="text-xl font-semibold">
                {editingId ? "Update Project" : "Create Project"}
              </h3>
              <Button variant="outline" onClick={resetProjectForm}>
                Close
              </Button>
            </div>
            <form onSubmit={submitProject} className="mt-6 grid gap-4">
              <Field
                label="Project Name"
                value={projectForm.projectName}
                onChange={(e) =>
                  setProjectForm((prev) => ({ ...prev, projectName: e.target.value }))
                }
                placeholder="Project name"
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
                placeholder="Short summary"
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
                />
              </div>
              {editingId && (
                <label className="flex flex-col gap-2 text-sm font-medium text-slate-200">
                  Status
                  <select
                    className="w-full rounded-2xl border border-slate-200/40 bg-white/5 px-4 py-3 text-sm text-white focus:border-amber-300 focus:outline-none focus:ring-2 focus:ring-amber-300/30"
                    value={projectForm.status}
                    onChange={(e) =>
                      setProjectForm((prev) => ({
                        ...prev,
                        status: Number(e.target.value),
                      }))
                    }
                  >
                    {Object.entries(statusLabels).map(([value, label]) => (
                      <option key={value} value={value}>
                        {label}
                      </option>
                    ))}
                  </select>
                </label>
              )}
              <div className="flex flex-wrap gap-3">
                <Button type="submit">{editingId ? "Update Project" : "Create Project"}</Button>
                <Button type="button" variant="outline" onClick={resetProjectForm}>
                  Cancel
                </Button>
              </div>
            </form>
          </div>
        </div>
      )}
    </main>
  );
}
