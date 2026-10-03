// @vitest-environment happy-dom
import { cleanup, render } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import type { User } from "../App.tsx";
import { PHONE_QUERY } from "../components/ui/useMediaQuery.ts";
import AppLayout from "./AppLayout.tsx";
import { navigationFor } from "./navigation.tsx";
import { usePageTrail } from "./trail.ts";

vi.mock("../features/notifications/NotificationBell.tsx", () => ({ default: () => null }));
vi.mock("../components/SiteFooter.tsx", () => ({ default: () => null }));

const accountant: User = { userId: "2", role: "Accountant", email: "c@d.e" };

function TaxPage() {
  usePageTrail([{ label: "Tax to FBR" }]);
  return <h1>Tax to FBR</h1>;
}

function show(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route element={<AppLayout user={accountant} navGroups={navigationFor("Accountant")} displayName="Ayesha" setModal={() => {}} onLogout={() => {}} />}>
          <Route path="/finance" element={<h1>Finance</h1>} />
          <Route path="/finance/tax" element={<TaxPage />} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

/** The phone top bar is the one header shown below the md breakpoint. */
const phoneBack = () => document.querySelector("header.md\\:hidden a") as HTMLAnchorElement | null;

beforeEach(() => {
  vi.stubGlobal("matchMedia", (query: string) => ({ matches: query === PHONE_QUERY, media: query, addEventListener: () => {}, removeEventListener: () => {} }));
});

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("phone top bar on the Finance pages", () => {
  it("goes back to Finance from Tax to FBR", () => {
    show("/finance/tax");
    const back = phoneBack();
    expect(back?.textContent).toBe("Finance");
    expect(back?.getAttribute("href")).toBe("/finance");
  });

  it("has no back link on the Finance home itself", () => {
    show("/finance");
    expect(phoneBack()?.textContent ?? "").not.toBe("Finance");
  });
});
