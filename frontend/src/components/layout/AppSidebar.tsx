import { Link, useLocation } from "react-router-dom";
import { getNavLinks, isNavActive, type AppUser } from "../../config/navigation.tsx";

interface AppSidebarProps {
  user: AppUser | null;
  displayName: string;
  displayInitial: string;
}

export default function AppSidebar({
  user,
  displayName,
  displayInitial,
}: AppSidebarProps) {
  const location = useLocation();
  const navLinks = getNavLinks(user);

  return (
    <aside className="sidebar">
      <div className="sidebar__brand">
        <Link to="/" className="sidebar__logo">
          <div className="sidebar__logo-icon">
            <span>DA</span>
          </div>
          <div>
            <span className="sidebar__logo-title">DeenAssociate</span>
            <span className="sidebar__logo-subtitle">
              {user?.role === "Admin" ? "Admin Portal" : "Management Portal"}
            </span>
          </div>
        </Link>
      </div>

      <nav className="sidebar__nav">
        {navLinks.map((link) => {
          const active = isNavActive(location.pathname, link.to);
          return (
            <Link
              key={link.to}
              to={link.to}
              className={`sidebar__link ${active ? "sidebar__link--active" : ""}`}
            >
              <span className="sidebar__link-icon">{link.icon}</span>
              <span>{link.label}</span>
            </Link>
          );
        })}
      </nav>

      {user && (
        <div className="sidebar__user">
          <div className="sidebar__user-avatar">{displayInitial}</div>
          <div className="sidebar__user-info">
            <span className="sidebar__user-name">{displayName}</span>
            <span className="sidebar__user-email">{user.email}</span>
          </div>
        </div>
      )}
    </aside>
  );
}
