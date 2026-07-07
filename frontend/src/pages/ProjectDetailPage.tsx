import { useEffect, useMemo, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { api, resolveMediaUrl } from "../api/api.ts";
import { parseProjectRow } from "../utils/parseProject.ts";
import { parseUnitsPayload } from "../utils/parseUnit.ts";
import { getProjectMedia } from "../api/media.ts";
import type { User } from "../App.tsx";
import type { ProjectMedia } from "../types/media.ts";
import Container from "../lib/Container.tsx";
import Button from "../lib/Button.tsx";
import TabLayout from "../lib/TabLayout.tsx";
import ProjectOverviewTab from "../components/project/ProjectOverviewTab.tsx";
import ProjectUnitsTab from "../components/project/ProjectUnitsTab.tsx";
import ProjectMediaTab from "../components/project/ProjectMediaTab.tsx";

interface Project {
  id: number;
  projectName: string;
  location: string;
  description?: string | null;
  startingDate: string;
  expectedCompletionDate?: string | null;
  status: number | string;
}

interface Unit {
  id: number;
  projectId: number;
  unitNumber: string;
  unitType: string;
  floorNumber: number;
  size: number;
  price: number;
  status: string;
}

type Props = { user: User | null };

const statusLabels: Record<number, string> = { 1: "Planning", 2: "Ongoing", 3: "Completed", 4: "Cancelled", 5: "Archived" };
const getStatusNum = (s: number | string) => (typeof s === "number" ? s : 1);

const LOADING_PLACEHOLDER_CARDS = 6;

export default function ProjectDetailPage({ user }: Props) {
  const { id } = useParams<{ id?: string }>();
  const navigate = useNavigate();
  const projectId = Number(id);

  const [project, setProject] = useState<Project | null>(null);
  const [units, setUnits] = useState<Unit[]>([]);
  const [media, setMedia] = useState<ProjectMedia[]>([]);
  const [loading, setLoading] = useState(false);
  const [mediaLoading, setMediaLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState("overview");
  const [unitStatusFilter, setUnitStatusFilter] = useState("");

  useEffect(() => {
    if (!projectId || Number.isNaN(projectId)) { setError("Invalid project ID."); return; }
    const load = async () => {
      setLoading(true);
      setError(null);
      try {
        const [projRes, unitRes] = await Promise.all([
          api(`/api/Project/${projectId}`, undefined, false),
          api(`/api/Unit/project/${projectId}`, undefined, false),
        ]);
        if (!projRes.ok) { setError("Project not found."); setProject(null); setUnits([]); return; }
        const rawProj: unknown = await projRes.json();
        const parsed = parseProjectRow(rawProj);
        if (!parsed) {
          setError("Project not found.");
          setProject(null);
          setUnits([]);
          return;
        }
        setProject(parsed);
        if (unitRes.ok) {
          const rawUnits: unknown = await unitRes.json();
          setUnits(parseUnitsPayload(rawUnits));
        } else {
          setUnits([]);
        }
      } catch { setError("Unable to load project details right now."); }
      finally { setLoading(false); }
    };
    load();
  }, [projectId]);

  // Load media once the project is available — the overview gallery and media tab both need it.
  useEffect(() => {
    if (!projectId || Number.isNaN(projectId)) return;
    const loadMedia = async () => {
      setMediaLoading(true);
      try { setMedia(await getProjectMedia(projectId)); }
      catch { console.error("Failed to load media"); }
      finally { setMediaLoading(false); }
    };
    loadMedia();
  }, [projectId]);

  const unitStats = useMemo(() => {
    const s = { available: 0, sold: 0, reserved: 0 };
    units.forEach((u) => {
      const st = u.status.toLowerCase();
      if (st === "available") s.available++;
      else if (st === "sold") s.sold++;
      else if (st === "reserved") s.reserved++;
    });
    return s;
  }, [units]);

  // Ordered list of image URLs for the overview gallery: cover first, then by displayOrder. Videos/documents excluded.
  const galleryImages = useMemo(() => {
    return media
      .filter((m) => m.mediaType !== "video" && m.category !== 4 && m.category !== 8)
      .slice()
      .sort((a, b) => Number(b.isCover) - Number(a.isCover) || a.displayOrder - b.displayOrder)
      .map((m) => resolveMediaUrl(m.mediaUrl));
  }, [media]);

  const statusNum = project ? getStatusNum(project.status) : 1;

  const tabs = [
    { id: "overview", label: "Overview", icon: <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><rect x="3" y="3" width="7" height="9" rx="1"/><rect x="14" y="3" width="7" height="5" rx="1"/><rect x="14" y="12" width="7" height="9" rx="1"/><rect x="3" y="16" width="7" height="5" rx="1"/></svg> },
    { id: "units", label: "Units", badge: units.length, icon: <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M3 9l9-7 9 7v11a2 2 0 01-2 2H5a2 2 0 01-2-2V9z"/><polyline points="9 22 9 12 15 12 15 22"/></svg> },
    { id: "media", label: "Media", badge: media.length || undefined, icon: <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><rect x="3" y="3" width="18" height="18" rx="2" ry="2"/><circle cx="8.5" cy="8.5" r="1.5"/><polyline points="21 15 16 10 5 21"/></svg> },
  ];

  return (
    <>
      <div className="relative overflow-hidden border-b border-[var(--border)] bg-[var(--bg-primary)]">
        <div className="absolute inset-0 mesh-gradient-subtle opacity-80" />
        <div className="relative w-full px-4 py-6 sm:px-5 sm:py-8 lg:px-6">
          <div className="mb-4 flex items-center gap-2 text-xs font-semibold text-[var(--text-muted)] sm:text-sm">
            <button onClick={() => navigate("/projects")} className="transition-colors hover:text-[var(--accent)]">Projects</button>
            <svg className="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="m9 18 6-6-6-6" /></svg>
            <span className="min-w-0 truncate text-[var(--text-secondary)]">{project?.projectName ?? "Loading..."}</span>
          </div>

          <div className="flex flex-col gap-5 sm:flex-row sm:items-end sm:justify-between">
            <div className="min-w-0">
              <div className="flex flex-wrap items-center gap-3">
                <h1 className="max-w-5xl text-3xl font-bold leading-tight text-[var(--text-heading)] sm:text-4xl lg:text-5xl">
                  {project ? project.projectName : "Loading..."}
                </h1>
                {project && (
                  <span className="rounded-full border border-[var(--border-active)] bg-[var(--accent-glow)] px-3.5 py-1.5 text-xs font-bold text-[var(--accent)] shadow-[0_8px_28px_rgba(99,102,241,0.14)]">
                    {statusLabels[statusNum] ?? "Unknown"}
                  </span>
                )}
              </div>
              <p className="mt-3 max-w-2xl text-sm leading-6 text-[var(--text-secondary)]">
                A focused project workspace for media, units, availability, and delivery progress.
              </p>
            </div>
            <Button variant="outline" size="sm" onClick={() => navigate(-1)} className="w-fit rounded-full px-4 shadow-[var(--shadow-sm)] hover:-translate-y-0.5">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round"><path d="m15 18-6-6 6-6" /></svg>
              Back
            </Button>
          </div>
        </div>
      </div>

      {/* Loading */}
      {loading && (
        <div className="py-20">
          <Container>
            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
              {[...Array(LOADING_PLACEHOLDER_CARDS)].map((_, i) => (
                <div key={i} className="rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] p-5">
                  <div className="skeleton mb-3 h-5 w-1/2" />
                  <div className="skeleton mb-2 h-3 w-3/4" />
                  <div className="skeleton h-3 w-1/2" />
                </div>
              ))}
            </div>
          </Container>
        </div>
      )}

      {/* Error */}
      {error && (
        <div className="py-10">
          <Container>
            <div className="rounded-2xl border border-rose-500/20 bg-rose-500/[0.06] px-5 py-4 text-sm text-rose-300 flex items-center gap-3">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><circle cx="12" cy="12" r="10"/><line x1="15" y1="9" x2="9" y2="15"/><line x1="9" y1="9" x2="15" y2="15"/></svg>
              {error}
            </div>
          </Container>
        </div>
      )}

      {/* Tabs */}
      {!loading && !error && project && (
        <TabLayout tabs={tabs} activeTab={activeTab} onTabChange={setActiveTab}>
          {activeTab === "overview" && (
            <ProjectOverviewTab
              project={project}
              totalUnits={units.length}
              unitStats={unitStats}
              galleryImages={galleryImages}
              activeUnitStatus={unitStatusFilter}
              onUnitStatusSelect={(status) => {
                setUnitStatusFilter(status);
                setActiveTab("units");
              }}
            />
          )}
          {activeTab === "units" && (
            <ProjectUnitsTab
              units={units}
              projectId={projectId}
              user={user}
              onUnitsChange={setUnits}
              statusFilter={unitStatusFilter}
              onStatusFilterChange={setUnitStatusFilter}
            />
          )}
          {activeTab === "media" && (
            <ProjectMediaTab projectId={projectId} media={media} onMediaChange={setMedia} user={user} loading={mediaLoading} />
          )}
        </TabLayout>
      )}
    </>
  );
}
