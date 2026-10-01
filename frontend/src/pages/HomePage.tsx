import { Link } from "react-router-dom";
import { Button, IconArrowRight, useIsPhone } from "../components/ui";
import { cx } from "../components/ui/cx.ts";
import { CompanyNumbers } from "../components/website/CompanyNumbers.tsx";
import { FadeUp } from "../components/website/FadeUp.tsx";
import { hoverLiftClass } from "../components/website/motion.ts";
import { ProjectPhotoCard } from "../components/website/ProjectPhotoCard.tsx";
import { useProjects } from "../contexts/projectsContextValue";
import { isPubliclyVisible } from "../utils/projectStatus.ts";

const PAGE = "mx-auto flex w-full max-w-[1500px] flex-col gap-8 px-4 py-5 md:px-8 md:py-7";

export default function HomePage() {
  const { projects, loading, error, reload } = useProjects();
  const phone = useIsPhone();
  const featured = projects.filter((project) => isPubliclyVisible(project.status)).slice(0, 6);

  return (
    <div className={PAGE}>
      <section className="relative rounded-popup bg-primary text-white">
        <HeroRings />
        <div className="relative px-5 pt-8 pb-2 md:px-10 md:pt-12 md:pb-4">
          <FadeUp>
            <h1 className="m-0 max-w-xl text-3xl leading-tight font-extrabold text-white md:text-5xl">
              Trusted real estate consultancy &amp; development in <span className="text-gold">Islamabad</span>
            </h1>
          </FadeUp>
          <FadeUp order={1} className="mt-6 flex flex-col gap-3 md:mt-8 md:flex-row">
            <div className={cx("w-full md:w-auto", hoverLiftClass)}>
              <Button variant="gold" size="lg" to="/projects" fullWidth className="md:w-auto">
                Explore projects
                <IconArrowRight size={16} />
              </Button>
            </div>
            <div className={cx("w-full md:w-auto", hoverLiftClass)}>
              <Button variant="light" size="lg" to="/contact" fullWidth className="md:w-auto">
                Contact us
              </Button>
            </div>
          </FadeUp>
          <FadeUp order={2} className="mt-8 md:mt-10">
            <CompanyNumbers layout={phone ? "panel" : "row"} />
          </FadeUp>
        </div>
      </section>

      <section aria-labelledby="featured-projects">
        <div className="mb-4 flex items-center justify-between gap-3">
          <h2 id="featured-projects" className="m-0 text-section font-extrabold text-ink">
            Featured projects
          </h2>
          <Link
            to="/projects"
            className={cx(
              "inline-flex shrink-0 items-center gap-1 text-small font-bold text-gold-text no-underline md:text-body",
              hoverLiftClass,
            )}
          >
            All projects
            <IconArrowRight size={16} />
          </Link>
        </div>

        {loading ? (
          <div className="grid grid-cols-2 gap-3 md:grid-cols-3 md:gap-4" aria-hidden="true">
            {Array.from({ length: 6 }, (_, index) => (
              <div key={index} data-testid="featured-placeholder" className="overflow-hidden rounded-card border border-line bg-card">
                <div className="aspect-[16/10] bg-track" />
                <div className="space-y-2 p-3">
                  <div className="h-4 w-2/3 rounded bg-track" />
                  <div className="h-3 w-1/2 rounded bg-track" />
                </div>
              </div>
            ))}
          </div>
        ) : error ? (
          <div className="rounded-card border border-dashed border-line px-4 py-12 text-center">
            <p className="m-0 text-body text-ink-muted">{error}</p>
            <Button variant="outline" className="mt-4" onClick={() => void reload()}>Try again</Button>
          </div>
        ) : featured.length === 0 ? (
          <div className="rounded-card border border-dashed border-line px-4 py-12 text-center text-body text-ink-muted">
            No featured projects yet.
          </div>
        ) : (
          <div className="grid grid-cols-2 gap-3 md:grid-cols-3 md:gap-4">
            {featured.map((project, index) => (
              <FadeUp key={project.id} order={index} className="min-w-0">
                <ProjectPhotoCard project={project} priority={index < 3} />
              </FadeUp>
            ))}
          </div>
        )}
      </section>
    </div>
  );
}

/** Faint gold rings in the top-right. Decoration only. */
function HeroRings() {
  return (
    <div aria-hidden="true" className="pointer-events-none absolute inset-0 overflow-hidden rounded-popup">
      <span className="absolute -top-16 -right-12 size-56 rounded-full border border-gold/25 md:-top-20 md:-right-8 md:size-72" />
      <span className="absolute -top-8 -right-4 size-36 rounded-full border border-gold/20 md:-top-8 md:right-2 md:size-48" />
      <span className="absolute top-4 right-6 size-16 rounded-full border border-gold/15 md:top-6 md:right-12 md:size-24" />
    </div>
  );
}
