import { Link } from "react-router-dom";
import Button from "../lib/Button";
import Container from "../lib/Container";

const FEATURES = [
  {
    icon: (
      <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
        <rect x="3" y="3" width="7" height="9" rx="1"/><rect x="14" y="3" width="7" height="5" rx="1"/><rect x="14" y="12" width="7" height="9" rx="1"/><rect x="3" y="16" width="7" height="5" rx="1"/>
      </svg>
    ),
    title: "Project Dashboard",
    description: "Monitor all projects at a glance with real-time status tracking and timeline views.",
  },
  {
    icon: (
      <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
        <path d="M3 9l9-7 9 7v11a2 2 0 01-2 2H5a2 2 0 01-2-2V9z"/><polyline points="9 22 9 12 15 12 15 22"/>
      </svg>
    ),
    title: "Unit Management",
    description: "Track individual units, their types, sizes, pricing, and availability across projects.",
  },
  {
    icon: (
      <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
        <path d="M17 21v-2a4 4 0 00-4-4H5a4 4 0 00-4-4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 00-3-3.87"/><path d="M16 3.13a4 4 0 010 7.75"/>
      </svg>
    ),
    title: "Team Access",
    description: "Role-based access control ensures the right people see the right information.",
  },
  {
    icon: (
      <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
        <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/>
      </svg>
    ),
    title: "Secure & Reliable",
    description: "Enterprise-grade security with JWT authentication and encrypted data storage.",
  },
  {
    icon: (
      <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
        <line x1="18" y1="20" x2="18" y2="10"/><line x1="12" y1="20" x2="12" y2="4"/><line x1="6" y1="20" x2="6" y2="14"/>
      </svg>
    ),
    title: "Analytics Ready",
    description: "Built-in reporting structure to gain insights into project performance and timelines.",
  },
  {
    icon: (
      <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
        <circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/>
      </svg>
    ),
    title: "Real-time Updates",
    description: "Stay informed with instant status updates and deadline tracking across all projects.",
  },
];

const STATS = [
  { value: "15+", label: "Years Experience" },
  { value: "500+", label: "Units Managed" },
  { value: "98%", label: "Client Satisfaction" },
  { value: "24/7", label: "System Uptime" },
];

