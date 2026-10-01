// @vitest-environment happy-dom
import { cleanup, render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { REDUCED_MOTION_QUERY } from "../components/website/motion.ts";
import AboutPage from "./AboutPage.tsx";

function stubMedia(reduce: boolean) {
  vi.stubGlobal("matchMedia", (query: string) => ({
    matches: query === REDUCED_MOTION_QUERY ? reduce : false,
    media: query,
    addEventListener: () => {},
    removeEventListener: () => {},
  }));
}

function show() {
  return render(
    <MemoryRouter>
      <AboutPage />
    </MemoryRouter>,
  );
}

beforeEach(() => stubMedia(false));
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("About page (KAN-85)", () => {
  it("shows the short story, the navy numbers panel and the three principles", () => {
    show();
    expect(screen.getByText("ABOUT DEEN ASSOCIATE")).toBeTruthy();
    expect(screen.getByRole("heading", { level: 1, name: "Fulfilling promises with trust and confidence" })).toBeTruthy();
    expect(screen.getByText(/honest advice and long-term relationships, not one-time deals/)).toBeTruthy();
    expect(screen.getByText(/Legacy Court and Seventeen Square/)).toBeTruthy();

    const panel = document.querySelector("[data-layout='panel']");
    expect(panel?.className).toContain("grid-cols-2");
    expect(panel?.closest("div")?.className).toContain("bg-primary");
    expect(screen.getByText("Established in Islamabad")).toBeTruthy();
    expect(screen.getByText("Commercial projects")).toBeTruthy();
    expect(screen.getByText("Apartments & units")).toBeTruthy();
    expect(screen.getByText("Years of experience")).toBeTruthy();

    expect(screen.getByRole("heading", { name: "What drives us" })).toBeTruthy();
    expect(screen.getByText("Trust & transparency")).toBeTruthy();
    expect(screen.getByText("Honest advice and clear communication at every stage.")).toBeTruthy();
    expect(screen.getByText("Customer first")).toBeTruthy();
    expect(screen.getByText("We protect your interests, from site selection to handover.")).toBeTruthy();
    expect(screen.getByText("Quality delivery")).toBeTruthy();
    expect(screen.getByText("Every detail is managed to a high professional standard.")).toBeTruthy();
    expect(screen.getByRole("heading", { name: "What drives us" }).closest("section")?.querySelector(".md\\:grid-cols-3")).toBeTruthy();
  });

  it("opens Contact from the call to action and drops the old sections", () => {
    show();
    const contact = screen.getByRole("link", { name: "Contact us" });
    expect(contact.getAttribute("href")).toBe("/contact");
    expect(contact.className).toContain("bg-primary");
    expect(screen.getByText("Looking for the right property?")).toBeTruthy();
    expect(screen.getByText("Talk to our team about bookings, site visits and investment advice.")).toBeTruthy();
    expect(screen.queryByText("WhatsApp Us")).toBeNull();
    expect(screen.queryByText("Head Office")).toBeNull();
    expect(screen.queryByText("Deen Villas")).toBeNull();
    expect(screen.queryByText("Why Choose Us")).toBeNull();
    expect(screen.queryByText("Residential Villas")).toBeNull();
    expect(screen.queryByText("Prime Locations")).toBeNull();
  });

  it("shows the final numbers with no fade when reduced motion is on", () => {
    stubMedia(true);
    show();
    expect(screen.getByText("2009")).toBeTruthy();
    expect(screen.getByText("9+")).toBeTruthy();
    expect(screen.getByText("10K+")).toBeTruthy();
    expect(screen.getByText("17+")).toBeTruthy();
    expect(screen.queryByText("0K+")).toBeNull();
    const heading = screen.getByRole("heading", { level: 1 });
    expect(heading.parentElement?.className ?? "").not.toContain("web-fade-up");
    const card = screen.getByText("Trust & transparency").closest("article");
    expect(card?.parentElement?.className ?? "").not.toContain("web-fade-up");
  });
});
