import { Link } from "react-router-dom";
import FeaturedProjectCard from "../components/FeaturedProjectCard.tsx";
import { useProjects } from "../contexts/projectsContextValue";
import Button from "../lib/Button.tsx";
import Container from "../lib/Container.tsx";
import { COMPANY_STATS } from "../lib/companyStats.ts";
import { isPubliclyVisible } from "../utils/projectStatus.ts";

export default function HomePage() {
  const { projects, loading, error, reload } = useProjects();
  const publicProjects = projects.filter((p) => isPubliclyVisible(p.status));
  const [hero, ...rest] = publicProjects;

  return (
    <>
      {/* ─── Hero ─── */}
      <section className="home-hero">
        <div className="home-hero__bg" aria-hidden="true">
          <img
            src="/images/bakcgound-image.png"
            alt=""
            className="home-hero__bg-img"
            fetchPriority="high"
            decoding="async"
          />
        </div>

        <Container className="home-hero__inner">
          <p className="home-hero__eyebrow">Who We Are</p>
          <h1 className="home-hero__title">Deen Associate</h1>
          <p className="home-hero__lead">
            A trusted real estate consultancy and development firm in Islamabad — helping clients buy, sell, and invest
            in residential, commercial, and mixed-use properties with confidence.
          </p>

          <div className="home-hero__actions">
            <Link to="/projects">
              <Button size="lg">View Projects</Button>
            </Link>
            <Link to="/contact">
              <Button variant="outline" size="lg" className="home-hero__outline-btn">
                Get in Touch
              </Button>
            </Link>
          </div>
        </Container>
      </section>

      {/* ─── Stats bar ─── */}
      <section className="about-stats">
        <Container>
          <div className="about-stats__grid">
            {COMPANY_STATS.map((stat) => (
              <div key={stat.label} className="about-stats__item">
                <p className="about-stats__value">{stat.value}</p>
                <p className="about-stats__label">{stat.label}</p>
              </div>
            ))}
          </div>
        </Container>
      </section>

      {/* ─── Featured Projects ─── */}
      <section className="featured-projects section-pad">
        <div className="featured-projects__inner">
          <div className="featured-projects__header">
            <h2 className="featured-projects__title">Featured Projects</h2>
            <p className="featured-projects__subtitle">
              Explore premium residential and commercial developments by Deen Associate across Islamabad and beyond.
            </p>
          </div>

          {loading ? (
            <div className="op-layout">
              <div className="op-card op-card--featured op-card--skeleton">
                <div className="skeleton op-skeleton-thumb" />
                <div className="op-body">
                  <div className="skeleton mb-3 h-6 w-3/4" />
                  <div className="skeleton h-4 w-1/2" />
                </div>
              </div>
              <div className="op-grid">
                {[...Array(6)].map((_, i) => (
                  <div key={i} className="op-card op-card--skeleton">
                    <div className="skeleton op-skeleton-thumb" />
                    <div className="op-body">
                      <div className="skeleton mb-3 h-5 w-2/3" />
                      <div className="skeleton h-4 w-full" />
                    </div>
                  </div>
                ))}
              </div>
            </div>
          ) : error ? (
            <div className="flex flex-col items-center gap-4 py-10 text-center">
              <p className="text-sm text-[var(--text-secondary)]">{error}</p>
              <Button variant="outline" size="sm" onClick={() => void reload()}>
                Try Again
              </Button>
            </div>
          ) : (
            <div className="op-layout">
              {hero && <FeaturedProjectCard project={hero} variant="featured" priority />}
              <div className="op-grid">
                {rest.map((project, index) => (
                  <FeaturedProjectCard key={project.id} project={project} priority={index < 3} />
                ))}
              </div>
            </div>
          )}
        </div>
      </section>
    </>
  );
}
