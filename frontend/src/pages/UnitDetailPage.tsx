import { useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { api } from "../api/api.ts";
import { getUnitMedia, deleteUnitMedia, setUnitCoverMedia, uploadUnitMediaBulk } from "../api/media.ts";
import type { User } from "../App.tsx";
import type { UnitMedia, UploadMediaDto } from "../types/media.ts";
import Button from "../lib/Button.tsx";
import Container from "../lib/Container.tsx";
import Field from "../lib/Field.tsx";
import MediaUpload from "../components/MediaUpload.tsx";
import MediaGallery from "../components/MediaGallery.tsx";

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

type Props = {
  user: User | null;
};

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
  const [showMediaUpload, setShowMediaUpload] = useState(false);
  const [uploadingMedia, setUploadingMedia] = useState(false);
  const [showEditForm, setShowEditForm] = useState(false);
  const [unitForm, setUnitForm] = useState({
    unitNumber: "",
    unitType: "",
    floorNumber: 1,
    size: 0,
    price: 0,
    status: "available",
  });
  const [unitError, setUnitError] = useState<string | null>(null);

  const isAdmin = user?.role === "Admin";

  useEffect(() => {
    if (!unitId || Number.isNaN(unitId)) {
      setError("Invalid unit ID.");
      return;
    }

    const load = async () => {
      setLoading(true);
      setError(null);

      try {
        const unitRes = await api(`/api/Unit/${unitId}`, undefined, false);

        if (!unitRes.ok) {
          setError("Unit not found.");
          setUnit(null);
          return;
        }

        const unitData = (await unitRes.json()) as Unit;
        setUnit(unitData);

        // Load project info
        if (unitData.projectId) {
          const projRes = await api(`/api/Project/${unitData.projectId}`, undefined, false);
          if (projRes.ok) {
            setProject((await projRes.json()) as Project);
          }
        }

        setUnitForm({
          unitNumber: unitData.unitNumber,
          unitType: unitData.unitType,
          floorNumber: unitData.floorNumber,
          size: unitData.size,
          price: unitData.price,
          status: unitData.status,
        });
      } catch {
        setError("Unable to load unit details right now.");
      } finally {
        setLoading(false);
      }
    };

    load();
  }, [unitId]);

  useEffect(() => {
    if (!unitId || Number.isNaN(unitId)) return;

    const loadMedia = async () => {
      setMediaLoading(true);
      try {
        const mediaData = await getUnitMedia(unitId);
        setMedia(mediaData);
      } catch {
        console.error("Failed to load media");
      } finally {
        setMediaLoading(false);
      }
    };

    loadMedia();
  }, [unitId]);

  const handleMediaUpload = async (files: File[], uploadDto?: UploadMediaDto) => {
    setUploadingMedia(true);
    try {
      const uploadedMedia = await uploadUnitMediaBulk(unitId, files, uploadDto);
      setMedia((prev) => [...prev, ...uploadedMedia]);
      setShowMediaUpload(false);
    } catch (error) {
      console.error("Upload failed:", error);
      alert("Failed to upload media. Please try again.");
    } finally {
      setUploadingMedia(false);
    }
  };

  const handleMediaDelete = async (mediaId: number) => {
    try {
      await deleteUnitMedia(unitId, mediaId);
      setMedia((prev) => prev.filter((m) => m.id !== mediaId));
    } catch (error) {
      console.error("Delete failed:", error);
      alert("Failed to delete media. Please try again.");
    }
  };

  const handleSetCover = async (mediaId: number) => {
    try {
      await setUnitCoverMedia(unitId, mediaId);
      setMedia((prev) => prev.map((m) => ({ ...m, isCover: m.id === mediaId })));
    } catch (error) {
      console.error("Set cover failed:", error);
      alert("Failed to set cover. Please try again.");
    }
  };

  const submitUnitUpdate = async () => {
    setUnitError(null);
    if (!unitForm.unitNumber || !unitForm.unitType || unitForm.floorNumber <= 0) {
      setUnitError("Unit number, type and floor number are required.");
      return;
    }
    try {
      const res = await api(`/api/Unit/${unitId}`, {
        method: "PUT",
        body: JSON.stringify(unitForm),
      });
      if (!res.ok) {
        const text = await res.text();
        setUnitError(text || "Unable to update unit.");
        return;
      }
      setShowEditForm(false);
      const unitRes = await api(`/api/Unit/${unitId}`, undefined, false);
      if (unitRes.ok) setUnit((await unitRes.json()) as Unit);
    } catch {
      setUnitError("Unable to update unit right now.");
    }
  };

  return (
    <>
      {/* ─── Unit Header ─── */}
      <div className="relative overflow-hidden border-b border-white/[0.04]">
        <div className="absolute inset-0 mesh-gradient-subtle" />
        <Container className="relative py-12 sm:py-16">
          {/* Breadcrumb */}
          <div className="mb-6 flex items-center gap-2 text-sm text-[var(--text-muted)]">
            <button onClick={() => navigate("/projects")} className="hover:text-[var(--text-primary)] transition-colors">
              Projects
            </button>
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
              <polyline points="9 18 15 12 9 6"/>
            </svg>
            {project && (
              <>
                <button onClick={() => navigate(`/projects/${project.id}`)} className="hover:text-[var(--text-primary)] transition-colors">
                  {project.projectName}
                </button>
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                  <polyline points="9 18 15 12 9 6"/>
                </svg>
              </>
            )}
            <span className="text-[var(--text-secondary)]">{unit?.unitNumber ?? "Loading..."}</span>
          </div>

          <div className="flex flex-col gap-6 lg:flex-row lg:items-start lg:justify-between">
            <div className="space-y-3">
              <div className="flex flex-wrap items-center gap-3">
                <h1 className="text-3xl font-bold text-[var(--text-heading)] sm:text-4xl">
                  {unit ? unit.unitNumber : "Loading..."}
                </h1>
                {unit && (
                  <span className={`rounded-full border px-2.5 py-0.5 text-[11px] font-semibold ${unitStatusColors[unit.status.toLowerCase()] ?? unitStatusColors.available}`}>
                    {unit.status}
                  </span>
                )}
              </div>
              <p className="max-w-2xl text-sm leading-relaxed text-[var(--text-secondary)]">
                {unit ? `${unit.unitType} on Floor ${unit.floorNumber}` : "Loading unit details..."}
              </p>
            </div>

            <div className="flex shrink-0 gap-2">
              <Button variant="outline" size="sm" onClick={() => navigate(-1)}>
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                  <polyline points="15 18 9 12 15 6"/>
                </svg>
                Back
              </Button>
              {isAdmin && unitId > 0 && (
                <>
                  <Button size="sm" onClick={() => setShowEditForm((prev) => !prev)}>
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                      <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7" />
                      <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z" />
                    </svg>
                    {showEditForm ? "Cancel" : "Edit Unit"}
                  </Button>
                  <Button size="sm" variant="outline" onClick={() => setShowMediaUpload((prev) => !prev)}>
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                      <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
                      <polyline points="17 8 12 3 7 8" />
                      <line x1="12" y1="3" x2="12" y2="15" />
                    </svg>
                    {showMediaUpload ? "Cancel" : "Upload Media"}
                  </Button>
                </>
              )}
            </div>
          </div>

          {/* Unit Stats */}
          {unit && (
            <div className="mt-8 grid grid-cols-2 gap-3 sm:grid-cols-4">
              {[
                {
                  label: "Type",
                  value: unit.unitType,
                  icon: (
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                      <rect x="3" y="3" width="18" height="18" rx="2" ry="2" />
                    </svg>
                  ),
                },
                {
                  label: "Floor",
                  value: unit.floorNumber,
                  icon: (
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                      <path d="M3 3h18v18H3z" />
                      <path d="M3 9h18" />
                      <path d="M3 15h18" />
                    </svg>
                  ),
                },
                {
                  label: "Size",
                  value: `${unit.size.toFixed(1)} sqm`,
                  icon: (
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                      <rect x="3" y="3" width="18" height="18" rx="2" ry="2" />
                      <path d="M3 9h18" />
                      <path d="M9 21V9" />
                    </svg>
                  ),
                },
                {
                  label: "Price",
                  value: `$${unit.price.toLocaleString()}`,
                  icon: (
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                      <circle cx="12" cy="12" r="10" />
                      <path d="M16 8h-6a2 2 0 1 0 0 4h4a2 2 0 1 1 0 4H8" />
                      <path d="M12 18V6" />
                    </svg>
                  ),
                },
              ].map((stat) => (
                <div
                  key={stat.label}
                  className="rounded-xl border border-[var(--border)] bg-[var(--surface-glass)] p-4"
                >
                  <div className="flex items-center gap-2 text-[var(--text-muted)]">
                    {stat.icon}
                    <span className="text-[11px] uppercase tracking-wider">{stat.label}</span>
                  </div>
                  <p className="mt-2 text-sm font-semibold text-[var(--text-heading)]">{stat.value}</p>
                </div>
              ))}
            </div>
          )}
        </Container>
      </div>

      {/* ─── Content ─── */}
      <div className="py-10 sm:py-14">
        <Container>
          {/* Media Upload Form */}
          {showMediaUpload && isAdmin && (
            <div className="mb-8 animate-scale-in rounded-2xl border border-[var(--accent-glow-strong)] bg-[var(--accent-glow)] p-6 shadow-sm">
              <h4 className="mb-4 text-sm font-semibold text-[var(--text-heading)]">Upload Unit Media</h4>
              <MediaUpload
                onUpload={handleMediaUpload}
                multiple={true}
                uploading={uploadingMedia}
              />
            </div>
          )}

          {/* Unit Media Gallery */}
          {!loading && !error && (
            <div className="mb-12">
              <div className="mb-6 flex items-center justify-between">
                <h2 className="text-xl font-semibold text-[var(--text-heading)]">Unit Gallery</h2>
                <span className="text-sm text-[var(--text-muted)]">
                  {media.length} media file{media.length !== 1 ? "s" : ""}
                </span>
              </div>
              <MediaGallery
                media={media}
                onDelete={isAdmin ? handleMediaDelete : undefined}
                onSetCover={isAdmin ? handleSetCover : undefined}
                isAdmin={isAdmin}
                loading={mediaLoading}
              />
            </div>
          )}

          {/* Edit Unit Form */}
          {showEditForm && isAdmin && (
            <div className="animate-scale-in rounded-2xl border border-[var(--accent-glow-strong)] bg-[var(--accent-glow)] p-6 shadow-sm">
              <h4 className="mb-4 text-sm font-semibold text-[var(--text-heading)]">Edit Unit</h4>
              <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
                <Field
                  label="Unit Number"
                  value={unitForm.unitNumber}
                  onChange={(e) => setUnitForm((prev) => ({ ...prev, unitNumber: e.target.value }))}
                  placeholder="e.g. A-101"
                />
                <Field
                  label="Unit Type"
                  value={unitForm.unitType}
                  onChange={(e) => setUnitForm((prev) => ({ ...prev, unitType: e.target.value }))}
                  placeholder="shop, flat, office..."
                />
                <Field
                  label="Floor Number"
                  type="number"
                  value={String(unitForm.floorNumber)}
                  onChange={(e) => setUnitForm((prev) => ({ ...prev, floorNumber: Number(e.target.value) }))}
                  placeholder="1"
                />
                <Field
                  label="Size (sqm)"
                  type="number"
                  value={String(unitForm.size)}
                  onChange={(e) => setUnitForm((prev) => ({ ...prev, size: Number(e.target.value) }))}
                  placeholder="0"
                />
                <Field
                  label="Price"
                  type="number"
                  value={String(unitForm.price)}
                  onChange={(e) => setUnitForm((prev) => ({ ...prev, price: Number(e.target.value) }))}
                  placeholder="0"
                />
                <div>
                  <label className="text-xs font-medium text-[var(--text-muted)] mb-2 block">Status</label>
                  <select
                    value={unitForm.status}
                    onChange={(e) => setUnitForm((prev) => ({ ...prev, status: e.target.value }))}
                    className="w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-4 py-2.5 text-sm text-[var(--text-primary)] transition-all focus:border-[var(--accent)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-glow)]"
                  >
                    <option value="available">Available</option>
                    <option value="sold">Sold</option>
                    <option value="reserved">Reserved</option>
                  </select>
                </div>
              </div>
              <div className="mt-4 flex items-center gap-3">
                <Button onClick={submitUnitUpdate} size="sm">Save Changes</Button>
                <Button variant="ghost" size="sm" onClick={() => setShowEditForm(false)}>Cancel</Button>
              </div>
              {unitError && (
                <p className="mt-3 text-xs text-rose-400 flex items-center gap-1.5">
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                    <circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/>
                  </svg>
                  {unitError}
                </p>
              )}
            </div>
          )}

          {/* Loading */}
          {loading && (
            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
              {[...Array(6)].map((_, i) => (
                <div key={i} className="rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] p-5">
                  <div className="skeleton mb-3 h-5 w-1/2" />
                  <div className="skeleton mb-2 h-3 w-3/4" />
                  <div className="skeleton h-3 w-1/2" />
                </div>
              ))}
            </div>
          )}

          {/* Error */}
          {error && (
            <div className="rounded-2xl border border-rose-500/20 bg-rose-500/[0.06] px-5 py-4 text-sm text-rose-300 flex items-center gap-3">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                <circle cx="12" cy="12" r="10"/><line x1="15" y1="9" x2="9" y2="15"/><line x1="9" y1="9" x2="15" y2="15"/>
              </svg>
              {error}
            </div>
          )}
        </Container>
      </div>
    </>
  );
}
