import { useEffect, useMemo, useState, type FormEvent } from "react";
import { api } from "../api/api.ts";
import { uploadProjectMedia } from "../api/media.ts";
import CoverImageField from "../components/CoverImageField.tsx";
import ProjectCard from "../components/ProjectCard.tsx";
import { parseProjectsPayload, type ProjectFromApi } from "../utils/parseProject.ts";
import type { User } from "../App.tsx";
import Button from "../lib/Button.tsx";
import Container from "../lib/Container.tsx";
import Field from "../lib/Field.tsx";
import Pagination from "../lib/Pagination.tsx";
import Section from "../lib/Section.tsx";

type Props = {
  user: User | null;
};

const PROJECT_CATEGORIES = ["Residential", "Commercial", "Mixed Use"] as const;

const statusLabels: Record<number, string> = {
  1: "Planning",
  2: "Ongoing",
  3: "Completed",
  4: "Cancelled",
  5: "Archived",
};

const emptyForm = () => ({
  projectName: "",
  location: "",
  category: "Residential",
  description: "",
  startingDate: "",
  expectedCompletionDate: "",
  status: 1,
});

export default function ProjectsPage({ user }: Props) {
  const PROJECTS_PER_PAGE = 6;
  const [projects, setProjects] = useState<ProjectFromApi[]>([]);
  const [currentPage, setCurrentPage] = useState(1);
  const [projectsLoading, setProjectsLoading] = useState(false);
  const [projectsError, setProjectsError] = useState<string | null>(null);
  const [modalError, setModalError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [showProjectModal, setShowProjectModal] = useState(false);
  const [coverFile, setCoverFile] = useState<File | null>(null);
  const [existingCoverUrl, setExistingCoverUrl] = useState<string | null>(null);
  const [projectForm, setProjectForm] = useState(emptyForm);

  const isAdmin = user?.role === "Admin";

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
    setProjectForm(emptyForm());
    setCoverFile(null);
    setExistingCoverUrl(null);
    setModalError(null);
    setEditingId(null);
    setShowProjectModal(false);
  };

  const openCreateModal = () => {
    setEditingId(null);
    setProjectForm(emptyForm());
    setCoverFile(null);
    setExistingCoverUrl(null);
    setModalError(null);
    setShowProjectModal(true);
  };

  const startEditProject = (project: ProjectFromApi) => {
    setEditingId(project.id);
    setShowProjectModal(true);
    setCoverFile(null);
    setExistingCoverUrl(project.coverImageUrl ?? null);
    setProjectForm({
      projectName: project.projectName,
      location: project.location,
      category: project.category || "Residential",
      description: project.description ?? "",
      startingDate: toInputDate(project.startingDate),
      expectedCompletionDate: toInputDate(project.expectedCompletionDate),
      status: typeof project.status === "number" ? project.status : 1,
    });
  };

  const submitProject = async (event: FormEvent) => {
    event.preventDefault();
    setModalError(null);
    setProjectsError(null);

    if (!projectForm.projectName || !projectForm.location || !projectForm.startingDate) {
      setModalError("Project name, location, and starting date are required.");
      return;
    }

    const payload = {
      projectName: projectForm.projectName,
      location: projectForm.location,
      category: projectForm.category || null,
      description: projectForm.description || null,
      startingDate: new Date(projectForm.startingDate).toISOString(),
      expectedCompletionDate: projectForm.expectedCompletionDate
        ? new Date(projectForm.expectedCompletionDate).toISOString()
        : null,
      ...(editingId ? { status: projectForm.status } : {}),
    };

    setSaving(true);
    try {
      const res = await api(editingId ? `/api/Project/${editingId}` : "/api/Project", {
        method: editingId ? "PUT" : "POST",
        body: JSON.stringify(payload),
      });

      if (!res.ok) {
        const text = await res.text();
        setModalError(text || "Unable to save project.");
        return;
      }

      let projectId = editingId;
      if (!projectId) {
        const created = (await res.json()) as { id?: number; Id?: number };
        projectId = created.id ?? created.Id ?? null;
      }

      if (coverFile && projectId) {
        try {
          await uploadProjectMedia(projectId, coverFile, {
            category: 2,
            isCover: true,
            altText: `${projectForm.projectName} cover`,
          });
        } catch {
          resetProjectForm();
          await loadProjects();
          setProjectsError("Project saved, but the cover image could not be uploaded.");
          return;
        }
      }

      resetProjectForm();
      await loadProjects();
    } catch {
      setModalError("Unable to save project right now.");
    } finally {
      setSaving(false);
    }
  };

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
      <div className="relative overflow-hidden border-b border-white/[0.04]">
        <div className="absolute inset-0 mesh-gradient-subtle" />
        <Container className="relative py-4 sm:py-4">
          <Section>
            <div className="flex flex-wrap items-center justify-center gap-4">
              <div className="flex items-center gap-2 rounded-full border border-[var(--border)] bg-[var(--surface-glass)] px-4 py-2">
                <span className="h-2 w-2 rounded-full bg-[var(--accent)]" />
                <span className="text-sm text-[var(--text-secondary)]">
                  {projects.length} project{projects.length !== 1 ? "s" : ""}
                </span>
              </div>
              {isAdmin && (
                <Button onClick={openCreateModal}>
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                    <line x1="12" y1="5" x2="12" y2="19" /><line x1="5" y1="12" x2="19" y2="12" />
                  </svg>
                  New Project
                </Button>
              )}
            </div>
          </Section>
        </Container>
      </div>

      <div className="projects-page py-12 sm:py-16">
        <Container>
          {projectsLoading && (
            <div className="projects-grid">
              {[...Array(6)].map((_, i) => (
                <div key={i} className="op-card op-card--skeleton">
                  <div className="skeleton op-skeleton-thumb" />
                  <div className="op-body">
                    <div className="skeleton mb-3 h-5 w-2/3" />
                    <div className="skeleton h-4 w-full" />
                  </div>
                </div>
              ))}
            </div>
          )}

          {projectsError && (
            <div className="rounded-2xl border border-rose-500/20 bg-rose-500/[0.06] px-5 py-4 text-sm text-rose-300 flex items-center gap-3">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                <circle cx="12" cy="12" r="10" /><line x1="15" y1="9" x2="9" y2="15" /><line x1="9" y1="9" x2="15" y2="15" />
              </svg>
              {projectsError}
            </div>
          )}

          {!projectsLoading && projects.length === 0 && !projectsError && (
            <div className="flex flex-col items-center justify-center py-20 text-center">
              <div className="mb-4 flex h-16 w-16 items-center justify-center rounded-2xl bg-[var(--surface-glass)] border border-[var(--border)]">
                <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" className="text-[var(--text-muted)]" strokeLinecap="round">
                  <rect x="3" y="3" width="7" height="9" rx="1" /><rect x="14" y="3" width="7" height="5" rx="1" /><rect x="14" y="12" width="7" height="9" rx="1" /><rect x="3" y="16" width="7" height="5" rx="1" />
                </svg>
              </div>
              <h3 className="text-lg font-semibold text-[var(--text-heading)]">No projects yet</h3>
              <p className="mt-2 max-w-sm text-sm text-[var(--text-muted)]">
                Create your first project to get started with tracking and management.
              </p>
            </div>
          )}

          {!projectsLoading && projects.length > 0 && (
            <>
              <div className="projects-grid">
                {paginatedProjects.map((project) => (
                  <ProjectCard
                    key={project.id}
                    project={project}
                    showAdminActions={isAdmin}
                    onEdit={startEditProject}
                  />
                ))}
              </div>

              <Pagination currentPage={currentPage} totalPages={totalPages} onPageChange={setCurrentPage} />
            </>
          )}
        </Container>
      </div>

      {isAdmin && showProjectModal && (
        <div className="project-modal">
          <div className="project-modal__backdrop" onClick={resetProjectForm} />
          <div className="project-modal__panel" role="dialog" aria-modal="true" aria-labelledby="project-modal-title">
            <div className="project-modal__header">
              <div>
                <h3 id="project-modal-title" className="project-modal__title">
                  {editingId ? "Update Project" : "Create Project"}
                </h3>
                <p className="project-modal__subtitle">
                  {editingId ? "Edit the project details below" : "Fill in the details to create a new project"}
                </p>
              </div>
              <button type="button" onClick={resetProjectForm} className="project-modal__close" aria-label="Close">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                  <line x1="18" y1="6" x2="6" y2="18" /><line x1="6" y1="6" x2="18" y2="18" />
                </svg>
              </button>
            </div>

            <form onSubmit={submitProject} className="project-modal__form">
              {modalError && (
                <div className="project-modal__error" role="alert">
                  {modalError}
                </div>
              )}
              <div className="project-modal__fields">
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

                <label className="project-modal__select-label">
                  <span>Category</span>
                  <select
                    className="project-modal__select"
                    value={projectForm.category}
                    onChange={(e) =>
                      setProjectForm((prev) => ({ ...prev, category: e.target.value }))
                    }
                  >
                    {PROJECT_CATEGORIES.map((cat) => (
                      <option key={cat} value={cat}>
                        {cat}
                      </option>
                    ))}
                  </select>
                </label>

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

                <CoverImageField
                  file={coverFile}
                  onChange={setCoverFile}
                  existingPreviewUrl={existingCoverUrl}
                />

                <div className="project-modal__dates">
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
                  <label className="project-modal__select-label">
                    <span>Status</span>
                    <select
                      className="project-modal__select"
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
              </div>

              <div className="project-modal__footer">
                <button type="button" className="project-modal__cancel" onClick={resetProjectForm}>
                  Cancel
                </button>
                <button type="submit" className="project-modal__submit" disabled={saving}>
                  {saving ? "Saving…" : editingId ? "Update Project" : "Create Project"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </>
  );
}
