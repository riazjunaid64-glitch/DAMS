// @vitest-environment happy-dom
import { cleanup, render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { REDUCED_MOTION_QUERY } from "../components/website/motion.ts";
import { SITE_CONTACT } from "../lib/siteContact.ts";
import ContactPage from "./ContactPage.tsx";

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
      <ContactPage />
    </MemoryRouter>,
  );
}

beforeEach(() => stubMedia(false));
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("Contact page (KAN-86)", () => {
  it("opens WhatsApp, the dialler and email from the contact-details setting", () => {
    show();
    expect(screen.getByText("CONTACT US")).toBeTruthy();
    expect(screen.getByRole("heading", { level: 1, name: "Get in touch" })).toBeTruthy();
    expect(screen.getByText("Talk to our team about bookings, site visits and investment plans.")).toBeTruthy();

    const whatsapp = screen.getByRole("link", { name: /Message on WhatsApp/ });
    expect(whatsapp.getAttribute("href")).toBe(SITE_CONTACT.whatsappUrl);
    expect(whatsapp.getAttribute("target")).toBe("_blank");
    expect(whatsapp.textContent).toContain(SITE_CONTACT.phoneMobile);
    expect(whatsapp.className).toContain("bg-primary");
    expect(whatsapp.className).toContain("web-lift");
    expect(whatsapp.querySelector(".md\\:hidden")).toBeTruthy();
    expect(whatsapp.querySelector(".hidden.md\\:block")).toBeTruthy();

    const call = screen.getByRole("link", { name: /Call the office/ });
    expect(call.getAttribute("href")).toBe(`tel:${SITE_CONTACT.phoneTel}`);
    expect(call.getAttribute("target")).toBeNull();
    expect(call.textContent).toContain(SITE_CONTACT.phone);
    expect(call.textContent).not.toContain(SITE_CONTACT.phoneMobile);
    expect(call.className).toContain("bg-card");

    const email = screen.getByRole("link", { name: /Send an email/ });
    expect(email.getAttribute("href")).toBe(`mailto:${SITE_CONTACT.email}`);
    expect(email.textContent).toContain(SITE_CONTACT.email);
    expect(document.querySelector(".md\\:grid-cols-3")).toBeTruthy();
  });

  it("shows the address, hours and map from the setting, and opens directions", () => {
    show();
    expect(screen.getByText(SITE_CONTACT.address)).toBeTruthy();
    expect(screen.getByText(SITE_CONTACT.hours)).toBeTruthy();

    const directions = screen.getByRole("link", { name: "Get directions" });
    expect(directions.getAttribute("href")).toBe(SITE_CONTACT.mapsUrl);
    expect(directions.getAttribute("target")).toBe("_blank");
    expect(directions.getAttribute("rel")).toContain("noopener");
    expect(directions.parentElement?.className).toContain("web-lift");

    const map = screen.getByTitle("Deen Associate office location");
    expect(map.getAttribute("src")).toBe(SITE_CONTACT.mapsEmbedUrl);
    expect(map.parentElement?.className).toContain("rounded-card");
    expect(map.parentElement?.parentElement?.className).toContain("h-[200px]");
  });

  it("drops the enquiry form and the old contact sections", () => {
    show();
    expect(screen.queryByRole("form")).toBeNull();
    expect(screen.queryByRole("textbox")).toBeNull();
    expect(screen.queryByText("How We Help")).toBeNull();
    expect(screen.queryByText("Make an Appointment")).toBeNull();
    expect(screen.queryByText("Call Office")).toBeNull();
    expect(screen.queryByText("Email Instead")).toBeNull();
    expect(screen.queryByText("Enquiry Type")).toBeNull();
    expect(screen.queryByText("Head Office")).toBeNull();
    expect(screen.queryByText("Open in Google Maps")).toBeNull();
    expect(screen.queryByText("Send via WhatsApp")).toBeNull();
    expect(screen.queryByText(SITE_CONTACT.website)).toBeNull();
    expect(screen.getAllByText(SITE_CONTACT.phoneMobile)).toHaveLength(1);
  });

  it("does not fade when reduced motion is on", () => {
    stubMedia(true);
    show();
    const heading = screen.getByRole("heading", { level: 1, name: "Get in touch" });
    expect(heading.parentElement?.className ?? "").not.toContain("web-fade-up");
    const card = screen.getByRole("link", { name: /Message on WhatsApp/ });
    expect(card.parentElement?.className ?? "").not.toContain("web-fade-up");
    const map = screen.getByTitle("Deen Associate office location");
    expect(map.parentElement?.parentElement?.className ?? "").not.toContain("web-fade-up");
  });

  it("fades the label, headline, cards, visit card and map in turn", () => {
    show();
    const label = screen.getByText("CONTACT US");
    expect(label.parentElement?.className).toContain("web-fade-up");
    expect(label.parentElement?.style.animationDelay).toBe("");

    const heading = screen.getByRole("heading", { level: 1 });
    expect(heading.parentElement?.className).toContain("web-fade-up");
    expect(heading.parentElement?.style.animationDelay).toBe("90ms");

    const whatsapp = screen.getByRole("link", { name: /Message on WhatsApp/ });
    expect(whatsapp.parentElement?.style.animationDelay).toBe("180ms");
    const email = screen.getByRole("link", { name: /Send an email/ });
    expect(email.parentElement?.style.animationDelay).toBe("360ms");

    const visit = screen.getByRole("heading", { name: "Visit us" });
    expect(visit.parentElement?.parentElement?.style.animationDelay).toBe("450ms");

    const map = screen.getByTitle("Deen Associate office location");
    expect(map.parentElement?.parentElement?.className).toContain("web-fade-up");
    expect(map.parentElement?.parentElement?.style.animationDelay).toBe("540ms");
  });
});
