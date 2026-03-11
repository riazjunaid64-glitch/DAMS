import { Link } from "react-router-dom";
import Button from "../lib/Button";
import Container from "../lib/Container";

export default function LandingPage() {
  return (
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
              <Link to="/projects">
                <Button>Explore Projects</Button>
              </Link>
              <Link to="/contact">
                <Button variant="outline">Contact Us</Button>
              </Link>
            </div>
          </div>
        </Container>
      </section>
    </main>
  );
}
