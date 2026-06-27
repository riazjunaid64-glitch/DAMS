import { Link } from "react-router-dom";
import Button from "../lib/Button.tsx";
import Container from "../lib/Container.tsx";

export default function HomePage() {
  return (
    <>
      {/* ─── Welcome Hero ─── */}
      <section className="relative overflow-hidden">
        {/* Building hero background */}
        <div className="hero-bg-image absolute inset-0" aria-hidden="true" />
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

      {/* ─── Quick Links Section ─── */}
      <section className="relative py-20 sm:py-24">
        <div className="absolute inset-0 mesh-gradient-subtle" />
        <Container className="relative">
          <div className="grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
            {[
              {
                to: "/projects",
                icon: (
                  <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
                    <rect x="3" y="3" width="7" height="9" rx="1"/><rect x="14" y="3" width="7" height="5" rx="1"/><rect x="14" y="12" width="7" height="9" rx="1"/><rect x="3" y="16" width="7" height="5" rx="1"/>
                  </svg>
                ),
                title: "Projects",
                description: "View all active and upcoming projects with timelines, status, and details.",
              },
              {
                to: "/about",
                icon: (
                  <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
                    <path d="M17 21v-2a4 4 0 00-4-4H5a4 4 0 00-4-4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 00-3-3.87"/><path d="M16 3.13a4 4 0 010 7.75"/>
                  </svg>
                ),
                title: "About Deen Associate",
                description: "Learn about our experience, values, and the team behind every project.",
              },
              {
                to: "/contact",
                icon: (
                  <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
                    <path d="M4 4h16c1.1 0 2 .9 2 2v12c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2V6c0-1.1.9-2 2-2z"/><polyline points="22,6 12,13 2,6"/>
                  </svg>
                ),
                title: "Contact Us",
                description: "Get in touch with our team for inquiries, support, or project discussions.",
              },
            ].map((card, i) => (
              <Link
                key={card.to}
                to={card.to}
                className="glass-card group block p-6 animate-fade-in-up"
                style={{ animationDelay: `${i * 80}ms` }}
              >
                <div className="mb-4 inline-flex h-11 w-11 items-center justify-center rounded-xl bg-[var(--accent-glow)] text-[var(--accent)] transition-colors group-hover:bg-[var(--accent-glow-strong)]">
                  {card.icon}
                </div>
                <h3 className="text-base font-semibold text-[var(--text-heading)]">{card.title}</h3>
                <p className="mt-2 text-sm leading-relaxed text-[var(--text-secondary)]">{card.description}</p>
                <div className="mt-4 flex items-center gap-1.5 text-xs font-semibold text-[var(--accent)] opacity-70 transition-all group-hover:translate-x-1 group-hover:opacity-100">
                  <span>Go to {card.title.toLowerCase()}</span>
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round">
                    <line x1="5" y1="12" x2="19" y2="12"/><polyline points="12 5 19 12 12 19"/>
                  </svg>
                </div>
              </Link>
            ))}
          </div>
        </Container>
      </section>
    </>
  );
}
