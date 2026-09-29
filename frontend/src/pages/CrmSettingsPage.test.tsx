// @vitest-environment happy-dom
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router-dom";
import { ToastProvider } from "../components/ui/Toast.tsx";
import CrmSettingsPage from "./CrmSettingsPage.tsx";

const employee = {
  employeeId: 4,
  userId: null,
  fullName: "Sana Sales",
  email: "sana@dams.test",
  role: null,
  status: "Active",
  canOwnLeads: false,
  jobTitle: "Coordinator",
  department: "Sales",
  phone: "03001112222",
  joinDate: "2024-01-01",
  access: "None",
  invitationExpiresAt: null,
};

const login = {
  userId: 12,
  fullName: "Imran Khan",
  email: "imran@dams.test",
  role: "Client",
  accountStatus: "Active",
};

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("Give DAMS access", () => {
  it("selecting a linkable login sends existingUserId and locks the email", async () => {
    const posts: unknown[] = [];
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
    vi.stubGlobal("fetch", (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      const method = (init?.method ?? "GET").toUpperCase();
      if (url.includes("/api/staff/linkable-users")) {
        return json([login]);
      }
      if (url.includes("/api/staff/accounts") && method === "POST") {
        posts.push(JSON.parse(String(init?.body)));
        return json({
          account: { ...employee, userId: login.userId, email: login.email, role: "Employee", access: "Active" },
          invitationRequired: false,
          invitationSent: false,
        });
      }
      if (url.includes("/api/staff/accounts")) return json([employee]);
      return json({ message: "not found" }, 404);
    });

    render(
      <MemoryRouter>
        <ToastProvider>
          <CrmSettingsPage user={{ userId: "1", email: "admin@dams.test", role: "Admin" }} />
        </ToastProvider>
      </MemoryRouter>,
    );

    const open = await screen.findByRole("button", { name: "Give DAMS access" });
    await waitFor(() => expect((open as HTMLButtonElement).disabled).toBe(false));
    fireEvent.click(open);

    fireEvent.click(await screen.findByRole("combobox", { name: /Existing login/ }));
    fireEvent.click(await screen.findByRole("option", { name: /Imran Khan/ }));

    const email = screen.getByLabelText(/Login email/) as HTMLInputElement;
    expect(email.value).toBe("imran@dams.test");
    expect(email.readOnly).toBe(true);
    expect(email.disabled).toBe(true);

    fireEvent.click(screen.getByRole("combobox", { name: /Employee/ }));
    fireEvent.click(await screen.findByRole("option", { name: /Sana Sales/ }));
    fireEvent.click(screen.getByRole("button", { name: "Give access" }));

    await waitFor(() => expect(posts).toHaveLength(1));
    expect(posts[0]).toMatchObject({
      existingUserId: 12,
      existingEmployeeId: 4,
      email: "imran@dams.test",
    });
  });
});

function json(body: unknown, status = 200) {
  return Promise.resolve(new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  }));
}
