import { useMemo, useState, type JSX } from "react";
import { Link, Outlet, useLocation } from "react-router-dom";
import type { User } from "../App.tsx";
import SiteFooter from "../components/SiteFooter.tsx";
import SiteLogo from "../components/SiteLogo.tsx";
import Button from "../lib/Button.tsx";

interface NavItem {
  to: string;
  label: string;
  icon: JSX.Element;
}

interface NavGroup {
  heading: string;
  items: NavItem[];
}

function IconHome() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="m3 9 9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z" /><polyline points="9 22 9 12 15 12 15 22" />
    </svg>
  );
}
function IconBuilding() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <rect x="4" y="2" width="16" height="20" rx="1" /><line x1="9" y1="7" x2="9" y2="7" /><line x1="15" y1="7" x2="15" y2="7" /><line x1="9" y1="12" x2="9" y2="12" /><line x1="15" y1="12" x2="15" y2="12" /><line x1="9" y1="17" x2="9" y2="17" /><line x1="15" y1="17" x2="15" y2="17" />
    </svg>
  );
}
function IconInfo() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <circle cx="12" cy="12" r="10" /><line x1="12" y1="16" x2="12" y2="12" /><line x1="12" y1="8" x2="12.01" y2="8" />
    </svg>
  );
}
function IconMail() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <rect x="2" y="4" width="20" height="16" rx="2" /><path d="m22 6-10 7L2 6" />
    </svg>
  );
}
function IconFolder() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M4 20h16a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13c0 1.1.9 2 2 2Z" />
    </svg>
  );
}
function IconChart() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <line x1="12" y1="20" x2="12" y2="10" /><line x1="18" y1="20" x2="18" y2="4" /><line x1="6" y1="20" x2="6" y2="16" />
    </svg>
  );
}
function IconInbox() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <polyline points="22 12 16 12 14 15 10 15 8 12 2 12" /><path d="M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11Z" />
    </svg>
  );
}
function IconCheck() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14" /><polyline points="22 4 12 14.01 9 11.01" />
    </svg>
  );
}
function IconUsers() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" /><circle cx="9" cy="7" r="4" /><path d="M23 21v-2a4 4 0 0 0-3-3.87" /><path d="M16 3.13a4 4 0 0 1 0 7.75" />
    </svg>
  );
}
function IconBadge() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <rect x="3" y="4" width="18" height="16" rx="2" /><path d="M9 4v2a2 2 0 0 0 2 2h2a2 2 0 0 0 2-2V4" /><line x1="8" y1="14" x2="16" y2="14" /><line x1="8" y1="18" x2="13" y2="18" />
    </svg>
  );
}
function IconWallet() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M21 12V7H5a2 2 0 0 1 0-4h14v4" /><path d="M3 5v14a2 2 0 0 0 2 2h16v-5" /><path d="M18 12a2 2 0 0 0 0 4h4v-4Z" />
    </svg>
  );
}
function IconMenu() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
      <line x1="3" y1="6" x2="21" y2="6" /><line x1="3" y1="12" x2="18" y2="12" /><line x1="3" y1="18" x2="15" y2="18" />
    </svg>
  );
}
function IconLogout() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4" /><polyline points="16 17 21 12 16 7" /><line x1="21" y1="12" x2="9" y2="12" />
    </svg>
  );
}

function buildNavGroups(user: User | null): NavGroup[] {
  const isAdmin = user?.role === "Admin";
  const groups: NavGroup[] = [
    {
      heading: "Explore",
      items: [
        { to: "/", label: "Home", icon: <IconHome /> },
        { to: "/projects", label: "Projects", icon: <IconBuilding /> },
        { to: "/about", label: "About", icon: <IconInfo /> },
        { to: "/contact", label: "Contact", icon: <IconMail /> },
      ],
    },
  ];

  if (user && !isAdmin) {
    groups.push({ heading: "Account", items: [{ to: "/my-projects", label: "My Projects", icon: <IconFolder /> }] });
  }

  if (isAdmin) {
    groups.push(
      { heading: "Overview", items: [{ to: "/finance", label: "Dashboard", icon: <IconChart /> }] },
      {
        heading: "Sales",
        items: [
          { to: "/bookings", label: "Booking Requests", icon: <IconInbox /> },
          { to: "/confirmed-bookings", label: "Confirmed Bookings", icon: <IconCheck /> },
        ],
      },
      {
        heading: "People",
        items: [
          { to: "/customers", label: "Customers", icon: <IconUsers /> },
          { to: "/employees", label: "Employees", icon: <IconBadge /> },
        ],
      },
      { heading: "Finance", items: [{ to: "/finance/accounts", label: "Accounts", icon: <IconWallet /> }] },
    );
  }

  return groups;
}

