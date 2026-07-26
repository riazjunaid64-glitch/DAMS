import { lazy, Suspense, useEffect, useMemo, useState } from "react";
import { Link, Route, Routes, useLocation } from "react-router-dom";
import { api, refreshAccessToken, setAccessToken } from "./api/api";
import AuthModal from "./components/AuthModal.tsx";
import SiteFooter from "./components/SiteFooter.tsx";
import SiteLogo from "./components/SiteLogo.tsx";
import { ProjectsProvider } from "./contexts/ProjectsContext.tsx";
import NotificationBell from "./features/notifications/NotificationBell.tsx";
import { detachPushOnLogout } from "./features/notifications/push.ts";
import Button from "./lib/Button.tsx";

const AboutPage = lazy(() => import("./pages/AboutPage.tsx"));
const ContactPage = lazy(() => import("./pages/ContactPage.tsx"));
const HomePage = lazy(() => import("./pages/HomePage.tsx"));
const LandingPage = lazy(() => import("./pages/LandingPage.tsx"));
const EmployeesPage = lazy(() => import("./pages/EmployeesPage.tsx"));
const EmployeeDetailPage = lazy(() => import("./pages/EmployeeDetailPage.tsx"));
const BookingRequestsPage = lazy(() => import("./pages/BookingRequestsPage.tsx"));
const CustomersPage = lazy(() => import("./pages/CustomersPage.tsx"));
const CustomerDetailPage = lazy(() => import("./pages/CustomerDetailPage.tsx"));
const ConfirmedBookingsPage = lazy(() => import("./pages/ConfirmedBookingsPage.tsx"));
const CreateBookingPage = lazy(() => import("./pages/CreateBookingPage.tsx"));
const ApplicationFormPage = lazy(() => import("./pages/ApplicationFormPage.tsx"));
const FinanceDashboardPage = lazy(() => import("./pages/FinanceDashboardPage.tsx"));
const BookingDetailPage = lazy(() => import("./pages/BookingDetailPage.tsx"));
const ReceiptPage = lazy(() => import("./pages/ReceiptPage.tsx"));
const ProjectDetailPage = lazy(() => import("./pages/ProjectDetailPage.tsx"));
const ProjectsPage = lazy(() => import("./pages/ProjectsPage.tsx"));
const UnitDetailPage = lazy(() => import("./pages/UnitDetailPage.tsx"));
const MyProjectsPage = lazy(() => import("./pages/MyProjectsPage.tsx"));
const MyProjectDetailPage = lazy(() => import("./pages/MyProjectDetailPage.tsx"));
const LeadsPage = lazy(() => import("./pages/LeadsPage.tsx"));
const LeadDetailPage = lazy(() => import("./pages/LeadDetailPage.tsx"));
const CrmSettingsPage = lazy(() => import("./pages/CrmSettingsPage.tsx"));
const NotificationsPage = lazy(() => import("./pages/NotificationsPage.tsx"));
const NotificationAdminPage = lazy(() => import("./pages/NotificationAdminPage.tsx"));

export interface User {
  userId: string;
  email: string;
  role: string;
}

const NAV_LINKS = [
  { to: "/", label: "Home" },
  { to: "/projects", label: "Projects" },
  { to: "/about", label: "About" },
  { to: "/contact", label: "Contact" },
];

