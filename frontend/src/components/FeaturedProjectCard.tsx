import type { CSSProperties } from "react";
import type { FeaturedProject } from "../lib/featuredProjects.ts";

type FeaturedProjectCardProps = {
  project: FeaturedProject;
  style?: CSSProperties;
  priority?: boolean;
  variant?: "default" | "featured";
};

export default function FeaturedProjectCard({
  project,
  style,
  priority = false,
  variant = "default",
}: FeaturedProjectCardProps) {
  const isFeatured = variant === "featured" || project.featured;

  return (
    <div
      className={isFeatured ? "op-card op-card--featured" : "op-card"}
      style={style}
      aria-label={project.title}
    >
      <div className="op-thumb">
        <img
          src={project.image}
          alt={project.title}
          className="op-thumb__img"
          loading={priority ? "eager" : "lazy"}
          decoding="async"
          fetchPriority={priority ? "high" : "auto"}
        />
        <div className="op-badge">{project.badge}</div>
      </div>

      <div className="op-body">
        <h3 className="op-title">{project.title}</h3>
        <p className="op-meta">
          <PinIcon />
          {project.location}
        </p>
      </div>

      <div className="op-foot">
        <span>
          <strong>Status:</strong> {project.status}
        </span>
      </div>
    </div>
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
