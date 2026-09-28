import { useState } from "react";
import type { User } from "../App.tsx";
import { api } from "../api/api.ts";
import {
  Button,
  DateField,
  Dropdown,
  EmptyState,
  IconBuilding,
  IconPlus,
  Modal,
  PageHeader,
  TextArea,
  TextField,
  useToast,
} from "../components/ui";
import { ProjectCard } from "../components/project/ProjectCard.tsx";
import { useProjects } from "../contexts/projectsContextValue.ts";
import { can } from "../features/access/permissions.ts";
import { projectStatusName, type ProjectFromApi } from "../utils/parseProject.ts";

type Props = { user: User | null };

const PAGE = "mx-auto flex w-full max-w-[1500px] flex-col gap-4 px-4 py-5 md:gap-5 md:px-8 md:py-7";
const CATEGORIES = ["Residential", "Commercial", "Mixed use"];
const STATUSES = ["Planning", "Ongoing", "Completed", "Cancelled", "Archived"];

async function readError(response: Response, fallback: string): Promise<string> {
  const text = await response.text();
  if (!text) return fallback;
  try {
    const body = JSON.parse(text) as { message?: string; title?: string; errors?: Record<string, string[]> };
    if (body.message) return body.message;
    const first = body.errors && Object.values(body.errors).flat()[0];
    if (first) return first;
    if (body.title) return body.title;
  } catch {
    // The server sent plain text.
  }
  return text.length < 300 ? text : fallback;
}

export default function ProjectsPage({ user }: Props) {
  const toast = useToast();
  const { projects, loading, error, reload } = useProjects();
  const canWrite = can(user?.role, "projects.write");
  const [creating, setCreating] = useState(false);

  return (
    <div className={PAGE}>
      <PageHeader
        title="Projects"
        actions={canWrite ? <Button icon={<IconPlus size={16} />} onClick={() => setCreating(true)}>New project</Button> : undefined}
      />
      {loading && projects.length === 0 ? (
        <div className="grid gap-4 md:grid-cols-3">{Array.from({ length: 3 }, (_, index) => <div key={index} className="h-64 animate-pulse rounded-card bg-track" />)}</div>
      ) : error && projects.length === 0 ? (
        <EmptyState icon={<IconBuilding size={26} />} title="Projects could not be loaded" action={<Button variant="outline" onClick={() => void reload()}>Try again</Button>} />
      ) : projects.length === 0 ? (
        <EmptyState icon={<IconBuilding size={26} />} title="No projects yet" />
      ) : (
        <div className="grid gap-4 md:grid-cols-3">
          {projects.map((project) => <ProjectCard key={project.id} project={project} />)}
        </div>
      )}
      {creating && (
        <ProjectDialog
          onClose={() => setCreating(false)}
          onSaved={async () => {
            setCreating(false);
            toast.success("Project created");
            await reload();
          }}
        />
      )}
    </div>
  );
}

export function ProjectDialog({ project, onClose, onSaved }: {
  project?: ProjectFromApi | null;
  onClose: () => void;
  onSaved: () => void | Promise<void>;
}) {
  const toast = useToast();
  const editing = project ?? null;
  const [name, setName] = useState(editing?.projectName ?? "");
  const [location, setLocation] = useState(editing?.location ?? "");
  const [category, setCategory] = useState(editing?.category === "Mixed Use" ? "Mixed use" : editing?.category ?? "");
  const [start, setStart] = useState(editing?.startingDate?.slice(0, 10) ?? "");
  const [completion, setCompletion] = useState(editing?.expectedCompletionDate?.slice(0, 10) ?? "");
  const [about, setAbout] = useState(editing?.description ?? "");
  const [status, setStatus] = useState(projectStatusName(editing?.status) || "Planning");
  const [saving, setSaving] = useState(false);
  const dateError = start && completion && completion < start ? "Completion date can't be before the start date." : undefined;
  const ready = name.trim().length > 0 && location.trim().length > 0 && !dateError;

  const save = async () => {
    setSaving(true);
    const payload = {
      projectName: name.trim(),
      location: location.trim(),
      category: category || null,
      description: about.trim() || null,
      startingDate: start || null,
      expectedCompletionDate: completion || null,
      ...(editing ? { status } : {}),
    };
    try {
      const response = await api(editing ? `/api/Project/${editing.id}` : "/api/Project", {
        method: editing ? "PUT" : "POST",
        body: JSON.stringify(payload),
      });
      if (!response.ok) {
        toast.error(await readError(response, "The project could not be saved."));
        setSaving(false);
        return;
      }
      await onSaved();
    } catch {
      toast.error("The project could not be saved.");
      setSaving(false);
    }
  };

  return (
    <Modal
      open
      onClose={onClose}
      title={editing ? "Edit project" : "New project"}
      size="md"
      phoneLayout="fullscreen"
      busy={saving}
      primaryAction={{ label: editing ? "Save changes" : "Create project", onClick: () => void save(), disabled: !ready, loading: saving }}
    >
      <div className="flex flex-col gap-4">
        <TextField label="Project name" required value={name} onChange={(event) => setName(event.target.value)} />
        <TextField label="Location" required value={location} onChange={(event) => setLocation(event.target.value)} />
        <Dropdown label="Category" value={category} onChange={setCategory} placeholder="Select" options={CATEGORIES.map((value) => ({ value, label: value }))} />
        <DateField label="Start date" value={start} onChange={(event) => setStart(event.target.value)} />
        <DateField label="Expected completion" value={completion} error={dateError} onChange={(event) => setCompletion(event.target.value)} />
        <TextArea label="About" value={about} onChange={(event) => setAbout(event.target.value)} />
        {editing && <Dropdown label="Status" value={status} onChange={setStatus} options={STATUSES.map((value) => ({ value, label: value }))} />}
      </div>
    </Modal>
  );
}
