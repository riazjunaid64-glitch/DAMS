// @vitest-environment happy-dom
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ToastProvider } from "../components/ui/Toast.tsx";
import CustomerDetailPage from "./CustomerDetailPage.tsx";

type Call = { url: string; method: string; body: unknown };
let calls: Call[];
let answers: Record<string, () => { status?: number; body: unknown }>;

vi.mock("../api/api.ts", () => ({
  api: async (url: string, init?: RequestInit) => {
    const method = init?.method ?? "GET";
    calls.push({ url, method, body: typeof init?.body === "string" ? JSON.parse(init.body) : undefined });
    const answer = answers[`${method} ${url}`];
    const { status = 200, body } = answer ? answer() : { status: 404, body: { message: "unexpected" } };
    return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
  },
}));

const base = {
  id: 5, fullName: "Usman Tariq", fatherName: "Tariq Mehmood", phone: "03334412987", cnic: "37405-1234567-1",
  email: "usman.tariq@gmail.com", address: "House 21, Street 4, Faisal Hills, Taxila", dateOfBirth: "1988-03-14T00:00:00",
  nationality: "Pakistani", occupation: "Business owner", whatsapp: "03334412987", notes: "Prefers calls after 5 pm.",
  status: "Active", blockedReason: null, blockedByName: null, blockedAt: null, bookingsCount: 2,
  createdAt: "2026-04-20T07:00:00", documentsNeeded: 2,
};
const blocked = { ...base, status: "Blocked", blockedReason: "Cheque bounced twice", blockedByName: "admin", blockedAt: "2026-09-30T10:00:00" };
const admin = { userId: 1, role: "Admin", email: "a@b.c", fullName: "A" } as never;
const sales = { userId: 2, role: "Sales employee", email: "s@b.c", fullName: "S" } as never;

function Where() {
  const location = useLocation();
  return <output data-testid="where">{location.pathname}{location.search}</output>;
}

function show(customer: Record<string, unknown> = base, user = admin) {
  answers["GET /api/Customer/5"] = () => ({ body: customer });
  return render(
    <MemoryRouter initialEntries={["/customers/5"]}>
      <ToastProvider>
        <Routes>
          <Route path="/customers/:id" element={<CustomerDetailPage user={user} />} />
          <Route path="*" element={null} />
        </Routes>
        <Where />
      </ToastProvider>
    </MemoryRouter>,
  );
}

function phone(on: boolean) {
  window.matchMedia = ((query: string) => ({
    matches: on && query.includes("max-width"), media: query, addEventListener: () => {}, removeEventListener: () => {},
  })) as unknown as typeof window.matchMedia;
}

const requested = (method: string, url: string) => calls.filter((c) => c.method === method && c.url === url);

beforeEach(() => {
  calls = [];
  answers = { "GET /api/Booking/customer/5": () => ({ body: { items: [{ id: 9, bookingReference: "BK-000009", projectName: "Floria", unitNumber: "B08" }, { id: 10, bookingReference: "BK-000010", projectName: "Floria", unitNumber: "B09" }] } }) };
  phone(false);
});
afterEach(cleanup);

describe("the header and tabs", () => {
  it("shows the name, the documents-needed badge, phone, CNIC, customer since, and the three buttons", async () => {
    show();
    expect(await screen.findByRole("heading", { name: "Usman Tariq" })).toBeTruthy();
    expect(screen.getByText("2 documents needed")).toBeTruthy();
    expect(screen.getByRole("link", { name: "0333 4412987" }).getAttribute("href")).toBe("tel:03334412987");
    expect(screen.getByText("37405-1234567-1")).toBeTruthy();
    expect(screen.getByText("Customer since Apr 20, 2026")).toBeTruthy();
    for (const name of ["Edit", "Block", "New booking"]) expect(screen.getByRole("button", { name })).toBeTruthy();
  });

  it("says Documents complete when nothing is needed", async () => {
    show({ ...base, documentsNeeded: 0 });
    expect(await screen.findByText("Documents complete")).toBeTruthy();
  });

  it("has Overview, Bookings with its count, and Documents — and no History", async () => {
    show();
    await screen.findByRole("heading", { name: "Usman Tariq" });
    const tabs = screen.getAllByRole("tab").map((tab) => tab.textContent);
    expect(tabs).toEqual(["Overview", "Bookings2", "Documents"]);
  });

  it("hides the Bookings count when there are none", async () => {
    answers["GET /api/Booking/customer/5"] = () => ({ body: { items: [] } });
    show({ ...base, bookingsCount: 0 });
    await screen.findByRole("heading", { name: "Usman Tariq" });
    expect(screen.getByRole("tab", { name: "Bookings" }).textContent).toBe("Bookings");
  });

  it("New booking opens New booking with this customer chosen", async () => {
    show();
    fireEvent.click(await screen.findByRole("button", { name: "New booking" }));
    expect(screen.getByTestId("where").textContent).toBe("/confirmed-bookings/new?customerId=5");
  });

  it("someone without the customers permission sees no customer", async () => {
    show(base, sales);
    expect(await screen.findByText("You don't have access to Customers.")).toBeTruthy();
    expect(calls).toHaveLength(0);
  });
});

