import { Link } from "react-router-dom";
import Container from "../lib/Container.tsx";
import SiteLogo from "./SiteLogo.tsx";
import { SITE_CONTACT } from "../lib/siteContact.ts";

type NavLink = { to: string; label: string };

type SiteFooterProps = {
  navLinks: NavLink[];
};

function ChevronLink({ to, label }: NavLink) {
  return (
    <li>
      <Link to={to} className="site-footer__link">
        {label}
      </Link>
    </li>
  );
}

export default function SiteFooter({ navLinks }: SiteFooterProps) {
  const scrollToTop = () => window.scrollTo({ top: 0, behavior: "smooth" });

  const openMapLocation = () => {
    const mapSection = document.getElementById("footer-location-map");
    mapSection?.scrollIntoView({ behavior: "smooth", block: "center" });
    window.open(SITE_CONTACT.mapsUrl, "_blank", "noopener,noreferrer");
  };

  return (
    <footer className="site-footer">
      <Container className="site-footer__main">
        <div className="site-footer__grid">
          {/* Brand column */}
          <div className="site-footer__brand">
            <SiteLogo variant="footer" />
            <p className="site-footer__about">{SITE_CONTACT.description}</p>
            <ul className="site-footer__social">
              <li>
                <a href={`tel:${SITE_CONTACT.phoneTel}`} aria-label="Call Deen Associate">
                  <PhoneIcon />
                </a>
              </li>
              <li>
                <a href={SITE_CONTACT.whatsappUrl} target="_blank" rel="noopener noreferrer" aria-label="WhatsApp">
                  <WhatsAppIcon />
                </a>
              </li>
              <li>
                <a href={`mailto:${SITE_CONTACT.email}`} aria-label="Email Deen Associate">
                  <MailIcon />
                </a>
              </li>
              <li>
                <button type="button" onClick={openMapLocation} className="site-footer__social-btn" aria-label="Location">
                  <PinIcon />
                </button>
              </li>
            </ul>
          </div>

          {/* Links + map + contact */}
          <div className="site-footer__columns">
            <div className="site-footer__col">
              <h3 className="site-footer__title">Useful Links</h3>
              <ul className="site-footer__links">
                {navLinks.map((link) => (
                  <ChevronLink key={link.to} {...link} />
                ))}
              </ul>
            </div>

            <div className="site-footer__col" id="footer-location-map">
              <h3 className="site-footer__title">Get Location</h3>
              <div className="site-footer__map-wrap">
                <iframe
                  title="Deen Associate office location"
                  src={SITE_CONTACT.mapsEmbedUrl}
                  loading="lazy"
                  referrerPolicy="no-referrer-when-downgrade"
                  className="site-footer__map"
                />
                <a
                  href={SITE_CONTACT.mapsUrl}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="site-footer__map-open"
                  aria-label="Open Deen Associate office in Google Maps"
                >
                  Open in Google Maps
                </a>
              </div>
            </div>

            <div className="site-footer__col site-footer__col--contact">
              <h3 className="site-footer__title">Contact Us</h3>
              <ul className="site-footer__contact">
                <li>
                  <a href={`tel:${SITE_CONTACT.phoneTel}`}>
                    <PhoneIcon />
                    <span>Call Now : {SITE_CONTACT.phone}</span>
                  </a>
                </li>
                <li>
                  <a href={`tel:${SITE_CONTACT.phoneMobileTel}`}>
                    <PhoneIcon />
                    <span>Mobile : {SITE_CONTACT.phoneMobile}</span>
                  </a>
                </li>
                <li>
                  <a href={SITE_CONTACT.whatsappUrl} target="_blank" rel="noopener noreferrer">
                    <WhatsAppIcon />
                    <span>Whatsapp Chat : {SITE_CONTACT.phoneMobile}</span>
                  </a>
                </li>
                <li>
                  <a href={`mailto:${SITE_CONTACT.email}`}>
                    <MailIcon />
                    <span>Email : {SITE_CONTACT.email}</span>
                  </a>
                </li>
                <li>
                  <button type="button" onClick={openMapLocation} className="site-footer__contact-btn">
                    <PinIcon />
                    <span>{SITE_CONTACT.address}</span>
                  </button>
                </li>
              </ul>
            </div>
          </div>
        </div>
      </Container>

      <div className="site-footer__bar">
        <Container className="site-footer__bar-inner">
          <p>© {new Date().getFullYear()} {SITE_CONTACT.brandName}. All rights reserved.</p>
          <ul className="site-footer__bar-links">
            <li>
              <Link to="/about">About</Link>
            </li>
            <li>
              <Link to="/projects">Projects</Link>
            </li>
            <li>
              <Link to="/contact">Contact</Link>
            </li>
          </ul>
        </Container>
      </div>

      <button type="button" className="site-footer__top" onClick={scrollToTop} aria-label="Back to top">
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round">
          <polyline points="18 15 12 9 6 15" />
        </svg>
      </button>
    </footer>
  );
}

function PhoneIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <rect x="5" y="2" width="14" height="20" rx="2" /><line x1="12" y1="18" x2="12.01" y2="18" />
    </svg>
  );
}

function WhatsAppIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
      <path d="M17.472 14.382c-.297-.149-1.758-.867-2.03-.967-.273-.099-.471-.148-.67.15-.197.297-.767.966-.94 1.164-.173.199-.347.223-.644.075-.297-.15-1.255-.463-2.39-1.475-.883-.788-1.48-1.761-1.653-2.059-.173-.297-.018-.458.13-.606.134-.133.298-.347.446-.52.149-.174.198-.298.298-.497.099-.198.05-.371-.025-.52-.075-.149-.669-1.612-.916-2.207-.242-.579-.487-.5-.669-.51-.173-.008-.371-.01-.57-.01-.198 0-.52.074-.792.372-.272.297-1.04 1.016-1.04 2.479 0 1.462 1.065 2.875 1.213 3.074.149.198 2.096 3.2 5.077 4.487.709.306 1.262.489 1.694.625.712.227 1.36.195 1.871.118.571-.085 1.758-.719 2.006-1.413.248-.694.248-1.289.173-1.413-.074-.124-.272-.198-.57-.347z" />
      <path d="M12 2C6.477 2 2 6.477 2 12c0 1.89.525 3.66 1.438 5.168L2 22l4.832-1.438A9.955 9.955 0 0012 22c5.523 0 10-4.477 10-10S17.523 2 12 2zm0 18a7.96 7.96 0 01-4.082-1.125l-.293-.175-2.868.86.86-2.868-.175-.293A7.96 7.96 0 014 12c0-4.411 3.589-8 8-8s8 3.589 8 8-3.589 8-8 8z" />
    </svg>
  );
}

function MailIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M4 4h16c1.1 0 2 .9 2 2v12c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2V6c0-1.1.9-2 2-2z" /><polyline points="22,6 12,13 2,6" />
    </svg>
  );
}

function PinIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0118 0z" /><circle cx="12" cy="10" r="3" />
    </svg>
  );
}
