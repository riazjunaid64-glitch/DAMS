// @vitest-environment happy-dom
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { api } from "../api/api.ts";
import { ToastProvider } from "../components/ui/Toast.tsx";
import UnitDetailPage from "./UnitDetailPage";

vi.mock("../api/api.ts", () => ({ api: vi.fn(), resolveMediaUrl: (path: string) => path }));
vi.mock("../api/media.ts", () => ({ getUnitMedia: vi.fn(async () => []), uploadUnitMediaBulk: vi.fn(), setUnitCoverMedia: vi.fn(), deleteUnitMedia: vi.fn() }));
vi.mock("../contexts/projectsContextValue.ts", () => ({ useProjects: () => ({ reload: vi.fn() }) }));

const json = (body: unknown) => Promise.resolve({ ok: true, status: 200, json: async () => body } as Response);
const unit = (extra: Record<string, unknown>) => ({ id: 810, projectId: 1, unitNumber: "810", unitType: "1 Bed", floorNumber: 8, floorName: "", size: 864, price: 14_255_985, status: "Available", ...extra });

function Where() {
  const location = useLocation();
  return <span data-testid="where">{location.pathname}{location.search}</span>;
}

function show(role: string | null, unitBody: Record<string, unknown>) {
  vi.mocked(api).mockImplementation((path: string) => {
    if (path === "/api/Unit/810") return json(unit(unitBody));
    if (path === "/api/Project/1") return json({ id: 1, projectName: "Floria Heights" });
    return json({});
  });
  render(
    <MemoryRouter initialEntries={["/units/810"]}>
      <ToastProvider>
        <Routes>
          <Route path="/units/:id" element={<UnitDetailPage user={role ? ({ role } as never) : null} />} />
          <Route path="*" element={<Where />} />
        </Routes>
      </ToastProvider>
    </MemoryRouter>,
  );
}

beforeEach(() => vi.mocked(api).mockReset());
afterEach(cleanup);

describe("Unit page booking buttons", () => {
  it("offers Book this unit on an Available unit to Admin and Accountant, and it opens New booking with the unit picked", async () => {
    for (const role of ["Admin", "Accountant"]) {
      show(role, {});
      fireEvent.click(await screen.findByRole("button", { name: "Book this unit" }));
      expect((await screen.findByTestId("where")).textContent).toBe("/confirmed-bookings/new?unitId=810");
      cleanup();
    }
  });

  it("offers Open booking instead on a booked unit, going to that booking", async () => {
    show("Admin", { status: "Booked", liveBookingId: 47, liveBookingReference: "BK-000047" });
    expect(screen.queryByRole("button", { name: "Book this unit" })).toBeNull();
    fireEvent.click(await screen.findByRole("button", { name: "Open booking BK-000047 →" }));
    expect((await screen.findByTestId("where")).textContent).toBe("/confirmed-bookings/47");
  });

  it("shows sales staff and visitors neither button", async () => {
    for (const role of ["Manager", "Employee", null]) {
      show(role, { liveBookingId: 47, liveBookingReference: "BK-000047" });
      await screen.findByText("Unit 810");
      expect(screen.queryByRole("button", { name: "Book this unit" })).toBeNull();
      expect(screen.queryByRole("button", { name: /Open booking/ })).toBeNull();
      cleanup();
    }
  });

  it("asks the server with the signed-in user's token only when they may open bookings", async () => {
    show("Admin", {});
    await screen.findByText("Unit 810");
    expect(vi.mocked(api)).toHaveBeenCalledWith("/api/Unit/810", undefined, true);
    cleanup();
    vi.mocked(api).mockClear();
    show("Manager", {});
    await screen.findByText("Unit 810");
    expect(vi.mocked(api)).toHaveBeenCalledWith("/api/Unit/810", undefined, false);
  });
});
