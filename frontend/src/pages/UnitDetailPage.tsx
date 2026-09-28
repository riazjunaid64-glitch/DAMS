import { useCallback, useEffect, useState } from "react";
import { useParams } from "react-router-dom";
import type { User } from "../App.tsx";
import { api, resolveMediaUrl } from "../api/api.ts";
import { deleteUnitMedia, getUnitMedia, setUnitCoverMedia, uploadUnitMediaBulk } from "../api/media.ts";
import {
  Button,
  Card,
  Dropdown,
  EmptyState,
  IconBuilding,
  InfoCard,
  Modal,
  NumberField,
  PageHeader,
  PhotoGallery,
  TextField,
  useToast,
  type Photo,
} from "../components/ui";
import { useProjects } from "../contexts/projectsContextValue.ts";
import { can } from "../features/access/permissions.ts";
import { unitStatus } from "../features/leads/labels.ts";
import { fieldForServerMessage, photoUploadNotice, unitDraftChanged, unitDraftReady, unitSavedMessage } from "../components/project/editRules.ts";
import { floorChoices, floorLabel } from "../lib/floors.ts";
import { formatPkr } from "../utils/currency.ts";
import { parseProjectRow } from "../utils/parseProject.ts";
import { parseUnitRow, type UnitFromApi } from "../utils/parseUnit.ts";
import { UNIT_TYPES, formatSqFt } from "../components/project/unitList.ts";
import type { UnitMedia } from "../types/media.ts";

type Props = { user: User | null };
const PAGE = "mx-auto flex w-full max-w-[1500px] flex-col gap-4 px-4 py-5 md:gap-5 md:px-8 md:py-7";

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
    // Plain text from the server.
  }
  return text.length < 300 ? text : fallback;
}

function toPhotos(items: UnitMedia[]): Photo[] {
  return items.map((item) => ({
    id: item.id,
    src: resolveMediaUrl(item.mediaUrl),
    alt: item.altText ?? "",
    isCover: item.isCover,
  }));
}

