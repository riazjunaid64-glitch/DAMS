import Container from "../lib/Container";
import Section from "../lib/Section";

export default function AboutPage() {
  return (
    <main>
      <Section
        eyebrow="About Us"
        title="Building more than structures"
        description="This section is a placeholder for your real story and values."
        className="bg-slate-900/70 py-16 sm:py-20"
      >
        <Container>
          <div className="grid gap-10 lg:grid-cols-[1.1fr_0.9fr] lg:items-center">
            <div className="space-y-6 text-slate-300">
              <p>
                At Deen Associate, we focus on transparency, efficiency, and modern management
                practices. Use this space to explain the vision, mission, and the experience your
                team brings to every project.
              </p>
              <p>
                You can also highlight your service standards, client relationships, and any
                operational advantages that set you apart.
              </p>
            </div>
            <div className="rounded-3xl border border-white/10 bg-gradient-to-br from-slate-800 to-slate-950 p-8">
              <div className="grid gap-6 sm:grid-cols-2">
                {[
                  { label: "Years Experience", value: "15+" },
                  { label: "Units Delivered", value: "500+" },
                  { label: "Client Satisfaction", value: "98%" },
                  { label: "Major Projects", value: "3" },
                ].map((item) => (
                  <div key={item.label}>
                    <p className="text-2xl font-semibold text-amber-300">{item.value}</p>
                    <p className="text-sm text-slate-300">{item.label}</p>
                  </div>
                ))}
              </div>
            </div>
          </div>
        </Container>
      </Section>
    </main>
  );
}
