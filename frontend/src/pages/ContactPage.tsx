import { useState, type FormEvent } from "react";
import Button from "../lib/Button.tsx";
import Container from "../lib/Container.tsx";
import Field from "../lib/Field.tsx";
import { SITE_CONTACT } from "../lib/siteContact.ts";

const INQUIRY_TOPICS = [
  "Project booking & availability",
  "Site visit appointment",
  "Payment plans & investment",
  "Commercial & mixed-use units",
  "Resale & property management",
  "General enquiry",
];

const HELP_WITH = [
  {
    title: "Bookings & Reservations",
    detail: "Secure your unit in CPEC Greens, Deen Square, Mall of Faisal Hills, and other active developments.",
  },
  {
    title: "Site Visits",
    detail: "Schedule a guided visit to our projects and sales office in G-8 Markaz, Islamabad.",
  },
  {
    title: "Investment Advice",
    detail: "Understand payment plans, ROI potential, and the right property type for your goals.",
  },
  {
    title: "After-Sales Support",
    detail: "Documentation, handover queries, and ongoing client support from our reception team.",
  },
];

function PinIcon() {
  return (
    <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0118 0z" />
      <circle cx="12" cy="10" r="3" />
    </svg>
  );
}

function PhoneIcon() {
  return (
    <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M22 16.92v3a2 2 0 01-2.18 2 19.79 19.79 0 01-8.63-3.07 19.5 19.5 0 01-6-6 19.79 19.79 0 01-3.07-8.67A2 2 0 014.11 2h3a2 2 0 012 1.72c.127.96.361 1.903.7 2.81a2 2 0 01-.45 2.11L8.09 9.91a16 16 0 006 6l1.27-1.27a2 2 0 012.11-.45c.907.339 1.85.573 2.81.7A2 2 0 0122 16.92z" />
    </svg>
  );
}

function MailIcon() {
  return (
    <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M4 4h16c1.1 0 2 .9 2 2v12c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2V6c0-1.1.9-2 2-2z" />
      <polyline points="22,6 12,13 2,6" />
    </svg>
  );
}

function ClockIcon() {
  return (
    <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <circle cx="12" cy="12" r="10" />
      <polyline points="12 6 12 12 16 14" />
    </svg>
  );
}

function WhatsAppIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
      <path d="M17.472 14.382c-.297-.149-1.758-.867-2.03-.967-.273-.099-.471-.148-.67.15-.197.297-.767.966-.94 1.164-.173.199-.347.223-.644.075-.297-.15-1.255-.463-2.39-1.475-.883-.788-1.48-1.761-1.653-2.059-.173-.297-.018-.458.13-.606.134-.133.298-.347.446-.52.149-.174.198-.298.298-.497.099-.198.05-.371-.025-.52-.075-.149-.669-1.612-.916-2.207-.242-.579-.487-.5-.669-.51-.173-.008-.371-.01-.57-.01-.198 0-.52.074-.792.372-.272.297-1.04 1.016-1.04 2.479 0 1.462 1.065 2.875 1.213 3.074.149.198 2.096 3.2 5.077 4.487.709.306 1.262.489 1.694.625.712.227 1.36.195 1.871.118.571-.085 1.758-.719 2.006-1.413.248-.694.248-1.289.173-1.413-.074-.124-.272-.198-.57-.347m-5.421 7.403h-.004a9.87 9.87 0 01-5.031-1.378l-.361-.214-3.741.982.998-3.648-.235-.374a9.86 9.86 0 01-1.51-5.26c.001-5.45 4.436-9.884 9.888-9.884 2.64 0 5.122 1.03 6.988 2.898a9.825 9.825 0 012.893 6.994c-.003 5.45-4.435 9.884-9.884 9.884m8.413-18.297A11.815 11.815 0 0012.05 0C5.495 0 .16 5.335.157 11.892c0 2.096.547 4.142 1.588 5.945L.057 24l6.305-1.654a11.882 11.882 0 005.683 1.448h.005c6.554 0 11.89-5.335 11.893-11.893a11.821 11.821 0 00-3.48-8.413z" />
    </svg>
  );
}

