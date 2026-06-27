import { Link } from "react-router-dom";
import FeaturedProjectCard from "../components/FeaturedProjectCard.tsx";
import Button from "../lib/Button.tsx";
import Container from "../lib/Container.tsx";
import { FEATURED_HERO_PROJECT, FEATURED_PROJECTS } from "../lib/featuredProjects.ts";

export default function HomePage() {
  return (
    <>
      {/* ─── Welcome Hero ─── */}
      <section className="relative overflow-hidden">
        {/* Building hero background */}
        <div className="hero-bg-image absolute inset-0" aria-hidden="true">
          <img
            src="/images/home-hero-bg-sm.jpg"
            alt=""
            className="hero-bg-image__img"
            fetchPriority="high"
            decoding="async"
          />
        </div>
        <div className="hero-bg-overlay absolute inset-0" aria-hidden="true" />

        <Container className="relative flex min-h-[85vh] items-center justify-center py-24">
          <div className="hero-copy-panel animate-fade-in-up mx-auto max-w-4xl px-8 py-12 text-center sm:px-12 sm:py-14">
            {/* Badge */}
            <div className="mb-6 inline-flex items-center gap-2 rounded-full border border-[var(--accent-warm)]/35 bg-white/10 px-4 py-1.5">
              <span className="h-1.5 w-1.5 rounded-full bg-[var(--accent-warm)] animate-pulse" />
              <span className="text-xs font-semibold uppercase tracking-wider text-[var(--accent-warm)]">Deen Associate</span>
            </div>

            {/* Title */}
            <h1 className="animate-fade-in-up-delay-1 text-4xl font-bold leading-[1.05] tracking-tight sm:text-6xl lg:text-7xl">
              <span className="block text-white">Welcome to</span>
              <span className="mt-1 block text-[var(--accent-warm)]">Deen Associate</span>
            </h1>

            {/* Subtitle */}
            <p className="animate-fade-in-up-delay-2 mx-auto mt-6 max-w-2xl text-base text-[#f0e0e2] sm:text-lg leading-relaxed">
              Manage and track all your projects, units, and progress — everything in one place.
            </p>

            {/* CTAs */}
            <div className="animate-fade-in-up-delay-3 mt-10 flex flex-wrap items-center justify-center gap-4">
              <Link to="/projects">
                <Button size="lg">
                  <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                    <rect x="3" y="3" width="7" height="9" rx="1"/><rect x="14" y="3" width="7" height="5" rx="1"/><rect x="14" y="12" width="7" height="9" rx="1"/><rect x="3" y="16" width="7" height="5" rx="1"/>
                  </svg>
                  View Projects
                </Button>
              </Link>
              <Link to="/contact">
                <Button
                  variant="outline"
                  size="lg"
                  className="!border-white/50 !text-white hover:!border-white hover:!bg-white/10 hover:!text-white"
                >
                  Contact Us
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                    <line x1="5" y1="12" x2="19" y2="12"/><polyline points="12 5 19 12 12 19"/>
                  </svg>
                </Button>
              </Link>
            </div>
          </div>
        </Container>
      </section>

      {/* ─── Featured Projects (Floria-style grid) ─── */}
      <section className="featured-projects section-pad">
        <div className="featured-projects__inner">
          <div className="featured-projects__header">
            <h2 className="featured-projects__title">Featured Projects</h2>
            <p className="featured-projects__subtitle">
              Explore premium residential and commercial developments by Deen Associate across Islamabad and beyond.
            </p>
          </div>

          <div className="op-layout">
            <FeaturedProjectCard
              project={FEATURED_HERO_PROJECT}
              variant="featured"
              priority
            />

            <div className="op-grid">
              {FEATURED_PROJECTS.map((project, index) => (
                <FeaturedProjectCard key={project.id} project={project} priority={index < 3} />
              ))}
            </div>
          </div>
        </div>
      </section>
    </>
  );
}
