// @vitest-environment happy-dom
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { PHONE_QUERY } from "../components/ui/useMediaQuery.ts";
import { REDUCED_MOTION_QUERY } from "../components/website/motion.ts";
import type { ProjectFromApi } from "../utils/parseProject.ts";
import HomePage from "./HomePage.tsx";

const reload = vi.fn(async () => {});
let state: { projects: ProjectFromApi[]; loading: boolean; error: string | null; reload: () => Promise<void> };

vi.mock("../contexts/projectsContextValue", () => ({
  useProjects: () => state,
}));

function project(id: number, over: Partial<ProjectFromApi> = {}): ProjectFromApi {
  return {
    id,
    projectName: `Project ${id}`,
    location: id === 2 ? "Faisal Hills, Islamabad and beyond the ridge" : "B-17, Islamabad",
    category: id % 3 === 0 ? "Mixed use" : id % 2 === 0 ? "Commercial" : "Residential",
    coverImageUrl: `/uploads/project-${id}.jpg`,
    description: null,
    startingDate: "2020-01-01",
    expectedCompletionDate: null,
    status: 2,
    createdAt: "2020-01-01",
    totalUnits: 10,
    availableUnits: 4,
    bookedUnits: 3,
    soldUnits: 3,
    floorCount: 0,
    ...over,
  };
}

function stubMedia({ phone = false, reduce = false } = {}) {
  vi.stubGlobal("matchMedia", (query: string) => ({
    matches: query === PHONE_QUERY ? phone : query === REDUCED_MOTION_QUERY ? reduce : false,
    media: query,
    addEventListener: () => {},
    removeEventListener: () => {},
  }));
}

function show() {
  return render(
    <MemoryRouter>
      <HomePage />
    </MemoryRouter>,
  );
}

beforeEach(() => {
  reload.mockReset();
  stubMedia();
  state = {
    projects: [1, 2, 3, 4, 5, 6, 7].map((id) => project(id)).concat(project(8, { status: 4, projectName: "Cancelled scheme" })),
    loading: false,
    error: null,
    reload,
  };
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("Home page (KAN-84)", () => {
  it("shows the navy headline, the two links and the four company numbers", () => {
    show();
    const heading = screen.getByRole("heading", { level: 1 });
    expect(heading.textContent).toBe("Trusted real estate consultancy & development in Islamabad");
    expect(heading.querySelector("span")?.className).toContain("text-gold");
    expect(heading.closest("section")?.className).toContain("bg-primary");
    expect(heading.closest("section")?.querySelector("img")).toBeNull();

    expect(screen.getByRole("link", { name: "Explore projects" }).getAttribute("href")).toBe("/projects");
    expect(screen.getByRole("link", { name: "Explore projects" }).className).toContain("bg-gold");
    expect(screen.getByRole("link", { name: "Contact us" }).getAttribute("href")).toBe("/contact");
    expect(screen.getByRole("link", { name: "Contact us" }).className).toContain("border-white");
    expect(screen.getByRole("link", { name: "All projects" }).getAttribute("href")).toBe("/projects");

    expect(screen.getByText("Established in Islamabad")).toBeTruthy();
    expect(screen.getByText("Commercial projects")).toBeTruthy();
    expect(screen.getByText("Apartments & units")).toBeTruthy();
    expect(screen.getByText("Years of experience")).toBeTruthy();
    expect(document.querySelector("[data-layout='row']")).toBeTruthy();
  });

  it("links the first six public projects and drops the View Details bar", () => {
    show();
    expect(screen.getByRole("heading", { name: "Featured projects" })).toBeTruthy();
    for (const id of [1, 2, 3, 4, 5, 6]) {
      const card = screen.getByRole("link", { name: new RegExp(`Project ${id}`) });
      expect(card.getAttribute("href")).toBe(`/projects/${id}`);
      expect(card.textContent).not.toContain("View Details");
    }
    expect(screen.queryByRole("link", { name: /Project 7/ })).toBeNull();
    expect(screen.queryByText("Cancelled scheme")).toBeNull();
    expect(screen.queryByText("View Details")).toBeNull();
    expect(screen.getAllByText("Residential").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Mixed use").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Commercial").length).toBeGreaterThan(0);
    const longLocation = screen.getByText("Faisal Hills, Islamabad and beyond the ridge");
    expect(longLocation.className).toContain("truncate");
    const grid = screen.getByRole("heading", { name: "Featured projects" }).closest("section")?.querySelector(".grid");
    expect(grid?.className).toContain("grid-cols-2");
    expect(grid?.className).toContain("md:grid-cols-3");
  });

  it("uses the 2 by 2 numbers on a phone", () => {
    stubMedia({ phone: true });
    show();
    expect(document.querySelector("[data-layout='panel']")?.className).toContain("grid-cols-2");
    expect(screen.getByRole("link", { name: "Explore projects" }).className).toContain("w-full");
  });

  it("shows six grey placeholders while projects load", () => {
    state = { projects: [], loading: true, error: null, reload };
    show();
    expect(screen.getAllByTestId("featured-placeholder")).toHaveLength(6);
    expect(screen.queryByText("No featured projects yet.")).toBeNull();
    expect(screen.queryByRole("link", { name: /Project/ })).toBeNull();
  });

  it("says when there are no public projects", () => {
    state = { projects: [project(9, { status: 5, projectName: "Archived scheme" })], loading: false, error: null, reload };
    show();
    expect(screen.getByText("No featured projects yet.")).toBeTruthy();
    expect(screen.queryByText("Archived scheme")).toBeNull();
  });

  it("shows the error and tries again", () => {
    state = { projects: [], loading: false, error: "Unable to load projects right now.", reload };
    show();
    expect(screen.getByText("Unable to load projects right now.")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Try again" }));
    expect(reload).toHaveBeenCalledOnce();
  });

  it("shows the final numbers with no fade when reduced motion is on", () => {
    stubMedia({ reduce: true });
    show();
    expect(screen.getByText("2009")).toBeTruthy();
    expect(screen.getByText("9+")).toBeTruthy();
    expect(screen.getByText("10K+")).toBeTruthy();
    expect(screen.getByText("17+")).toBeTruthy();
    expect(screen.queryByText("0K+")).toBeNull();
    expect(screen.getByRole("heading", { level: 1 }).parentElement?.className ?? "").not.toContain("web-fade-up");
    const card = screen.getByRole("link", { name: /Project 1/ });
    expect(card.parentElement?.className ?? "").not.toContain("web-fade-up");
  });
});
