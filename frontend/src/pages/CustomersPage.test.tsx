// @vitest-environment happy-dom
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter, useLocation } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ToastProvider } from "../components/ui/Toast.tsx";
import CustomersPage from "./CustomersPage.tsx";

const calls: string[] = [];
let respond: (url: string, init?: RequestInit) => { status?: number; body: unknown };

vi.mock("../api/api.ts", () => ({
  api: async (url: string, init?: RequestInit) => {
    calls.push(url);
    const { status = 200, body } = respond(url, init);
    return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
  },
}));

const row = (id: number, over: Record<string, unknown> = {}) => ({
  id,
  fullName: `Customer ${id}`,
  phone: "03334412987",
  cnic: "37405-1234567-1",
  isBlocked: false,
  bookingsCount: 2,
  documentsNeeded: 0,
  ...over,
});

function Where() {
  const location = useLocation();
  return <output data-testid="where">{location.pathname}{location.search}</output>;
}

function show(user = { userId: 1, role: "Admin", email: "a@b.c", fullName: "A" } as never) {
  return render(
    <MemoryRouter initialEntries={["/customers"]}>
      <ToastProvider>
        <CustomersPage user={user} />
        <Where />
      </ToastProvider>
    </MemoryRouter>,
  );
}

function phone(matches: boolean) {
  vi.stubGlobal("matchMedia", (query: string) => ({
    matches,
    media: query,
    addEventListener: () => {},
    removeEventListener: () => {},
  }));
}

beforeEach(() => {
  calls.length = 0;
  phone(false);
  respond = () => ({
    body: {
      items: [row(1), row(2, { isBlocked: true, documentsNeeded: 2, cnic: null })],
      totalCount: 128,
      totalCustomers: 128,
      documentsNeededCount: 19,
      page: 1,
      pageSize: 20,
      totalPages: 7,
    },
  });
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("Customers list (KAN-79)", () => {
  it("shows summary cards, the slim columns and Document setup / New customer", async () => {
    show();
    await screen.findAllByText("Customer 1");
    expect(screen.getAllByText("128").length).toBeGreaterThan(0);
    expect(screen.getAllByText("19").length).toBeGreaterThan(0);
    expect(screen.getByRole("button", { name: "Document setup" })).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Document setup" }));
    expect(screen.getByTestId("where").textContent).toBe("/customers/document-setup");
    expect(screen.getByRole("button", { name: "New customer" })).toBeTruthy();
    expect(screen.getAllByText("Blocked").length).toBeGreaterThan(0);
    expect(screen.getAllByText("2 needed").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Complete").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Not added").length).toBeGreaterThan(0);
    expect(screen.queryByText("Email")).toBeNull();
    expect(screen.queryByText("Source")).toBeNull();
    expect(calls.some((url) => url.startsWith("/api/Customer?") && url.includes("pageSize=20"))).toBe(true);
  });

  it("filters to documents needed when that card is clicked", async () => {
    show();
    await screen.findAllByText("Customer 1");
    fireEvent.click(screen.getByRole("button", { name: /Documents needed/i }));
    await waitFor(() => expect(screen.getByTestId("where").textContent).toContain("documentsNeededOnly=1"));
  });

  it("tells unsigned and unauthorized visitors they cannot open Customers", () => {
    show(null as never);
    expect(screen.getByText("Sign in required")).toBeTruthy();
    show({ userId: 2, role: "Sales employee", email: "s@b.c", fullName: "S" } as never);
    expect(screen.getByText("You don't have access to Customers.")).toBeTruthy();
  });

  it("opens New customer and posts Walk-in with all fields", async () => {
    respond = (_url, init) => {
      if (init?.method === "POST") {
        const body = JSON.parse(String(init.body)) as Record<string, unknown>;
        expect(body.source).toBe("WalkIn");
        expect(body.fullName).toBe("Hamza Iqbal");
        expect(body.phone).toBe("03001234567");
        expect(body.nationality).toBe("Pakistani");
        return { body: { id: 99 } };
      }
      return {
        body: {
          items: [],
          totalCount: 0,
          totalCustomers: 0,
          documentsNeededCount: 0,
          page: 1,
          pageSize: 20,
          totalPages: 0,
        },
      };
    };
    show();
    await screen.findByText("No customers yet");
    fireEvent.click(screen.getByRole("button", { name: "New customer" }));
    expect(screen.getByRole("heading", { name: "New customer" })).toBeTruthy();
    fireEvent.change(screen.getByLabelText(/Full name/i), { target: { value: "Hamza Iqbal" } });
    fireEvent.change(screen.getByLabelText(/^Mobile/i), { target: { value: "03001234567" } });
    fireEvent.change(screen.getByLabelText(/Nationality/i), { target: { value: "Pakistani" } });
    fireEvent.click(screen.getByRole("button", { name: "Save customer" }));
    await waitFor(() => expect(screen.getByTestId("where").textContent).toBe("/customers/99"));
  });
});