export default function UnitDetailPage({ user }: Props) {
  const { id } = useParams<{ id?: string }>();
  const unitId = Number(id);
  const toast = useToast();
  const canWrite = can(user?.role, "projects.write");
  const { reload: reloadProjects } = useProjects();
  const [unit, setUnit] = useState<UnitFromApi | null>(null);
  const [projectName, setProjectName] = useState("");
  const [media, setMedia] = useState<UnitMedia[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [editing, setEditing] = useState(false);
  const [uploading, setUploading] = useState(false);

  const load = useCallback(async () => {
    if (!unitId || Number.isNaN(unitId)) {
      setError("Unit not found.");
      setLoading(false);
      return;
    }
    setError(null);
    try {
      const unitResponse = await api(`/api/Unit/${unitId}`, undefined, false);
      if (!unitResponse.ok) {
        setError("Unit not found.");
        setUnit(null);
        return;
      }
      const parsed = parseUnitRow(await unitResponse.json());
      if (!parsed) {
        setError("Unit not found.");
        setUnit(null);
        return;
      }
      setUnit(parsed);
      const [projectResponse, photos] = await Promise.all([
        parsed.projectId ? api(`/api/Project/${parsed.projectId}`, undefined, false) : Promise.resolve(null),
        getUnitMedia(unitId).catch(() => [] as UnitMedia[]),
      ]);
      if (projectResponse?.ok) {
        const project = parseProjectRow(await projectResponse.json());
        setProjectName(project?.projectName ?? "");
      }
      setMedia(photos);
    } catch {
      setError("This unit could not be loaded.");
    } finally {
      setLoading(false);
    }
  }, [toast, unitId]);

  useEffect(() => { setLoading(true); void load(); }, [load]);

  const reloadMedia = async () => {
    try { setMedia(await getUnitMedia(unitId)); }
    catch (caught) { toast.error(caught instanceof Error ? caught.message : "Photos could not be loaded."); }
  };

  if (loading && !unit) {
    return <div className={PAGE}><div className="h-16 animate-pulse rounded-card bg-track" /><div className="h-40 animate-pulse rounded-card bg-track" /></div>;
  }
  if (error || !unit) {
    return <div className={PAGE}><EmptyState icon={<IconBuilding size={26} />} title={error ?? "Unit not found"} action={<Button variant="outline" onClick={() => { setLoading(true); void load(); }}>Try again</Button>} /></div>;
  }

  const photos = toPhotos(media);
  const shown = unitStatus(unit.status);

  return (
    <div className={PAGE}>
      <PageHeader
        back={{ to: `/projects/${unit.projectId}`, label: projectName || "Project" }}
        title={`Unit ${unit.unitNumber}`}
        status={shown}
        subtitle={projectName || undefined}
        actions={canWrite ? <Button variant="outline" onClick={() => setEditing(true)}>Edit unit</Button> : undefined}
      />
      <div className="grid grid-cols-2 gap-2.5 md:grid-cols-4">
        <InfoCard label="Type" value={unit.unitType || "—"} highlight />
        <InfoCard label="Size" value={formatSqFt(unit.size)} />
        <InfoCard label="Floor" value={floorLabel(unit.floorNumber)} />
        <InfoCard label="Price" value={formatPkr(unit.price)} />
      </div>
      <Card title={`Photos (${photos.length})`}>
        {photos.length === 0 && <p className="m-0 mb-3 text-sm text-ink-muted">No photos for this unit yet</p>}
        <PhotoGallery
          photos={photos}
          canManage={canWrite}
          uploading={uploading}
          onUpload={(files) => {
            setUploading(true);
            void uploadUnitMediaBulk(unit.id, files)
              .then(async (saved) => {
                const notice = photoUploadNotice(files.length, saved.length);
                if (notice.success) toast.success(notice.success);
                if (notice.error) toast.error(notice.error);
                if (saved.length > 0) await reloadMedia();
              })
              .catch((caught: unknown) => toast.error(caught instanceof Error ? caught.message : "Photos could not be uploaded."))
              .finally(() => setUploading(false));
          }}
          onSetCover={(photo) => {
            void setUnitCoverMedia(unit.id, Number(photo.id))
              .then(async () => { toast.success("Cover photo updated"); await reloadMedia(); })
              .catch((caught: unknown) => toast.error(caught instanceof Error ? caught.message : "The cover photo could not be changed."));
          }}
          onDelete={(photo) => {
            void deleteUnitMedia(unit.id, Number(photo.id))
              .then(async () => { toast.success("Photo deleted"); await reloadMedia(); })
              .catch((caught: unknown) => toast.error(caught instanceof Error ? caught.message : "The photo could not be deleted."));
          }}
        />
      </Card>
      {editing && (
        <UnitDialog
          projectId={unit.projectId}
          unit={unit}
          onClose={() => setEditing(false)}
          onSaved={async (saved) => {
            setEditing(false);
            toast.success(unitSavedMessage(saved.unitNumber, false));
            await Promise.all([load(), reloadProjects()]);
          }}
        />
      )}
    </div>
  );
}

export function UnitDialog({ projectId, unit, onClose, onSaved }: {
  projectId: number;
  unit?: UnitFromApi | null;
  onClose: () => void;
  onSaved: (saved: { unitNumber: string }) => void | Promise<void>;
}) {
  const toast = useToast();
  const editing = unit ?? null;
  const [types, setTypes] = useState<string[]>([...UNIT_TYPES]);
  const [number, setNumber] = useState(editing?.unitNumber ?? "");
  const [type, setType] = useState(editing?.unitType ?? "");
  const [floor, setFloor] = useState(editing ? String(editing.floorNumber) : "");
  const [size, setSize] = useState(editing ? String(editing.size) : "");
  const [price, setPrice] = useState(editing ? String(editing.price) : "");
  const [numberError, setNumberError] = useState<string | undefined>();
  const [saving, setSaving] = useState(false);
  const draft = { unitNumber: number, unitType: type, floorNumber: floor, size, price };
  const ready = unitDraftReady(draft);
  const changed = editing ? unitDraftChanged(draft, editing) : true;
  const canSave = ready && changed && !saving;

  useEffect(() => {
    let current = true;
    void api("/api/Unit/types", undefined, false)
      .then(async (response) => {
        if (!response.ok || !current) return;
        const list = await response.json() as unknown;
        if (Array.isArray(list) && list.every((item) => typeof item === "string")) {
          const next = [...list];
          if (editing?.unitType && !next.includes(editing.unitType)) next.push(editing.unitType);
          setTypes(next);
        }
      })
      .catch(() => undefined);
    return () => { current = false; };
  }, [editing?.unitType]);

  const save = async () => {
    setSaving(true);
    const shared = {
      unitNumber: number.trim(),
      unitType: type,
      floorNumber: Number(floor),
      size: Number(size),
      price: Number(price),
    };
    const payload = editing ? { ...shared, status: editing.status } : { ...shared, projectId };
    try {
      const response = await api(editing ? `/api/Unit/${editing.id}` : "/api/Unit", {
        method: editing ? "PUT" : "POST",
        body: JSON.stringify(payload),
      });
      if (!response.ok) {
        const message = await readError(response, "The unit could not be saved.");
        if (fieldForServerMessage(message) === "unitNumber") setNumberError(message);
        toast.error(message);
        setSaving(false);
        return;
      }
      await onSaved({ unitNumber: shared.unitNumber });
    } catch {
      toast.error("The unit could not be saved.");
      setSaving(false);
    }
  };

  const typeOptions = types.map((value) => ({ value, label: value }));

  return (
    <Modal
      open
      onClose={onClose}
      title={editing ? `Edit unit ${editing.unitNumber}` : "Add unit"}
      size="md"
      phoneLayout="fullscreen"
      busy={saving}
      primaryAction={{ label: editing ? "Save changes" : "Add unit", onClick: () => void save(), disabled: !canSave, loading: saving }}
    >
      <div className="flex flex-col gap-4">
        <TextField label="Unit number" required value={number} error={numberError} onChange={(event) => { setNumber(event.target.value); setNumberError(undefined); }} />
        <Dropdown label="Type" required placeholder="Select" value={type} onChange={setType} options={typeOptions} />
        <Dropdown label="Floor" required placeholder="Select" value={floor} onChange={setFloor} options={floorChoices(editing?.floorNumber)} />
        <NumberField label="Size" required decimals={0} suffix="sq ft" value={size} onChange={setSize} />
        <NumberField label="Price" required decimals={0} prefix="Rs" value={price} onChange={setPrice} />
      </div>
    </Modal>
  );
}
