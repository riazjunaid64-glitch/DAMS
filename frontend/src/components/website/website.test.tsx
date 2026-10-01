// @vitest-environment happy-dom
import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { IconArrowRight } from "../ui/icons.tsx";
import { CompanyNumbers } from "./CompanyNumbers.tsx";
import { FadeUp } from "./FadeUp.tsx";
import { COUNT_UP_MS, countUpFrame, REDUCED_MOTION_QUERY, splitStatValue } from "./motion.ts";

function stubMedia(reduce: boolean) {
  vi.stubGlobal("matchMedia", (query: string) => ({
    matches: query === REDUCED_MOTION_QUERY ? reduce : false,
    media: query,
    addEventListener: () => {},
    removeEventListener: () => {},
  }));
}

beforeEach(() => {
  stubMedia(false);
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("company number count-up", () => {
  it("splits each fixed figure and finishes on that exact text", () => {
    expect(splitStatValue("2009")).toEqual({ target: 2009, suffix: "" });
    expect(splitStatValue("9+")).toEqual({ target: 9, suffix: "+" });
    expect(splitStatValue("10K+")).toEqual({ target: 10, suffix: "K+" });
    expect(splitStatValue("17+")).toEqual({ target: 17, suffix: "+" });
    expect(countUpFrame("2009", 0)).toBe("0");
    expect(countUpFrame("9+", 0)).toBe("0+");
    expect(countUpFrame("10K+", 0)).toBe("0K+");
    expect(countUpFrame("2009", COUNT_UP_MS)).toBe("2009");
    expect(countUpFrame("9+", COUNT_UP_MS)).toBe("9+");
    expect(countUpFrame("10K+", COUNT_UP_MS)).toBe("10K+");
    expect(countUpFrame("17+", COUNT_UP_MS)).toBe("17+");
  });

  it("shows the final numbers immediately when reduced motion is on", () => {
    stubMedia(true);
    render(<CompanyNumbers layout="row" />);
    expect(screen.getByText("2009")).toBeTruthy();
    expect(screen.getByText("9+")).toBeTruthy();
    expect(screen.getByText("10K+")).toBeTruthy();
    expect(screen.getByText("17+")).toBeTruthy();
    expect(screen.queryByText("0K+")).toBeNull();
    expect(screen.getByText("Commercial projects")).toBeTruthy();
    expect(screen.getByText("Apartments & units")).toBeTruthy();
    expect(screen.getByText("Years of experience")).toBeTruthy();
  });

  it("starts from zero when motion is allowed", () => {
    render(<CompanyNumbers layout="panel" />);
    expect(screen.getByText("0")).toBeTruthy();
    expect(screen.getByText("0K+")).toBeTruthy();
    expect(screen.queryByText("2009")).toBeNull();
    expect(document.querySelector("[data-layout='panel']")?.className).toContain("grid-cols-2");
  });

  it("lays the desktop figures out in a row", () => {
    const { container } = render(<CompanyNumbers layout="row" />);
    expect(container.querySelector("[data-layout='row']")?.className).toContain("grid-cols-4");
  });
});

describe("fade up", () => {
  it("does not animate when reduced motion is on", () => {
    stubMedia(true);
    render(<FadeUp><p>Headline</p></FadeUp>);
    expect(screen.getByText("Headline").parentElement?.className ?? "").not.toContain("web-fade-up");
  });

  it("fades up once when motion is allowed", () => {
    render(<FadeUp order={1}><p>Headline</p></FadeUp>);
    const node = screen.getByText("Headline").parentElement;
    expect(node?.className).toContain("web-fade-up");
    expect(node?.style.animationDelay).toBe("90ms");
  });
});

describe("arrow icon", () => {
  it("draws", () => {
    const { container } = render(<IconArrowRight />);
    expect(container.querySelector("svg")).toBeTruthy();
  });
});
