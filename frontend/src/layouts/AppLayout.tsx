import { useState, type ReactNode } from "react";
import { Link, Outlet, useLocation } from "react-router-dom";
import type { User } from "../App.tsx";
import SiteLogo from "../components/SiteLogo.tsx";
import SiteFooter from "../components/SiteFooter.tsx";
import NotificationBell from "../features/notifications/NotificationBell.tsx";
import Button from "../lib/Button.tsx";

interface NavLink {
  to: string;
  label: string;
}

interface AppLayoutProps {
  user: User | null;
  mainNavLinks: NavLink[];
  displayName: string;
  displayInitial: string;
  setModal: (m: null | "login" | "signup") => void;
  onLogout: () => void;
}

const ICONS: Record<string, ReactNode> = {
  "/": <IconHome />,
  "/projects": <IconBuilding />,
  "/about": <IconInfo />,
  "/contact": <IconMail />,
  "/crm": <IconTarget />,
  "/bookings": <IconInbox />,
  "/confirmed-bookings": <IconCalendarCheck />,
  "/customers": <IconUsers />,
  "/employees": <IconBadge />,
  "/finance": <IconWallet />,
  "/notifications/settings": <IconBell />,
  "/my-projects": <IconFolder />,
};

function iconFor(to: string): ReactNode {
  return ICONS[to] ?? <IconDot />;
}

function isActive(pathname: string, to: string): boolean {
  if (to === "/") return pathname === "/";
  return pathname === to || pathname.startsWith(`${to}/`);
}

export default function AppLayout({ user, mainNavLinks, displayName, displayInitial, setModal, onLogout }: AppLayoutProps) {
  const location = useLocation();
  const [mobileOpen, setMobileOpen] = useState(false);
  const [collapsed, setCollapsed] = useState(false);

  // One button drives both: on desktop it collapses/expands the persistent
  // sidebar; on mobile it opens/closes the off-canvas drawer. Each breakpoint's
  // CSS only reacts to the state that applies to it.
  const toggleNav = () => {
    setCollapsed((v) => !v);
    setMobileOpen((v) => !v);
  };

  return (
    <div className={`app-shell ${collapsed ? "app-shell--collapsed" : ""}`}>
      {mobileOpen && <div className="app-shell__backdrop" onClick={() => { setMobileOpen(false); setCollapsed(false); }} />}

      {/* ─── Sidebar ─── */}
      <aside className={`app-sidebar ${mobileOpen ? "app-sidebar--open" : ""}`}>
        <div className="app-sidebar__brand">
          <SiteLogo variant="nav" className="app-sidebar__logo" />
        </div>

        <nav className="app-sidebar__nav">
          {mainNavLinks.map((link) => (
            <Link
              key={link.to}
              to={link.to}
              onClick={() => setMobileOpen(false)}
              className={`app-nav-link ${isActive(location.pathname, link.to) ? "app-nav-link--active" : ""}`}
            >
              <span className="app-nav-link__icon">{iconFor(link.to)}</span>
              <span className="app-nav-link__label">{link.label}</span>
            </Link>
          ))}
        </nav>

        <div className="app-sidebar__foot">
          {user ? (
            <div className="app-profile">
              <span className="app-profile__avatar">{displayInitial}</span>
              <span className="app-profile__meta">
                <span className="app-profile__name">{displayName}</span>
                <span className="app-profile__role">{user.role}</span>
              </span>
              <button type="button" className="app-profile__logout" onClick={onLogout} aria-label="Log out">
                <IconLogout />
              </button>
            </div>
          ) : (
            <div className="app-sidebar__auth">
              <Button variant="outline" size="sm" className="app-auth-btn w-full" onClick={() => setModal("login")}>Log in</Button>
              <Button size="sm" className="w-full" onClick={() => setModal("signup")}>Sign Up</Button>
            </div>
          )}
        </div>
      </aside>

      {/* ─── Main ─── */}
      <div className="app-main">
        <header className="app-topbar">
          <button
            type="button"
            className="app-topbar__menu"
            aria-label="Toggle navigation"
            onClick={toggleNav}
          >
            <IconMenu />
          </button>

          {/* Brand shows in the topbar only when the sidebar is collapsed,
              so the DEEN ASSOCIATES logo stays visible. */}
          <div className="app-topbar__brand">
            <SiteLogo variant="nav" className="app-topbar__logo" />
          </div>

          <div className="app-topbar__spacer" />

          <div className="app-topbar__actions">
            {user ? (
              <>
                <div className="app-topbar__bell">
                  <NotificationBell signedIn />
                </div>
                <span className="app-topbar__avatar" title={displayName}>{displayInitial}</span>
              </>
            ) : (
              <Button size="sm" className="hidden sm:inline-flex" onClick={() => setModal("signup")}>Sign Up</Button>
            )}
          </div>
        </header>

        <main className="app-content">
          <Outlet />
        </main>

        <SiteFooter navLinks={mainNavLinks} />
      </div>
    </div>
  );
}

