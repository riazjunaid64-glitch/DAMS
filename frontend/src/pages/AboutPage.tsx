import Container from "../lib/Container";
import Section from "../lib/Section";

const TEAM = [
  { name: "Ahmed Khan", role: "CEO & Founder", initials: "AK" },
  { name: "Sara Malik", role: "Head of Operations", initials: "SM" },
  { name: "Usman Ali", role: "Lead Architect", initials: "UA" },
  { name: "Fatima Noor", role: "Client Relations", initials: "FN" },
];

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
    description: "Streamlined workflows and smart tools to reduce delays and maximize output.",
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
      <div className="relative overflow-hidden border-b border-white/[0.04]">
        <div className="absolute inset-0 mesh-gradient-subtle" />
        <Container className="relative py-16 sm:py-24">
          <Section
            eyebrow="About Us"
            title="Building more than structures"
            description="At Deen Associate, we combine modern management with deep industry expertise to deliver exceptional results for every project."
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
                <p className="relative text-3xl font-bold text-white">{stat.value}</p>
                <p className="relative mt-1.5 text-sm text-[#6b6b80]">{stat.label}</p>
              </div>
            ))}
          </div>
        </Container>
      </div>

      {/* Story Section */}
      <div className="relative border-y border-white/[0.04] py-16 sm:py-20">
        <div className="absolute inset-0 mesh-gradient-subtle" />
        <Container className="relative">
          <div className="grid gap-12 lg:grid-cols-2 lg:items-center">
            <div className="space-y-6">
              <div className="inline-flex items-center gap-2">
                <span className="h-px w-8 bg-gradient-to-r from-transparent to-indigo-500" />
                <span className="text-xs font-semibold uppercase tracking-[0.2em] text-indigo-400">Our Story</span>
              </div>
              <h2 className="text-2xl font-bold text-white sm:text-3xl">
                Driven by a vision for modern project management
              </h2>
              <div className="space-y-4 text-sm leading-relaxed text-[#a1a1b5]">
                <p>
                  At Deen Associate, we focus on transparency, efficiency, and modern management
                  practices. Our team brings over 15 years of experience in project development
                  and management, ensuring every project meets the highest standards.
                </p>
                <p>
                  We believe in building lasting relationships with our clients through trust,
                  consistent delivery, and a commitment to innovation. Our digital-first approach
                  sets us apart from traditional management firms.
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
                  <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-indigo-500/[0.1] text-indigo-400">
                    {value.icon}
                  </div>
                  <div>
                    <h3 className="text-sm font-semibold text-white">{value.title}</h3>
                    <p className="mt-1 text-sm text-[#a1a1b5]">{value.description}</p>
                  </div>
                </div>
              ))}
            </div>
          </div>
        </Container>
      </div>

      {/* Team Section */}
      <div className="py-16 sm:py-20">
        <Container>
          <div className="mb-12 text-center">
            <div className="mb-4 inline-flex items-center gap-2">
              <span className="h-px w-8 bg-gradient-to-r from-transparent to-indigo-500" />
              <span className="text-xs font-semibold uppercase tracking-[0.2em] text-indigo-400">Our Team</span>
              <span className="h-px w-8 bg-gradient-to-l from-transparent to-indigo-500" />
            </div>
            <h2 className="text-2xl font-bold text-white sm:text-3xl">
              The people behind the projects
            </h2>
          </div>

          <div className="grid gap-5 sm:grid-cols-2 lg:grid-cols-4">
            {TEAM.map((member, i) => (
              <div
                key={member.name}
                className="glass-card group p-6 text-center animate-fade-in-up"
                style={{ animationDelay: `${i * 80}ms` }}
              >
                <div className="mx-auto mb-4 flex h-16 w-16 items-center justify-center rounded-2xl bg-gradient-to-br from-indigo-500/20 to-violet-500/20 border border-indigo-500/10 text-xl font-bold text-indigo-300 transition-transform group-hover:scale-105">
                  {member.initials}
                </div>
                <h3 className="text-sm font-semibold text-white">{member.name}</h3>
                <p className="mt-1 text-xs text-[#6b6b80]">{member.role}</p>
              </div>
            ))}
          </div>
        </Container>
      </div>
    </>
  );
}
