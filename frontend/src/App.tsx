import { useEffect, useMemo, useState } from "react";
import { Link, Route, Routes, useLocation } from "react-router-dom";
import { api } from "./api/api";
import AuthModal from "./components/AuthModal";
import { useTheme } from "./context/ThemeContext";
import Button from "./lib/Button";
import Container from "./lib/Container";
import AboutPage from "./pages/AboutPage";
import ContactPage from "./pages/ContactPage";
import HomePage from "./pages/HomePage";
import LandingPage from "./pages/LandingPage";
import ProjectDetailPage from "./pages/ProjectDetailPage";
import ProjectsPage from "./pages/ProjectsPage";

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
  const [scrolled, setScrolled] = useState(false);
  const location = useLocation();
  const { theme, toggleTheme } = useTheme();

  const displayName = useMemo(
    () => (user?.email ? user.email.split("@")[0] : "User"),
    [user]
  );
  const displayInitial = displayName[0]?.toUpperCase() ?? "U";

  // close mobile nav on route change
  useEffect(() => {
    setMobileOpen(false);
  }, [location.pathname]);

  // track scroll for navbar glass effect
  useEffect(() => {
    const handler = () => setScrolled(window.scrollY > 20);
    window.addEventListener("scroll", handler, { passive: true });
    return () => window.removeEventListener("scroll", handler);
  }, []);

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
    <div className="flex min-h-screen flex-col">
      {/* ─── Navbar ─── */}
      <header
        className={`sticky top-0 z-50 transition-all duration-300 ${
          scrolled
            ? "border-b border-[var(--nav-border)] bg-[var(--nav-bg)] backdrop-blur-xl shadow-sm"
            : "bg-transparent"
        }`}
      >
        <Container className="flex h-16 items-center justify-between">
          {/* Logo */}
          <Link to="/" className="group flex items-center gap-3">
            <div className="relative flex h-9 w-9 items-center justify-center rounded-xl bg-gradient-to-br from-indigo-500 to-violet-600 shadow-md transition-transform group-hover:scale-105">
              <span className="text-sm font-bold text-white">DA</span>
            </div>
            <span className="text-base font-semibold tracking-tight text-[var(--text-heading)]">
              Deen<span className="text-indigo-500">Associate</span>
            </span>
          </Link>

          {/* Desktop Nav */}
          <nav className="hidden items-center gap-1 sm:flex">
            {NAV_LINKS.map((link) => {
              const active = location.pathname === link.to;
              return (
                <Link
                  key={link.to}
                  to={link.to}
                  className={`relative rounded-lg px-4 py-2 text-sm font-medium transition-all duration-200 ${
                    active
                      ? "text-[var(--text-heading)] bg-[var(--surface-glass-active)] shadow-sm"
                      : "text-[var(--text-secondary)] hover:text-[var(--text-primary)] hover:bg-[var(--surface-glass-hover)]"
                  }`}
                >
                  {link.label}
                  {active && (
                    <span className="absolute bottom-0 left-1/2 h-[2px] w-4 -translate-x-1/2 rounded-full bg-indigo-500" />
                  )}
                </Link>
              );
            })}
          </nav>

          {/* Desktop Actions */}
          <div className="hidden items-center gap-3 sm:flex">
            <button
              onClick={toggleTheme}
              className="flex h-9 w-9 items-center justify-center rounded-lg border border-[var(--border)] bg-[var(--surface-glass)] text-[var(--text-primary)] transition-all hover:bg-[var(--surface-glass-hover)] hover:border-[var(--border-hover)]"
              aria-label="Toggle theme"
            >
              {theme === "dark" ? (
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                  <circle cx="12" cy="12" r="5"/><line x1="12" y1="1" x2="12" y2="3"/><line x1="12" y1="21" x2="12" y2="23"/><line x1="4.22" y1="4.22" x2="5.64" y2="5.64"/><line x1="18.36" y1="18.36" x2="19.78" y2="19.78"/><line x1="1" y1="12" x2="3" y2="12"/><line x1="21" y1="12" x2="23" y2="12"/><line x1="4.22" y1="19.78" x2="5.64" y2="18.36"/><line x1="18.36" y1="5.64" x2="19.78" y2="4.22"/>
                </svg>
              ) : (
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                  <path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z"/>
                </svg>
              )}
            </button>
            {!user ? (
              <>
                <Button variant="ghost" size="sm" onClick={() => setModal("login")}>
                  Log in
                </Button>
                <Button size="sm" onClick={() => setModal("signup")}>
                  Sign Up
                </Button>
              </>
            ) : (
              <div className="flex items-center gap-3">
                <div className="flex items-center gap-2.5 rounded-full border border-[var(--border)] bg-[var(--surface-glass)] px-3 py-1.5 shadow-sm">
                  <div className="flex h-7 w-7 items-center justify-center rounded-full bg-gradient-to-br from-indigo-500 to-violet-600 text-xs font-semibold text-white shadow-sm">
                    {displayInitial}
                  </div>
                  <span className="text-sm font-medium text-[var(--text-secondary)]">{displayName}</span>
                </div>
                <Button variant="ghost" size="sm" onClick={logout}>
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                    <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4" /><polyline points="16 17 21 12 16 7" /><line x1="21" y1="12" x2="9" y2="12" />
                  </svg>
                </Button>
              </div>
            )}
          </div>

          {/* Mobile Toggle */}
          <button
            type="button"
            className="inline-flex h-10 w-10 items-center justify-center rounded-xl border border-[var(--border)] bg-[var(--surface-glass)] text-[var(--text-primary)] transition hover:bg-[var(--surface-glass-hover)] sm:hidden"
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
        </Container>

        {/* Mobile Menu */}
        {mobileOpen && (
          <div className="pointer-events-none absolute inset-x-0 top-full z-50 sm:hidden">
            <Container className="relative">
              <div className="pointer-events-auto ml-auto mt-0 w-[min(20rem,calc(100vw-1.5rem))] animate-scale-in rounded-b-2xl border border-[var(--border)] bg-[var(--nav-bg)] p-3 shadow-2xl backdrop-blur-xl">
                <div className="flex flex-col gap-1">
                {NAV_LINKS.map((link) => {
                  const active = location.pathname === link.to;
                  return (
                    <Link
                      key={link.to}
                      to={link.to}
                      className={`rounded-xl px-4 py-3 text-sm font-medium transition-all ${
                        active
                          ? "text-[var(--accent)] bg-[var(--accent-glow)]"
                          : "text-[var(--text-secondary)] hover:text-[var(--text-primary)] hover:bg-[var(--surface-glass-hover)]"
                      }`}
                    >
                      {link.label}
                    </Link>
                  );
                })}
                <div className="mt-2 border-t border-[var(--border)] pt-2">
                  <div className="mb-2 flex items-center justify-between rounded-xl border border-[var(--border)] bg-[var(--surface-glass)] px-3 py-2">
                    <span className="text-sm font-medium text-[var(--text-secondary)]">Theme</span>
                    <button
                      onClick={toggleTheme}
                      className="flex h-9 w-9 items-center justify-center rounded-lg border border-[var(--border)] bg-[var(--surface-glass)] text-[var(--text-primary)] transition hover:bg-[var(--surface-glass-hover)]"
                      aria-label="Toggle theme"
                    >
                      {theme === "dark" ? (
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                          <circle cx="12" cy="12" r="5"/><line x1="12" y1="1" x2="12" y2="3"/><line x1="12" y1="21" x2="12" y2="23"/><line x1="4.22" y1="4.22" x2="5.64" y2="5.64"/><line x1="18.36" y1="18.36" x2="19.78" y2="19.78"/><line x1="1" y1="12" x2="3" y2="12"/><line x1="21" y1="12" x2="23" y2="12"/><line x1="4.22" y1="19.78" x2="5.64" y2="18.36"/><line x1="18.36" y1="5.64" x2="19.78" y2="4.22"/>
                        </svg>
                      ) : (
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                          <path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z"/>
                        </svg>
                      )}
                    </button>
                  </div>
                  {!user ? (
                    <div className="flex flex-col gap-2">
                      <Button variant="outline" onClick={() => setModal("login")}>
                        Log in
                      </Button>
                      <Button onClick={() => setModal("signup")}>
                        Sign Up
                      </Button>
                    </div>
                  ) : (
                    <div className="flex flex-col gap-2">
                      <div className="flex items-center gap-2.5 rounded-xl border border-[var(--border)] bg-[var(--surface-glass)] px-3 py-2">
                        <div className="flex h-8 w-8 items-center justify-center rounded-full bg-gradient-to-br from-indigo-500 to-violet-600 text-xs font-semibold text-white shadow-sm">
                          {displayInitial}
                        </div>
                        <span className="text-sm font-medium text-[var(--text-primary)]">{displayName}</span>
                      </div>
                      <Button variant="outline" onClick={logout}>
                        Logout
                      </Button>
                    </div>
                  )}
                </div>
              </div>
              </div>
            </Container>
          </div>
        )}
      </header>

      {/* ─── Pages ─── */}
      <main className="flex-1">
        <Routes>
          <Route path="/" element={<HomePage />} />
          <Route path="/landing" element={<LandingPage />} />
          <Route path="/projects" element={<ProjectsPage user={user} />} />
          <Route path="/projects/:id" element={<ProjectDetailPage user={user} />} />
          <Route path="/about" element={<AboutPage />} />
          <Route path="/contact" element={<ContactPage />} />
        </Routes>
      </main>

      {/* ─── Footer ─── */}
      <footer className="relative border-t border-[var(--border)] bg-[var(--bg-secondary)]">
        <div className="absolute inset-x-0 top-0 h-px bg-gradient-to-r from-transparent via-[var(--accent-glow-strong)] to-transparent" />
        <Container className="py-12">
          <div className="grid gap-8 sm:grid-cols-2 lg:grid-cols-4">
            {/* Brand */}
            <div className="space-y-4 lg:col-span-2">
              <div className="flex items-center gap-3">
                <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-gradient-to-br from-indigo-500 to-violet-600 text-xs font-bold text-white shadow-md">
                  DA
                </div>
                <span className="text-base font-semibold text-[var(--text-heading)]">
                  Deen<span className="text-[var(--accent)]">Associate</span>
                </span>
              </div>
              <p className="max-w-sm text-sm leading-relaxed text-[var(--text-muted)]">
                Project management and unit tracking portal for Deen Associate.
              </p>
            </div>

            {/* Quick Links */}
            <div>
              <h4 className="mb-4 text-xs font-semibold uppercase tracking-[0.15em] text-[var(--text-muted)]">
                Navigation
              </h4>
              <ul className="space-y-2.5">
                {NAV_LINKS.map((link) => (
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

            {/* Contact */}
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
  );
}

export default App;
