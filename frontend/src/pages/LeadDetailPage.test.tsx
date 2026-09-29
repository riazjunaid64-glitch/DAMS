// @vitest-environment happy-dom
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { ToastProvider } from "../components/ui/Toast.tsx";
import type { FollowUp, LeadDetail } from "../features/leads/types.ts";
import LeadDetailPage from "./LeadDetailPage.tsx";

vi.mock("../features/leads/EngagementDialogs.tsx", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../features/leads/EngagementDialogs.tsx")>();
  return {
    ...actual,
    NewFollowUpDialog: ({ onSaved, onClose }: { onSaved: () => void; onClose: () => void }) => (
      <div>
        <button type="button" onClick={onSaved}>test-save-follow-up</button>
        <button type="button" onClick={onClose}>test-close</button>
      </div>
    ),
  };
});

const staffUser = { userId: "1", email: "staff@dams.test", role: "Employee" as const };

const followUp = (id: number, status: FollowUp["status"], dueAt: string, title = `Follow-up ${id}`): FollowUp =>
  ({ id, status, dueAt, title, type: "Call", priority: "Medium", assignedEmployeeId: 1 }) as FollowUp;

function sampleLead(): LeadDetail {
  return {
    id: 1,
    leadReference: "L-001",
    firstName: "Ali",
    lastName: "Khan",
    fullName: "Ali Khan",
    phone: "03001234567",
    sourceName: "Walk-in",
    propertyType: "2 Bed",
    purchaseIntent: "SelfUse",
    paymentPreference: "Unknown",
    stage: "New",
    stageGroup: "New",
    createdAt: "2026-09-01T10:00:00Z",
    assignmentState: "Assigned",
    qualification: "Unqualified",
    preferredContactMethod: "Phone",
    leadSourceId: 1,
    sourceCode: "walkin",
    concurrencyToken: "tok",
    openFollowUpCount: 1,
    documentCount: 0,
    counts: { timeline: 0, communications: 0, followUps: 1, siteVisits: 0 },
  };
}

function renderLead(initialEntry = "/crm/leads/1?tab=followUps") {
  return render(
    <MemoryRouter initialEntries={[initialEntry]}>
      <ToastProvider>
        <Routes>
          <Route path="/crm/leads/:id" element={<LeadDetailPage user={staffUser} />} />
        </Routes>
      </ToastProvider>
    </MemoryRouter>,
  );
}

function stubMatchMedia() {
  vi.stubGlobal("matchMedia", (query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addListener: () => {},
    removeListener: () => {},
    addEventListener: () => {},
    removeEventListener: () => {},
    dispatchEvent: () => false,
  }));
}

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("LeadDetailPage load coordination", () => {
  it("keeps the newest follow-up list when an older GET finishes last", async () => {
    stubMatchMedia();
    let resolveSlowFollowUps: (body: FollowUp[]) => void;
    const slowFollowUps = new Promise<Response>((resolve) => {
      resolveSlowFollowUps = (body) => resolve(json(body));
    });
    let followUpsGets = 0;
    const oldList = [followUp(1, "Pending", "2026-09-28T10:00:00")];
    const newList = [followUp(1, "Pending", "2026-09-28T10:00:00"), followUp(2, "Pending", "2026-09-30T10:00:00", "Saved after slow load")];

    vi.stubGlobal("fetch", (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      const method = (init?.method ?? "GET").toUpperCase();
      if (url.endsWith("/api/leads/1") && method === "GET") return json(sampleLead());
      if (url.includes("external-submissions")) return json([]);
      if (url.includes("/follow-ups") && method === "GET") {
        followUpsGets += 1;
        if (followUpsGets === 1) return slowFollowUps;
        return json(newList);
      }
      return json({ message: "not found" }, 404);
    });

    renderLead();
    await screen.findByRole("heading", { name: "Ali Khan" });
    fireEvent.click(screen.getByRole("button", { name: "Follow-up" }));
    fireEvent.click(await screen.findByRole("button", { name: "test-save-follow-up" }));
    await screen.findByText("Saved after slow load");
    resolveSlowFollowUps!(oldList);
    await waitFor(() => expect(screen.getByText("Saved after slow load")).toBeTruthy());
    expect(screen.queryByText("Follow-up 2")).toBeNull();
  });

  it("shows a stale notice, disables row actions, and retries a failed refresh", async () => {
    stubMatchMedia();
    let followUpsGets = 0;
    const items = [followUp(1, "Pending", "2026-09-28T10:00:00")];

    vi.stubGlobal("fetch", (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      const method = (init?.method ?? "GET").toUpperCase();
      if (url.endsWith("/api/leads/1") && method === "GET") return json(sampleLead());
      if (url.includes("external-submissions")) return json([]);
      if (url.includes("/follow-ups") && method === "GET") {
        followUpsGets += 1;
        if (followUpsGets === 1) return json(items);
        if (followUpsGets === 2) return json({ message: "Server error" }, 500);
        return json(items);
      }
      return json({ message: "not found" }, 404);
    });

    renderLead();
    await screen.findByRole("button", { name: "Mark done" });
    fireEvent.click(screen.getByRole("button", { name: "Follow-up" }));
    fireEvent.click(await screen.findByRole("button", { name: "test-save-follow-up" }));
    await screen.findByText("Couldn't refresh — showing older data");
    expect(screen.queryByRole("button", { name: "Mark done" })).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Try again" }));
    await waitFor(() => expect(screen.queryByText("Couldn't refresh — showing older data")).toBeNull());
    expect(followUpsGets).toBeGreaterThanOrEqual(3);
  });

  it("opens the lead when external submissions fail", async () => {
    stubMatchMedia();
    vi.stubGlobal("fetch", (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      const method = (init?.method ?? "GET").toUpperCase();
      if (url.endsWith("/api/leads/1") && method === "GET") return json(sampleLead());
      if (url.includes("external-submissions")) return json({ message: "Server error" }, 500);
      return json({ message: "not found" }, 404);
    });

    renderLead("/crm/leads/1");
    await screen.findByRole("heading", { name: "Ali Khan" });
    expect(screen.queryByText("This lead could not be opened")).toBeNull();
    await screen.findByText("Source details unavailable");
    expect(screen.getByText("Walk-in")).toBeTruthy();
  });
});

function json(body: unknown, status = 200) {
  return Promise.resolve(new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  }));
}
