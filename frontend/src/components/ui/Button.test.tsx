// @vitest-environment happy-dom
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { MemoryRouter, useLocation } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { Button } from "./Button.tsx";

function Where() {
  const location = useLocation();
  return <output data-testid="where">{location.pathname}</output>;
}

afterEach(() => {
  cleanup();
});

describe("Button links", () => {
  it("forwards aria-label, title, id, data, style, tabIndex, and onClick to an in-app link", () => {
    const onClick = vi.fn();
    render(
      <MemoryRouter initialEntries={["/here"]}>
        <Button
          to="/projects"
          aria-label="Explore projects"
          title="Projects"
          id="explore"
          data-track="home"
          style={{ marginTop: 8 }}
          tabIndex={2}
          onClick={onClick}
        >
          Explore
        </Button>
        <Where />
      </MemoryRouter>,
    );

    const link = screen.getByRole("link", { name: "Explore projects" });
    expect(link.getAttribute("title")).toBe("Projects");
    expect(link.id).toBe("explore");
    expect(link.getAttribute("data-track")).toBe("home");
    expect(link.style.marginTop).toBe("8px");
    expect(link.getAttribute("tabindex")).toBe("2");
    expect(link.getAttribute("href")).toBe("/projects");

    fireEvent.click(link);
    expect(onClick).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId("where").textContent).toBe("/projects");
  });

  it("forwards the same props to an external anchor", () => {
    const onClick = vi.fn();
    render(
      <Button
        href="https://maps.example/office"
        target="_blank"
        rel="noopener noreferrer"
        aria-label="Office map"
        title="Open the map"
        id="map"
        data-track="contact"
        style={{ marginTop: 4 }}
        tabIndex={3}
        onClick={onClick}
      >
        Map
      </Button>,
    );

    const link = screen.getByRole("link", { name: "Office map" });
    expect(link.getAttribute("href")).toBe("https://maps.example/office");
    expect(link.getAttribute("target")).toBe("_blank");
    expect(link.getAttribute("rel")).toBe("noopener noreferrer");
    expect(link.getAttribute("title")).toBe("Open the map");
    expect(link.id).toBe("map");
    expect(link.getAttribute("data-track")).toBe("contact");
    expect(link.style.marginTop).toBe("4px");
    expect(link.getAttribute("tabindex")).toBe("3");

    fireEvent.click(link);
    expect(onClick).toHaveBeenCalledTimes(1);
  });

  it("does not navigate or act when a link is disabled", () => {
    const onClick = vi.fn();
    render(
      <MemoryRouter initialEntries={["/here"]}>
        <Button to="/there" disabled aria-label="Go there" title="Blocked" id="go" data-track="blocked" onClick={onClick}>
          Go
        </Button>
        <Where />
      </MemoryRouter>,
    );

    const link = screen.getByRole("link", { name: "Go there" });
    expect(link.getAttribute("aria-disabled")).toBe("true");
    expect(link.hasAttribute("href")).toBe(false);
    expect(link.getAttribute("tabindex")).toBe("-1");
    expect(link.id).toBe("go");
    expect(link.getAttribute("title")).toBe("Blocked");
    expect(link.getAttribute("data-track")).toBe("blocked");
    expect(link.className).toContain("aria-disabled:bg-disabled");
    expect(link.className).toContain("aria-disabled:cursor-not-allowed");

    fireEvent.click(link);
    fireEvent.keyDown(link, { key: "Enter" });
    link.focus();
    expect(onClick).not.toHaveBeenCalled();
    expect(screen.getByTestId("where").textContent).toBe("/here");
    expect(document.activeElement).not.toBe(link);
  });

  it("does not navigate when a link is loading", () => {
    const onClick = vi.fn();
    render(
      <MemoryRouter initialEntries={["/here"]}>
        <Button to="/there" loading aria-label="Opening map" onClick={onClick}>
          Map
        </Button>
        <Where />
      </MemoryRouter>,
    );

    const link = screen.getByRole("link", { name: "Opening map" });
    expect(link.getAttribute("aria-disabled")).toBe("true");
    expect(link.getAttribute("aria-busy")).toBe("true");
    expect(link.hasAttribute("href")).toBe(false);
    expect(link.getAttribute("tabindex")).toBe("-1");
    expect(link.querySelector(".animate-spin")).toBeTruthy();

    fireEvent.click(link);
    expect(onClick).not.toHaveBeenCalled();
    expect(screen.getByTestId("where").textContent).toBe("/here");
  });

  it("still disables a real button", () => {
    const onClick = vi.fn();
    render(<Button disabled aria-label="Save record" title="Save" id="save" onClick={onClick}>Save</Button>);
    const button = screen.getByRole("button", { name: "Save record" });
    expect(button.id).toBe("save");
    expect(button.getAttribute("title")).toBe("Save");
    expect(button.hasAttribute("disabled")).toBe(true);
    fireEvent.click(button);
    expect(onClick).not.toHaveBeenCalled();
    expect(screen.queryByRole("link")).toBeNull();
  });
});
