import { useMemo, useRef, useState } from "react";
import { resolveMediaUrl } from "../../api/api.ts";

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
  activeUnitStatus?: string;
  onUnitStatusSelect?: (status: string) => void;
}

const statusLabels: Record<number, string> = { 1: "Planning", 2: "Ongoing", 3: "Completed", 4: "Cancelled", 5: "Archived" };

const dummyGalleryImages = [
  "https://images.unsplash.com/photo-1600585154340-be6161a56a0c?auto=format&fit=crop&w=1800&q=85",
  "https://images.unsplash.com/photo-1600607687939-ce8a6c25118c?auto=format&fit=crop&w=1800&q=85",
  "https://images.unsplash.com/photo-1600566753190-17f0baa2a6c3?auto=format&fit=crop&w=1800&q=85",
  "https://images.unsplash.com/photo-1600573472592-401b489a3cdc?auto=format&fit=crop&w=1800&q=85",
  "https://images.unsplash.com/photo-1600047509807-ba8f99d2cdde?auto=format&fit=crop&w=1800&q=85",
  "https://images.unsplash.com/photo-1600607687920-4e2a09cf159d?auto=format&fit=crop&w=1800&q=85",
  "https://images.unsplash.com/photo-1600566753086-00f18fb6b3ea?auto=format&fit=crop&w=1800&q=85",
  "https://images.unsplash.com/photo-1605276374104-dee2a0ed3cd6?auto=format&fit=crop&w=1800&q=85",
  "https://images.unsplash.com/photo-1600607688969-a5bfcd646154?auto=format&fit=crop&w=1800&q=85",
  "https://images.unsplash.com/photo-1600566752355-35792bedcfea?auto=format&fit=crop&w=1800&q=85",
];

const formatDate = (date?: string | null) => {
  if (!date) return "--";
  const parsed = new Date(date);
  if (Number.isNaN(parsed.getTime())) return "--";
  return parsed.toLocaleDateString("en-US", { month: "short", day: "numeric", year: "numeric" });
};

const getStatusNum = (s: number | string) => (typeof s === "number" ? s : 1);

const getPercent = (value: number, total: number) => {
  if (!total) return "0% of total";
  return `${((value / total) * 100).toFixed(1).replace(".0", "")}% of total`;
};

const iconClass = "h-5 w-5";

