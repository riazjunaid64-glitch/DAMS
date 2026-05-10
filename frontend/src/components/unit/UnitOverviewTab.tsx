import { useState } from "react";
import { useNavigate } from "react-router-dom";
import type { User } from "../../App.tsx";
import Button from "../../lib/Button.tsx";
import Field from "../../lib/Field.tsx";
import { api } from "../../api/api.ts";

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

interface Props {
  unit: Unit;
  project: Project | null;
  user: User | null;
  onUnitUpdate: (unit: Unit) => void;
  coverImage?: string | null;
}

const unitStatusColors: Record<string, string> = {
  available: "text-emerald-400 bg-emerald-500/10 border-emerald-500/20",
  sold: "text-rose-400 bg-rose-500/10 border-rose-500/20",
  reserved: "text-amber-400 bg-amber-500/10 border-amber-500/20",
};

export default function UnitOverviewTab({ unit, project, user, onUnitUpdate, coverImage }: Props) {
  const navigate = useNavigate();
  const isAdmin = user?.role === "Admin";
  const [showEditForm, setShowEditForm] = useState(false);
  const [unitError, setUnitError] = useState<string | null>(null);
  const [unitForm, setUnitForm] = useState({
    unitNumber: unit.unitNumber,
    unitType: unit.unitType,
    floorNumber: unit.floorNumber,
    size: unit.size,
    price: unit.price,
    status: unit.status,
  });

  const submitUnitUpdate = async () => {
    setUnitError(null);
    if (!unitForm.unitNumber || !unitForm.unitType || unitForm.floorNumber <= 0) {
      setUnitError("Unit number, type and floor number are required.");
      return;
    }
    try {
      const res = await api(`/api/Unit/${unit.id}`, { method: "PUT", body: JSON.stringify(unitForm) });
      if (!res.ok) { setUnitError((await res.text()) || "Unable to update unit."); return; }
      setShowEditForm(false);
      const unitRes = await api(`/api/Unit/${unit.id}`, undefined, false);
      if (unitRes.ok) onUnitUpdate((await unitRes.json()) as Unit);
    } catch { setUnitError("Unable to update unit right now."); }
  };

  const statusKey = unit.status.toLowerCase();
  const statusColor = unitStatusColors[statusKey] ?? unitStatusColors.available;

  const infoItems = [
    { label: "Unit Number", value: unit.unitNumber, icon: <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M4 15s1-1 4-1 5 2 8 2 4-1 4-1V3s-1 1-4 1-5-2-8-2-4 1-4 1z"/><line x1="4" y1="22" x2="4" y2="15"/></svg> },
    { label: "Unit Type", value: unit.unitType, icon: <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><rect x="3" y="3" width="18" height="18" rx="2" ry="2"/></svg> },
    { label: "Floor", value: `${unit.floorNumber}`, icon: <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M3 3h18v18H3z"/><path d="M3 9h18"/><path d="M3 15h18"/></svg> },
    { label: "Size", value: `${unit.size.toFixed(1)} sqm`, icon: <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><rect x="3" y="3" width="18" height="18" rx="2" ry="2"/><path d="M3 9h18"/><path d="M9 21V9"/></svg> },
    { label: "Price", value: `$${unit.price.toLocaleString()}`, icon: <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><circle cx="12" cy="12" r="10"/><path d="M16 8h-6a2 2 0 100 4h4a2 2 0 110 4H8"/><path d="M12 18V6"/></svg> },
    { label: "Status", value: unit.status, icon: <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M22 11.08V12a10 10 0 11-5.93-9.14"/><polyline points="22 4 12 14.01 9 11.01"/></svg> },
  ];

  return (
    <div className="py-8 sm:py-10">
      <div className="mx-auto w-full max-w-7xl px-5 sm:px-8 lg:px-10">
        {/* Top Section: Cover + Key Details */}
        <div className="grid gap-6 lg:grid-cols-5 mb-10">
          {/* Cover Image */}
          <div className="lg:col-span-2">
            <div className="aspect-[4/3] rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] overflow-hidden">
              {coverImage ? (
                <img src={coverImage} alt={unit.unitNumber} className="h-full w-full object-cover" />
              ) : (
                <div className="flex h-full w-full items-center justify-center">
                  <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" className="text-[var(--text-muted)]" strokeLinecap="round"><path d="M3 9l9-7 9 7v11a2 2 0 01-2 2H5a2 2 0 01-2-2V9z"/><polyline points="9 22 9 12 15 12 15 22"/></svg>
                </div>
              )}
            </div>
          </div>

          {/* Key Details */}
          <div className="lg:col-span-3 space-y-5">
            <div className="flex items-center gap-3 flex-wrap">
              <span className="text-2xl font-bold text-[var(--text-heading)]">${unit.price.toLocaleString()}</span>
              <span className={`rounded-full border px-3 py-1 text-xs font-semibold ${statusColor}`}>{unit.status}</span>
            </div>

            <div className="grid grid-cols-3 gap-3">
              {[
                { label: "Type", value: unit.unitType },
                { label: "Floor", value: `${unit.floorNumber}` },
                { label: "Size", value: `${unit.size.toFixed(1)} sqm` },
              ].map((s) => (
                <div key={s.label} className="rounded-xl border border-[var(--border)] bg-[var(--surface-glass)] p-4 text-center">
                  <p className="text-sm font-semibold text-[var(--text-heading)]">{s.value}</p>
                  <p className="text-[11px] uppercase tracking-wider text-[var(--text-muted)] mt-1">{s.label}</p>
                </div>
              ))}
            </div>

            {/* Project Reference */}
            {project && (
              <div className="rounded-xl border border-[var(--border)] bg-[var(--surface-glass)] p-4 flex items-center justify-between">
                <div>
                  <p className="text-[11px] uppercase tracking-wider text-[var(--text-muted)] mb-1">Project</p>
                  <p className="text-sm font-semibold text-[var(--text-heading)]">{project.projectName}</p>
                  <p className="text-xs text-[var(--text-muted)]">{project.location}</p>
                </div>
                <Button variant="ghost" size="sm" onClick={() => navigate(`/projects/${project.id}`)}>
                  View Project
                </Button>
              </div>
            )}

            {isAdmin && (
              <Button size="sm" onClick={() => setShowEditForm((p) => !p)}>
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M11 4H4a2 2 0 00-2 2v14a2 2 0 002 2h14a2 2 0 002-2v-7"/><path d="M18.5 2.5a2.121 2.121 0 013 3L12 15l-4 1 1-4 9.5-9.5z"/></svg>
                {showEditForm ? "Cancel Edit" : "Edit Unit"}
              </Button>
            )}
          </div>
        </div>

        {/* Edit Form */}
        {showEditForm && isAdmin && (
          <div className="mb-10 animate-scale-in rounded-2xl border border-[var(--accent-glow-strong)] bg-[var(--accent-glow)] p-6 shadow-sm">
            <h4 className="mb-4 text-sm font-semibold text-[var(--text-heading)]">Edit Unit</h4>
            <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
              <Field label="Unit Number" value={unitForm.unitNumber} onChange={(e) => setUnitForm((p) => ({ ...p, unitNumber: e.target.value }))} placeholder="e.g. A-101" />
              <Field label="Unit Type" value={unitForm.unitType} onChange={(e) => setUnitForm((p) => ({ ...p, unitType: e.target.value }))} placeholder="shop, flat, office..." />
              <Field label="Floor Number" type="number" value={String(unitForm.floorNumber)} onChange={(e) => setUnitForm((p) => ({ ...p, floorNumber: Number(e.target.value) }))} placeholder="1" />
              <Field label="Size (sqm)" type="number" value={String(unitForm.size)} onChange={(e) => setUnitForm((p) => ({ ...p, size: Number(e.target.value) }))} placeholder="0" />
              <Field label="Price" type="number" value={String(unitForm.price)} onChange={(e) => setUnitForm((p) => ({ ...p, price: Number(e.target.value) }))} placeholder="0" />
              <div>
                <label className="text-xs font-medium text-[var(--text-muted)] mb-2 block">Status</label>
                <select value={unitForm.status} onChange={(e) => setUnitForm((p) => ({ ...p, status: e.target.value }))} className="w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-4 py-2.5 text-sm text-[var(--text-primary)] transition-all focus:border-[var(--accent)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-glow)]">
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
            {unitError && <p className="mt-3 text-xs text-rose-400 flex items-center gap-1.5"><svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>{unitError}</p>}
          </div>
        )}

        {/* Full Info Grid */}
        <h2 className="section-title">Unit Specifications</h2>
        <div className="info-grid">
          {infoItems.map((item) => (
            <div key={item.label} className="info-card">
              <div className="info-card__label">{item.icon}<span>{item.label}</span></div>
              <div className="info-card__value">{item.value}</div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
