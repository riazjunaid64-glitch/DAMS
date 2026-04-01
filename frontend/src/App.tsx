import { useEffect, useMemo, useState } from "react";
import { Link, Route, Routes, useLocation } from "react-router-dom";
import { api } from "./api/api";
import AuthModal from "./components/AuthModal";
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
            ? "border-b border-white/[0.06] bg-[#0a0a0f]/80 backdrop-blur-xl shadow-[0_1px_3px_rgba(0,0,0,0.3)]"
            : "bg-transparent"
        }`}
      >
        <Container className="flex h-16 items-center justify-between">
          {/* Logo */}
          <Link to="/" className="group flex items-center gap-3">
            <div className="relative flex h-9 w-9 items-center justify-center rounded-xl bg-gradient-to-br from-indigo-500 to-violet-600 shadow-[0_4px_12px_rgba(99,102,241,0.3)] transition-transform group-hover:scale-105">
              <span className="text-sm font-bold text-white">DA</span>
            </div>
            <span className="text-base font-semibold tracking-tight text-white">
              Deen<span className="text-indigo-400">Associate</span>
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
                      ? "text-white bg-white/[0.06]"
                      : "text-[#a1a1b5] hover:text-white hover:bg-white/[0.04]"
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
            {!user ? (
              <>
                <Button variant="ghost" size="sm" onClick={() => setModal("login")}>
                  Log in
                </Button>
                <Button size="sm" onClick={() => setModal("signup")}>
                  Get Started
                </Button>
              </>
            ) : (
              <div className="flex items-center gap-3">
                <div className="flex items-center gap-2.5 rounded-full border border-white/[0.06] bg-white/[0.03] px-3 py-1.5">
                  <div className="flex h-7 w-7 items-center justify-center rounded-full bg-gradient-to-br from-indigo-500 to-violet-600 text-xs font-semibold text-white">
                    {displayInitial}
                  </div>
                  <span className="text-sm font-medium text-[#a1a1b5]">{displayName}</span>
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
            className="inline-flex h-10 w-10 items-center justify-center rounded-xl border border-white/[0.08] bg-white/[0.03] text-white transition hover:bg-white/[0.06] sm:hidden"
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
          <div className="animate-fade-in border-t border-white/[0.06] bg-[#0a0a0f]/95 backdrop-blur-xl sm:hidden">
            <Container className="py-5">
              <div className="flex flex-col gap-1">
                {NAV_LINKS.map((link) => {
                  const active = location.pathname === link.to;
                  return (
                    <Link
                      key={link.to}
                      to={link.to}
                      className={`rounded-xl px-4 py-3 text-sm font-medium transition-all ${
                        active
                          ? "text-white bg-white/[0.06]"
                          : "text-[#a1a1b5] hover:text-white hover:bg-white/[0.04]"
                      }`}
                    >
                      {link.label}
                    </Link>
                  );
                })}
                <div className="mt-4 flex flex-col gap-2 border-t border-white/[0.06] pt-4">
                  {!user ? (
                    <>
                      <Button variant="outline" onClick={() => setModal("login")}>
                        Log in
                      </Button>
                      <Button onClick={() => setModal("signup")}>
                        Get Started
                      </Button>
                    </>
                  ) : (
                    <>
                      <div className="flex items-center gap-2.5 px-1 py-2">
                        <div className="flex h-8 w-8 items-center justify-center rounded-full bg-gradient-to-br from-indigo-500 to-violet-600 text-xs font-semibold text-white">
                          {displayInitial}
                        </div>
                        <span className="text-sm font-medium text-white">{displayName}</span>
                      </div>
                      <Button variant="outline" onClick={logout}>
                        Logout
                      </Button>
                    </>
                  )}
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
      <footer className="relative border-t border-white/[0.06] bg-[#0a0a0f]">
        <div className="absolute inset-x-0 top-0 h-px bg-gradient-to-r from-transparent via-indigo-500/20 to-transparent" />
        <Container className="py-12">
          <div className="grid gap-8 sm:grid-cols-2 lg:grid-cols-4">
            {/* Brand */}
            <div className="space-y-4 lg:col-span-2">
              <div className="flex items-center gap-3">
                <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-gradient-to-br from-indigo-500 to-violet-600 text-xs font-bold text-white">
                  DA
                </div>
                <span className="text-base font-semibold text-white">
                  Deen<span className="text-indigo-400">Associate</span>
                </span>
              </div>
              <p className="max-w-sm text-sm leading-relaxed text-[#6b6b80]">
                A modern workspace to track projects, manage units, and keep every stakeholder informed from start to finish.
              </p>
            </div>

            {/* Quick Links */}
            <div>
              <h4 className="mb-4 text-xs font-semibold uppercase tracking-[0.15em] text-[#6b6b80]">
                Navigation
              </h4>
              <ul className="space-y-2.5">
                {NAV_LINKS.map((link) => (
                  <li key={link.to}>
                    <Link
                      to={link.to}
                      className="text-sm text-[#a1a1b5] transition-colors hover:text-white"
                    >
                      {link.label}
                    </Link>
                  </li>
                ))}
              </ul>
            </div>

            {/* Contact */}
            <div>
              <h4 className="mb-4 text-xs font-semibold uppercase tracking-[0.15em] text-[#6b6b80]">
                Contact
              </h4>
              <ul className="space-y-2.5 text-sm text-[#a1a1b5]">
                <li>contact@deenassociate.com</li>
                <li>+1 (555) 123-4567</li>
                <li>123 Business Avenue</li>
              </ul>
            </div>
          </div>

          <div className="mt-10 flex flex-col items-center justify-between gap-4 border-t border-white/[0.06] pt-8 sm:flex-row">
            <p className="text-xs text-[#6b6b80]">
              © {new Date().getFullYear()} Deen Associate. All rights reserved.
            </p>
            <div className="flex items-center gap-4">
              <span className="text-xs text-[#6b6b80]">Built with precision</span>
              <span className="h-1 w-1 rounded-full bg-indigo-500" />
              <span className="text-xs text-[#6b6b80]">DAMS v2.0</span>
            </div>
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
