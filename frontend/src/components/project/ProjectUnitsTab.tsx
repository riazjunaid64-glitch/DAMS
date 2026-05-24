import { useDeferredValue, useEffect, useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import type { User } from "../../App.tsx";
import Button from "../../lib/Button.tsx";
import Field from "../../lib/Field.tsx";
import Pagination from "../../lib/Pagination.tsx";
import { api } from "../../api/api.ts";
import { parseUnitsPayload } from "../../utils/parseUnit.ts";

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
  statusFilter: string;
  onStatusFilterChange: (status: string) => void;
}

const unitStatusStyles: Record<string, string> = {
  available: "border-emerald-500/20 bg-emerald-500/10 text-emerald-500",
  sold: "border-rose-500/20 bg-rose-500/10 text-rose-500",
  reserved: "border-amber-500/20 bg-amber-500/10 text-amber-500",
};

const unitImages = [
  "https://images.unsplash.com/photo-1600607687939-ce8a6c25118c?auto=format&fit=crop&w=900&q=80",
  "https://images.unsplash.com/photo-1600585154340-be6161a56a0c?auto=format&fit=crop&w=900&q=80",
  "https://images.unsplash.com/photo-1600566753190-17f0baa2a6c3?auto=format&fit=crop&w=900&q=80",
  "https://images.unsplash.com/photo-1600047509807-ba8f99d2cdde?auto=format&fit=crop&w=900&q=80",
  "https://images.unsplash.com/photo-1600607687920-4e2a09cf159d?auto=format&fit=crop&w=900&q=80",
  "https://images.unsplash.com/photo-1600566753086-00f18fb6b3ea?auto=format&fit=crop&w=900&q=80",
  "https://images.unsplash.com/photo-1605276374104-dee2a0ed3cd6?auto=format&fit=crop&w=900&q=80",
  "https://images.unsplash.com/photo-1600607688969-a5bfcd646154?auto=format&fit=crop&w=900&q=80",
];

const iconClass = "h-5 w-5";

const icons = {
  search: (
    <svg className="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
      <circle cx="11" cy="11" r="8" />
      <path d="m21 21-4.35-4.35" />
    </svg>
  ),
  plus: (
    <svg className="h-4.5 w-4.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round">
      <path d="M12 5v14M5 12h14" />
    </svg>
  ),
  building: (
    <svg className={iconClass} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M4 22V3h12v19" />
      <path d="M16 8h4v14" />
      <path d="M8 7h4M8 11h4M8 15h4M8 19h4" />
    </svg>
  ),
  available: (
    <svg className={iconClass} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M4 4h16v16H4z" />
      <path d="m8 12 2.5 2.5L16 9" />
    </svg>
  ),
  reserved: (
    <svg className={iconClass} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M6 3h12v18l-6-3-6 3V3Z" />
      <path d="M9 9h6" />
    </svg>
  ),
  sold: (
    <svg className={iconClass} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <circle cx="12" cy="12" r="9" />
      <path d="M15 9h-4a2 2 0 0 0 0 4h2a2 2 0 0 1 0 4H9" />
      <path d="M12 7v10" />
    </svg>
  ),
  floor: (
    <svg className="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M4 21V5a2 2 0 0 1 2-2h9v18" />
      <path d="M15 8h3a2 2 0 0 1 2 2v11" />
      <path d="M8 7h3M8 11h3M8 15h3" />
    </svg>
  ),
  size: (
    <svg className="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M4 8V4h4M20 8V4h-4M4 16v4h4M20 16v4h-4" />
      <path d="M4 4l6 6M20 4l-6 6M4 20l6-6M20 20l-6-6" />
    </svg>
  ),
  filter: (
    <svg className="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M22 3H2l8 9.46V19l4 2v-8.54L22 3Z" />
    </svg>
  ),
  heart: (
    <svg className="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M20.8 4.6a5.5 5.5 0 0 0-7.8 0L12 5.6l-1-1a5.5 5.5 0 0 0-7.8 7.8l1 1L12 21l7.8-7.6 1-1a5.5 5.5 0 0 0 0-7.8Z" />
    </svg>
  ),
  arrow: (
    <svg className="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round">
      <path d="m9 18 6-6-6-6" />
    </svg>
  ),
};