const icons = {
  camera: (
    <svg className={iconClass} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M14.5 4h-5L7 7H4a2 2 0 0 0-2 2v9a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2V9a2 2 0 0 0-2-2h-3l-2.5-3Z" />
      <circle cx="12" cy="13" r="3" />
    </svg>
  ),
  location: (
    <svg className={iconClass} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M20 10c0 6-8 12-8 12S4 16 4 10a8 8 0 1 1 16 0Z" />
      <circle cx="12" cy="10" r="3" />
    </svg>
  ),
  calendar: (
    <svg className={iconClass} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <rect x="3" y="4" width="18" height="18" rx="2" />
      <path d="M16 2v4M8 2v4M3 10h18" />
    </svg>
  ),
  clipboard: (
    <svg className={iconClass} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2" />
      <rect x="8" y="2" width="8" height="4" rx="1" />
      <path d="m9 14 2 2 4-4" />
    </svg>
  ),
  flag: (
    <svg className={iconClass} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M4 22V4" />
      <path d="M4 5c4-2 8 2 12 0v10c-4 2-8-2-12 0" />
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
  check: (
    <svg className={iconClass} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <circle cx="12" cy="12" r="10" />
      <path d="m9 12 2 2 4-4" />
    </svg>
  ),
};

export default function ProjectOverviewTab({ project, totalUnits, unitStats, coverImage, activeUnitStatus = "", onUnitStatusSelect }: Props) {
  const [activeImage, setActiveImage] = useState(0);
  const touchStartX = useRef<number | null>(null);
  const heroRef = useRef<HTMLElement | null>(null);
  const statusNum = getStatusNum(project.status);
  const statusLabel = statusLabels[statusNum] ?? "Unknown";

  const galleryImages = useMemo(() => {
    const cover = coverImage ? [resolveMediaUrl(coverImage)] : [];
    return [...cover, ...dummyGalleryImages].slice(0, 10);
  }, [coverImage]);

  const timeline = [
    { label: "Planning", state: statusNum === 1 ? "Current" : "Done", icon: icons.clipboard },
    { label: "Construction", state: statusNum === 2 ? "Current" : statusNum > 2 ? "Done" : "Upcoming", icon: icons.building },
    { label: "Handover", state: statusNum === 3 ? "Current" : statusNum > 3 ? "Done" : "Upcoming", icon: icons.flag },
    { label: "Completed", state: statusNum === 3 ? "Done" : "Upcoming", icon: icons.check },
  ];

  const details = [
    { label: "Location", value: project.location || "--", icon: icons.location, tone: "text-[var(--accent)] bg-[var(--accent-glow)]" },
    { label: "Start Date", value: formatDate(project.startingDate), icon: icons.calendar, tone: "text-[var(--accent-secondary)] bg-[var(--accent-glow)]" },
    { label: "Expected End", value: formatDate(project.expectedCompletionDate), icon: icons.clipboard, tone: "text-[var(--text-secondary)] bg-[var(--surface-glass-active)]" },
    { label: "Status", value: statusLabel, icon: icons.flag, tone: "text-[var(--accent-secondary)] bg-[var(--accent-glow)]" },
  ];

  const stats = [
    { label: "Total Units", value: totalUnits, subtext: "", icon: icons.building, tone: "text-[var(--accent)] bg-[var(--accent-glow)]" },
    { label: "Available", value: unitStats.available, subtext: getPercent(unitStats.available, totalUnits), status: "available", icon: icons.available, tone: "text-[var(--accent-emerald)] bg-[var(--accent-emerald-glow)]" },
    { label: "Reserved", value: unitStats.reserved, subtext: getPercent(unitStats.reserved, totalUnits), status: "reserved", icon: icons.reserved, tone: "text-[var(--accent-warm)] bg-[var(--accent-warm-glow)]" },
    { label: "Sold", value: unitStats.sold, subtext: getPercent(unitStats.sold, totalUnits), status: "sold", icon: icons.sold, tone: "text-[var(--accent-rose)] bg-[var(--accent-rose-glow)]" },
  ];

  const showImage = (direction: number) => {
    setActiveImage((current) => (current + direction + galleryImages.length) % galleryImages.length);
  };

  return (
    <div className="py-4 sm:py-5">
      <div className="w-full space-y-5 px-4 sm:px-5 lg:px-6">
        <section
          ref={heroRef}
          className="relative overflow-hidden rounded-2xl border border-[var(--border)] bg-[var(--bg-card)] shadow-[0_18px_70px_rgba(0,0,0,0.14)] animate-fade-in-up"
          onTouchStart={(event) => {
            touchStartX.current = event.touches[0]?.clientX ?? null;
          }}
          onTouchEnd={(event) => {
            if (touchStartX.current === null) return;
            const endX = event.changedTouches[0]?.clientX ?? touchStartX.current;
            const delta = touchStartX.current - endX;
            if (Math.abs(delta) > 45) showImage(delta > 0 ? 1 : -1);
            touchStartX.current = null;
          }}
        >
          <div className="aspect-[16/10] min-h-[210px] sm:aspect-[16/7] sm:min-h-[300px] lg:aspect-[16/5.4] lg:min-h-[340px] lg:max-h-[430px]">
            <img
              key={galleryImages[activeImage]}
              src={galleryImages[activeImage]}
              alt={`${project.projectName} gallery ${activeImage + 1}`}
              className="h-full w-full object-cover animate-fade-in"
            />
          </div>
          <div className="pointer-events-none absolute inset-0 bg-gradient-to-t from-black/55 via-black/5 to-transparent" />

          <button
            type="button"
            onClick={() => showImage(-1)}
            className="focus-ring absolute left-3 top-1/2 grid h-10 w-10 -translate-y-1/2 place-items-center rounded-full border border-white/25 bg-white/80 text-slate-950 shadow-lg backdrop-blur-xl transition hover:-translate-x-0.5 hover:bg-white sm:left-5 sm:h-11 sm:w-11"
            aria-label="Previous image"
          >
            <svg className="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round">
              <path d="m15 18-6-6 6-6" />
            </svg>
          </button>
          <button
            type="button"
            onClick={() => showImage(1)}
            className="focus-ring absolute right-3 top-1/2 grid h-10 w-10 -translate-y-1/2 place-items-center rounded-full border border-white/25 bg-white/80 text-slate-950 shadow-lg backdrop-blur-xl transition hover:translate-x-0.5 hover:bg-white sm:right-5 sm:h-11 sm:w-11"
            aria-label="Next image"
          >
            <svg className="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round">
              <path d="m9 18 6-6-6-6" />
            </svg>
          </button>

          <button
            type="button"
            onClick={() => heroRef.current?.requestFullscreen?.()}
            className="focus-ring absolute right-4 top-4 hidden h-10 w-10 place-items-center rounded-full border border-white/20 bg-black/25 text-white shadow-lg backdrop-blur-xl transition hover:-translate-y-0.5 hover:bg-black/35 sm:grid"
            aria-label="Open fullscreen view"
          >
            <svg className="h-4.5 w-4.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <path d="M8 3H5a2 2 0 0 0-2 2v3M21 8V5a2 2 0 0 0-2-2h-3M16 21h3a2 2 0 0 0 2-2v-3M3 16v3a2 2 0 0 0 2 2h3" />
            </svg>
          </button>

          <div className="absolute bottom-4 left-4 flex items-center gap-2 rounded-xl border border-white/20 bg-black/35 px-3 py-2 text-xs font-bold text-white shadow-lg backdrop-blur-xl sm:bottom-5 sm:left-5">
            {icons.camera}
            <span>{activeImage + 1} / {galleryImages.length}</span>
          </div>

          <div className="absolute bottom-5 left-1/2 flex max-w-[48%] -translate-x-1/2 items-center justify-center gap-1.5 rounded-full bg-black/20 px-2.5 py-2 backdrop-blur-xl sm:gap-2">
            {galleryImages.map((_, index) => (
              <button
                key={index}
                type="button"
                onClick={() => setActiveImage(index)}
                className={`h-2 rounded-full transition-all ${index === activeImage ? "w-6 bg-[var(--accent)]" : "w-2 bg-white/75 hover:bg-white"}`}
                aria-label={`Show image ${index + 1}`}
              />
            ))}
          </div>
        </section>

        <section className="rounded-2xl border border-[var(--border)] bg-[var(--glass-bg)] p-5 shadow-[var(--shadow-sm)] animate-fade-in-up-delay-1 sm:p-7 lg:p-8">
          <div className="max-w-5xl">
            <p className="mb-2 text-xs font-bold uppercase text-[var(--accent)]">Overview</p>
            <h2 className="mb-4 text-2xl font-bold text-[var(--text-heading)]">About This Project</h2>
          </div>
          <p className="max-w-4xl text-sm leading-7 text-[var(--text-secondary)] sm:text-base sm:leading-8">
            {project.description || `${project.projectName} is a premium development featuring modern units, thoughtfully planned spaces, and amenities designed for a comfortable lifestyle.`}
          </p>

          <div className="mt-7 flex flex-wrap items-center gap-x-5 gap-y-3 text-sm font-semibold text-[var(--text-secondary)]">
            <span className="inline-flex items-center gap-2">{icons.location}{project.location || "--"}</span>
            <span className="hidden h-5 w-px bg-[var(--border)] sm:block" />
            <span className="inline-flex items-center gap-2">{icons.calendar}{formatDate(project.startingDate)}</span>
            <span className="hidden h-5 w-px bg-[var(--border)] sm:block" />
            <span className="rounded-full border border-[var(--border-active)] bg-[var(--accent-glow)] px-3 py-1 text-xs font-bold text-[var(--accent)]">{statusLabel}</span>
          </div>
        </section>

        <section className="rounded-2xl border border-[var(--border)] bg-[var(--glass-bg)] p-5 shadow-[var(--shadow-sm)] animate-fade-in-up-delay-2 sm:p-7 lg:p-8">
          <div className="mb-8 flex flex-col gap-2 sm:flex-row sm:items-end sm:justify-between">
            <div>
              <p className="mb-2 text-xs font-bold uppercase text-[var(--accent)]">Delivery</p>
              <h2 className="text-xl font-bold text-[var(--text-heading)]">Project Timeline</h2>
            </div>
            <p className="text-sm font-semibold text-[var(--text-secondary)]">Current stage: <span className="text-[var(--accent)]">{statusLabel}</span></p>
          </div>
          <div className="relative grid gap-7 sm:grid-cols-4 sm:px-4">
            <div className="absolute left-10 right-10 top-6 hidden h-px bg-[var(--border)] sm:block" />
            <div className="absolute left-10 top-6 hidden h-px bg-[var(--accent)] sm:block" style={{ width: `${Math.max(0, Math.min(statusNum - 1, 3)) * 30}%` }} />
            <div className="absolute bottom-6 left-6 top-6 w-px bg-[var(--border)] sm:hidden" />
            <div className="absolute left-6 top-6 w-px bg-[var(--accent)] sm:hidden" style={{ height: `${Math.max(0, Math.min(statusNum - 1, 3)) * 32}%` }} />
            {timeline.map((item) => {
              const isCurrent = item.state === "Current";
              const isDone = item.state === "Done";
              return (
                <div key={item.label} className="relative z-10 flex items-center gap-4 sm:flex-col sm:items-center sm:gap-3">
                  <div className={`grid h-12 w-12 shrink-0 place-items-center rounded-full border transition-all duration-300 ${isCurrent ? "border-[var(--accent)] bg-[var(--accent)] text-white shadow-[var(--btn-primary-shadow)]" : isDone ? "border-[var(--accent)] bg-[var(--accent-glow)] text-[var(--accent)]" : "border-[var(--border)] bg-[var(--bg-elevated)] text-[var(--text-secondary)]"}`}>
                    {item.icon}
                  </div>
                  <div className="sm:text-center">
                    <p className="text-sm font-bold text-[var(--text-heading)]">{item.label}</p>
                    <p className={`mt-1 text-xs font-semibold ${isCurrent ? "text-[var(--accent)]" : "text-[var(--text-secondary)]"}`}>{item.state}</p>
                  </div>
                </div>
              );
            })}
          </div>
        </section>

        <section className="rounded-2xl border border-[var(--border)] bg-[var(--glass-bg)] p-5 shadow-[var(--shadow-sm)] animate-fade-in-up-delay-3 sm:p-7 lg:p-8">
          <div className="mb-6">
            <p className="mb-2 text-xs font-bold uppercase text-[var(--accent)]">Operations</p>
            <h2 className="text-xl font-bold text-[var(--text-heading)]">Project Details</h2>
          </div>

          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
            {details.map((item) => (
              <div key={item.label} className="rounded-xl border border-[var(--border)] bg-[var(--bg-card)] p-4 shadow-[var(--shadow-sm)] transition hover:-translate-y-0.5 hover:border-[var(--border-hover)] hover:shadow-[var(--shadow-md)]">
                <div className="flex items-center gap-4">
                  <div className={`grid h-11 w-11 shrink-0 place-items-center rounded-xl ${item.tone}`}>{item.icon}</div>
                  <div className="min-w-0">
                    <p className="text-sm font-semibold text-[var(--text-secondary)]">{item.label}</p>
                    <p className="mt-1 truncate text-sm font-bold text-[var(--text-heading)]">{item.value}</p>
                  </div>
                </div>
              </div>
            ))}
          </div>

          <div className="mt-3 grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
            {stats.map((item) => {
              const card = (
                <div className={`h-full rounded-xl border bg-[var(--bg-card)] p-5 text-left shadow-[var(--shadow-sm)] transition ${activeUnitStatus === item.status ? "border-[var(--accent)] shadow-[var(--shadow-glow)]" : "border-[var(--border)]"} ${item.status ? "hover:-translate-y-0.5 hover:border-[var(--border-hover)] hover:shadow-[var(--shadow-md)]" : ""}`}>
                  <div className="flex items-center gap-5">
                    <div className={`grid h-12 w-12 shrink-0 place-items-center rounded-xl ${item.tone}`}>{item.icon}</div>
                    <div>
                      <p className="text-3xl font-bold text-[var(--text-heading)]">{item.value}</p>
                      <p className="mt-1 text-sm font-semibold text-[var(--text-secondary)]">{item.label}</p>
                      {item.subtext && <p className="mt-3 text-sm font-semibold text-[var(--text-secondary)]">{item.subtext}</p>}
                    </div>
                  </div>
                </div>
              );

              if (!item.status) return <div key={item.label}>{card}</div>;

              return (
                <button key={item.label} type="button" onClick={() => onUnitStatusSelect?.(item.status!)} className="focus-ring text-left" aria-pressed={activeUnitStatus === item.status}>
                  {card}
                </button>
              );
            })}
          </div>
        </section>
      </div>
    </div>
  );
}
