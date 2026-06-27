import { Link } from "react-router-dom";
import Button from "../lib/Button.tsx";
import Container from "../lib/Container.tsx";
import { COMPANY_STATS } from "../lib/companyStats.ts";
import { SITE_CONTACT } from "../lib/siteContact.ts";

const PROPERTY_TYPES = [
  "Residential Villas",
  "Apartments",
  "Commercial Projects",
  "Mixed-Use Developments",
  "Farm Houses",
  "Investment Properties",
];

const VALUES = [
  {
    title: "Trust & Transparency",
    description:
      "We build lasting relationships through honest advice, clear communication, and full transparency at every stage of your property journey.",
  },
  {
    title: "Customer-First Approach",
    description:
      "Our success is rooted in protecting your interests — from site selection and booking to handover and long-term investment planning.",
  },
  {
    title: "Quality Delivery",
    description:
      "From architects and designers to contractors and final handover, every detail is managed to the highest professional standards.",
  },
];

const WHY_CHOOSE = [
  { title: "Prime Locations", detail: "Projects in B-17, Faisal Hills, and high-growth Islamabad corridors." },
  { title: "End-to-End Service", detail: "Consultancy, development, marketing, and after-sales support under one roof." },
  { title: "Flexible Plans", detail: "Investment-friendly payment structures designed for families and investors." },
  { title: "Expert Guidance", detail: "Experienced team helping you buy, sell, or invest at the right value." },
  { title: "Gated Communities", detail: "Secure, well-planned developments with modern amenities and infrastructure." },
  { title: "24/7 Support", detail: "Dedicated reception, info desk, and client support when you need answers." },
];

export default function AboutPage() {
  return (
    <>
      {/* Hero */}
      <section className="about-hero">
        <Container className="about-hero__inner">
          <p className="about-eyebrow">Who We Are</p>
          <h1 className="about-hero__title">Deen Associate</h1>
          <p className="about-hero__lead">
            A trusted real estate consultancy and development firm in Islamabad — helping clients buy, sell, and invest
            in residential, commercial, and mixed-use properties with confidence.
          </p>
        </Container>
      </section>

      {/* Stats */}
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

      {/* Story */}
      <section className="about-story">
        <Container>
          <div className="about-story__grid">
            <div className="about-story__content">
              <p className="about-eyebrow">Our Story</p>
              <h2 className="about-section-title">Fulfilling promises with trust and confidence</h2>
              <div className="about-prose">
                <p>
                  Deen Associate is built on a simple promise: deliver what we commit to, and earn your trust through
                  every interaction. Since <strong>2009</strong>, we have served buyers, sellers, and investors across
                  Islamabad and surrounding growth corridors with a customer-oriented approach and a focus on long-term
                  relationships — not one-time transactions.
                </p>
                <p>
                  We guide clients through the full real estate lifecycle — from selecting the right project and
                  understanding market value, to booking, documentation, and handover. Whether you are looking for a
                  family home, a commercial investment, or a mixed-use opportunity, our team works to protect your
                  interests at every step.
                </p>
                <p>
                  Our portfolio includes landmark developments such as <strong>CPEC Greens</strong>,{" "}
                  <strong>Deen Square</strong>, <strong>Mall of Faisal Hills</strong>, <strong>Urban Complex</strong>,{" "}
                  <strong>Legacy Court</strong>, and <strong>Seventeen Square</strong> — each designed with modern
                  planning, strong locations, and investor-friendly payment options.
                </p>
              </div>
            </div>

            <div className="about-story__panel">
              <div className="about-story__card">
                <p className="about-story__card-label">Head Office</p>
                <p className="about-story__card-text">{SITE_CONTACT.address}</p>
              </div>
              <div className="about-story__card">
                <p className="about-story__card-label">Property Expertise</p>
                <ul className="about-tags">
                  {PROPERTY_TYPES.map((type) => (
                    <li key={type}>{type}</li>
                  ))}
                </ul>
              </div>
              <div className="about-story__highlight">
                <p className="about-story__highlight-title">Deen Villas</p>
                <p className="about-story__highlight-text">
                  Luxury residential villas in a serene, gated community — spacious layouts, modern kitchens,
                  attached washrooms, parking, and landscaped surroundings for comfortable family living.
                </p>
              </div>
            </div>
          </div>
        </Container>
      </section>

      {/* Values */}
      <section className="about-values">
        <Container>
          <div className="about-values__header">
            <p className="about-eyebrow">Our Principles</p>
            <h2 className="about-section-title">What drives everything we do</h2>
          </div>
          <div className="about-values__grid">
            {VALUES.map((value) => (
              <article key={value.title} className="about-value-card">
                <h3>{value.title}</h3>
                <p>{value.description}</p>
              </article>
            ))}
          </div>
        </Container>
      </section>

      {/* Why Choose Us */}
      <section className="about-why">
        <Container>
          <div className="about-why__header">
            <p className="about-eyebrow">Why Choose Us</p>
            <h2 className="about-section-title">Your partner in smart real estate decisions</h2>
            <p className="about-why__intro">
              From first inquiry to final handover, Deen Associate combines market knowledge, project quality, and
              responsive client service — so you can invest with clarity and peace of mind.
            </p>
          </div>
          <div className="about-why__grid">
            {WHY_CHOOSE.map((item) => (
              <div key={item.title} className="about-why__item">
                <span className="about-why__dot" aria-hidden="true" />
                <div>
                  <h3>{item.title}</h3>
                  <p>{item.detail}</p>
                </div>
              </div>
            ))}
          </div>
        </Container>
      </section>

      {/* CTA */}
      <section className="about-cta">
        <Container>
          <div className="about-cta__box">
            <div>
              <h2>Looking for the right property?</h2>
              <p>Speak with our team for bookings, site visits, investment advice, or project details.</p>
            </div>
            <div className="about-cta__actions">
              <Link to="/contact">
                <Button size="lg">Contact Us</Button>
              </Link>
              <a href={SITE_CONTACT.whatsappUrl} target="_blank" rel="noopener noreferrer">
                <Button variant="outline" size="lg" className="about-cta__outline-btn">
                  WhatsApp Us
                </Button>
              </a>
            </div>
          </div>
        </Container>
      </section>
    </>
  );
}
