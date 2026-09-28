import { Link } from "react-router-dom";
import { resolveMediaUrl } from "../../api/api.ts";
import { projectStatusName, type ProjectFromApi } from "../../utils/parseProject.ts";
import { IconImage, IconMapPin, StatusBadge } from "../ui";

/** One project on the Projects page. The whole card opens the project. */
export function ProjectCard({ project }: { project: ProjectFromApi }) {
  const status = projectStatusName(project.status);
  return (
    <Link to={`/projects/${project.id}`} className="block overflow-hidden rounded-card border border-line bg-card font-ui text-ink no-underline transition-colors hover:border-ink-faint focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary">
      {project.coverImageUrl ? (
        <img src={resolveMediaUrl(project.coverImageUrl)} alt="" className="h-[170px] w-full object-cover md:h-[190px]" />
      ) : (
        <span className="flex h-[170px] w-full items-center justify-center bg-track text-ink-faint md:h-[190px]">
          <IconImage size={28} />
        </span>
      )}
      <span className="block p-4">
        <span className="flex items-start justify-between gap-2">
          <span className="text-section font-extrabold">{project.projectName}</span>
          {status && <StatusBadge status={status} />}
        </span>
        {project.location && (
          <span className="mt-1.5 flex items-center gap-1.5 text-small text-ink-muted">
            <IconMapPin size={14} />
            {project.location}
          </span>
        )}
        <span className="mt-3 block border-t border-line-soft pt-3 text-sm text-ink-2">
          {project.totalUnits > 0 ? (
            <>
              <b className="font-extrabold text-success">{project.availableUnits.toLocaleString("en-PK")}</b>
              {` of ${project.totalUnits.toLocaleString("en-PK")} units available`}
            </>
          ) : "No units added yet"}
        </span>
      </span>
    </Link>
  );
}
