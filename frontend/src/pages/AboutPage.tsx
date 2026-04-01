import Container from "../lib/Container";
import Section from "../lib/Section";

const VALUES = [
  {
    icon: (
      <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
        <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/>
      </svg>
    ),
    title: "Transparency",
    description: "Every decision, every report, every milestone — shared openly with all stakeholders.",
  },
  {
    icon: (
      <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
        <polyline points="22 12 18 12 15 21 9 3 6 12 2 12"/>
      </svg>
    ),
    title: "Efficiency",
    description: "Streamlined workflows and processes to reduce delays and maximize output.",
  },
  {
    icon: (
      <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
        <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/>
      </svg>
    ),
    title: "Reliability",
    description: "Consistent delivery, trusted partnerships, and unwavering commitment to quality.",
  },
];

export default function AboutPage() {
  return (
    <>
      {/* Hero Section */}
      <div className="relative overflow-hidden border-b border-[var(--border)]">
        <div className="absolute inset-0 mesh-gradient-subtle" />
        <Container className="relative py-16 sm:py-24">
          <Section
            eyebrow="About Us"
            title="Building more than structures"
            description="At Deen Associate, we focus on transparency, efficiency, and modern management practices to deliver exceptional results."
          >
            <div />
          </Section>
        </Container>
      </div>

      {/* Stats Section */}
      <div className="py-16 sm:py-20">
        <Container>
          <div className="grid gap-5 sm:grid-cols-2 lg:grid-cols-4">
            {[
              { value: "15+", label: "Years Experience", color: "from-indigo-500 to-violet-600" },
              { value: "500+", label: "Units Delivered", color: "from-emerald-500 to-teal-600" },
              { value: "98%", label: "Client Satisfaction", color: "from-amber-500 to-orange-600" },
              { value: "3+", label: "Major Projects", color: "from-rose-500 to-pink-600" },
            ].map((stat, i) => (
              <div
                key={stat.label}
                className="glass-card group relative overflow-hidden p-6 text-center animate-fade-in-up"
                style={{ animationDelay: `${i * 80}ms` }}
              >
                <div className={`pointer-events-none absolute -right-8 -top-8 h-24 w-24 rounded-full bg-gradient-to-br ${stat.color} opacity-[0.06] blur-xl transition-opacity group-hover:opacity-[0.12]`} />
                <p className="relative text-3xl font-bold text-[var(--text-heading)]">{stat.value}</p>
                <p className="relative mt-1.5 text-sm text-[var(--text-muted)]">{stat.label}</p>
              </div>
            ))}
          </div>
        </Container>
      </div>

      {/* Story + Values */}
      <div className="relative border-y border-white/[0.04] py-16 sm:py-20">
        <div className="absolute inset-0 mesh-gradient-subtle" />
        <Container className="relative">
          <div className="grid gap-12 lg:grid-cols-2 lg:items-center">
            <div className="space-y-6">
              <div className="inline-flex items-center gap-2">
                <span className="h-px w-8 bg-gradient-to-r from-transparent to-indigo-500" />
                <span className="text-xs font-semibold uppercase tracking-[0.2em] text-indigo-500">Our Story</span>
              </div>
              <h2 className="text-2xl font-bold text-[var(--text-heading)] sm:text-3xl">
                Driven by a vision for quality and accountability
              </h2>
              <div className="space-y-4 text-sm leading-relaxed text-[var(--text-secondary)]">
                <p>
                  At Deen Associate, we focus on transparency, efficiency, and modern management
                  practices. Our team brings over 15 years of experience in project development
                  and management, ensuring every project meets the highest standards.
                </p>
                <p>
                  We believe in building lasting relationships through trust,
                  consistent delivery, and a commitment to excellence. Our approach
                  to project management emphasizes clear communication and operational transparency.
                </p>
              </div>
            </div>

            {/* Values */}
            <div className="space-y-4">
              {VALUES.map((value, i) => (
                <div
                  key={value.title}
                  className="glass-card flex items-start gap-4 p-5 animate-fade-in-up"
                  style={{ animationDelay: `${i * 100}ms` }}
                >
                  <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-indigo-500/[0.1] text-indigo-500">
                    {value.icon}
                  </div>
                  <div>
                    <h3 className="text-sm font-semibold text-[var(--text-heading)]">{value.title}</h3>
                    <p className="mt-1 text-sm text-[var(--text-secondary)]">{value.description}</p>
                  </div>
                </div>
              ))}
            </div>
          </div>
        </Container>
      </div>
    </>
  );
}
