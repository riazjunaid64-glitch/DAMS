import { useEffect, useMemo, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { api } from "../api/api";
import type { User } from "../App";
import Button from "../lib/Button";
import Container from "../lib/Container";
import Field from "../lib/Field";
import Pagination from "../lib/Pagination";

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

type Props = {
  user: User | null;
};

const statusLabels: Record<number, string> = {
  1: "Planning",
  2: "Ongoing",
  3: "Completed",
  4: "Cancelled",
  5: "Archived",
};

const statusStyles: Record<number, string> = {
  1: "status-planning",
  2: "status-ongoing",
  3: "status-completed",
  4: "status-cancelled",
  5: "status-archived",
};

const unitStatusColors: Record<string, string> = {
  available: "text-emerald-400 bg-emerald-500/10 border-emerald-500/20",
  sold: "text-rose-400 bg-rose-500/10 border-rose-500/20",
  reserved: "text-amber-400 bg-amber-500/10 border-amber-500/20",
};

export default function ProjectDetailPage({ user }: Props) {
  const { id } = useParams<{ id?: string }>();
  const navigate = useNavigate();
  const projectId = Number(id);

  const [project, setProject] = useState<Project | null>(null);
  const [units, setUnits] = useState<Unit[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState("");
  const [typeFilter, setTypeFilter] = useState("");
  const [page, setPage] = useState(1);
  const [showUnitForm, setShowUnitForm] = useState(false);
  const [unitError, setUnitError] = useState<string | null>(null);
  const [unitForm, setUnitForm] = useState({
    unitNumber: "",
    unitType: "",
    floorNumber: 1,
    size: 0,
    price: 0,
  });

  const itemsPerPage = 9;
  const isAdmin = user?.role === "Admin";

  useEffect(() => {
    if (!projectId || Number.isNaN(projectId)) {
      setError("Invalid project ID.");
      return;
    }

    const load = async () => {
      setLoading(true);
      setError(null);

      try {
        const [projRes, unitRes] = await Promise.all([
          api(`/api/Project/${projectId}`, undefined, false),
          api(`/api/Unit/project/${projectId}`, undefined, false),
        ]);

        if (!projRes.ok) {
          setError("Project not found.");
          setProject(null);
          setUnits([]);
          return;
        }

        if (!unitRes.ok) {
          setError("Unable to load units for this project.");
          setUnits([]);
          return;
        }

        const projData = (await projRes.json()) as Project;
        const unitData = (await unitRes.json()) as Unit[];

        setProject(projData);
        setUnits(unitData);
      } catch {
        setError("Unable to load project details right now.");
      } finally {
        setLoading(false);
      }
    };

    load();
  }, [projectId]);

  const availableTypes = useMemo(() => {
    const types = Array.from(new Set(units.map((u) => u.unitType))).sort();
    return ["", ...types];
  }, [units]);

  const filteredUnits = useMemo(() => {
    const term = search.trim().toLowerCase();
    return units.filter((unit) => {
      const matchesSearch =
        unit.unitNumber.toLowerCase().includes(term) ||
        unit.unitType.toLowerCase().includes(term) ||
        unit.status.toLowerCase().includes(term);
      const matchesType = typeFilter ? unit.unitType === typeFilter : true;
      return matchesSearch && matchesType;
    });
  }, [search, typeFilter, units]);

  const totalPages = Math.max(1, Math.ceil(filteredUnits.length / itemsPerPage));
  const paginatedUnits = filteredUnits.slice((page - 1) * itemsPerPage, page * itemsPerPage);

  useEffect(() => {
    if (page > totalPages) setPage(1);
  }, [page, totalPages]);

  const formatDate = (date?: string | null) => {
    if (!date) return "—";
    const parsed = new Date(date);
    if (Number.isNaN(parsed.getTime())) return "—";
    return parsed.toLocaleDateString("en-US", { month: "short", day: "numeric", year: "numeric" });
  };

  const getStatusNum = (s: number | string) => (typeof s === "number" ? s : 1);

  const submitUnit = async () => {
    setUnitError(null);
    if (!unitForm.unitNumber || !unitForm.unitType || unitForm.floorNumber <= 0) {
      setUnitError("Unit number, type and floor number are required.");
      return;
    }
    try {
      const res = await api("/api/Unit", {
        method: "POST",
        body: JSON.stringify({
          projectId,
          unitNumber: unitForm.unitNumber,
          unitType: unitForm.unitType,
          floorNumber: unitForm.floorNumber,
          size: unitForm.size,
          price: unitForm.price,
        }),
      });
      if (!res.ok) {
        const text = await res.text();
        setUnitError(text || "Unable to create unit.");
        return;
      }
      setUnitForm({ unitNumber: "", unitType: "", floorNumber: 1, size: 0, price: 0 });
      setShowUnitForm(false);
      setPage(1);
      const unitRes = await api(`/api/Unit/project/${projectId}`, undefined, false);
      if (unitRes.ok) setUnits((await unitRes.json()) as Unit[]);
    } catch {
      setUnitError("Unable to create unit right now.");
    }
  };

  const statusNum = project ? getStatusNum(project.status) : 1;

  return (
    <>
      {/* ─── Project Header ─── */}
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
            <span className="text-[var(--text-secondary)]">{project?.projectName ?? "Loading..."}</span>
          </div>

          <div className="flex flex-col gap-6 lg:flex-row lg:items-start lg:justify-between">
            <div className="space-y-3">
              <div className="flex flex-wrap items-center gap-3">
                <h1 className="text-3xl font-bold text-[var(--text-heading)] sm:text-4xl">
                  {project ? project.projectName : "Loading..."}
                </h1>
                {project && (
                  <span className={`rounded-full px-3 py-1 text-xs font-semibold ${statusStyles[statusNum] ?? "status-archived"}`}>
                    {statusLabels[statusNum] ?? "Unknown"}
                  </span>
                )}
              </div>
              <p className="max-w-2xl text-sm leading-relaxed text-[var(--text-secondary)]">
                {project?.description ?? "Loading project details..."}
              </p>
            </div>

            <div className="flex shrink-0 gap-2">
              <Button variant="outline" size="sm" onClick={() => navigate(-1)}>
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                  <polyline points="15 18 9 12 15 6"/>
                </svg>
                Back
              </Button>
              {isAdmin && projectId > 0 && (
                <Button size="sm" onClick={() => setShowUnitForm((prev) => !prev)}>
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                    <line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>
                  </svg>
                  {showUnitForm ? "Cancel" : "Add Unit"}
                </Button>
              )}
            </div>
          </div>

          {/* Project Stats */}
          {project && (
            <div className="mt-8 grid grid-cols-2 gap-3 sm:grid-cols-4">
              {[
                {
                  label: "Location",
                  value: project.location,
                  icon: (
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                      <path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0118 0z"/><circle cx="12" cy="10" r="3"/>
                    </svg>
                  ),
                },
                {
                  label: "Start Date",
                  value: formatDate(project.startingDate),
                  icon: (
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                      <rect x="3" y="4" width="18" height="18" rx="2" ry="2"/><line x1="16" y1="2" x2="16" y2="6"/><line x1="8" y1="2" x2="8" y2="6"/><line x1="3" y1="10" x2="21" y2="10"/>
                    </svg>
                  ),
                },
                {
                  label: "Expected End",
                  value: formatDate(project.expectedCompletionDate),
                  icon: (
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                      <circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/>
                    </svg>
                  ),
                },
                {
                  label: "Total Units",
                  value: `${units.length}`,
                  icon: (
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                      <rect x="3" y="3" width="7" height="9" rx="1"/><rect x="14" y="3" width="7" height="5" rx="1"/><rect x="14" y="12" width="7" height="9" rx="1"/><rect x="3" y="16" width="7" height="5" rx="1"/>
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
          {/* Create Unit Form */}
          {showUnitForm && isAdmin && (
            <div className="mb-8 animate-scale-in rounded-2xl border border-[var(--accent-glow-strong)] bg-[var(--accent-glow)] p-6 shadow-sm">
              <h4 className="mb-4 text-sm font-semibold text-[var(--text-heading)]">Create New Unit</h4>
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
              </div>
              <div className="mt-4 flex items-center gap-3">
                <Button onClick={submitUnit} size="sm">Save Unit</Button>
                <Button variant="ghost" size="sm" onClick={() => setShowUnitForm(false)}>Cancel</Button>
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

          {/* Filters */}
          {!loading && !error && (
            <div className="mb-6 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
              <div className="flex flex-1 gap-3">
                <div className="relative flex-1 max-w-sm">
                  <svg className="absolute left-3.5 top-1/2 -translate-y-1/2 text-[var(--text-muted)]" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                    <circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/>
                  </svg>
                  <input
                    value={search}
                    onChange={(e) => { setSearch(e.target.value); setPage(1); }}
                    className="w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] py-2.5 pl-10 pr-4 text-sm text-[var(--text-primary)] placeholder:text-[var(--text-muted)] transition-all focus:border-[var(--accent)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-glow)]"
                    placeholder="Search units..."
                  />
                </div>
                <select
                  value={typeFilter}
                  onChange={(e) => { setTypeFilter(e.target.value); setPage(1); }}
                  className="rounded-xl border border-white/[0.08] bg-white/[0.03] px-4 py-2.5 text-sm text-white transition-all focus:border-indigo-500/60 focus:outline-none focus:ring-2 focus:ring-indigo-500/20"
                >
                  <option value="">All types</option>
                  {availableTypes.map((type) =>
                    type ? <option key={type} value={type}>{type}</option> : null
                  )}
                </select>
              </div>
              <span className="text-sm text-[#6b6b80]">
                {filteredUnits.length} unit{filteredUnits.length !== 1 ? "s" : ""} found
              </span>
            </div>
          )}

          {/* Unit Grid */}
          {!loading && !error && paginatedUnits.length > 0 && (
            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
              {paginatedUnits.map((unit, i) => {
                const statusKey = unit.status.toLowerCase();
                const statusColor = unitStatusColors[statusKey] ?? "text-[#a1a1b5] bg-white/[0.04] border-white/[0.06]";
                return (
                  <div
                    key={unit.id}
                    className="glass-card group p-5 animate-fade-in-up"
                    style={{ animationDelay: `${i * 40}ms` }}
                  >
                    {/* Unit Header */}
                    <div className="flex items-center justify-between">
                      <h3 className="text-base font-semibold text-white">{unit.unitNumber}</h3>
                      <span className={`rounded-full border px-2.5 py-0.5 text-[11px] font-semibold ${statusColor}`}>
                        {unit.status}
                      </span>
                    </div>

                    {/* Type Badge */}
                    <div className="mt-3 inline-flex items-center gap-1.5 rounded-md bg-indigo-500/[0.08] px-2 py-0.5">
                      <span className="text-[11px] font-medium text-indigo-300 uppercase tracking-wider">{unit.unitType}</span>
                    </div>

                    {/* Details */}
                    <div className="mt-4 space-y-2">
                      <div className="flex items-center justify-between text-sm">
                        <span className="text-[#6b6b80]">Floor</span>
                        <span className="font-medium text-[#a1a1b5]">{unit.floorNumber}</span>
                      </div>
                      <div className="flex items-center justify-between text-sm">
                        <span className="text-[#6b6b80]">Size</span>
                        <span className="font-medium text-[#a1a1b5]">{unit.size.toFixed(1)} sqm</span>
                      </div>
                      <div className="flex items-center justify-between text-sm">
                        <span className="text-[#6b6b80]">Price</span>
                        <span className="font-semibold text-white">
                          ${unit.price.toLocaleString()}
                        </span>
                      </div>
                    </div>
                  </div>
                );
              })}
            </div>
          )}

          {/* Empty Units */}
          {!loading && !error && filteredUnits.length === 0 && (
            <div className="flex flex-col items-center justify-center py-20 text-center">
              <div className="mb-4 flex h-14 w-14 items-center justify-center rounded-2xl bg-white/[0.03] border border-white/[0.06]">
                <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" className="text-[#6b6b80]" strokeLinecap="round">
                  <path d="M3 9l9-7 9 7v11a2 2 0 01-2 2H5a2 2 0 01-2-2V9z"/><polyline points="9 22 9 12 15 12 15 22"/>
                </svg>
              </div>
              <h3 className="text-base font-semibold text-white">No units found</h3>
              <p className="mt-1 text-sm text-[#6b6b80]">
                {search || typeFilter ? "Try adjusting your search or filters." : "Add units to this project to get started."}
              </p>
            </div>
          )}

          <Pagination currentPage={page} totalPages={totalPages} onPageChange={setPage} />
        </Container>
      </div>
    </>
  );
}