const formatCurrency = (value: number) =>
  new Intl.NumberFormat("en-US", { style: "currency", currency: "USD", maximumFractionDigits: 0 }).format(value || 0);

const getPercent = (value: number, total: number) => {
  if (!total) return "0% of total";
  return `${((value / total) * 100).toFixed(1).replace(".0", "")}% of total`;
};

export default function ProjectUnitsTab({ units, projectId, user, onUnitsChange, statusFilter, onStatusFilterChange }: Props) {
  const navigate = useNavigate();
  const isAdmin = user?.role === "Admin";

  const [search, setSearch] = useState("");
  const [typeFilter, setTypeFilter] = useState("");
  const [floorFilter, setFloorFilter] = useState("");
  const [sortBy, setSortBy] = useState("newest");
  const [page, setPage] = useState(1);
  const [showUnitForm, setShowUnitForm] = useState(false);
  const [unitError, setUnitError] = useState<string | null>(null);
  const [unitForm, setUnitForm] = useState({ unitNumber: "", unitType: "", floorNumber: 1, size: 0, price: 0 });
  const deferredSearch = useDeferredValue(search);

  const itemsPerPage = 12;

  const unitStats = useMemo(() => {
    const stats = { total: units.length, available: 0, reserved: 0, sold: 0 };
    units.forEach((unit) => {
      const status = unit.status.toLowerCase();
      if (status === "available") stats.available++;
      if (status === "reserved") stats.reserved++;
      if (status === "sold") stats.sold++;
    });
    return stats;
  }, [units]);

  const availableTypes = useMemo(() => ["", ...Array.from(new Set(units.map((u) => u.unitType))).sort()], [units]);
  const availableFloors = useMemo(() => ["", ...Array.from(new Set(units.map((u) => u.floorNumber))).sort((a, b) => a - b).map(String)], [units]);

  const filteredUnits = useMemo(() => {
    const term = deferredSearch.trim().toLowerCase();
    const result = units.filter((unit) => {
      const matchesSearch = unit.unitNumber.toLowerCase().includes(term) || unit.unitType.toLowerCase().includes(term) || unit.status.toLowerCase().includes(term);
      const matchesType = typeFilter ? unit.unitType === typeFilter : true;
      const matchesStatus = statusFilter ? unit.status.toLowerCase() === statusFilter : true;
      const matchesFloor = floorFilter ? String(unit.floorNumber) === floorFilter : true;
      return matchesSearch && matchesType && matchesStatus && matchesFloor;
    });

    return [...result].sort((a, b) => {
      if (sortBy === "price-high") return b.price - a.price;
      if (sortBy === "price-low") return a.price - b.price;
      if (sortBy === "size-high") return b.size - a.size;
      if (sortBy === "floor") return a.floorNumber - b.floorNumber;
      return b.id - a.id;
    });
  }, [deferredSearch, floorFilter, sortBy, statusFilter, typeFilter, units]);

  const totalPages = Math.max(1, Math.ceil(filteredUnits.length / itemsPerPage));
  const paginatedUnits = filteredUnits.slice((page - 1) * itemsPerPage, page * itemsPerPage);
  const rangeStart = filteredUnits.length ? (page - 1) * itemsPerPage + 1 : 0;
  const rangeEnd = Math.min(page * itemsPerPage, filteredUnits.length);

  useEffect(() => { if (page > totalPages) setPage(1); }, [page, totalPages]);

  const selectStatus = (status: string) => {
    onStatusFilterChange(status);
    setPage(1);
  };

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
      if (unitRes.ok) onUnitsChange(parseUnitsPayload(await unitRes.json()));
    } catch { setUnitError("Unable to create unit right now."); }
  };

  const statCards = [
    { label: "Total Units", value: unitStats.total, subtext: "Inventory", status: "", icon: icons.building, tone: "text-[var(--accent)] bg-[var(--accent-glow)]" },
    { label: "Available", value: unitStats.available, subtext: getPercent(unitStats.available, unitStats.total), status: "available", icon: icons.available, tone: "text-[var(--accent-emerald)] bg-[var(--accent-emerald-glow)]" },
    { label: "Reserved", value: unitStats.reserved, subtext: getPercent(unitStats.reserved, unitStats.total), status: "reserved", icon: icons.reserved, tone: "text-[var(--accent-warm)] bg-[var(--accent-warm-glow)]" },
    { label: "Sold", value: unitStats.sold, subtext: getPercent(unitStats.sold, unitStats.total), status: "sold", icon: icons.sold, tone: "text-[var(--accent-rose)] bg-[var(--accent-rose-glow)]" },
  ];

  return (
    <div className="py-4 sm:py-5">
      <div className="w-full space-y-5 px-4 sm:px-5 lg:px-6">
        <section className="rounded-2xl border border-[var(--border)] bg-[var(--glass-bg)] p-4 shadow-[var(--shadow-sm)] animate-fade-in-up sm:p-5 lg:p-6">
          <div className="flex flex-col gap-5 lg:flex-row lg:items-center lg:justify-between">
            <div className="grid flex-1 gap-3 sm:grid-cols-2 xl:grid-cols-4">
              {statCards.map((card) => {
                const isActive = statusFilter === card.status;
                return (
                  <button
                    key={card.label}
                    type="button"
                    onClick={() => selectStatus(card.status)}
                    className={`focus-ring rounded-xl border bg-[var(--bg-card)] p-4 text-left shadow-[var(--shadow-sm)] transition hover:-translate-y-0.5 hover:border-[var(--border-hover)] hover:shadow-[var(--shadow-md)] ${
                      isActive ? "border-[var(--accent)] shadow-[var(--shadow-glow)]" : "border-[var(--border)]"
                    }`}
                    aria-pressed={isActive}
                  >
                    <div className="flex items-center gap-4">
                      <div className={`grid h-12 w-12 shrink-0 place-items-center rounded-xl ${card.tone}`}>{card.icon}</div>
                      <div>
                        <p className="text-3xl font-bold leading-none text-[var(--text-heading)]">{card.value}</p>
                        <p className="mt-1 text-sm font-semibold text-[var(--text-secondary)]">{card.label}</p>
                        <p className="mt-1 text-xs font-semibold text-[var(--text-muted)]">{card.subtext}</p>
                      </div>
                    </div>
                  </button>
                );
              })}
            </div>

            {isAdmin && (
              <Button size="md" onClick={() => setShowUnitForm((p) => !p)} className="h-12 shrink-0 rounded-xl px-5">
                {icons.plus}
                {showUnitForm ? "Cancel" : "Add Unit"}
              </Button>
            )}
          </div>
        </section>

        {showUnitForm && isAdmin && (
          <section className="animate-scale-in rounded-2xl border border-[var(--accent-glow-strong)] bg-[var(--accent-glow)] p-5 shadow-[var(--shadow-sm)] sm:p-6">
            <div className="mb-5">
              <h3 className="text-lg font-bold text-[var(--text-heading)]">Create New Unit</h3>
              <p className="mt-1 text-sm text-[var(--text-secondary)]">Add an inventory record for this project.</p>
            </div>
            <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-5">
              <Field label="Unit Number" value={unitForm.unitNumber} onChange={(e) => setUnitForm((p) => ({ ...p, unitNumber: e.target.value }))} placeholder="e.g. A-101" />
              <Field label="Unit Type" value={unitForm.unitType} onChange={(e) => setUnitForm((p) => ({ ...p, unitType: e.target.value }))} placeholder="shop, flat, office..." />
              <Field label="Floor Number" type="number" value={String(unitForm.floorNumber)} onChange={(e) => setUnitForm((p) => ({ ...p, floorNumber: Number(e.target.value) }))} placeholder="1" />
              <Field label="Size (sqm)" type="number" value={String(unitForm.size)} onChange={(e) => setUnitForm((p) => ({ ...p, size: Number(e.target.value) }))} placeholder="0" />
              <Field label="Price" type="number" value={String(unitForm.price)} onChange={(e) => setUnitForm((p) => ({ ...p, price: Number(e.target.value) }))} placeholder="0" />
            </div>
            <div className="mt-5 flex flex-wrap items-center gap-3">
              <Button onClick={submitUnit} size="sm">Save Unit</Button>
              <Button variant="ghost" size="sm" onClick={() => setShowUnitForm(false)}>Cancel</Button>
            </div>
            {unitError && <p className="mt-3 flex items-center gap-1.5 text-xs font-semibold text-rose-400">{unitError}</p>}
          </section>
        )}

        <section className="rounded-2xl border border-[var(--border)] bg-[var(--glass-bg)] p-4 shadow-[var(--shadow-sm)] animate-fade-in-up-delay-1 sm:p-5">
          <div className="grid gap-3 xl:grid-cols-[minmax(240px,1.35fr)_repeat(4,minmax(150px,0.55fr))_auto]">
            <label className="relative block">
              <span className="absolute left-4 top-1/2 -translate-y-1/2 text-[var(--text-muted)]">{icons.search}</span>
              <input
                value={search}
                onChange={(e) => { setSearch(e.target.value); setPage(1); }}
                className="h-12 w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] py-3 pl-12 pr-4 text-sm font-semibold text-[var(--text-primary)] placeholder:text-[var(--text-muted)] transition-all hover:border-[var(--border-hover)] focus:border-[var(--accent)] focus:bg-[var(--input-bg-focus)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-glow)]"
                placeholder="Search units by number, type, status..."
              />
            </label>

            <select value={typeFilter} onChange={(e) => { setTypeFilter(e.target.value); setPage(1); }} className="h-12 rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-4 text-sm font-semibold text-[var(--text-primary)] transition-all hover:border-[var(--border-hover)] focus:border-[var(--accent)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-glow)]">
              <option value="">All Types</option>
              {availableTypes.map((t) => t ? <option key={t} value={t}>{t}</option> : null)}
            </select>

            <select value={statusFilter} onChange={(e) => { selectStatus(e.target.value); }} className="h-12 rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-4 text-sm font-semibold text-[var(--text-primary)] transition-all hover:border-[var(--border-hover)] focus:border-[var(--accent)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-glow)]">
              <option value="">All Statuses</option>
              <option value="available">Available</option>
              <option value="reserved">Reserved</option>
              <option value="sold">Sold</option>
            </select>

            <select value={floorFilter} onChange={(e) => { setFloorFilter(e.target.value); setPage(1); }} className="h-12 rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-4 text-sm font-semibold text-[var(--text-primary)] transition-all hover:border-[var(--border-hover)] focus:border-[var(--accent)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-glow)]">
              <option value="">All Floors</option>
              {availableFloors.map((floor) => floor ? <option key={floor} value={floor}>Floor {floor}</option> : null)}
            </select>

            <select value={sortBy} onChange={(e) => setSortBy(e.target.value)} className="h-12 rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-4 text-sm font-semibold text-[var(--text-primary)] transition-all hover:border-[var(--border-hover)] focus:border-[var(--accent)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-glow)]">
              <option value="newest">Newest First</option>
              <option value="price-high">Price High</option>
              <option value="price-low">Price Low</option>
              <option value="size-high">Largest Size</option>
              <option value="floor">Floor Number</option>
            </select>

            <button
              type="button"
              onClick={() => { setSearch(""); setTypeFilter(""); setFloorFilter(""); selectStatus(""); setSortBy("newest"); }}
              className="focus-ring grid h-12 w-full place-items-center rounded-xl border border-[var(--border)] bg-[var(--input-bg)] text-[var(--text-secondary)] transition hover:border-[var(--border-hover)] hover:bg-[var(--surface-glass-hover)] hover:text-[var(--text-heading)] xl:w-12"
              aria-label="Reset filters"
            >
              {icons.filter}
            </button>
          </div>
        </section>

        <section className="rounded-2xl border border-[var(--border)] bg-[var(--glass-bg)] p-4 shadow-[var(--shadow-sm)] animate-fade-in-up-delay-2 sm:p-5">
          {paginatedUnits.length > 0 ? (
            <div className="grid gap-5 sm:grid-cols-2 lg:grid-cols-3 2xl:grid-cols-4">
              {paginatedUnits.map((unit, index) => {
                const statusKey = unit.status.toLowerCase();
                const statusStyle = unitStatusStyles[statusKey] ?? "border-[var(--border)] bg-[var(--surface-glass)] text-[var(--text-secondary)]";
                const image = unitImages[(unit.id + index) % unitImages.length];

                return (
                  <article
                    key={unit.id}
                    onClick={() => navigate(`/units/${unit.id}`)}
                    className="group cursor-pointer overflow-hidden rounded-2xl border border-[var(--border)] bg-[var(--bg-card)] shadow-[var(--shadow-sm)] transition-all duration-300 hover:-translate-y-1 hover:border-[var(--border-hover)] hover:shadow-[0_18px_55px_rgba(0,0,0,0.14)]"
                  >
                    <div className="relative aspect-[16/10] overflow-hidden bg-[var(--bg-elevated)]">
                      <img src={image} alt={`Unit ${unit.unitNumber}`} className="h-full w-full object-cover transition duration-500 group-hover:scale-105" />
                      <div className="absolute inset-0 bg-gradient-to-t from-black/35 via-transparent to-black/10" />
                      <button
                        type="button"
                        onClick={(event) => event.stopPropagation()}
                        className="focus-ring absolute left-4 top-4 grid h-10 w-10 place-items-center rounded-full border border-white/30 bg-white/85 text-slate-700 shadow-lg backdrop-blur transition hover:scale-105 hover:bg-white"
                        aria-label="Favorite unit"
                      >
                        {icons.heart}
                      </button>
                      <span className={`absolute right-4 top-4 rounded-full border px-3 py-1.5 text-xs font-bold shadow-lg backdrop-blur ${statusStyle}`}>{unit.status}</span>
                    </div>

                    <div className="p-5">
                      <div className="flex items-start justify-between gap-3">
                        <div className="min-w-0">
                          <h3 className="truncate text-2xl font-bold text-[var(--text-heading)]">{unit.unitNumber}</h3>
                          <span className="mt-2 inline-flex rounded-full bg-[var(--accent-glow)] px-3 py-1 text-xs font-bold uppercase text-[var(--accent)]">{unit.unitType}</span>
                        </div>
                      </div>

                      <div className="mt-5 grid grid-cols-2 gap-3 text-sm font-semibold text-[var(--text-secondary)]">
                        <div className="flex items-center gap-2">{icons.floor}<span>Floor {unit.floorNumber}</span></div>
                        <div className="flex items-center gap-2">{icons.size}<span>{unit.size.toFixed(1)} sqm</span></div>
                      </div>

                      <div className="mt-5 flex items-end justify-between gap-4">
                        <div>
                          <p className="text-xs font-semibold text-[var(--text-muted)]">Price</p>
                          <p className="mt-1 text-xl font-bold text-[var(--text-heading)]">{formatCurrency(unit.price)}</p>
                        </div>
                        <button
                          type="button"
                          onClick={(event) => { event.stopPropagation(); navigate(`/units/${unit.id}`); }}
                          className="focus-ring inline-flex h-11 items-center gap-2 rounded-full border border-[var(--border)] bg-[var(--surface-glass)] px-4 text-sm font-bold text-[var(--text-heading)] transition hover:border-[var(--accent)] hover:bg-[var(--accent-glow)] hover:text-[var(--accent)]"
                        >
                          View Details
                          {icons.arrow}
                        </button>
                      </div>
                    </div>
                  </article>
                );
              })}
            </div>
          ) : (
            <div className="flex flex-col items-center justify-center py-20 text-center">
              <div className="mb-4 grid h-14 w-14 place-items-center rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] text-[var(--text-muted)]">
                {icons.building}
              </div>
              <h3 className="text-base font-bold text-[var(--text-heading)]">No units found</h3>
              <p className="mt-1 text-sm text-[var(--text-muted)]">{search || typeFilter || statusFilter || floorFilter ? "Try adjusting your search or filters." : "Add units to this project to get started."}</p>
            </div>
          )}

          <div className="mt-8 flex flex-col gap-4 border-t border-[var(--border)] pt-5 text-sm font-semibold text-[var(--text-secondary)] lg:flex-row lg:items-center lg:justify-between">
            <p>Showing {rangeStart} to {rangeEnd} of {filteredUnits.length} units</p>
            <Pagination currentPage={page} totalPages={totalPages} onPageChange={setPage} />
            <p className="text-[var(--text-muted)]">Items per page <span className="text-[var(--text-heading)]">{itemsPerPage}</span></p>
          </div>
        </section>
      </div>
    </div>
  );
}
