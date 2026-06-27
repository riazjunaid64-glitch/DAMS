import { Link } from "react-router-dom";
import FeaturedProjectCard from "../components/FeaturedProjectCard.tsx";
import Button from "../lib/Button.tsx";
import Container from "../lib/Container.tsx";
import { COMPANY_STATS } from "../lib/companyStats.ts";
import { FEATURED_HERO_PROJECT, FEATURED_PROJECTS } from "../lib/featuredProjects.ts";

export default function HomePage() {
  return (
    <>
      {/* ─── Hero (About-style) ─── */}
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

          <div className="op-layout">
            <FeaturedProjectCard project={FEATURED_HERO_PROJECT} variant="featured" priority />

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