/* ─── Icons ─── */
function IconHome() {
  return <svg width="19" height="19" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><path d="m3 9 9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z" /><polyline points="9 22 9 12 15 12 15 22" /></svg>;
}
function IconBuilding() {
  return <svg width="19" height="19" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><rect x="4" y="2" width="16" height="20" rx="1.5" /><path d="M9 7h.01M15 7h.01M9 12h.01M15 12h.01M9 17h.01M15 17h.01" /></svg>;
}
function IconInfo() {
  return <svg width="19" height="19" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><circle cx="12" cy="12" r="10" /><path d="M12 16v-4M12 8h.01" /></svg>;
}
function IconMail() {
  return <svg width="19" height="19" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><rect x="2" y="4" width="20" height="16" rx="2" /><path d="m22 6-10 7L2 6" /></svg>;
}
function IconTarget() {
  return <svg width="19" height="19" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><circle cx="12" cy="12" r="9" /><circle cx="12" cy="12" r="5" /><circle cx="12" cy="12" r="1" /></svg>;
}
function IconInbox() {
  return <svg width="19" height="19" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><polyline points="22 12 16 12 14 15 10 15 8 12 2 12" /><path d="M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11Z" /></svg>;
}
function IconCalendarCheck() {
  return <svg width="19" height="19" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><rect x="3" y="4" width="18" height="18" rx="2" /><path d="M16 2v4M8 2v4M3 10h18M9 16l2 2 4-4" /></svg>;
}
function IconUsers() {
  return <svg width="19" height="19" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" /><circle cx="9" cy="7" r="4" /><path d="M23 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75" /></svg>;
}
function IconBadge() {
  return <svg width="19" height="19" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><rect x="3" y="4" width="18" height="16" rx="2" /><path d="M9 4v2a2 2 0 0 0 2 2h2a2 2 0 0 0 2-2V4M8 14h8M8 18h5" /></svg>;
}
function IconWallet() {
  return <svg width="19" height="19" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><path d="M21 12V7H5a2 2 0 0 1 0-4h14v4" /><path d="M3 5v14a2 2 0 0 0 2 2h16v-5" /><path d="M18 12a2 2 0 0 0 0 4h4v-4Z" /></svg>;
}
function IconBell() {
  return <svg width="19" height="19" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><path d="M6 8a6 6 0 0 1 12 0c0 7 3 9 3 9H3s3-2 3-9M10.3 21a1.94 1.94 0 0 0 3.4 0" /></svg>;
}
function IconFolder() {
  return <svg width="19" height="19" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><path d="M4 20h16a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13c0 1.1.9 2 2 2Z" /></svg>;
}
function IconDot() {
  return <svg width="19" height="19" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8"><circle cx="12" cy="12" r="3" /></svg>;
}
function IconMenu() {
  return <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><line x1="3" y1="6" x2="21" y2="6" /><line x1="3" y1="12" x2="21" y2="12" /><line x1="3" y1="18" x2="21" y2="18" /></svg>;
}
function IconLogout() {
  return <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round"><path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4" /><polyline points="16 17 21 12 16 7" /><line x1="21" y1="12" x2="9" y2="12" /></svg>;
}
