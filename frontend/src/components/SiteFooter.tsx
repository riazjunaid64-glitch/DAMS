import Container from "../lib/Container.tsx";
import SiteLogo from "./SiteLogo.tsx";
import { SITE_CONTACT } from "../lib/siteContact.ts";

type NavLink = { to: string; label: string };

type SiteFooterProps = {
  navLinks?: NavLink[];
};

export default function SiteFooter(_props: SiteFooterProps) {
  const openMap = () => window.open(SITE_CONTACT.mapsUrl, "_blank", "noopener,noreferrer");

  return (
    <footer className="site-footer site-footer--dark site-footer--center">
      <Container>
        <div className="footer-center">
          <SiteLogo variant="footer" />

          {/* Contact row */}
          <div className="footer-contact">
            <a href={`tel:${SITE_CONTACT.phoneMobileTel}`} className="footer-contact__item">
              <PhoneIcon />
              <span>{SITE_CONTACT.phoneMobile}</span>
            </a>
            <span className="footer-contact__sep" aria-hidden="true" />
            <a href={SITE_CONTACT.whatsappUrl} target="_blank" rel="noopener noreferrer" className="footer-contact__item">
              <WhatsAppIcon />
              <span>WhatsApp</span>
            </a>
            <span className="footer-contact__sep" aria-hidden="true" />
            <a href={`mailto:${SITE_CONTACT.email}`} className="footer-contact__item">
              <MailIcon />
              <span>{SITE_CONTACT.email}</span>
            </a>
          </div>

          {/* Social icons */}
          <ul className="footer-social">
            <li>
              <a href={SITE_CONTACT.social.linkedin} target="_blank" rel="noopener noreferrer" aria-label="LinkedIn"><LinkedInIcon /></a>
            </li>
            <li>
              <a href={SITE_CONTACT.social.facebook} target="_blank" rel="noopener noreferrer" aria-label="Facebook"><FacebookIcon /></a>
            </li>
            <li>
              <a href={SITE_CONTACT.social.twitter} target="_blank" rel="noopener noreferrer" aria-label="X (Twitter)"><TwitterIcon /></a>
            </li>
            <li>
              <a href={SITE_CONTACT.social.instagram} target="_blank" rel="noopener noreferrer" aria-label="Instagram"><InstagramIcon /></a>
            </li>
            <li>
              <a href={SITE_CONTACT.social.youtube} target="_blank" rel="noopener noreferrer" aria-label="YouTube"><YouTubeIcon /></a>
            </li>
          </ul>

          {/* View Map */}
          <button type="button" className="footer-map-btn" onClick={openMap}>View Map</button>
        </div>
      </Container>

      <div className="footer-bottom">
        <p>© {new Date().getFullYear()} All Rights Reserved | {SITE_CONTACT.brandName} Management System</p>
      </div>
    </footer>
  );
}

function PhoneIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <rect x="5" y="2" width="14" height="20" rx="2" /><line x1="12" y1="18" x2="12.01" y2="18" />
    </svg>
  );
}
function WhatsAppIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
      <path d="M17.472 14.382c-.297-.149-1.758-.867-2.03-.967-.273-.099-.471-.148-.67.15-.197.297-.767.966-.94 1.164-.173.199-.347.223-.644.075-.297-.15-1.255-.463-2.39-1.475-.883-.788-1.48-1.761-1.653-2.059-.173-.297-.018-.458.13-.606.134-.133.298-.347.446-.52.149-.174.198-.298.298-.497.099-.198.05-.371-.025-.52-.075-.149-.669-1.612-.916-2.207-.242-.579-.487-.5-.669-.51-.173-.008-.371-.01-.57-.01-.198 0-.52.074-.792.372-.272.297-1.04 1.016-1.04 2.479 0 1.462 1.065 2.875 1.213 3.074.149.198 2.096 3.2 5.077 4.487.709.306 1.262.489 1.694.625.712.227 1.36.195 1.871.118.571-.085 1.758-.719 2.006-1.413.248-.694.248-1.289.173-1.413-.074-.124-.272-.198-.57-.347z" />
      <path d="M12 2C6.477 2 2 6.477 2 12c0 1.89.525 3.66 1.438 5.168L2 22l4.832-1.438A9.955 9.955 0 0012 22c5.523 0 10-4.477 10-10S17.523 2 12 2zm0 18a7.96 7.96 0 01-4.082-1.125l-.293-.175-2.868.86.86-2.868-.175-.293A7.96 7.96 0 014 12c0-4.411 3.589-8 8-8s8 3.589 8 8-3.589 8-8 8z" />
    </svg>
  );
}
function MailIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M4 4h16c1.1 0 2 .9 2 2v12c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2V6c0-1.1.9-2 2-2z" /><polyline points="22,6 12,13 2,6" />
    </svg>
  );
}
function LinkedInIcon() {
  return (
    <svg width="17" height="17" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
      <path d="M19 3a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h14zM8.34 17.34V10.4H6.03v6.94h2.31zM7.18 9.44a1.34 1.34 0 1 0 0-2.68 1.34 1.34 0 0 0 0 2.68zm10.16 7.9v-3.8c0-2.03-1.08-2.97-2.53-2.97-1.17 0-1.69.64-1.98 1.09v-.94h-2.31c.03.65 0 6.94 0 6.94h2.31v-3.88c0-.2.01-.41.07-.56.17-.41.54-.84 1.18-.84.83 0 1.16.63 1.16 1.56v3.72h2.31z" />
    </svg>
  );
}
function FacebookIcon() {
  return (
    <svg width="17" height="17" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
      <path d="M22 12a10 10 0 1 0-11.56 9.88v-6.99H7.9V12h2.54V9.8c0-2.5 1.49-3.89 3.78-3.89 1.09 0 2.24.2 2.24.2v2.46h-1.26c-1.24 0-1.63.77-1.63 1.56V12h2.78l-.44 2.89h-2.34v6.99A10 10 0 0 0 22 12z" />
    </svg>
  );
}
function TwitterIcon() {
  return (
    <svg width="15" height="15" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
      <path d="M18.244 2.25h3.308l-7.227 8.26 8.502 11.24H16.17l-5.214-6.817L4.99 21.75H1.68l7.73-8.835L1.254 2.25H8.08l4.713 6.231zm-1.161 17.52h1.833L7.084 4.126H5.117z" />
    </svg>
  );
}
function InstagramIcon() {
  return (
    <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <rect x="2" y="2" width="20" height="20" rx="5" /><circle cx="12" cy="12" r="4" /><line x1="17.5" y1="6.5" x2="17.51" y2="6.5" />
    </svg>
  );
}
function YouTubeIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
      <path d="M23 12s0-3.4-.43-5.03a2.62 2.62 0 0 0-1.85-1.85C19.08 4.7 12 4.7 12 4.7s-7.08 0-8.72.42A2.62 2.62 0 0 0 1.43 6.97C1 8.6 1 12 1 12s0 3.4.43 5.03a2.62 2.62 0 0 0 1.85 1.85c1.64.42 8.72.42 8.72.42s7.08 0 8.72-.42a2.62 2.62 0 0 0 1.85-1.85C23 15.4 23 12 23 12zM9.75 15.02V8.98L15.5 12l-5.75 3.02z" />
    </svg>
  );
}
