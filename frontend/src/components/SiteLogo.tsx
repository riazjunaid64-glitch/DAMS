import { Link } from "react-router-dom";
import { SITE_CONTACT } from "../lib/siteContact.ts";

type SiteLogoProps = {
  variant?: "nav" | "footer";
  className?: string;
};

export default function SiteLogo({ variant = "nav", className = "" }: SiteLogoProps) {
  return (
    <Link
      to="/"
      className={`site-logo site-logo--${variant}${className ? ` ${className}` : ""}`}
      aria-label={SITE_CONTACT.brandName}
    >
      <img
        src={SITE_CONTACT.logoNavSrc}
        alt={SITE_CONTACT.brandName}
        className="site-logo__img"
        decoding="async"
        fetchPriority="high"
      />
    </Link>
  );
}
