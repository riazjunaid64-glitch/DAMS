import { useCallback, useEffect, useMemo, useState } from "react";
import { useParams } from "react-router-dom";
import type { User } from "../App.tsx";
import { api, resolveMediaUrl } from "../api/api.ts";
import { deleteProjectMedia, getProjectMedia, setProjectCoverMedia, uploadProjectMediaBulk } from "../api/media.ts";
import {
  Button,
  Card,
  EmptyState,
  FilterBar,
  IconBuilding,
  IconPlus,
  KeyValueGrid,
  ListCard,
  LoadMore,
  PageHeader,
  Pagination,
  PhotoGallery,
  PhotoSlider,
  StatCard,
  StatSummary,
  Tabs,
  useIsPhone,
  useToast,
  type Photo,
} from "../components/ui";
import { ProjectDialog } from "./ProjectsPage.tsx";
import { useProjects } from "../contexts/projectsContextValue.ts";
import { can } from "../features/access/permissions.ts";
import { unitStatus } from "../features/leads/labels.ts";
import { formatDay } from "../lib/dates.ts";
import { floorLabel } from "../lib/floors.ts";
import { formatPkr } from "../utils/currency.ts";
import { parseProjectRow, projectStatusName, type ProjectFromApi } from "../utils/parseProject.ts";
import { parseUnitsPayload, type UnitFromApi } from "../utils/parseUnit.ts";
import type { ProjectMedia } from "../types/media.ts";
import {
  UNIT_TYPES,
  availableByType,
  filterUnits,
  floorsInUse,
  formatSqFt,
  sortUnits,
  type UnitSort,
} from "../components/project/unitList.ts";
import { UnitDialog } from "./UnitDetailPage.tsx";

type Props = { user: User | null };
const PAGE = "mx-auto flex w-full max-w-[1500px] flex-col gap-4 px-4 py-5 md:gap-5 md:px-8 md:py-7";
const PAGE_SIZE = 20;

const SORTS = [
  { value: "priceAsc", label: "Price: low to high" },
  { value: "priceDesc", label: "Price: high to low" },
  { value: "sizeDesc", label: "Size: large to small" },
];
const STATUS_FILTERS = [
  { value: "Available", label: "Available" },
  { value: "Booked", label: "Booked" },
  { value: "Sold", label: "Sold" },
];

function toPhotos(items: ProjectMedia[]): Photo[] {
  return items.map((item) => ({
    id: item.id,
    src: resolveMediaUrl(item.mediaUrl),
    alt: item.altText ?? "",
    isCover: item.isCover,
  }));
}