describe("Overview", () => {
  it("shows the personal details, contact details and notes", async () => {
    show();
    await screen.findByText("Personal details");
    for (const text of ["Tariq Mehmood", "Mar 14, 1988", "Pakistani", "Business owner", "usman.tariq@gmail.com", "House 21, Street 4, Faisal Hills, Taxila", "Prefers calls after 5 pm."]) {
      expect(screen.getByText(text)).toBeTruthy();
    }
    expect(screen.getByText("0333 4412987", { selector: "dd" })).toBeTruthy();
  });

  it("shows Not added for what was never filled in, Same as mobile for a blank WhatsApp, and no Notes card without notes", async () => {
    show({ ...base, fatherName: null, dateOfBirth: null, nationality: "", occupation: null, email: null, address: null, whatsapp: null, notes: null });
    await screen.findByText("Personal details");
    expect(screen.getAllByText("Not added")).toHaveLength(6);
    expect(screen.getByText("Same as mobile")).toBeTruthy();
    expect(screen.queryByText("Notes")).toBeNull();
  });
});

describe("a blocked customer", () => {
  it("shows the red Blocked badge, the Notice with reason, person and date, and only Edit and Unblock", async () => {
    show(blocked);
    expect(await screen.findByText("New bookings are stopped")).toBeTruthy();
    expect(screen.getByText("Cheque bounced twice · Blocked by admin on Sep 30, 2026")).toBeTruthy();
    expect(screen.getByText("Blocked", { selector: "span" })).toBeTruthy();
    expect(screen.queryByText("2 documents needed")).toBeNull();
    expect(screen.getByRole("button", { name: "Edit" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Unblock" })).toBeTruthy();
    expect(screen.queryByRole("button", { name: "New booking" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Block" })).toBeNull();
  });

  it("Unblock asks first, then unblocks, thanks the user and reloads", async () => {
    answers["POST /api/Customer/5/unblock"] = () => ({ body: base });
    show(blocked);
    fireEvent.click(await screen.findByRole("button", { name: "Unblock" }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText("Unblock Usman Tariq?")).toBeTruthy();
    expect(within(dialog).getByText("New bookings are allowed again.")).toBeTruthy();
    answers["GET /api/Customer/5"] = () => ({ body: base });
    fireEvent.click(within(dialog).getByRole("button", { name: "Unblock" }));

    expect(await screen.findByText("Customer unblocked")).toBeTruthy();
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(requested("POST", "/api/Customer/5/unblock")).toHaveLength(1);
    await waitFor(() => expect(screen.queryByText("New bookings are stopped")).toBeNull());
  });
});

describe("Block", () => {
  async function open() {
    show();
    fireEvent.click(await screen.findByRole("button", { name: "Block" }));
    return screen.findByRole("dialog");
  }

  it("asks for a reason and sends nothing while it is empty", async () => {
    const dialog = await open();
    expect(within(dialog).getByText("Block Usman Tariq?")).toBeTruthy();
    expect(within(dialog).getByText("New bookings for this customer will be stopped. Existing bookings stay as they are.")).toBeTruthy();
    fireEvent.click(within(dialog).getByRole("button", { name: "Block customer" }));
    expect(await within(dialog).findByText("Enter the reason for blocking this customer.")).toBeTruthy();
    expect(requested("POST", "/api/Customer/5/block")).toHaveLength(0);
  });

  it("blocks with the reason, thanks the user and shows the blocked page", async () => {
    answers["POST /api/Customer/5/block"] = () => ({ body: blocked });
    const dialog = await open();
    fireEvent.change(within(dialog).getByLabelText(/Reason/), { target: { value: "  Cheque bounced twice " } });
    answers["GET /api/Customer/5"] = () => ({ body: blocked });
    fireEvent.click(within(dialog).getByRole("button", { name: "Block customer" }));

    expect(await screen.findByText("Customer blocked")).toBeTruthy();
    expect(requested("POST", "/api/Customer/5/block")[0]!.body).toEqual({ reason: "Cheque bounced twice" });
    expect(await screen.findByText("New bookings are stopped")).toBeTruthy();
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
  });

  it("keeps the popup open with what was typed when the server refuses", async () => {
    answers["POST /api/Customer/5/block"] = () => ({ status: 400, body: { message: "This customer is already blocked." } });
    const dialog = await open();
    fireEvent.change(within(dialog).getByLabelText(/Reason/), { target: { value: "Fraud" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Block customer" }));

    expect(await within(dialog).findByText("This customer is already blocked.")).toBeTruthy();
    expect((within(dialog).getByLabelText(/Reason/) as HTMLTextAreaElement).value).toBe("Fraud");
    expect(screen.getByRole("dialog")).toBeTruthy();
  });
});

describe("Edit", () => {
  async function open() {
    show();
    fireEvent.click(await screen.findByRole("button", { name: "Edit" }));
    return screen.findByRole("dialog");
  }

  it("opens the shared form filled in, with Notes, and no Status or Source", async () => {
    const dialog = await open();
    expect(within(dialog).getByText("Edit customer")).toBeTruthy();
    expect(within(dialog).getByText("Usman Tariq", { selector: "span" })).toBeTruthy();
    expect((within(dialog).getByLabelText(/Full name/) as HTMLInputElement).value).toBe("Usman Tariq");
    expect((within(dialog).getByLabelText(/S\/O, W\/O, D\/O/) as HTMLInputElement).value).toBe("Tariq Mehmood");
    expect((within(dialog).getByLabelText(/Notes/) as HTMLTextAreaElement).value).toBe("Prefers calls after 5 pm.");
    expect(within(dialog).queryByText(/Status/i)).toBeNull();
    expect(within(dialog).queryByText(/Source/i)).toBeNull();
    expect(within(dialog).getByRole("button", { name: "Save changes" })).toBeTruthy();
  });

  it("saves every field including the personal details, thanks the user and reloads", async () => {
    answers["PUT /api/Customer/5"] = () => ({ body: base });
    const dialog = await open();
    fireEvent.change(within(dialog).getByLabelText(/Occupation/), { target: { value: "Architect" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Save changes" }));

    expect(await screen.findByText("Customer saved")).toBeTruthy();
    const [saved] = requested("PUT", "/api/Customer/5");
    expect(saved!.body).toEqual({
      fullName: "Usman Tariq", fatherName: "Tariq Mehmood", phone: "03334412987", cnic: "37405-1234567-1",
      email: "usman.tariq@gmail.com", whatsapp: "03334412987", dateOfBirth: "1988-03-14", nationality: "Pakistani",
      occupation: "Architect", address: "House 21, Street 4, Faisal Hills, Taxila", notes: "Prefers calls after 5 pm.",
    });
    expect(saved!.body).not.toHaveProperty("status");
    expect(saved!.body).not.toHaveProperty("source");
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(requested("GET", "/api/Customer/5").length).toBeGreaterThan(1);
  });

  it("refuses a mobile that belongs to another customer, under the field, with Open customer", async () => {
    answers["PUT /api/Customer/5"] = () => ({
      status: 409,
      body: { message: "Already used", field: "phone", existingCustomerId: 8, existingCustomerName: "Amina Shah" },
    });
    const dialog = await open();
    fireEvent.click(within(dialog).getByRole("button", { name: "Save changes" }));

    expect(await within(dialog).findByText(/Already used by Amina Shah\./)).toBeTruthy();
    expect(screen.getByRole("dialog")).toBeTruthy();
    fireEvent.click(within(dialog).getByRole("button", { name: "Open customer" }));
    expect(screen.getByTestId("where").textContent).toBe("/customers/8");
  });

  it("checks the form before sending", async () => {
    const dialog = await open();
    fireEvent.change(within(dialog).getByLabelText(/Mobile/), { target: { value: "123" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Save changes" }));
    expect(await within(dialog).findByText(/Enter a valid mobile number/)).toBeTruthy();
    expect(requested("PUT", "/api/Customer/5")).toHaveLength(0);
  });
});

describe("on a phone", () => {
  beforeEach(() => phone(true));

  it("has New booking and ⋯, whose sheet holds Edit details and Block customer", async () => {
    show();
    await screen.findByRole("heading", { name: "Usman Tariq" });
    expect(screen.getByRole("button", { name: "New booking" })).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Back to Customers" })).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "More actions" }));
    const sheet = await screen.findByRole("dialog");
    expect(within(sheet).getByText("Edit details")).toBeTruthy();
    fireEvent.click(within(sheet).getByText("Block customer"));
    expect(await screen.findByText("Block Usman Tariq?")).toBeTruthy();
  });

  it("a blocked customer has Unblock and the Edit pencil, and no ⋯", async () => {
    show(blocked);
    await screen.findByText("New bookings are stopped");
    expect(screen.getByRole("button", { name: "Unblock" })).toBeTruthy();
    expect(screen.queryByRole("button", { name: "More actions" })).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Edit Usman Tariq" }));
    expect(await screen.findByText("Edit customer")).toBeTruthy();
  });
});
