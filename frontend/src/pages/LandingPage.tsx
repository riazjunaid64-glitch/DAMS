import { Link } from "react-router-dom";
import Button from "../lib/Button";
import Container from "../lib/Container";

export default function LandingPage() {
  return (
    <section className="relative min-h-[85vh] overflow-hidden">
      <div className="absolute inset-0 mesh-gradient-hero" />
      <div className="dot-grid absolute inset-0 opacity-30" />
      <div className="absolute left-1/2 top-0 h-[500px] w-[500px] -translate-x-1/2 -translate-y-1/3 rounded-full bg-indigo-500/[0.08] blur-[100px]" />

      <Container className="relative flex min-h-[85vh] items-center justify-center py-24">
        <div className="mx-auto max-w-3xl text-center">
          <div className="animate-fade-in-up mb-6 inline-flex items-center gap-2 rounded-full border border-indigo-500/20 bg-indigo-500/[0.08] px-4 py-1.5">
            <span className="h-1.5 w-1.5 rounded-full bg-indigo-400 animate-pulse" />
            <span className="text-xs font-medium text-indigo-300">Welcome to DAMS</span>
          </div>

          <h1 className="animate-fade-in-up-delay-1 text-4xl font-bold leading-[1.1] sm:text-5xl lg:text-6xl">
            <span className="text-white">Manage Projects with </span>
            <span className="bg-gradient-to-r from-indigo-400 to-violet-400 bg-clip-text text-transparent">
              Full Visibility
            </span>
          </h1>

          <p className="animate-fade-in-up-delay-2 mx-auto mt-6 max-w-xl text-base text-[#a1a1b5] sm:text-lg leading-relaxed">
            Track projects, manage units, align teams, and keep stakeholders informed — all in one place.
          </p>

          <div className="animate-fade-in-up-delay-3 mt-10 flex flex-wrap items-center justify-center gap-4">
            <Link to="/projects">
              <Button size="lg">Explore Projects</Button>
            </Link>
            <Link to="/contact">
              <Button variant="outline" size="lg">Contact Us</Button>
            </Link>
          </div>
        </div>
      </Container>
    </section>
  );
}