export default function ProjectDetailPage({ user }: Props) {
  const { id } = useParams<{ id?: string }>();
  const projectId = Number(id);
  const toast = useToast();
  const isPhone = useIsPhone();
  const canWrite = can(user?.role, "projects.write");
  const { reload: reloadProjects } = useProjects();
  const [project, setProject] = useState<ProjectFromApi | null>(null);
  const [units, setUnits] = useState<UnitFromApi[]>([]);
  const [media, setMedia] = useState<ProjectMedia[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [tab, setTab] = useState("overview");
  const [editing, setEditing] = useState(false);
  const [adding, setAdding] = useState(false);

  const load = useCallback(async () => {
    if (!projectId || Number.isNaN(projectId)) {
      setError("Project not found.");
      setLoading(false);
      return;
    }
    setError(null);
    try {
      const [projectResponse, unitResponse, photos] = await Promise.all([
        api(`/api/Project/${projectId}`, undefined, false),
        api(`/api/Unit/project/${projectId}`, undefined, false),
        getProjectMedia(projectId).catch(() => [] as ProjectMedia[]),
      ]);
      if (!projectResponse.ok) {
        setError("Project not found.");
        setProject(null);
        return;
      }
      const parsed = parseProjectRow(await projectResponse.json());
      if (!parsed) {
        setError("Project not found.");
        setProject(null);
        return;
      }
      setProject(parsed);
      setUnits(unitResponse.ok ? parseUnitsPayload(await unitResponse.json()) : []);
      setMedia(photos);
    } catch {
      setError("This project could not be loaded.");
    } finally {
      setLoading(false);
    }
  }, [projectId]);

  useEffect(() => { setLoading(true); void load(); }, [load]);

  const refresh = async () => {
    await load();
    await reloadProjects();
  };

  if (loading && !project) {
    return <div className={PAGE}><div className="h-16 animate-pulse rounded-card bg-track" /><div className="h-64 animate-pulse rounded-card bg-track" /></div>;
  }
  if (error || !project) {
    return (
      <div className={PAGE}>
        <EmptyState icon={<IconBuilding size={26} />} title={error ?? "Project not found"} action={<Button variant="outline" onClick={() => { setLoading(true); void load(); }}>Try again</Button>} />
      </div>
    );
  }

  const status = projectStatusName(project.status);
  const photos = toPhotos(media);

  return (
    <div className={PAGE}>
      {isPhone && tab === "overview" && <PhotoSlider photos={photos} />}
      <PageHeader
        back={{ to: "/projects", label: "Projects" }}
        title={project.projectName}
        status={status || undefined}
        subtitle={project.location}
        actions={canWrite ? <Button variant="outline" onClick={() => setEditing(true)}>Edit project</Button> : undefined}
      />
      <Tabs
        aria-label="Project"
        value={tab}
        onChange={setTab}
        items={[
          { id: "overview", label: "Overview" },
          { id: "units", label: "Units", count: project.totalUnits || units.length },
          { id: "media", label: "Media", count: media.length },
        ]}
      />
      {tab === "overview" && (
        <Overview project={project} units={units} photos={photos} isPhone={isPhone} onSeeUnits={() => setTab("units")} />
      )}
      {tab === "units" && (
        <UnitsTab units={units} canWrite={canWrite} isPhone={isPhone} onAdd={() => setAdding(true)} />
      )}
      {tab === "media" && (
        <MediaTab projectId={project.id} media={media} canWrite={canWrite} onChange={() => void refresh()} />
      )}
      {editing && (
        <ProjectDialog
          project={project}
          onClose={() => setEditing(false)}
          onSaved={async () => {
            setEditing(false);
            toast.success("Project saved");
            await refresh();
          }}
        />
      )}
      {adding && (
        <UnitDialog
          projectId={project.id}
          onClose={() => setAdding(false)}
          onSaved={async () => {
            setAdding(false);
            toast.success("Unit added");
            await refresh();
          }}
        />
      )}
    </div>
  );
}

function Overview({ project, units, photos, isPhone, onSeeUnits }: {
  project: ProjectFromApi;
  units: UnitFromApi[];
  photos: Photo[];
  isPhone: boolean;
  onSeeUnits: () => void;
}) {
  const types = availableByType(units);
  const counts = (
    <StatSummary
      total={{ label: "Total units", value: project.totalUnits.toLocaleString("en-PK") }}
      phoneColumns={3}
      items={[
        { label: "Available", value: project.availableUnits.toLocaleString("en-PK"), tone: "green" },
        { label: "Booked", value: project.bookedUnits.toLocaleString("en-PK"), tone: "orange" },
        { label: "Sold", value: project.soldUnits.toLocaleString("en-PK"), tone: "grey" },
      ]}
    />
  );
  const byType = project.totalUnits > 0 && (
    <div className="mt-4">
      <p className="m-0 text-caption font-bold uppercase tracking-[0.4px] text-ink-muted">Available by type</p>
      <div className="mt-2 flex flex-col gap-1.5">
        {types.map((row) => (
          <div key={row.type} className="flex justify-between text-sm">
            <span className="text-ink-2">{row.type}</span>
            <span className="font-extrabold text-ink">{row.count.toLocaleString("en-PK")}</span>
          </div>
        ))}
      </div>
      <Button variant="link" className="mt-3" onClick={onSeeUnits}>See all units</Button>
    </div>
  );
  const details = (
    <Card title="Details">
      <KeyValueGrid items={[
        { label: "Category", value: project.category },
        { label: "Status", value: projectStatusName(project.status) },
        { label: "Start date", value: project.startingDate ? formatDay(project.startingDate) : undefined },
        { label: "Expected completion", value: project.expectedCompletionDate ? formatDay(project.expectedCompletionDate) : undefined },
      ]} />
    </Card>
  );
  const about = project.description ? <Card title="About"><p className="m-0 text-sm text-ink-2">{project.description}</p></Card> : null;

  if (isPhone) {
    return <div className="flex flex-col gap-4">{counts}{byType}{details}{about}</div>;
  }

  return (
    <div className="grid items-start gap-5 md:grid-cols-[minmax(0,1.3fr)_minmax(280px,0.7fr)]">
      <div className="flex flex-col gap-4">
        <PhotoSlider photos={photos} />
        {about}
      </div>
      <div className="flex flex-col gap-4">
        <Card title="Units">
          <div className="grid grid-cols-2 gap-2">
            <StatCard label="Total" value={project.totalUnits.toLocaleString("en-PK")} />
            <StatCard label="Available" value={project.availableUnits.toLocaleString("en-PK")} tone="green" />
            <StatCard label="Booked" value={project.bookedUnits.toLocaleString("en-PK")} tone="orange" />
            <StatCard label="Sold" value={project.soldUnits.toLocaleString("en-PK")} tone="grey" />
          </div>
          {byType}
        </Card>
        {details}
      </div>
    </div>
  );
}

function UnitsTab({ units, canWrite, isPhone, onAdd }: {
  units: UnitFromApi[];
  canWrite: boolean;
  isPhone: boolean;
  onAdd: () => void;
}) {
  const [search, setSearch] = useState("");
  const [type, setType] = useState("");
  const [status, setStatus] = useState("");
  const [floor, setFloor] = useState("");
  const [sort, setSort] = useState<UnitSort>("");
  const [page, setPage] = useState(1);
  const [shown, setShown] = useState(PAGE_SIZE);
  const floors = useMemo(() => floorsInUse(units), [units]);
  const filtered = useMemo(
    () => sortUnits(filterUnits(units, { search, type, status, floor }), sort),
    [units, search, type, status, floor, sort],
  );
  const resetPage = () => { setPage(1); setShown(PAGE_SIZE); };
  const toggleStatus = (next: string) => { setStatus((current) => current === next ? "" : next); resetPage(); };

  const pageRows = isPhone ? filtered.slice(0, shown) : filtered.slice((page - 1) * PAGE_SIZE, page * PAGE_SIZE);

  return (
    <div className="flex flex-col gap-4">
      <StatSummary
        total={{ label: "Total units", value: units.length.toLocaleString("en-PK") }}
        phoneColumns={3}
        items={[
          { label: "Available", value: units.filter((unit) => unitStatus(unit.status) === "Available").length.toLocaleString("en-PK"), tone: "green", selected: status === "Available", onClick: () => toggleStatus("Available") },
          { label: "Booked", value: units.filter((unit) => unitStatus(unit.status) === "Booked").length.toLocaleString("en-PK"), tone: "orange", selected: status === "Booked", onClick: () => toggleStatus("Booked") },
          { label: "Sold", value: units.filter((unit) => unitStatus(unit.status) === "Sold").length.toLocaleString("en-PK"), tone: "grey", selected: status === "Sold", onClick: () => toggleStatus("Sold") },
        ]}
      />
      <div className="flex items-start gap-3">
        <div className="min-w-0 flex-1">
          <FilterBar
            search={{ value: search, onSearch: (value) => { setSearch(value); resetPage(); }, placeholder: "Unit number" }}
            filters={[
              { type: "select", key: "type", label: "Type", options: UNIT_TYPES.map((value) => ({ value, label: value })) },
              { type: "select", key: "status", label: "Status", options: STATUS_FILTERS },
              { type: "select", key: "floor", label: "Floor", options: floors },
              { type: "select", key: "sort", label: "Sort", allLabel: "Unit number", options: SORTS },
            ]}
            values={{ type, status, floor, sort }}
            onChange={(changes) => {
              if ("type" in changes) setType(changes.type ?? "");
              if ("status" in changes) setStatus(changes.status ?? "");
              if ("floor" in changes) setFloor(changes.floor ?? "");
              if ("sort" in changes) setSort((changes.sort ?? "") as UnitSort);
              resetPage();
            }}
            onReset={() => { setSearch(""); setType(""); setStatus(""); setFloor(""); setSort(""); resetPage(); }}
            onAdd={canWrite ? onAdd : undefined}
            addLabel="Add unit"
          />
        </div>
        {canWrite && !isPhone && <Button icon={<IconPlus size={16} />} onClick={onAdd}>Add unit</Button>}
      </div>
      {filtered.length === 0 ? (
        <EmptyState icon={<IconBuilding size={26} />} title={units.length === 0 ? "No units added yet" : "No units match"} />
      ) : (
        <>
          <div className="grid gap-3 md:grid-cols-4">
            {pageRows.map((unit) => {
              const shownStatus = unitStatus(unit.status);
              return (
                <ListCard
                  key={unit.id}
                  to={`/units/${unit.id}`}
                  reference={`Unit ${unit.unitNumber}`}
                  status={shownStatus}
                  title={unit.unitType || "—"}
                  detail={`${floorLabel(unit.floorNumber)} · ${formatSqFt(unit.size)}`}
                  value={formatPkr(unit.price)}
                />
              );
            })}
          </div>
          {isPhone ? (
            <LoadMore shown={Math.min(shown, filtered.length)} total={filtered.length} onLoadMore={() => setShown((count) => count + PAGE_SIZE)} />
          ) : (
            <Pagination page={page} onPageChange={setPage} totalCount={filtered.length} pageSize={PAGE_SIZE} itemLabel="units" />
          )}
        </>
      )}
    </div>
  );
}

function MediaTab({ projectId, media, canWrite, onChange }: {
  projectId: number;
  media: ProjectMedia[];
  canWrite: boolean;
  onChange: () => void;
}) {
  const toast = useToast();
  const [uploading, setUploading] = useState(false);
  const photos = toPhotos(media);

  const run = async (success: string, action: () => Promise<unknown>) => {
    try {
      await action();
      toast.success(success);
      onChange();
    } catch (caught) {
      toast.error(caught instanceof Error ? caught.message : "That photo could not be updated.");
    }
  };

  return (
    <div className="flex flex-col gap-3">
      {photos.length === 0 && <p className="m-0 text-sm text-ink-muted">No photos yet</p>}
      <PhotoGallery
        photos={photos}
        canManage={canWrite}
        uploading={uploading}
        onUpload={(files) => {
          setUploading(true);
          void run("Photos uploaded", () => uploadProjectMediaBulk(projectId, files)).finally(() => setUploading(false));
        }}
        onSetCover={(photo) => void run("Cover photo updated", () => setProjectCoverMedia(projectId, Number(photo.id)))}
        onDelete={(photo) => void run("Photo deleted", () => deleteProjectMedia(projectId, Number(photo.id)))}
      />
    </div>
  );
}
