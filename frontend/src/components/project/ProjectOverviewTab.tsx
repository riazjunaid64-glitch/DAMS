interface Project {
  id: number;
  projectName: string;
  location: string;
  description?: string | null;
  startingDate: string;
  expectedCompletionDate?: string | null;
  status: number | string;
}

interface Props {
  project: Project;
  totalUnits: number;
  unitStats: { available: number; sold: number; reserved: number };
  coverImage?: string | null;
}

const statusLabels: Record<number, string> = { 1: "Planning", 2: "Ongoing", 3: "Completed", 4: "Cancelled", 5: "Archived" };

const formatDate = (date?: string | null) => {
  if (!date) return "—";
  const parsed = new Date(date);
  if (Number.isNaN(parsed.getTime())) return "—";
  return parsed.toLocaleDateString("en-US", { month: "short", day: "numeric", year: "numeric" });
};

const getStatusNum = (s: number | string) => (typeof s === "number" ? s : 1);

export default function ProjectOverviewTab({ project, totalUnits, unitStats, coverImage }: Props) {
  const statusNum = getStatusNum(project.status);

  const infoItems = [
    { label: "Location", value: project.location, icon: <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0118 0z"/><circle cx="12" cy="10" r="3"/></svg> },
    { label: "Start Date", value: formatDate(project.startingDate), icon: <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><rect x="3" y="4" width="18" height="18" rx="2" ry="2"/><line x1="16" y1="2" x2="16" y2="6"/><line x1="8" y1="2" x2="8" y2="6"/><line x1="3" y1="10" x2="21" y2="10"/></svg> },
    { label: "Expected End", value: formatDate(project.expectedCompletionDate), icon: <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/></svg> },
    { label: "Status", value: statusLabels[statusNum] ?? "Unknown", icon: <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M22 11.08V12a10 10 0 11-5.93-9.14"/><polyline points="22 4 12 14.01 9 11.01"/></svg> },
    { label: "Total Units", value: `${totalUnits}`, icon: <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><rect x="3" y="3" width="7" height="9" rx="1"/><rect x="14" y="3" width="7" height="5" rx="1"/><rect x="14" y="12" width="7" height="9" rx="1"/><rect x="3" y="16" width="7" height="5" rx="1"/></svg> },
    { label: "Available", value: `${unitStats.available}`, icon: <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><polyline points="20 6 9 17 4 12"/></svg> },
    { label: "Sold", value: `${unitStats.sold}`, icon: <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><circle cx="12" cy="12" r="10"/><path d="M16 8h-6a2 2 0 100 4h4a2 2 0 110 4H8"/><path d="M12 18V6"/></svg> },
    { label: "Reserved", value: `${unitStats.reserved}`, icon: <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><rect x="3" y="11" width="18" height="11" rx="2" ry="2"/><path d="M7 11V7a5 5 0 0110 0v4"/></svg> },
  ];

  return (
    <div className="py-8 sm:py-10">
      <div className="mx-auto w-full max-w-7xl px-5 sm:px-8 lg:px-10">
        {/* Cover Image + Description */}
        <div className="grid gap-6 lg:grid-cols-5 mb-10">
          {/* Cover */}
          <div className="lg:col-span-2">
            <div className="aspect-[4/3] rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] overflow-hidden">
              {coverImage ? (
                <img src={coverImage} alt={project.projectName} className="h-full w-full object-cover" />
              ) : (
                <div className="flex h-full w-full items-center justify-center">
                  <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" className="text-[var(--text-muted)]" strokeLinecap="round"><rect x="3" y="3" width="18" height="18" rx="2" ry="2"/><circle cx="8.5" cy="8.5" r="1.5"/><polyline points="21 15 16 10 5 21"/></svg>
                </div>
              )}
            </div>
          </div>

          {/* Description */}
          <div className="lg:col-span-3 space-y-5">
            <div>
              <h2 className="section-title">About This Project</h2>
              <p className="text-sm leading-relaxed text-[var(--text-secondary)]">
                {project.description || "No description provided for this project yet."}
              </p>
            </div>

            {/* Quick Stats Row */}
            <div className="grid grid-cols-3 gap-3">
              {[
                { label: "Available", value: unitStats.available, color: "text-emerald-400" },
                { label: "Sold", value: unitStats.sold, color: "text-rose-400" },
                { label: "Reserved", value: unitStats.reserved, color: "text-amber-400" },
              ].map((s) => (
                <div key={s.label} className="rounded-xl border border-[var(--border)] bg-[var(--surface-glass)] p-4 text-center">
                  <p className={`text-2xl font-bold ${s.color}`}>{s.value}</p>
                  <p className="text-[11px] uppercase tracking-wider text-[var(--text-muted)] mt-1">{s.label}</p>
                </div>
              ))}
            </div>
          </div>
        </div>

        {/* Info Grid */}
        <h2 className="section-title">Project Details</h2>
        <div className="info-grid">
          {infoItems.map((item) => (
            <div key={item.label} className="info-card">
              <div className="info-card__label">
                {item.icon}
                <span>{item.label}</span>
              </div>
              <div className="info-card__value">{item.value}</div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
