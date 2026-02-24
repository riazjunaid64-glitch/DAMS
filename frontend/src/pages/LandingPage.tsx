import { useEffect, useState } from "react";
import { api } from "../api/api";
import AuthModal from "../components/AuthModal.tsx";
import Button from "../lib/Button";
import Container from "../lib/Container";
import Field from "../lib/Field";
import NavLink from "../lib/NavLink";
import Section from "../lib/Section";

interface User {
  userId: string;
  email: string;
  firstName: string;
  role: string;
}

export default function LandingPage() {
  const [modal, setModal] = useState<null | "login" | "signup">(null);
  const [user, setUser] = useState<User | null>(null);
  const [mobileOpen, setMobileOpen] = useState(false);

  const fetchProfile = async () => {
    const res = await api("/api/Auth/profile");
    if (!res.ok) {
      localStorage.removeItem("token");
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
          <div className="flex items-center gap-3">
            <div className="h-9 w-9 rounded-xl border border-amber-300/40 bg-amber-400/10" />
            <div className="text-base font-semibold tracking-wide">Deen Associate</div>
          </div>
          <nav className="hidden items-center gap-6 sm:flex">
            <NavLink href="#projects">Projects</NavLink>
            <NavLink href="#about">About Us</NavLink>
            <NavLink href="#contact">Contact Us</NavLink>
           
            {!user ? (
              <>
                <Button
                  variant="ghost"
                  className="hidden sm:inline-flex"
                  onClick={() => setModal("login")}
                >
                  Login
                </Button>
                <Button
                  variant="primary"
                  className="hidden sm:inline-flex"
                  onClick={() => setModal("signup")}
                >
                  Sign Up
                </Button>
              </>
            ) : (
              <div className="hidden items-center gap-3 sm:flex">
                <div className="flex items-center gap-2">
                  <div className="flex h-8 w-8 items-center justify-center rounded-full bg-amber-400/20 text-sm font-semibold text-amber-300">
                    {user.firstName?.[0]?.toUpperCase() || "U"}
                  </div>
                  <span className="text-sm font-medium">{user.firstName}</span>
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
                <NavLink href="#projects" onClick={() => setMobileOpen(false)}>
                  Projects
                </NavLink>
                <NavLink href="#about" onClick={() => setMobileOpen(false)}>
                  About Us
                </NavLink>
                <NavLink href="#contact" onClick={() => setMobileOpen(false)}>
                  Contact Us
                </NavLink>
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
                        {user.firstName?.[0]?.toUpperCase() || "U"}
                      </div>
                      <span className="text-sm font-medium">{user.firstName}</span>
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

      <main>
        <section className="relative min-h-screen overflow-hidden">
          <div className="absolute inset-0 bg-[radial-gradient(circle_at_top,rgba(56,189,248,0.18),transparent_55%)]" />
          <div className="absolute inset-0 bg-[linear-gradient(120deg,rgba(15,23,42,0.9),rgba(15,23,42,0.7))]" />
          <div className="absolute right-[-20%] top-[-30%] h-[420px] w-[420px] rounded-full bg-amber-400/10 blur-3xl" />
          <div className="absolute bottom-[-30%] left-[-10%] h-[380px] w-[380px] rounded-full bg-cyan-400/10 blur-3xl" />
          <Container className="relative flex min-h-screen items-center justify-center py-24">
            <div className="mx-auto max-w-4xl text-center">
              <p className="hero-animate text-xs font-semibold uppercase tracking-[0.3em] text-amber-300">
                Deen Associate Management System
              </p>
              <h1 className="hero-title hero-animate-delay mt-4 text-4xl font-semibold sm:text-6xl lg:text-7xl">
                Build, Manage, and Deliver with Confidence
              </h1>
              <p className="hero-animate-late mx-auto mt-5 max-w-2xl text-base text-slate-200 sm:text-lg">
                A modern workspace to track projects, align teams, and keep every stakeholder
                informed from start to finish.
              </p>
              <div className="hero-animate-late mt-8 flex flex-wrap items-center justify-center gap-4">
                <Button>Explore Projects</Button>
                <Button variant="outline">Contact Us</Button>
              </div>
            </div>
          </Container>
        </section>

        <Section
          id="projects"
          eyebrow="Projects"
          title="Featured Projects"
          description="A quick overview of our main developments. We will connect these to real data later."
          className="bg-slate-950 py-16 sm:py-20"
        >
          <Container>
            <div className="grid gap-6 md:grid-cols-3">
              {[
                "Urban Complex",
                "Deen Villas",
                "Floria Heights",
              ].map((name) => (
                <div
                  key={name}
                  className="rounded-3xl border border-white/10 bg-white/5 p-6 shadow-[0_24px_60px_-50px_rgba(15,23,42,0.9)]"
                >
                  <div className="mb-5 h-40 rounded-2xl bg-gradient-to-br from-slate-800 to-slate-900" />
                  <h3 className="text-lg font-semibold">{name}</h3>
                  <p className="mt-3 text-sm text-slate-300">
                    Placeholder description for this project. Replace with real
                    copy when ready.
                  </p>
                </div>
              ))}
            </div>
          </Container>
        </Section>

        <Section
          id="about"
          eyebrow="About Us"
          title="Building more than structures"
          description="This section is a placeholder for your real story and values."
          className="bg-slate-900/70 py-16 sm:py-20"
        >
          <Container>
            <div className="grid gap-10 lg:grid-cols-[1.1fr_0.9fr] lg:items-center">
              <div className="space-y-6 text-slate-300">
                <p>
                  At Deen Associate, we focus on transparency, efficiency, and
                  modern management practices. Use this space to explain the
                  vision, mission, and the experience your team brings to every
                  project.
                </p>
                <p>
                  You can also highlight your service standards, client
                  relationships, and any operational advantages that set you
                  apart.
                </p>
              </div>
              <div className="rounded-3xl border border-white/10 bg-gradient-to-br from-slate-800 to-slate-950 p-8">
                <div className="grid gap-6 sm:grid-cols-2">
                  {[
                    { label: "Years Experience", value: "15+" },
                    { label: "Units Delivered", value: "500+" },
                    { label: "Client Satisfaction", value: "98%" },
                    { label: "Major Projects", value: "3" },
                  ].map((item) => (
                    <div key={item.label}>
                      <p className="text-2xl font-semibold text-amber-300">
                        {item.value}
                      </p>
                      <p className="text-sm text-slate-300">{item.label}</p>
                    </div>
                  ))}
                </div>
              </div>
            </div>
          </Container>
        </Section>

        <Section
          id="contact"
          eyebrow="Contact"
          title="Start the Conversation"
          description="Send a message and our team will respond soon."
          className="bg-slate-950 py-16 sm:py-20"
        >
          <Container>
            <div className="grid gap-10 lg:grid-cols-2 lg:items-start">
              <div className="space-y-6 rounded-3xl border border-white/10 bg-white/5 p-8">
                <div>
                  <h3 className="text-lg font-semibold">Visit Our Office</h3>
                  <p className="mt-2 text-sm text-slate-300">
                    123 Business Avenue, City Center
                  </p>
                </div>
                <div>
                  <h3 className="text-lg font-semibold">Support Hours</h3>
                  <p className="mt-2 text-sm text-slate-300">
                    Sunday - Thursday: 9am - 6pm
                    <br />
                    Friday: Closed
                  </p>
                </div>
              </div>
              <div className="rounded-3xl border border-white/10 bg-white/5 p-8">
                <h3 className="text-lg font-semibold">Contact Us</h3>
                <p className="mt-3 text-sm text-slate-300">
                  This area can later include your real contact form or any CTA
                  content. For now, the navbar contact button will scroll here.
                </p>
                <div className="mt-6 grid gap-3 text-sm text-slate-300">
                  <p>Phone: +1 (555) 123-4567</p>
                  <p>Email: contact@deenassociate.com</p>
                </div>
              </div>
            </div>
          </Container>
        </Section>
      </main>

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