function findActive(pathname: string, items: NavItem[]): string | null {
  let best: string | null = null;
  for (const item of items) {
    const match = item.to === "/" ? pathname === "/" : pathname === item.to || pathname.startsWith(`${item.to}/`);
    if (match && (!best || item.to.length > best.length)) best = item.to;
  }
  return best;
}

interface NavLink {
  to: string;
  label: string;
}

interface AppLayoutProps {
  user: User | null;
  mainNavLinks: NavLink[];
  setModal: (modal: null | "login" | "signup") => void;
  logout: () => void;
}

export default function AppLayout({ user, mainNavLinks, setModal, logout }: AppLayoutProps) {
  const location = useLocation();
  const [mobileOpen, setMobileOpen] = useState(false);

  const navGroups = useMemo(() => buildNavGroups(user), [user]);
  const allItems = useMemo(() => navGroups.flatMap((g) => g.items), [navGroups]);
  const activeTo = findActive(location.pathname, allItems);
  const pageLabel = allItems.find((i) => i.to === activeTo)?.label ?? "";

  const displayName = user?.email ? user.email.split("@")[0] : "";
  const displayInitial = displayName[0]?.toUpperCase() ?? "U";

  return (
    <div className="admin-shell">
      {mobileOpen && <div className="admin-shell__backdrop" onClick={() => setMobileOpen(false)} />}

      <aside className={`admin-sidebar ${mobileOpen ? "admin-sidebar--open" : ""}`}>
        <div className="admin-sidebar__brand">
          <SiteLogo variant="nav" className="scale-90 origin-left" />
        </div>
        <nav className="admin-sidebar__nav">
          {navGroups.map((group) => (
            <div className="admin-nav-section" key={group.heading}>
              <p className="admin-nav-section__label">{group.heading}</p>
              {group.items.map((item) => (
                <Link
                  key={item.to}
                  to={item.to}
                  onClick={() => setMobileOpen(false)}
                  className={`admin-nav-link ${activeTo === item.to ? "admin-nav-link--active" : ""}`}
                >
                  <span className="admin-nav-link__icon">{item.icon}</span>
                  {item.label}
                </Link>
              ))}
            </div>
          ))}
        </nav>
        {!user && (
          <div className="admin-sidebar__auth">
            <Button variant="outline" size="sm" className="admin-sidebar__auth-login w-full" onClick={() => setModal("login")}>Log in</Button>
            <Button size="sm" className="w-full" onClick={() => setModal("signup")}>Sign Up</Button>
          </div>
        )}
      </aside>

      <div className="admin-main">
        <header className="admin-topbar">
          <button
            type="button"
            className="admin-topbar__menu-btn"
            aria-label="Toggle navigation"
            onClick={() => setMobileOpen((v) => !v)}
          >
            <IconMenu />
          </button>
          {pageLabel && <h1 className="admin-topbar__title">{pageLabel}</h1>}
          <div className="admin-topbar__spacer" />
          {user ? (
            <div className="admin-topbar__user">
              <span className="admin-topbar__avatar">{displayInitial}</span>
              <span className="admin-topbar__name hidden sm:inline">{displayName}</span>
              <button type="button" className="admin-topbar__logout" onClick={logout} aria-label="Log out">
                <IconLogout />
              </button>
            </div>
          ) : (
            <div className="hidden items-center gap-2 sm:flex">
              <Button variant="ghost" size="sm" onClick={() => setModal("login")}>Log in</Button>
              <Button size="sm" onClick={() => setModal("signup")}>Sign Up</Button>
            </div>
          )}
        </header>
        <main className="admin-content">
          <Outlet />
        </main>
        <SiteFooter navLinks={mainNavLinks} />
      </div>
    </div>
  );
}
