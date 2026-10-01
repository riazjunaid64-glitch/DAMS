// @vitest-environment happy-dom
import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { IconBlock, IconIdCard } from "./icons.tsx";
import { ConfirmDialog } from "./Modal.tsx";
import { PageHeader } from "./PageHeader.tsx";

beforeEach(() => {
  vi.stubGlobal("matchMedia", (query: string) => ({ matches: false, media: query, addEventListener: () => {}, removeEventListener: () => {} }));
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("ConfirmDialog content", () => {
  it("shows what it is given under the message and keeps its buttons", () => {
    const onConfirm = vi.fn();
    render(
      <ConfirmDialog open onClose={() => {}} onConfirm={onConfirm} title="Block Usman Tariq?" message="New bookings will be stopped." confirmLabel="Block customer" danger>
        <label>Reason<textarea /></label>
      </ConfirmDialog>,
    );
    const dialog = screen.getByRole("dialog");
    expect(within(dialog).getByText("New bookings will be stopped.")).toBeTruthy();
    expect(within(dialog).getByLabelText("Reason")).toBeTruthy();
    fireEvent.click(within(dialog).getByRole("button", { name: "Block customer" }));
    expect(onConfirm).toHaveBeenCalledOnce();
  });

  it("can grey the confirm button, and cannot be closed while loading", () => {
    const onClose = vi.fn();
    const { rerender } = render(<ConfirmDialog open onClose={onClose} onConfirm={() => {}} message="Sure?" confirmLabel="Go" confirmDisabled />);
    expect((screen.getByRole("button", { name: "Go" }) as HTMLButtonElement).disabled).toBe(true);
    rerender(<ConfirmDialog open onClose={onClose} onConfirm={() => {}} message="Sure?" confirmLabel="Go" loading />);
    expect((screen.getByRole("button", { name: "Cancel" }) as HTMLButtonElement).disabled).toBe(true);
  });
});

describe("PageHeader badge and details", () => {
  it("shows a badge with its own words and tone, and a details row under the title", () => {
    render(
      <MemoryRouter>
        <PageHeader title="Usman Tariq" badge={{ text: "2 documents needed", tone: "orange" }} details={<span>Customer since Apr 20, 2026</span>} />
      </MemoryRouter>,
    );
    const badge = screen.getByText("2 documents needed");
    expect(badge.className).toContain("text-warning");
    expect(screen.getByText("Customer since Apr 20, 2026")).toBeTruthy();
  });
});

describe("new icons", () => {
  it("draw", () => {
    const { container } = render(<><IconBlock /><IconIdCard /></>);
    expect(container.querySelectorAll("svg")).toHaveLength(2);
  });
});
