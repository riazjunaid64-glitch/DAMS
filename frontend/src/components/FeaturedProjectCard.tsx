import { Link } from "react-router-dom";
import { resolveMediaUrl } from "../api/api";
import type { ProjectFromApi } from "../utils/parseProject";
import { PUBLIC_STATUS_LABELS, getStatusNum } from "../utils/projectStatus.ts";

type Props = {
  project: ProjectFromApi;
  priority?: boolean;
  variant?: "default" | "featured";
};

export default function FeaturedProjectCard({ project, priority = false, variant = "default" }: Props) {
  const isFeatured = variant === "featured";
  const imgSrc = resolveMediaUrl(project.coverImageUrl);
  const statusNum = getStatusNum(project.status);
  const statusLabel = PUBLIC_STATUS_LABELS[statusNum] ?? "Available";
  const badge = project.category ?? "Project";

  return (
    <Link
      to={`/projects/${project.id}`}
      className={isFeatured ? "op-card op-card--featured" : "op-card"}
      aria-label={project.projectName}
    >
      <div className="op-thumb">
        {imgSrc ? (
          <img
            src={imgSrc}
            alt={project.projectName}
            className="op-thumb__img"
            loading={priority ? "eager" : "lazy"}
            decoding="async"
            fetchPriority={priority ? "high" : "auto"}
          />
        ) : (
          <div className="op-thumb__img op-thumb__placeholder" />
        )}
        <div className="op-badge">{badge}</div>
      </div>

      <div className="op-body">
        <h3 className="op-title">{project.projectName}</h3>
        <p className="op-meta">
          <PinIcon />
          {project.location}
        </p>
      </div>

      <div className="op-foot op-foot--split">
        <span>
          <strong>Status:</strong> {statusLabel}
        </span>
        <span className="op-foot__cta">View Details</span>
      </div>
    </Link>
  );
}

function PinIcon() {
  return (
    <svg
      width="13"
      height="13"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0118 0z" />
      <circle cx="12" cy="10" r="3" />
    </svg>
  );
}
