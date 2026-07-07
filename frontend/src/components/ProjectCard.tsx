import { Link } from "react-router-dom";
import { resolveMediaUrl } from "../api/api.ts";
import type { ProjectFromApi } from "../utils/parseProject.ts";
import { PUBLIC_STATUS_LABELS, getStatusNum } from "../utils/projectStatus.ts";

const DEFAULT_COVER = "/images/home-hero-bg.jpg";

type ProjectCardProps = {
  project: ProjectFromApi;
  onEdit?: (project: ProjectFromApi) => void;
  showAdminActions?: boolean;
};

export default function ProjectCard({ project, onEdit, showAdminActions = false }: ProjectCardProps) {
  const statusNum = getStatusNum(project.status);
  const statusText = PUBLIC_STATUS_LABELS[statusNum] ?? "Unknown";
  const badge = (project.category || "Mixed Use").toUpperCase();
  const coverSrc = project.coverImageUrl
    ? resolveMediaUrl(project.coverImageUrl)
    : DEFAULT_COVER;

  return (
    <article className="op-card op-card--link">
      <Link to={`/projects/${project.id}`} className="op-card__link" aria-label={`View ${project.projectName}`}>
        <div className="op-thumb">
          <img
            src={coverSrc}
            alt={project.projectName}
            className="op-thumb__img"
            loading="lazy"
            decoding="async"
          />
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
            <strong>Status:</strong> {statusText}
          </span>
          <span className="op-foot__cta">View Details</span>
        </div>
      </Link>

      {showAdminActions && onEdit && (
        <button
          type="button"
          className="op-card__edit"
          onClick={(e) => {
            e.preventDefault();
            e.stopPropagation();
            onEdit(project);
          }}
          aria-label={`Edit ${project.projectName}`}
        >
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            <path d="M11 4H4a2 2 0 00-2 2v14a2 2 0 002 2h14a2 2 0 002-2v-7" />
            <path d="M18.5 2.5a2.121 2.121 0 013 3L12 15l-4 1 1-4 9.5-9.5z" />
          </svg>
        </button>
      )}
    </article>
  );
}

function PinIcon() {
  return (
    <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0118 0z" />
      <circle cx="12" cy="10" r="3" />
    </svg>
  );
}
