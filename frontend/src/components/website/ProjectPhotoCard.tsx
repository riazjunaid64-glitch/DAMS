import { Link } from "react-router-dom";
import { resolveMediaUrl } from "../../api/api.ts";
import type { ProjectFromApi } from "../../utils/parseProject.ts";
import { cx } from "../ui/cx.ts";
import { IconImage, IconMapPin } from "../ui/icons.tsx";
import { hoverLiftClass, hoverZoomClass } from "./motion.ts";

/** Cover photo, category chip, name and location. The whole card opens the project. */
export function ProjectPhotoCard({ project, priority = false }: { project: ProjectFromApi; priority?: boolean }) {
  const src = resolveMediaUrl(project.coverImageUrl);
  return (
    <Link
      to={`/projects/${project.id}`}
      className={cx(
        "group flex h-full min-w-0 flex-col overflow-hidden rounded-card border border-line bg-card font-ui text-ink no-underline",
        hoverLiftClass,
      )}
    >
      <span className="relative block aspect-[16/10] overflow-hidden bg-track">
        {src ? (
          <img
            src={src}
            alt=""
            className={cx("size-full object-cover", hoverZoomClass)}
            loading={priority ? "eager" : "lazy"}
            decoding="async"
            fetchPriority={priority ? "high" : "auto"}
          />
        ) : (
          <span className="flex size-full items-center justify-center text-ink-faint">
            <IconImage size={28} />
          </span>
        )}
        {project.category ? (
          <span className="absolute top-2.5 left-2.5 rounded-full bg-card px-2.5 py-1 text-caption font-bold text-ink">
            {project.category}
          </span>
        ) : null}
      </span>
      <span className="block min-w-0 px-3 py-3 md:px-4">
        <span className="block text-small font-extrabold text-ink md:text-body">{project.projectName}</span>
        {project.location ? (
          <span className="mt-1 flex min-w-0 items-center gap-1 text-caption text-ink-muted md:text-small">
            <IconMapPin size={14} className="shrink-0 text-gold-text" />
            <span className="min-w-0 truncate md:overflow-visible md:whitespace-normal">{project.location}</span>
          </span>
        ) : null}
      </span>
    </Link>
  );
}