function App() {
  const [modal, setModal] = useState<null | "login" | "signup">(null);
  const [user, setUser] = useState<User | null>(null);
  const [mobileOpen, setMobileOpen] = useState(false);
  const location = useLocation();

  const displayName = useMemo(
    () => (user?.email ? user.email.split("@")[0] : "User"),
    [user]
  );
  const displayInitial = displayName[0]?.toUpperCase() ?? "U";

  const mainNavLinks = useMemo(() => {
    if (user?.role === "Admin") {
      return [
        ...NAV_LINKS,
        { to: "/crm", label: "Lead CRM" },
        { to: "/bookings", label: "Requests" },
        { to: "/confirmed-bookings", label: "Bookings" },
        { to: "/customers", label: "Customers" },
        { to: "/employees", label: "Employees" },
        { to: "/finance", label: "Finance" },
        { to: "/notifications/settings", label: "Notifications" },
      ];
    }

    if (user?.role === "Manager" || user?.role === "Employee") {
      return [...NAV_LINKS, { to: "/crm", label: "Lead CRM" }];
    }

    if (user) {
      return [...NAV_LINKS, { to: "/my-projects", label: "My Projects" }];
    }

    return NAV_LINKS;
  }, [user]);

  const fetchProfile = async () => {
    const res = await api("/api/Auth/profile");
    if (!res.ok) {
      setAccessToken(null);
      setUser(null);
      return;
    }
    const data = await res.json();
    setUser(data);
  };

  useEffect(() => {
    // On page load, try to silently restore the session using the httpOnly refresh cookie.
    // If the cookie is absent or expired, the user stays logged out.
    void refreshAccessToken().then((ok) => {
      if (!ok) return;
      void api("/api/Auth/profile").then(async (res) => {
        if (res.ok) setUser(await res.json());
      });
    });
  }, []);

  const logout = async () => {
    // Detach this browser's push subscription first, while the session is still valid.
    // On a shared computer that is what stops the next person from receiving the previous
    // user's notifications.
    await detachPushOnLogout();
    void api("/api/Auth/logout", { method: "POST" }, false);
    setAccessToken(null);
    setUser(null);
  };

  return (
    <ProjectsProvider>
    <div className="flex min-h-screen flex-col">
      {/* ─── Navbar ─── */}
      <header className="site-header sticky top-0 z-50 border-b border-[var(--nav-border)] bg-[var(--nav-bg)] shadow-md transition-all duration-300">
        <div className="site-nav">
          <div className="site-nav__brand">
            <SiteLogo variant="nav" />
          </div>

          {/* Desktop Nav */}
          <nav className="site-nav__links hidden items-center gap-1 sm:flex">
            {mainNavLinks.map((link) => {
              const active = location.pathname === link.to;
              return (
                <Link
                  key={link.to}
                  to={link.to}
                  className={`relative rounded-lg px-4 py-2 text-sm font-medium transition-all duration-200 ${
                    active
                      ? "text-[var(--nav-text-active)] bg-white/10"
                      : "text-[var(--nav-text-muted)] hover:text-[var(--nav-text)] hover:bg-white/5"
                  }`}
                >
                  {link.label}
                  {active && (
                    <span className="absolute bottom-0 left-1/2 h-[2px] w-4 -translate-x-1/2 rounded-full bg-[var(--nav-text-active)]" />
                  )}
                </Link>
              );
            })}
          </nav>

          {/* Desktop Actions */}
          <div className="site-nav__actions hidden items-center gap-3 sm:flex">
            {!user ? (
              <>
                <Button variant="ghost" size="sm" className="nav-btn-ghost" onClick={() => setModal("login")}>
                  Log in
                </Button>
                <Button size="sm" className="nav-btn-accent" onClick={() => setModal("signup")}>
                  Sign Up
                </Button>
              </>
            ) : (
              <div className="flex items-center gap-3">
                <div className="flex items-center gap-2.5 rounded-full border border-white/15 bg-white/10 px-3 py-1.5">
                  <div className="flex h-7 w-7 items-center justify-center rounded-full bg-[var(--accent-warm)] text-xs font-semibold text-[var(--accent)]">
                    {displayInitial}
                  </div>
                  <span className="text-sm font-medium text-[var(--nav-text-muted)]">{displayName}</span>
                </div>
                <Button variant="ghost" size="sm" className="nav-btn-ghost" onClick={() => void logout()}>
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                    <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4" /><polyline points="16 17 21 12 16 7" /><line x1="21" y1="12" x2="9" y2="12" />
                  </svg>
                </Button>
              </div>
            )}
          </div>

          {/* Mobile Toggle — a direct grid child (matches .site-nav's column count at
              each breakpoint; see index.css) and display:none above sm, same as before. */}
          <button
            type="button"
            className="site-nav__menu-btn inline-flex h-10 w-10 items-center justify-center rounded-xl border border-white/15 bg-white/10 text-[var(--nav-text)] transition hover:bg-white/15 sm:hidden"
            aria-label="Toggle navigation"
            onClick={() => setMobileOpen((prev) => !prev)}
          >
            {mobileOpen ? (
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                <line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>
              </svg>
            ) : (
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                <line x1="3" y1="6" x2="21" y2="6"/><line x1="3" y1="12" x2="18" y2="12"/><line x1="3" y1="18" x2="15" y2="18"/>
              </svg>
            )}
          </button>

          {/* The bell: its own grid column at every breakpoint (see .site-nav__bell in
              index.css), mounted once so it never opens two live streams for one person. */}
          {user && (
            <div className="site-nav__bell">
              <NotificationBell signedIn />
            </div>
          )}
        </div>

        {/* Mobile Menu */}
        {mobileOpen && (
          <div className="pointer-events-none absolute inset-x-0 top-full z-50 sm:hidden">
            <div className="site-nav__mobile-wrap relative">
              <div className="pointer-events-auto ml-auto mt-0 w-[min(20rem,calc(100vw-1.5rem))] animate-scale-in rounded-b-2xl border border-[var(--nav-border)] bg-[var(--nav-bg)] p-3 shadow-2xl">
                <div className="flex flex-col gap-1">
                {mainNavLinks.map((link) => {
                  const active = location.pathname === link.to;
                  return (
                    <Link
                      key={link.to}
                      to={link.to}
                      onClick={() => setMobileOpen(false)}
                      className={`rounded-xl px-4 py-3 text-sm font-medium transition-all ${
                        active
                          ? "text-[var(--nav-text-active)] bg-white/10"
                          : "text-[var(--nav-text-muted)] hover:text-[var(--nav-text)] hover:bg-white/5"
                      }`}
                    >
                      {link.label}
                    </Link>
                  );
                })}
                <div className="mt-2 border-t border-white/10 pt-2">
                  {!user ? (
                    <div className="flex flex-col gap-2">
                      <Button variant="ghost" className="nav-btn-ghost" onClick={() => setModal("login")}>
                        Log in
                      </Button>
                      <Button className="nav-btn-accent" onClick={() => setModal("signup")}>
                        Sign Up
                      </Button>
                    </div>
                  ) : (
                    <div className="flex flex-col gap-2">
                      <div className="flex items-center gap-2.5 rounded-xl border border-white/15 bg-white/10 px-3 py-2">
                        <div className="flex h-8 w-8 items-center justify-center rounded-full bg-[var(--accent-warm)] text-xs font-semibold text-[var(--accent)]">
                          {displayInitial}
                        </div>
                        <span className="text-sm font-medium text-[var(--nav-text)]">{displayName}</span>
                      </div>
                      <Button variant="ghost" className="nav-btn-ghost" onClick={() => void logout()}>
                        Logout
                      </Button>
                    </div>
                  )}
                </div>
              </div>
              </div>
            </div>
          </div>
        )}
      </header>

      {/* ─── Pages ─── */}
      <main className="flex-1">
        <Suspense fallback={<div className="flex min-h-[50vh] items-center justify-center text-sm text-[var(--text-muted)]">Loading…</div>}>
          <Routes>
          <Route path="/" element={<HomePage />} />
          <Route path="/landing" element={<LandingPage />} />
          <Route path="/projects" element={<ProjectsPage user={user} />} />
          <Route path="/projects/:id" element={<ProjectDetailPage user={user} />} />
          <Route path="/units/:id" element={<UnitDetailPage user={user} />} />
          <Route path="/about" element={<AboutPage />} />
          <Route path="/contact" element={<ContactPage />} />
          <Route path="/my-projects" element={<MyProjectsPage user={user} />} />
          <Route path="/my-projects/:id" element={<MyProjectDetailPage user={user} />} />
          <Route path="/bookings" element={<BookingRequestsPage user={user} />} />
          <Route path="/confirmed-bookings" element={<ConfirmedBookingsPage user={user} />} />
          <Route path="/confirmed-bookings/new" element={<CreateBookingPage user={user} />} />
          <Route path="/confirmed-bookings/:id" element={<BookingDetailPage user={user} />} />
          <Route path="/application-form" element={<ApplicationFormPage user={user} />} />
          <Route path="/receipt/:bookingId/:paymentId" element={<ReceiptPage user={user} />} />
          <Route path="/customers" element={<CustomersPage user={user} />} />
          <Route path="/customers/:id" element={<CustomerDetailPage user={user} />} />
          <Route path="/employees" element={<EmployeesPage user={user} />} />
          <Route path="/employees/:id" element={<EmployeeDetailPage user={user} />} />
          <Route path="/finance" element={<FinanceDashboardPage user={user} />} />
          <Route path="/crm" element={<LeadsPage user={user} />} />
          <Route path="/crm/leads/:id" element={<LeadDetailPage user={user} />} />
          <Route path="/crm/settings" element={<CrmSettingsPage user={user} />} />
          <Route path="/notifications" element={<NotificationsPage user={user} />} />
          <Route path="/notifications/settings" element={<NotificationAdminPage user={user} />} />
          </Routes>
        </Suspense>
      </main>

      <SiteFooter navLinks={mainNavLinks} />

      {/* ─── Auth Modal ─── */}
      {modal && (
        <AuthModal
          mode={modal}
          onClose={() => {
            setModal(null);
            fetchProfile();
          }}
          onSuccess={() => {}}
        />
      )}
    </div>
    </ProjectsProvider>
  );
}

export default App;
