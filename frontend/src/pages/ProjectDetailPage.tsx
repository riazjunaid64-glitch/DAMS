import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { api } from "../api/api";
import type { User } from "../App";
import Container from "../lib/Container";
import Section from "../lib/Section";
import Pagination from "../lib/Pagination";
import Button from "../lib/Button";

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
    if (!date) return "-";
    const parsed = new Date(date);
    if (Number.isNaN(parsed.getTime())) return "-";
    return parsed.toLocaleDateString();
  };

  const isAdmin = user?.role === "Admin";

  return (
    <main className="min-h-screen bg-slate-950 pb-10">
      <Section
        eyebrow="Project Details"
        title={project ? project.projectName : "Loading project..."}
        description={project?.description ?? "View and search units for this project."}
        className="bg-slate-950 py-10"
      >
        <Container>
          <div className="space-y-6">
            <div className="flex flex-wrap items-center justify-between gap-4">
              <div className="flex gap-2">
                <Button variant="outline" onClick={() => navigate(-1)}>
                  Back to Projects
                </Button>
                {isAdmin && projectId > 0 && (
                  <Button onClick={() => setShowUnitForm((prev) => !prev)}>
                    {showUnitForm ? "Hide" : "Create Unit"}
                  </Button>
                )}
              </div>
              <div className="text-sm text-slate-400">
                {project && `${project.location} • ${project.status}`} 
                {project && `• Start: ${formatDate(project.startingDate)} • End: ${formatDate(project.expectedCompletionDate)}`}
              </div>
            </div>

            {showUnitForm && isAdmin && (
              <div className="rounded-3xl border border-emerald-300/30 bg-emerald-500/10 p-6 text-sm text-emerald-100">
                <h4 className="mb-3 font-semibold">Create Unit</h4>
                <div className="grid gap-3 md:grid-cols-2">
                  <input
                    value={unitForm.unitNumber}
                    onChange={(e) => setUnitForm((prev) => ({ ...prev, unitNumber: e.target.value }))}
                    className="rounded-lg border border-white/20 bg-slate-900 px-3 py-2 text-sm"
                    placeholder="Unit Number"
                  />
                  <input
                    value={unitForm.unitType}
                    onChange={(e) => setUnitForm((prev) => ({ ...prev, unitType: e.target.value }))}
                    className="rounded-lg border border-white/20 bg-slate-900 px-3 py-2 text-sm"
                    placeholder="Unit Type (shop, flat, etc.)"
                  />
                  <input
                    type="number"
                    value={unitForm.floorNumber}
                    onChange={(e) => setUnitForm((prev) => ({ ...prev, floorNumber: Number(e.target.value) }))}
                    className="rounded-lg border border-white/20 bg-slate-900 px-3 py-2 text-sm"
                    placeholder="Floor Number"
                  />
                  <input
                    type="number"
                    value={unitForm.size}
                    onChange={(e) => setUnitForm((prev) => ({ ...prev, size: Number(e.target.value) }))}
                    className="rounded-lg border border-white/20 bg-slate-900 px-3 py-2 text-sm"
                    placeholder="Size (sqm)"
                  />
                  <input
                    type="number"
                    value={unitForm.price}
                    onChange={(e) => setUnitForm((prev) => ({ ...prev, price: Number(e.target.value) }))}
                    className="rounded-lg border border-white/20 bg-slate-900 px-3 py-2 text-sm"
                    placeholder="Price"
                  />
                </div>
                <div className="mt-3 flex items-center gap-2">
                  <Button
                    onClick={async () => {
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
                    }}
                  >
                    Save Unit
                  </Button>
                  <Button variant="outline" onClick={() => setShowUnitForm(false)}>
                    Cancel
                  </Button>
                </div>
                {unitError && <p className="mt-2 text-xs text-red-300">{unitError}</p>}
              </div>
            )}

            {loading && (
              <div className="rounded-3xl border border-white/10 bg-white/5 p-6 text-sm">
                Loading details...
              </div>
            )}

            {error && (
              <div className="rounded-3xl border border-rose-300/30 bg-rose-500/10 p-6 text-sm text-rose-100">
                {error}
              </div>
            )}

            <div className="grid gap-4 md:grid-cols-3">
              <input
                value={search}
                onChange={(e) => { setSearch(e.target.value); setPage(1); }}
                className="rounded-xl border border-white/20 bg-slate-900 px-4 py-2 text-sm"
                placeholder="Search units by number, type, status..."
              />
              <select
                value={typeFilter}
                onChange={(e) => { setTypeFilter(e.target.value); setPage(1); }}
                className="rounded-xl border border-white/20 bg-slate-900 px-4 py-2 text-sm"
              >
                <option value="">All types</option>
                {availableTypes.map((type) =>
                  type ? (
                    <option key={type} value={type}>{type}</option>
                  ) : null
                )}
              </select>
              <div className="text-right text-sm text-slate-300">
                {filteredUnits.length} units found
              </div>
            </div>

            <div className="grid gap-6 sm:grid-cols-2 lg:grid-cols-3" style={{ minHeight: "calc(100vh - 320px)" }}>
              {paginatedUnits.map((unit) => (
                <div
                  key={unit.id}
                  className="rounded-3xl border border-white/10 bg-white/5 p-5 shadow-[0_20px_40px_-30px_rgba(15,23,42,0.9)]"
                >
                  <div className="flex items-center justify-between">
                    <h3 className="text-lg font-semibold">{unit.unitNumber}</h3>
                    <span className="rounded-full bg-amber-400/20 px-3 py-1 text-xs font-semibold text-amber-300">
                      {unit.unitType}
                    </span>
                  </div>
                  <p className="mt-2 text-sm text-slate-300">Status: {unit.status}</p>
                  <div className="mt-3 text-sm text-slate-300 space-y-1">
                    <div>Floor: {unit.floorNumber}</div>
                    <div>Size: {unit.size.toFixed(2)} sqm</div>
                    <div>Price: �{unit.price.toFixed(2)}</div>
                  </div>
                </div>
              ))}
            </div>

            <Pagination currentPage={page} totalPages={totalPages} onPageChange={setPage} />
          </div>
        </Container>
      </Section>
    </main>
  );
}
