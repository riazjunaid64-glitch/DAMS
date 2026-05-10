import { useMemo, useState, useEffect } from "react";
import { useNavigate } from "react-router-dom";
import type { User } from "../../App.tsx";
import Button from "../../lib/Button.tsx";
import Field from "../../lib/Field.tsx";
import Pagination from "../../lib/Pagination.tsx";
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

interface Props {
  units: Unit[];
  projectId: number;
  user: User | null;
  onUnitsChange: (units: Unit[]) => void;
}

const unitStatusColors: Record<string, string> = {
  available: "text-emerald-400 bg-emerald-500/10 border-emerald-500/20",
  sold: "text-rose-400 bg-rose-500/10 border-rose-500/20",
  reserved: "text-amber-400 bg-amber-500/10 border-amber-500/20",
};

export default function ProjectUnitsTab({ units, projectId, user, onUnitsChange }: Props) {
  const navigate = useNavigate();
  const isAdmin = user?.role === "Admin";

  const [search, setSearch] = useState("");
  const [typeFilter, setTypeFilter] = useState("");
  const [statusFilter, setStatusFilter] = useState("");
  const [page, setPage] = useState(1);
  const [showUnitForm, setShowUnitForm] = useState(false);
  const [unitError, setUnitError] = useState<string | null>(null);
  const [unitForm, setUnitForm] = useState({ unitNumber: "", unitType: "", floorNumber: 1, size: 0, price: 0 });

  const itemsPerPage = 12;

  const availableTypes = useMemo(() => {
    return ["", ...Array.from(new Set(units.map((u) => u.unitType))).sort()];
  }, [units]);

  const filteredUnits = useMemo(() => {
    const term = search.trim().toLowerCase();
    return units.filter((unit) => {
      const matchesSearch = unit.unitNumber.toLowerCase().includes(term) || unit.unitType.toLowerCase().includes(term) || unit.status.toLowerCase().includes(term);
      const matchesType = typeFilter ? unit.unitType === typeFilter : true;
      const matchesStatus = statusFilter ? unit.status.toLowerCase() === statusFilter : true;
      return matchesSearch && matchesType && matchesStatus;
    });
  }, [search, typeFilter, statusFilter, units]);

  const totalPages = Math.max(1, Math.ceil(filteredUnits.length / itemsPerPage));
  const paginatedUnits = filteredUnits.slice((page - 1) * itemsPerPage, page * itemsPerPage);

  useEffect(() => { if (page > totalPages) setPage(1); }, [page, totalPages]);

  const submitUnit = async () => {
    setUnitError(null);
    if (!unitForm.unitNumber || !unitForm.unitType || unitForm.floorNumber <= 0) {
      setUnitError("Unit number, type and floor number are required.");
      return;
    }
    try {
      const res = await api("/api/Unit", {
        method: "POST",
        body: JSON.stringify({ projectId, ...unitForm }),
      });
      if (!res.ok) { setUnitError((await res.text()) || "Unable to create unit."); return; }
      setUnitForm({ unitNumber: "", unitType: "", floorNumber: 1, size: 0, price: 0 });
      setShowUnitForm(false);
      setPage(1);
      const unitRes = await api(`/api/Unit/project/${projectId}`, undefined, false);
      if (unitRes.ok) onUnitsChange((await unitRes.json()) as Unit[]);
    } catch { setUnitError("Unable to create unit right now."); }
  };

  return (
    <div className="py-8 sm:py-10">
      <div className="mx-auto w-full max-w-7xl px-5 sm:px-8 lg:px-10">
        {/* Header */}
        <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between mb-6">
          <div>
            <h2 className="section-title" style={{ marginBottom: 0 }}>Units</h2>
            <p className="text-sm text-[var(--text-muted)] mt-1">{filteredUnits.length} unit{filteredUnits.length !== 1 ? "s" : ""} found</p>
          </div>
          {isAdmin && (
            <Button size="sm" onClick={() => setShowUnitForm((p) => !p)}>
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
              {showUnitForm ? "Cancel" : "Add Unit"}
            </Button>
          )}
        </div>

        {/* Create Unit Form */}
        {showUnitForm && isAdmin && (
          <div className="mb-8 animate-scale-in rounded-2xl border border-[var(--accent-glow-strong)] bg-[var(--accent-glow)] p-6 shadow-sm">
            <h4 className="mb-4 text-sm font-semibold text-[var(--text-heading)]">Create New Unit</h4>
            <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
              <Field label="Unit Number" value={unitForm.unitNumber} onChange={(e) => setUnitForm((p) => ({ ...p, unitNumber: e.target.value }))} placeholder="e.g. A-101" />
              <Field label="Unit Type" value={unitForm.unitType} onChange={(e) => setUnitForm((p) => ({ ...p, unitType: e.target.value }))} placeholder="shop, flat, office..." />
              <Field label="Floor Number" type="number" value={String(unitForm.floorNumber)} onChange={(e) => setUnitForm((p) => ({ ...p, floorNumber: Number(e.target.value) }))} placeholder="1" />
              <Field label="Size (sqm)" type="number" value={String(unitForm.size)} onChange={(e) => setUnitForm((p) => ({ ...p, size: Number(e.target.value) }))} placeholder="0" />
              <Field label="Price" type="number" value={String(unitForm.price)} onChange={(e) => setUnitForm((p) => ({ ...p, price: Number(e.target.value) }))} placeholder="0" />
            </div>
            <div className="mt-4 flex items-center gap-3">
              <Button onClick={submitUnit} size="sm">Save Unit</Button>
              <Button variant="ghost" size="sm" onClick={() => setShowUnitForm(false)}>Cancel</Button>
            </div>
            {unitError && <p className="mt-3 text-xs text-rose-400 flex items-center gap-1.5"><svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>{unitError}</p>}
          </div>
        )}

        {/* Filters */}
        <div className="mb-6 flex flex-col gap-3 sm:flex-row sm:items-center">
          <div className="relative flex-1 max-w-sm">
            <svg className="absolute left-3.5 top-1/2 -translate-y-1/2 text-[var(--text-muted)]" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/></svg>
            <input value={search} onChange={(e) => { setSearch(e.target.value); setPage(1); }} className="w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] py-2.5 pl-10 pr-4 text-sm text-[var(--text-primary)] placeholder:text-[var(--text-muted)] transition-all focus:border-[var(--accent)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-glow)]" placeholder="Search units..." />
          </div>
          <select value={typeFilter} onChange={(e) => { setTypeFilter(e.target.value); setPage(1); }} className="rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-4 py-2.5 text-sm text-[var(--text-primary)] transition-all focus:border-[var(--accent)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-glow)]">
            <option value="">All types</option>
            {availableTypes.map((t) => t ? <option key={t} value={t}>{t}</option> : null)}
          </select>
          <select value={statusFilter} onChange={(e) => { setStatusFilter(e.target.value); setPage(1); }} className="rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-4 py-2.5 text-sm text-[var(--text-primary)] transition-all focus:border-[var(--accent)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-glow)]">
            <option value="">All statuses</option>
            <option value="available">Available</option>
            <option value="sold">Sold</option>
            <option value="reserved">Reserved</option>
          </select>
        </div>

        {/* Unit Grid */}
        {paginatedUnits.length > 0 ? (
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
            {paginatedUnits.map((unit, i) => {
              const statusKey = unit.status.toLowerCase();
              const statusColor = unitStatusColors[statusKey] ?? "text-[var(--text-secondary)] bg-[var(--surface-glass)] border-[var(--border)]";
              return (
                <div key={unit.id} onClick={() => navigate(`/units/${unit.id}`)} className="glass-card group p-5 animate-fade-in-up cursor-pointer transition-all duration-200 hover:border-[var(--accent)] hover:shadow-lg hover:shadow-[var(--accent-glow)]" style={{ animationDelay: `${i * 40}ms` }}>
                  <div className="flex items-center justify-between">
                    <h3 className="text-base font-semibold text-[var(--text-heading)]">{unit.unitNumber}</h3>
                    <span className={`rounded-full border px-2.5 py-0.5 text-[11px] font-semibold ${statusColor}`}>{unit.status}</span>
                  </div>
                  <div className="mt-3 inline-flex items-center gap-1.5 rounded-md bg-[var(--accent-glow)] px-2 py-0.5">
                    <span className="text-[11px] font-medium text-[var(--accent)] uppercase tracking-wider">{unit.unitType}</span>
                  </div>
                  <div className="mt-4 space-y-2">
                    <div className="flex items-center justify-between text-sm"><span className="text-[var(--text-muted)]">Floor</span><span className="font-medium text-[var(--text-secondary)]">{unit.floorNumber}</span></div>
                    <div className="flex items-center justify-between text-sm"><span className="text-[var(--text-muted)]">Size</span><span className="font-medium text-[var(--text-secondary)]">{unit.size.toFixed(1)} sqm</span></div>
                    <div className="flex items-center justify-between text-sm"><span className="text-[var(--text-muted)]">Price</span><span className="font-semibold text-[var(--text-heading)]">${unit.price.toLocaleString()}</span></div>
                  </div>
                </div>
              );
            })}
          </div>
        ) : (
          <div className="flex flex-col items-center justify-center py-20 text-center">
            <div className="mb-4 flex h-14 w-14 items-center justify-center rounded-2xl bg-[var(--surface-glass)] border border-[var(--border)]">
              <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" className="text-[var(--text-muted)]" strokeLinecap="round"><path d="M3 9l9-7 9 7v11a2 2 0 01-2 2H5a2 2 0 01-2-2V9z"/><polyline points="9 22 9 12 15 12 15 22"/></svg>
            </div>
            <h3 className="text-base font-semibold text-[var(--text-heading)]">No units found</h3>
            <p className="mt-1 text-sm text-[var(--text-muted)]">{search || typeFilter || statusFilter ? "Try adjusting your search or filters." : "Add units to this project to get started."}</p>
          </div>
        )}

        <Pagination currentPage={page} totalPages={totalPages} onPageChange={setPage} />
      </div>
    </div>
  );
}
