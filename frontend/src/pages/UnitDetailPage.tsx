import { useEffect, useMemo, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { api } from "../api/api.ts";
import { getUnitMedia } from "../api/media.ts";
import type { User } from "../App.tsx";
import type { UnitMedia } from "../types/media.ts";
import Container from "../lib/Container.tsx";
import Button from "../lib/Button.tsx";
import TabLayout from "../lib/TabLayout.tsx";
import UnitOverviewTab from "../components/unit/UnitOverviewTab.tsx";
import UnitMediaTab from "../components/unit/UnitMediaTab.tsx";

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

interface Project {
  id: number;
  projectName: string;
  location: string;
}

type Props = { user: User | null };

const unitStatusColors: Record<string, string> = {
  available: "text-emerald-400 bg-emerald-500/10 border-emerald-500/20",
  sold: "text-rose-400 bg-rose-500/10 border-rose-500/20",
  reserved: "text-amber-400 bg-amber-500/10 border-amber-500/20",
};

export default function UnitDetailPage({ user }: Props) {
  const { id } = useParams<{ id?: string }>();
  const navigate = useNavigate();
  const unitId = Number(id);

  const [unit, setUnit] = useState<Unit | null>(null);
  const [project, setProject] = useState<Project | null>(null);
  const [media, setMedia] = useState<UnitMedia[]>([]);
  const [loading, setLoading] = useState(false);
  const [mediaLoading, setMediaLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState("overview");

  useEffect(() => {
    if (!unitId || Number.isNaN(unitId)) { setError("Invalid unit ID."); return; }
    const load = async () => {
      setLoading(true);
      setError(null);
      try {
        const unitRes = await api(`/api/Unit/${unitId}`, undefined, false);
        if (!unitRes.ok) { setError("Unit not found."); setUnit(null); return; }
        const unitData = (await unitRes.json()) as Unit;
        setUnit(unitData);
        if (unitData.projectId) {
          const projRes = await api(`/api/Project/${unitData.projectId}`, undefined, false);
          if (projRes.ok) setProject((await projRes.json()) as Project);
        }
      } catch { setError("Unable to load unit details right now."); }
      finally { setLoading(false); }
    };
    load();
  }, [unitId]);

  // Lazy-load media only when media tab is active
  useEffect(() => {
    if (activeTab !== "media" || !unitId || Number.isNaN(unitId) || media.length > 0) return;
    const loadMedia = async () => {
      setMediaLoading(true);
      try { setMedia(await getUnitMedia(unitId)); }
      catch { console.error("Failed to load media"); }
      finally { setMediaLoading(false); }
    };
    loadMedia();
  }, [activeTab, unitId]);

  const coverImage = useMemo(() => {
    const cover = media.find((m) => m.isCover);
    return cover?.mediaUrl ?? (media.length > 0 ? media[0].mediaUrl : null);
  }, [media]);

  const statusKey = unit?.status.toLowerCase() ?? "available";
  const statusColor = unitStatusColors[statusKey] ?? unitStatusColors.available;

  const tabs = [
    { id: "overview", label: "Overview", icon: <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><rect x="3" y="3" width="7" height="9" rx="1"/><rect x="14" y="3" width="7" height="5" rx="1"/><rect x="14" y="12" width="7" height="9" rx="1"/><rect x="3" y="16" width="7" height="5" rx="1"/></svg> },
    { id: "media", label: "Media", badge: media.length || undefined, icon: <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><rect x="3" y="3" width="18" height="18" rx="2" ry="2"/><circle cx="8.5" cy="8.5" r="1.5"/><polyline points="21 15 16 10 5 21"/></svg> },
  ];

  return (
    <>
      {/* Workspace Header */}
      <div className="relative overflow-hidden border-b border-[var(--border)]">
        <div className="absolute inset-0 mesh-gradient-subtle" />
        <Container className="relative py-8 sm:py-10">
          {/* Breadcrumb */}
          <div className="mb-4 flex items-center gap-2 text-sm text-[var(--text-muted)]">
            <button onClick={() => navigate("/projects")} className="hover:text-[var(--text-primary)] transition-colors">Projects</button>
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><polyline points="9 18 15 12 9 6"/></svg>
            {project && (
              <>
                <button onClick={() => navigate(`/projects/${project.id}`)} className="hover:text-[var(--text-primary)] transition-colors">{project.projectName}</button>
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><polyline points="9 18 15 12 9 6"/></svg>
              </>
            )}
            <span className="text-[var(--text-secondary)]">{unit?.unitNumber ?? "Loading..."}</span>
          </div>

          <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
            <div className="flex flex-wrap items-center gap-3">
              <h1 className="text-2xl font-bold text-[var(--text-heading)] sm:text-3xl">
                {unit ? unit.unitNumber : "Loading..."}
              </h1>
              {unit && (
                <>
                  <span className={`rounded-full border px-3 py-1 text-xs font-semibold ${statusColor}`}>{unit.status}</span>
                  <span className="rounded-md bg-[var(--accent-glow)] px-2.5 py-0.5 text-[11px] font-medium text-[var(--accent)] uppercase tracking-wider">{unit.unitType}</span>
                </>
              )}
            </div>
            <Button variant="outline" size="sm" onClick={() => navigate(-1)}>
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><polyline points="15 18 9 12 15 6"/></svg>
              Back
            </Button>
          </div>
        </Container>
      </div>

      {/* Loading */}
      {loading && (
        <div className="py-20">
          <Container>
            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
              {[...Array(6)].map((_, i) => (
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
      {!loading && !error && unit && (
        <TabLayout tabs={tabs} activeTab={activeTab} onTabChange={setActiveTab}>
          {activeTab === "overview" && (
            <UnitOverviewTab unit={unit} project={project} user={user} onUnitUpdate={setUnit} coverImage={coverImage} />
          )}
          {activeTab === "media" && (
            <UnitMediaTab unitId={unitId} media={media} onMediaChange={setMedia} user={user} loading={mediaLoading} />
          )}
        </TabLayout>
      )}
    </>
  );
}
