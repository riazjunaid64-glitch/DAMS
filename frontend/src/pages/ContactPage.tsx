import { useState, type FormEvent } from "react";
import Button from "../lib/Button.tsx";
import Container from "../lib/Container.tsx";
import Field from "../lib/Field.tsx";
import Section from "../lib/Section.tsx";
import { SITE_CONTACT } from "../lib/siteContact.ts";

const CONTACT_INFO = [
  {
    icon: (
      <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
        <path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0118 0z"/><circle cx="12" cy="10" r="3"/>
      </svg>
    ),
    label: "Visit Our Office",
    value: SITE_CONTACT.address,
  },
  {
    icon: (
      <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
        <path d="M22 16.92v3a2 2 0 01-2.18 2 19.79 19.79 0 01-8.63-3.07 19.5 19.5 0 01-6-6 19.79 19.79 0 01-3.07-8.67A2 2 0 014.11 2h3a2 2 0 012 1.72c.127.96.361 1.903.7 2.81a2 2 0 01-.45 2.11L8.09 9.91a16 16 0 006 6l1.27-1.27a2 2 0 012.11-.45c.907.339 1.85.573 2.81.7A2 2 0 0122 16.92z"/>
      </svg>
    ),
    label: "Call Us",
    value: `${SITE_CONTACT.phone} / ${SITE_CONTACT.phoneMobile}`,
  },
  {
    icon: (
      <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
        <path d="M4 4h16c1.1 0 2 .9 2 2v12c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2V6c0-1.1.9-2 2-2z"/><polyline points="22,6 12,13 2,6"/>
      </svg>
    ),
    label: "Email Us",
    value: SITE_CONTACT.email,
  },
  {
    icon: (
      <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
        <circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/>
      </svg>
    ),
    label: "Support Hours",
    value: SITE_CONTACT.hours,
  },
];

export default function ContactPage() {
  const [form, setForm] = useState({ name: "", email: "", message: "" });
  const [submitted, setSubmitted] = useState(false);

  const handleSubmit = (e: FormEvent) => {
    e.preventDefault();
    // Simulate form submission
    setSubmitted(true);
    setTimeout(() => setSubmitted(false), 4000);
    setForm({ name: "", email: "", message: "" });
  };

  return (
    <>
      {/* Header */}
      <div className="relative overflow-hidden border-b border-[var(--border)]">
        <div className="absolute inset-0 mesh-gradient-subtle" />
        <Container className="relative py-16 sm:py-20">
          <Section
            eyebrow="Contact"
            title="Start the Conversation"
            description="Have a question about our services or want to discuss a project? We'd love to hear from you."
          >
            <div />
          </Section>
        </Container>
      </div>

      {/* Content */}
      <div className="py-16 sm:py-20">
        <Container>
          <div className="grid gap-8 lg:grid-cols-5 lg:items-start">
            {/* Contact Cards */}
            <div className="space-y-4 lg:col-span-2">
              {CONTACT_INFO.map((info, i) => (
                <div
                  key={info.label}
                  className="glass-card flex items-start gap-4 p-5 animate-fade-in-up"
                  style={{ animationDelay: `${i * 80}ms` }}
                >
                  <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-indigo-500/[0.1] text-indigo-500">
                    {info.icon}
                  </div>
                  <div>
                    <h3 className="text-sm font-semibold text-[var(--text-heading)]">{info.label}</h3>
                    <p className="mt-1 text-sm text-[var(--text-secondary)]">{info.value}</p>
                  </div>
                </div>
              ))}

              {/* Map */}
              <div className="glass-card overflow-hidden animate-fade-in-up" style={{ animationDelay: "320ms" }}>
                <iframe
                  title="Deen Associate office location"
                  src={SITE_CONTACT.mapsEmbedUrl}
                  loading="lazy"
                  referrerPolicy="no-referrer-when-downgrade"
                  className="block h-48 w-full border-0"
                />
                <a
                  href={SITE_CONTACT.mapsUrl}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="block bg-[var(--accent)] px-4 py-3 text-center text-sm font-semibold text-[var(--accent-warm)] transition hover:bg-[var(--accent-light)] hover:text-white"
                >
                  Open in Google Maps
                </a>
              </div>
            </div>

            {/* Contact Form */}
            <div className="lg:col-span-3">
              <div className="glass-card animate-fade-in-up overflow-hidden" style={{ animationDelay: "160ms" }}>
                <div className="border-b border-[var(--border)] px-6 py-4">
                  <h3 className="text-lg font-semibold text-[var(--text-heading)]">Send us a message</h3>
                  <p className="mt-0.5 text-xs text-[var(--text-muted)]">Fill out the form and we'll get back to you within 24 hours.</p>
                </div>

                <form onSubmit={handleSubmit} className="p-6 space-y-4">
                  <div className="grid gap-4 sm:grid-cols-2">
                    <Field
                      label="Full Name"
                      value={form.name}
                      onChange={(e) => setForm((prev) => ({ ...prev, name: e.target.value }))}
                      placeholder="Your name"
                    />
                    <Field
                      label="Email Address"
                      type="email"
                      value={form.email}
                      onChange={(e) => setForm((prev) => ({ ...prev, email: e.target.value }))}
                      placeholder="you@example.com"
                    />
                  </div>
                  <Field
                    label="Message"
                    as="textarea"
                    value={form.message}
                    onChange={(e) => setForm((prev) => ({ ...prev, message: e.target.value }))}
                    placeholder="Tell us about your project or question..."
                  />

                  {submitted && (
                    <div className="rounded-xl border border-emerald-500/20 bg-emerald-500/[0.06] px-4 py-3 text-sm text-emerald-300 flex items-center gap-2 animate-scale-in">
                      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                        <path d="M22 11.08V12a10 10 0 11-5.93-9.14"/><polyline points="22 4 12 14.01 9 11.01"/>
                      </svg>
                      Message sent! We'll get back to you soon.
                    </div>
                  )}

                  <div className="flex justify-end pt-2">
                    <Button type="submit" size="lg">
                      Send Message
                      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                        <line x1="22" y1="2" x2="11" y2="13"/><polygon points="22 2 15 22 11 13 2 9 22 2"/>
                      </svg>
                    </Button>
                  </div>
                </form>
              </div>
            </div>
          </div>
        </Container>
      </div>
    </>
  );
}
