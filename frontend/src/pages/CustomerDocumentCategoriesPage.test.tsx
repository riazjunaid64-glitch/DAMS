// @vitest-environment happy-dom
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ToastProvider } from "../components/ui/Toast.tsx";
import CustomerDocumentCategoriesPage from "./CustomerDocumentCategoriesPage.tsx";

const calls: { url: string; method: string; body?: string }[] = [];
let failSwitch = false;
let failSave = false;

vi.mock("../api/api.ts", () => ({
  api: async (url: string, init?: RequestInit) => {
    const method = init?.method ?? "GET";
    calls.push({ url, method, body: typeof init?.body === "string" ? init.body : undefined });
    if (method === "PUT" && url.endsWith("/ask-every-customer") && failSwitch) {
      return new Response(JSON.stringify({ message: "Could not ask everyone." }), { status: 500, headers: { "Content-Type": "application/json" } });
    }
    if ((method === "POST" || method === "PUT") && url.includes("/categories") && failSave) {
      return new Response(JSON.stringify({ message: "A document with this name already exists." }), { status: 409, headers: { "Content-Type": "application/json" } });
    }
    if (method === "DELETE") return new Response(JSON.stringify({ message: "Document removed." }), { status: 200, headers: { "Content-Type": "application/json" } });
    if (method === "POST" || method === "PUT") {
      return new Response(JSON.stringify({ id: 5, name: "Passport", asksEveryCustomer: method === "PUT" && url.endsWith("/ask-every-customer") }), { status: 200, headers: { "Content-Type": "application/json" } });
    }
    return new Response(JSON.stringify({
      nonBlockedCustomerCount: 4,
      documents: [
        { id: 1, name: "CNIC Front", asksEveryCustomer: true },
        { id: 5, name: "Passport", asksEveryCustomer: false },
      ],
    }), { status: 200, headers: { "Content-Type": "application/json" } });
  },
}));

const admin = { userId: 1, role: "Admin", email: "a@b.c", fullName: "A" } as never;

function phone(matches: boolean) {
  vi.stubGlobal("matchMedia", (query: string) => ({
    matches,
    media: query,
    addEventListener: () => {},
    removeEventListener: () => {},
  }));
}

function show(user = admin) {
  return render(
    <ToastProvider>
      <CustomerDocumentCategoriesPage user={user} />
    </ToastProvider>,
  );
}

beforeEach(() => {
  calls.length = 0;
  failSwitch = false;
  failSave = false;
  phone(false);
});

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("Document setup (KAN-83)", () => {
  it("lists documents with a switch and an edit pencil", async () => {
    show();
    expect(await screen.findByRole("heading", { name: "Document setup" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "New document" })).toBeTruthy();
    expect(screen.getByText("CNIC Front")).toBeTruthy();
    expect(screen.getByRole("switch", { name: "Ask every customer for CNIC Front" }).getAttribute("aria-checked")).toBe("true");
    expect(screen.getByRole("switch", { name: "Ask every customer for Passport" }).getAttribute("aria-checked")).toBe("false");
    expect(screen.getByRole("button", { name: "Edit Passport" })).toBeTruthy();
    expect(screen.queryByText("Code")).toBeNull();
    expect(screen.queryByText("Assign")).toBeNull();
  });

  it("asks before turning a switch on, and puts it back when the save fails", async () => {
    failSwitch = true;
    show();
    const toggle = await screen.findByRole("switch", { name: "Ask every customer for Passport" });
    fireEvent.click(toggle);
    expect(await screen.findByRole("heading", { name: "Ask every customer for Passport?" })).toBeTruthy();
    expect(screen.getByText("Passport will show as Needed on all 4 customers.")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Ask everyone" }));
    expect((await screen.findByRole("alert")).textContent).toContain("Could not ask everyone.");
    await waitFor(() => expect(screen.getByRole("switch", { name: "Ask every customer for Passport" }).getAttribute("aria-checked")).toBe("false"));
    expect(calls.some((call) => call.url.endsWith("/ask-every-customer") && call.body?.includes("true"))).toBe(true);
  });

  it("shows a name clash under the field and keeps the popup open", async () => {
    failSave = true;
    show();
    fireEvent.click(await screen.findByRole("button", { name: "Edit Passport" }));
    expect(screen.getByRole("heading", { name: "Edit document" })).toBeTruthy();
    fireEvent.change(screen.getByLabelText(/Name/i), { target: { value: "CNIC Front" } });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    expect((await screen.findByRole("alert")).textContent).toContain("A document with this name already exists.");
    expect(screen.getByRole("heading", { name: "Edit document" })).toBeTruthy();
    expect((screen.getByLabelText(/Name/i) as HTMLInputElement).value).toBe("CNIC Front");
  });

  it("confirms remove from inside edit", async () => {
    show();
    fireEvent.click(await screen.findByRole("button", { name: "Edit Passport" }));
    fireEvent.click(screen.getByRole("button", { name: "Remove" }));
    expect(await screen.findByRole("heading", { name: "Remove Passport?" })).toBeTruthy();
    expect(screen.getByText("Customers who already have a file keep it.")).toBeTruthy();
    fireEvent.click(screen.getAllByRole("button", { name: "Remove" }).at(-1)!);
    await waitFor(() => expect(calls.some((call) => call.method === "DELETE")).toBe(true));
    expect((await screen.findByRole("status")).textContent).toContain("Document removed.");
  });

  it("refuses sales staff", () => {
    show({ userId: 2, role: "Manager", email: "m@b.c", fullName: "M" } as never);
    expect(screen.getByText("You don't have access to Document setup.")).toBeTruthy();
    expect(calls).toHaveLength(0);
  });

  it("shows only the plus on a phone", async () => {
    phone(true);
    show();
    expect(await screen.findByRole("button", { name: "New document" })).toBeTruthy();
    expect(screen.queryByText("New document")).toBeNull();
    expect(screen.getByRole("columnheader", { name: "Ask every customer" })).toBeTruthy();
  });
});
