import { useEffect, useMemo, useState } from "react";
import { Link, Route, Routes } from "react-router-dom";
import { api } from "./api/api";
import AuthModal from "./components/AuthModal";
import Button from "./lib/Button";
import Container from "./lib/Container";
import AboutPage from "./pages/AboutPage";
import ContactPage from "./pages/ContactPage";
import LandingPage from "./pages/LandingPage";
import ProjectDetailPage from "./pages/ProjectDetailPage";
import ProjectsPage from "./pages/ProjectsPage";

export interface User {
  userId: string;
  email: string;
  role: string;
}

function App() {
  const [modal, setModal] = useState<null | "login" | "signup">(null);
  const [user, setUser] = useState<User | null>(null);
  const [mobileOpen, setMobileOpen] = useState(false);

  const displayName = useMemo(
    () => (user?.email ? user.email.split("@")[0] : "User"),
    [user]
  );
  const displayInitial = displayName[0]?.toUpperCase() ?? "U";

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
    <div className="min-h-screen bg-slate-950 text-white">
      <header className="sticky top-0 z-20 border-b border-white/10 bg-slate-950/80 backdrop-blur">
        <Container className="flex h-16 items-center justify-between">
          <Link to="/" className="flex items-center gap-3">
            <div className="h-9 w-9 rounded-xl border border-amber-300/40 bg-amber-400/10" />
            <div className="text-base font-semibold tracking-wide">Deen Associate</div>
          </Link>
          <nav className="hidden items-center gap-6 sm:flex">
            <Link className="text-sm font-medium text-slate-200/80 transition hover:text-amber-200" to="/projects">Projects</Link>
            <Link className="text-sm font-medium text-slate-200/80 transition hover:text-amber-200" to="/about">About Us</Link>
            <Link className="text-sm font-medium text-slate-200/80 transition hover:text-amber-200" to="/contact">Contact Us</Link>
            {!user ? (
              <>
                <Button variant="ghost" className="hidden sm:inline-flex" onClick={() => setModal("login")}>
                  Login
                </Button>
                <Button variant="primary" className="hidden sm:inline-flex" onClick={() => setModal("signup")}>
                  Sign Up
                </Button>
              </>
            ) : (
              <div className="hidden items-center gap-3 sm:flex">
                <div className="flex items-center gap-2">
                  <div className="flex h-8 w-8 items-center justify-center rounded-full bg-amber-400/20 text-sm font-semibold text-amber-300">
                    {displayInitial}
                  </div>
                  <span className="text-sm font-medium">{displayName}</span>
                </div>
                <Button variant="outline" onClick={logout}>
                  Logout
                </Button>
              </div>
            )}
          </nav>
          <button
            type="button"
            className="inline-flex items-center justify-center rounded-full border border-white/20 p-2 text-white sm:hidden"
            aria-label="Toggle navigation"
            onClick={() => setMobileOpen((prev) => !prev)}
          >
            <span className="block h-5 w-5">
              <span className="block h-[2px] w-5 bg-white" />
              <span className="mt-1.5 block h-[2px] w-5 bg-white" />
              <span className="mt-1.5 block h-[2px] w-5 bg-white" />
            </span>
          </button>
        </Container>
        {mobileOpen && (
          <div className="border-t border-white/10 bg-slate-950/95 sm:hidden">
            <Container className="py-4">
              <div className="flex flex-col gap-4">
                <Link className="text-sm font-medium text-slate-200/80 transition hover:text-amber-200" to="/projects" onClick={() => setMobileOpen(false)}>Projects</Link>
                <Link className="text-sm font-medium text-slate-200/80 transition hover:text-amber-200" to="/about" onClick={() => setMobileOpen(false)}>About Us</Link>
                <Link className="text-sm font-medium text-slate-200/80 transition hover:text-amber-200" to="/contact" onClick={() => setMobileOpen(false)}>Contact Us</Link>
                {!user ? (
                  <div className="flex flex-col gap-3">
                    <Button
                      variant="outline"
                      onClick={() => {
                        setMobileOpen(false);
                        setModal("login");
                      }}
                    >
                      Login
                    </Button>
                    <Button
                      onClick={() => {
                        setMobileOpen(false);
                        setModal("signup");
                      }}
                    >
                      Sign Up
                    </Button>
                  </div>
                ) : (
                  <div className="flex flex-col gap-3">
                    <div className="flex items-center gap-2">
                      <div className="flex h-8 w-8 items-center justify-center rounded-full bg-amber-400/20 text-sm font-semibold text-amber-300">
                        {displayInitial}
                      </div>
                      <span className="text-sm font-medium">{displayName}</span>
                    </div>
                    <Button
                      variant="outline"
                      onClick={() => {
                        setMobileOpen(false);
                        logout();
                      }}
                    >
                      Logout
                    </Button>
                  </div>
                )}
              </div>
            </Container>
          </div>
        )}
      </header>

      <Routes>
        <Route path="/" element={<LandingPage />} />
        <Route path="/projects" element={<ProjectsPage user={user} />} />
        <Route path="/projects/:id" element={<ProjectDetailPage user={user} />} />
        <Route path="/about" element={<AboutPage />} />
        <Route path="/contact" element={<ContactPage />} />
      </Routes>

      <footer className="border-t border-white/10 bg-slate-950 py-10">
        <Container className="flex flex-col gap-4 text-sm text-slate-400 sm:flex-row sm:items-center sm:justify-between">
          <p>Deen Associate Management System</p>
          <p>© 2026 Deen Associate. All rights reserved.</p>
        </Container>
      </footer>

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
