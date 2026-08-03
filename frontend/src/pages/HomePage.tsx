import { Link } from "react-router-dom";
import { resolveMediaUrl } from "../api/api";
import { useProjects } from "../contexts/projectsContextValue";
import { COMPANY_STATS } from "../lib/companyStats.ts";
import { isPubliclyVisible } from "../utils/projectStatus.ts";

export default function HomePage() {
  const { projects, loading, error, reload } = useProjects();
  const featured = projects.filter((p) => isPubliclyVisible(p.status)).slice(0, 6);

  return (
    <div className="dash-home">
      {/* ─── Hero + stats fill the first viewport ─── */}
      <section className="dash-top">
        <div className="dash-hero">
          <div className="dash-hero__bg" aria-hidden="true">
            <img src="/images/bakcgound-image.png" alt="" fetchPriority="high" decoding="async" />
          </div>
          <div className="dash-hero__overlay" aria-hidden="true" />
          <div className="dash-hero__inner">
            <h1 className="dash-hero__brand">Deen Associates</h1>
            <p className="dash-hero__sub">
              Trusted Real Estate Consultancy &amp; Development in Islamabad.
            </p>
            <Link to="/projects" className="dash-btn-gold">
              Explore Projects
              <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round">
                <line x1="5" y1="12" x2="19" y2="12" /><polyline points="12 5 19 12 12 19" />
              </svg>
            </Link>
          </div>
        </div>

        <div className="dash-stats">
          {COMPANY_STATS.map((stat) => (
            <div key={stat.label} className="dash-stat">
              <p className="dash-stat__value">{stat.value}</p>
              <p className="dash-stat__label">{stat.label}</p>
            </div>
          ))}
        </div>
      </section>

      {/* ─── Featured Projects ─── */}
      <h2 className="dash-section-head">Featured Projects</h2>

      {loading ? (
        <div className="dash-proj-grid">
          {[...Array(6)].map((_, i) => (
            <div key={i} className="dash-proj">
              <div className="dash-proj__thumb skeleton" />
              <div className="dash-proj__body">
                <div className="skeleton mb-2 h-5 w-2/3" />
                <div className="skeleton h-4 w-1/2" />
              </div>
              <div className="dash-proj__foot">
                <div className="skeleton h-8 w-full" />
              </div>
            </div>
          ))}
        </div>
      ) : error ? (
        <div className="dash-empty">
          <p>{error}</p>
          <button type="button" className="dash-btn-gold mt-4" onClick={() => void reload()}>Try Again</button>
        </div>
      ) : featured.length === 0 ? (
        <div className="dash-empty">No featured projects yet.</div>
      ) : (
        <div className="dash-proj-grid">
          {featured.map((project, index) => {
            const img = resolveMediaUrl(project.coverImageUrl);
            return (
              <Link key={project.id} to={`/projects/${project.id}`} className="dash-proj" aria-label={project.projectName}>
                <div className="dash-proj__thumb">
                  {img ? (
                    <img src={img} alt={project.projectName} loading={index < 3 ? "eager" : "lazy"} decoding="async" />
                  ) : null}
                  {project.category && <span className="dash-proj__badge">{project.category}</span>}
                </div>
                <div className="dash-proj__body">
                  <h3 className="dash-proj__title">{project.projectName}</h3>
                  <p className="dash-proj__meta">
                    <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                      <path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0118 0z" /><circle cx="12" cy="10" r="3" />
                    </svg>
                    {project.location}
                  </p>
                </div>
                <div className="dash-proj__foot">
                  <span className="dash-proj__cta">View Details</span>
                </div>
              </Link>
            );
          })}
        </div>
      )}
    </div>
  );
}