export default function HomePage() {
  return (
    <>
      {/* ─── Hero Section ─── */}
      <section className="relative overflow-hidden">
        {/* Background Effects */}
        <div className="absolute inset-0 mesh-gradient-hero" />
        <div className="dot-grid absolute inset-0 opacity-30" />
        <div className="absolute left-1/2 top-0 h-[600px] w-[600px] -translate-x-1/2 -translate-y-1/2 rounded-full bg-indigo-500/[0.07] blur-[120px]" />
        <div className="absolute bottom-0 right-0 h-[400px] w-[400px] translate-x-1/4 translate-y-1/4 rounded-full bg-violet-500/[0.05] blur-[100px]" />

        {/* Floating Orbs */}
        <div className="absolute left-[15%] top-[20%] h-2 w-2 rounded-full bg-indigo-400/40 animate-float" />
        <div className="absolute right-[20%] top-[30%] h-1.5 w-1.5 rounded-full bg-violet-400/30 animate-float-slow" />
        <div className="absolute left-[70%] top-[60%] h-2.5 w-2.5 rounded-full bg-cyan-400/20 animate-float" />

        <Container className="relative flex min-h-[90vh] items-center justify-center py-24">
          <div className="mx-auto max-w-4xl text-center">
            {/* Badge */}
            <div className="animate-fade-in-up mb-6 inline-flex items-center gap-2 rounded-full border border-indigo-500/20 bg-indigo-500/[0.08] px-4 py-1.5">
              <span className="h-1.5 w-1.5 rounded-full bg-indigo-400 animate-pulse" />
              <span className="text-xs font-medium text-indigo-300">Deen Associate Management System</span>
            </div>

            {/* Title */}
            <h1 className="animate-fade-in-up-delay-1 text-4xl font-bold leading-[1.1] tracking-tight sm:text-6xl lg:text-7xl">
              <span className="text-white">Build, Manage &</span>
              <br />
              <span className="bg-gradient-to-r from-indigo-400 via-violet-400 to-purple-400 bg-clip-text text-transparent animate-gradient">
                Deliver with Confidence
              </span>
            </h1>

            {/* Subtitle */}
            <p className="animate-fade-in-up-delay-2 mx-auto mt-6 max-w-2xl text-base text-[#a1a1b5] sm:text-lg leading-relaxed">
              A modern workspace to track projects, align teams, and keep every stakeholder
              informed — from blueprint to handover.
            </p>

            {/* CTAs */}
            <div className="animate-fade-in-up-delay-3 mt-10 flex flex-wrap items-center justify-center gap-4">
              <Link to="/projects">
                <Button size="lg">
                  <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                    <rect x="3" y="3" width="7" height="9" rx="1"/><rect x="14" y="3" width="7" height="5" rx="1"/><rect x="14" y="12" width="7" height="9" rx="1"/><rect x="3" y="16" width="7" height="5" rx="1"/>
                  </svg>
                  Explore Projects
                </Button>
              </Link>
              <Link to="/contact">
                <Button variant="outline" size="lg">
                  Get in Touch
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                    <line x1="5" y1="12" x2="19" y2="12"/><polyline points="12 5 19 12 12 19"/>
                  </svg>
                </Button>
              </Link>
            </div>

            {/* Trust indicators */}
            <div className="animate-fade-in-up-delay-4 mt-16 flex flex-wrap items-center justify-center gap-8 border-t border-white/[0.06] pt-8">
              {STATS.map((stat) => (
                <div key={stat.label} className="text-center">
                  <p className="text-2xl font-bold text-white">{stat.value}</p>
                  <p className="mt-1 text-xs text-[#6b6b80]">{stat.label}</p>
                </div>
              ))}
            </div>
          </div>
        </Container>
      </section>

      {/* ─── Features Grid ─── */}
      <section className="relative py-24 sm:py-32">
        <div className="absolute inset-0 mesh-gradient-subtle" />
        <Container className="relative">
          <div className="mx-auto mb-16 max-w-2xl text-center">
            <div className="mb-4 inline-flex items-center gap-2">
              <span className="h-px w-8 bg-gradient-to-r from-transparent to-indigo-500" />
              <span className="text-xs font-semibold uppercase tracking-[0.2em] text-indigo-400">Features</span>
              <span className="h-px w-8 bg-gradient-to-l from-transparent to-indigo-500" />
            </div>
            <h2 className="text-3xl font-bold text-white sm:text-4xl">
              Everything you need to manage projects
            </h2>
            <p className="mt-4 text-base text-[#a1a1b5]">
              Powerful tools designed for modern project management workflows.
            </p>
          </div>

          <div className="grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
            {FEATURES.map((feature, i) => (
              <div
                key={feature.title}
                className="glass-card group p-6"
                style={{ animationDelay: `${i * 80}ms` }}
              >
                <div className="mb-4 inline-flex h-11 w-11 items-center justify-center rounded-xl bg-indigo-500/[0.1] text-indigo-400 transition-colors group-hover:bg-indigo-500/[0.15]">
                  {feature.icon}
                </div>
                <h3 className="text-base font-semibold text-white">{feature.title}</h3>
                <p className="mt-2 text-sm leading-relaxed text-[#a1a1b5]">{feature.description}</p>
              </div>
            ))}
          </div>
        </Container>
      </section>

      {/* ─── CTA Section ─── */}
      <section className="relative py-24">
        <Container>
          <div className="relative overflow-hidden rounded-3xl border border-white/[0.06] bg-gradient-to-br from-indigo-500/[0.08] via-[#16161f] to-violet-500/[0.06] p-12 sm:p-16">
            {/* Decorative */}
            <div className="absolute right-0 top-0 h-64 w-64 rounded-full bg-indigo-500/[0.08] blur-[80px]" />
            <div className="absolute bottom-0 left-0 h-48 w-48 rounded-full bg-violet-500/[0.06] blur-[60px]" />

            <div className="relative mx-auto max-w-2xl text-center">
              <h2 className="text-3xl font-bold text-white sm:text-4xl">
                Ready to streamline your projects?
              </h2>
              <p className="mt-4 text-base text-[#a1a1b5]">
                Join Deen Associate and experience the future of project management.
              </p>
              <div className="mt-8 flex flex-wrap items-center justify-center gap-4">
                <Link to="/projects">
                  <Button size="lg">View All Projects</Button>
                </Link>
                <Link to="/about">
                  <Button variant="outline" size="lg">Learn More</Button>
                </Link>
              </div>
            </div>
          </div>
        </Container>
      </section>
    </>
  );
}