export default function ContactPage() {
  const [form, setForm] = useState({
    name: "",
    email: "",
    phone: "",
    topic: INQUIRY_TOPICS[0],
    message: "",
  });
  const [submitted, setSubmitted] = useState(false);

  const handleSubmit = (e: FormEvent) => {
    e.preventDefault();

    const lines = [
      `Name: ${form.name}`,
      `Email: ${form.email}`,
      form.phone ? `Phone: ${form.phone}` : null,
      `Topic: ${form.topic}`,
      "",
      form.message,
    ].filter(Boolean);

    const text = encodeURIComponent(lines.join("\n"));
    window.open(`${SITE_CONTACT.whatsappUrl}?text=${text}`, "_blank", "noopener,noreferrer");

    setSubmitted(true);
    setTimeout(() => setSubmitted(false), 5000);
    setForm({
      name: "",
      email: "",
      phone: "",
      topic: INQUIRY_TOPICS[0],
      message: "",
    });
  };

  return (
    <>
      <section className="contact-hero">
        <Container className="contact-hero__inner">
          <p className="about-eyebrow">Contact Us</p>
          <h1 className="contact-hero__title">Get in Touch</h1>
          <p className="contact-hero__lead">
            Visit our head office in G-8 Markaz, call our team, or send an enquiry about bookings, site visits,
            payment plans, and investment opportunities across Deen Associate projects.
          </p>

          <div className="contact-hero__actions">
            <a href={SITE_CONTACT.whatsappUrl} target="_blank" rel="noopener noreferrer">
              <Button size="lg">
                <WhatsAppIcon />
                Make an Appointment
              </Button>
            </a>
            <a href={`tel:${SITE_CONTACT.phoneTel}`}>
              <Button variant="outline" size="lg">
                Call Office
              </Button>
            </a>
          </div>
        </Container>
      </section>

      <section className="contact-main">
        <Container>
          <div className="contact-main__grid">
            <aside className="contact-sidebar">
              <div className="contact-card">
                <span className="contact-card__icon">
                  <PinIcon />
                </span>
                <div>
                  <h2>Head Office</h2>
                  <p>{SITE_CONTACT.address}</p>
                  <a href={SITE_CONTACT.mapsUrl} target="_blank" rel="noopener noreferrer" className="contact-card__link">
                    Open in Google Maps
                  </a>
                </div>
              </div>

              <div className="contact-card">
                <span className="contact-card__icon">
                  <PhoneIcon />
                </span>
                <div>
                  <h2>Phone</h2>
                  <p>
                    <a href={`tel:${SITE_CONTACT.phoneTel}`}>{SITE_CONTACT.phone}</a>
                    <br />
                    <a href={`tel:${SITE_CONTACT.phoneMobileTel}`}>{SITE_CONTACT.phoneMobile}</a>
                  </p>
                </div>
              </div>

              <div className="contact-card">
                <span className="contact-card__icon">
                  <MailIcon />
                </span>
                <div>
                  <h2>Email</h2>
                  <p>
                    <a href={`mailto:${SITE_CONTACT.email}`}>{SITE_CONTACT.email}</a>
                  </p>
                  <a href={SITE_CONTACT.website} target="_blank" rel="noopener noreferrer" className="contact-card__link">
                    thedeenassociates.com
                  </a>
                </div>
              </div>

              <div className="contact-card">
                <span className="contact-card__icon">
                  <ClockIcon />
                </span>
                <div>
                  <h2>Office Hours</h2>
                  <p>{SITE_CONTACT.hours}</p>
                  <p className="contact-card__note">WhatsApp enquiries welcome for faster booking support.</p>
                </div>
              </div>

              <div className="contact-map">
                <iframe
                  title="Deen Associate office location"
                  src={SITE_CONTACT.mapsEmbedUrl}
                  loading="lazy"
                  referrerPolicy="no-referrer-when-downgrade"
                  className="contact-map__frame"
                />
                <a
                  href={SITE_CONTACT.mapsUrl}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="contact-map__cta"
                >
                  Get Directions
                </a>
              </div>
            </aside>

            <div className="contact-form-panel">
              <div className="contact-form-panel__header">
                <p className="about-eyebrow">Send Enquiry</p>
                <h2 className="contact-form-panel__title">Tell us how we can help</h2>
                <p className="contact-form-panel__intro">
                  Share your details and our team will respond with project information, availability, or a site visit
                  appointment. Your message opens in WhatsApp — the same channel we use for client bookings.
                </p>
              </div>

              <form onSubmit={handleSubmit} className="contact-form">
                <div className="contact-form__row">
                  <Field
                    label="Full Name"
                    value={form.name}
                    onChange={(e) => setForm((prev) => ({ ...prev, name: e.target.value }))}
                    placeholder="Your full name"
                    required
                  />
                  <Field
                    label="Phone Number"
                    type="tel"
                    value={form.phone}
                    onChange={(e) => setForm((prev) => ({ ...prev, phone: e.target.value }))}
                    placeholder="+92 3XX XXXXXXX"
                  />
                </div>

                <Field
                  label="Email Address"
                  type="email"
                  value={form.email}
                  onChange={(e) => setForm((prev) => ({ ...prev, email: e.target.value }))}
                  placeholder="you@example.com"
                  required
                />

                <label className="contact-form__select-label">
                  <span>Enquiry Type</span>
                  <select
                    value={form.topic}
                    onChange={(e) => setForm((prev) => ({ ...prev, topic: e.target.value }))}
                    className="contact-form__select"
                  >
                    {INQUIRY_TOPICS.map((topic) => (
                      <option key={topic} value={topic}>
                        {topic}
                      </option>
                    ))}
                  </select>
                </label>

                <Field
                  label="Message"
                  as="textarea"
                  value={form.message}
                  onChange={(e) => setForm((prev) => ({ ...prev, message: e.target.value }))}
                  placeholder="Tell us which project interests you, preferred unit type, or any questions you have..."
                  required
                />

                {submitted && (
                  <div className="contact-form__success" role="status">
                    Enquiry ready — WhatsApp opened. Our team will get back to you shortly.
                  </div>
                )}

                <div className="contact-form__actions">
                  <Button type="submit" size="lg">
                    Send via WhatsApp
                    <WhatsAppIcon />
                  </Button>
                  <a href={`mailto:${SITE_CONTACT.email}?subject=Deen%20Associate%20Enquiry`}>
                    <Button type="button" variant="outline" size="lg">
                      Email Instead
                    </Button>
                  </a>
                </div>
              </form>
            </div>
          </div>
        </Container>
      </section>

      <section className="contact-help">
        <Container>
          <div className="contact-help__header">
            <p className="about-eyebrow">How We Help</p>
            <h2 className="contact-help__title">Speak with our real estate team</h2>
            <p className="contact-help__intro">
              Deen Associate has served buyers, sellers, and investors in Islamabad since 2009. Whether you are
              exploring your first home or adding to your portfolio, we guide you from enquiry to handover.
            </p>
          </div>

          <div className="contact-help__grid">
            {HELP_WITH.map((item) => (
              <article key={item.title} className="contact-help__card">
                <h3>{item.title}</h3>
                <p>{item.detail}</p>
              </article>
            ))}
          </div>
        </Container>
      </section>
    </>
  );
}
