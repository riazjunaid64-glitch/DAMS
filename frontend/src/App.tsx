import { useEffect, useMemo, useState } from "react";
import { Link, Route, Routes } from "react-router-dom";
import { api } from "./api/api";
import AuthModal from "./components/AuthModal.tsx";
import AppSidebar from "./components/layout/AppSidebar.tsx";
import Button from "./lib/Button.tsx";
import Container from "./lib/Container.tsx";
import { getNavLinks } from "./config/navigation.tsx";
import AboutPage from "./pages/AboutPage.tsx";
import ContactPage from "./pages/ContactPage.tsx";
import HomePage from "./pages/HomePage.tsx";
import LandingPage from "./pages/LandingPage.tsx";
import EmployeesPage from "./pages/EmployeesPage.tsx";
import EmployeeDetailPage from "./pages/EmployeeDetailPage.tsx";
import BookingRequestsPage from "./pages/BookingRequestsPage.tsx";
import CustomersPage from "./pages/CustomersPage.tsx";
import CustomerDetailPage from "./pages/CustomerDetailPage.tsx";
import ConfirmedBookingsPage from "./pages/ConfirmedBookingsPage.tsx";
import CreateBookingPage from "./pages/CreateBookingPage.tsx";
import ApplicationFormPage from "./pages/ApplicationFormPage.tsx";
import FinanceDashboardPage from "./pages/FinanceDashboardPage.tsx";
import BookingDetailPage from "./pages/BookingDetailPage.tsx";
import ReceiptPage from "./pages/ReceiptPage.tsx";
import ProjectDetailPage from "./pages/ProjectDetailPage.tsx";
import ProjectsPage from "./pages/ProjectsPage.tsx";
import UnitDetailPage from "./pages/UnitDetailPage.tsx";
import MyProjectsPage from "./pages/MyProjectsPage.tsx";
import MyProjectDetailPage from "./pages/MyProjectDetailPage.tsx";

export interface User {
  userId: string;
  email: string;
  role: string;
}

function App() {
  const [modal, setModal] = useState<null | "login" | "signup">(null);
  const [user, setUser] = useState<User | null>(null);
  const [sidebarOpen, setSidebarOpen] = useState(
    () => typeof window !== "undefined" && window.matchMedia("(min-width: 1024px)").matches
  );
  const displayName = useMemo(
    () => (user?.email ? user.email.split("@")[0] : "User"),
    [user]
  );
  const displayInitial = displayName[0]?.toUpperCase() ?? "U";

  const mainNavLinks = useMemo(() => getNavLinks(user), [user]);

  const fetchProfile = async () => {
    const res = await api("/api/Auth/profile");
    if (!res.ok) {
      localStorage.removeItem("token");
      setUser(null);
      return;
    }
    const data = await res.json();
    setUser(data);
  };

  useEffect(() => {
    const token = localStorage.getItem("token");
    if (token) fetchProfile();
  }, []);

  const logout = () => {
    localStorage.removeItem("token");
    localStorage.removeItem("refreshToken");
    setUser(null);
  };

  return (
    <div className="app-shell">
      <div className={`sidebar-drawer ${sidebarOpen ? "sidebar-drawer--open" : ""}`}>
        <AppSidebar
          user={user}
          displayName={displayName}
          displayInitial={displayInitial}
        />
      </div>

      <div className="app-main">
        <header className="top-bar">
          <div className="flex items-center gap-3">
            <button
              type="button"
              className="top-bar__menu-btn"
              aria-label={sidebarOpen ? "Close navigation" : "Open navigation"}
              aria-expanded={sidebarOpen}
              onClick={() => setSidebarOpen((prev) => !prev)}
            >
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                <line x1="3" y1="6" x2="21" y2="6" />
                <line x1="3" y1="12" x2="18" y2="12" />
                <line x1="3" y1="18" x2="15" y2="18" />
              </svg>
            </button>
          </div>

          <div className="flex items-center gap-3 sm:gap-4">
            {user?.role === "Admin" && (
              <span className="top-bar__badge">Admin</span>
            )}

            {!user ? (
              <div className="flex items-center gap-2">
                <Button variant="ghost" size="sm" onClick={() => setModal("login")}>
                  Log in
                </Button>
                <Button size="sm" onClick={() => setModal("signup")}>
                  Sign Up
                </Button>
              </div>
            ) : (
              <div className="flex items-center gap-3 sm:gap-4">
                <div className="flex items-center gap-2.5">
                  <div className="top-bar__avatar">{displayInitial}</div>
                  <span className="hidden text-sm font-medium text-[var(--text-secondary)] sm:inline">
                    {displayName}
                  </span>
                </div>
                <button type="button" onClick={logout} className="top-bar__logout">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                    <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4" />
                    <polyline points="16 17 21 12 16 7" />
                    <line x1="21" y1="12" x2="9" y2="12" />
                  </svg>
                  <span className="hidden sm:inline">Logout</span>
                </button>
              </div>
            )}
          </div>
        </header>

        <main className="app-content">
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
          </Routes>
        </main>

        <footer className="app-footer">
          <Container className="py-10">
            <div className="grid gap-8 sm:grid-cols-2 lg:grid-cols-4">
              <div className="space-y-4 lg:col-span-2">
                <div className="flex items-center gap-3">
                  <div className="sidebar__logo-icon !h-8 !w-8 !rounded-lg">
                    <span className="text-xs">DA</span>
                  </div>
                  <span className="text-base font-semibold text-[var(--text-heading)]">
                    Deen<span className="text-[var(--accent)]">Associate</span>
                  </span>
                </div>
                <p className="max-w-sm text-sm leading-relaxed text-[var(--text-muted)]">
                  Project management and unit tracking portal for Deen Associate.
                </p>
              </div>

              <div>
                <h4 className="mb-4 text-xs font-semibold uppercase tracking-[0.15em] text-[var(--text-muted)]">
                  Navigation
                </h4>
                <ul className="space-y-2.5">
                  {mainNavLinks.map((link) => (
                    <li key={link.to}>
                      <Link
                        to={link.to}
                        className="text-sm text-[var(--text-secondary)] transition-colors hover:text-[var(--text-primary)]"
                      >
                        {link.label}
                      </Link>
                    </li>
                  ))}
                </ul>
              </div>

              <div>
                <h4 className="mb-4 text-xs font-semibold uppercase tracking-[0.15em] text-[var(--text-muted)]">
                  Contact
                </h4>
                <ul className="space-y-2.5 text-sm text-[var(--text-secondary)]">
                  <li>contact@deenassociate.com</li>
                  <li>+1 (555) 123-4567</li>
                  <li>123 Business Avenue</li>
                </ul>
              </div>
            </div>

            <div className="mt-10 flex flex-col items-center justify-between gap-4 border-t border-[var(--border)] pt-8 sm:flex-row">
              <p className="text-xs text-[var(--text-muted)]">
                © {new Date().getFullYear()} Deen Associate. All rights reserved.
              </p>
              <p className="text-xs text-[var(--text-muted)]">
                Deen Associate Management System
              </p>
            </div>
          </Container>
        </footer>
      </div>

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
  );
}

export default App;
