// @vitest-environment happy-dom
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter, useLocation } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ToastProvider } from "../components/ui/Toast.tsx";
import BookingsPage from "./BookingsPage.tsx";

const calls: string[] = [];
let respond: (url: string) => { status?: number; body: unknown };

vi.mock("../api/api.ts", () => ({
  api: async (url: string) => {
    calls.push(url);
    const { status = 200, body } = respond(url);
    return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
  },
}));

const row = (id: number, over: Record<string, unknown> = {}) => ({
  id,
  bookingReference: `BK-${String(id).padStart(6, "0")}`,
  customerName: `Customer ${id}`,
  customerPhone: "03334412987",
  projectName: "Floria Heights",
  unitNumber: `B${id}`,
  status: "PaymentPlanActive",
  agreedSalePrice: 12_800_000,
  bookingAmountReceived: 500_000,
  bookingAmountRequired: 500_000,
  bookingAmountRemaining: 0,
  bookingDate: "2026-09-28T07:00:00Z",
  ...over,
});

const counts = { total: 46, awaitingBookingAmount: 6, paymentPlanActive: 34, possessionGiven: 2, saleCompleted: 2, cancelled: 2 };

function Where() {
  const location = useLocation();
  return <output data-testid="where">{location.pathname}{location.search}</output>;
}

function show(user = { userId: 1, role: "Admin", email: "a@b.c", fullName: "A" } as never) {
  return render(
    <MemoryRouter initialEntries={["/confirmed-bookings"]}>
      <ToastProvider>
        <BookingsPage user={user} />
        <Where />
      </ToastProvider>
    </MemoryRouter>,
  );
}

function phone(matches: boolean) {
  vi.stubGlobal("matchMedia", (query: string) => ({ matches, media: query, addEventListener: () => {}, removeEventListener: () => {} }));
}

beforeEach(() => {
  calls.length = 0;
  phone(false);
  respond = (url) => url.includes("/summary")
    ? { body: counts }
    : { body: { items: [row(1), row(2, { status: "AwaitingBookingAmount", bookingAmountReceived: 150_000, bookingAmountRequired: 500_000, bookingAmountRemaining: 350_000 })], totalCount: 46, page: 1, pageSize: 20, totalPages: 3 } };
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("Bookings list", () => {
  it("shows the counts from the server and 20 per page", async () => {
    show();
    await screen.findAllByText("BK-000001");
    expect(screen.getAllByText("46").length).toBeGreaterThan(0);
    expect(screen.getByText("34")).toBeTruthy();
    expect(calls.some((url) => url.startsWith("/api/Booking?") && url.includes("pageSize=20") && url.includes("page=1"))).toBe(true);
  });

  it("writes phone, date, price and the booking-amount states", async () => {
    show();
    await screen.findAllByText("BK-000001");
    expect(screen.getAllByText("0333 4412987").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Sep 28, 2026").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Rs 12,800,000").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Received").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Rs 350,000 due").length).toBeGreaterThan(0);
    expect(screen.getAllByText(/of Rs 500,000/).length).toBeGreaterThan(0);
    expect(screen.getAllByText("Payment plan").length).toBeGreaterThan(0);
  });

  it("says Not set for a booking whose terms are not set, and shows no due line on a cancelled one", async () => {
    respond = (url) => url.includes("/summary")
      ? { body: counts }
      : { body: { items: [
        row(1, { status: "AwaitingBookingAmount", agreedSalePrice: 0, bookingAmountRequired: 0, bookingAmountReceived: 0 }),
        row(2, { status: "Cancelled", bookingAmountReceived: 150_000, bookingAmountRequired: 500_000, bookingAmountRemaining: 350_000 }),
      ], totalCount: 2, page: 1, pageSize: 20, totalPages: 1 } };
    show();
    await screen.findAllByText("BK-000001");
    // Agreed price and booking amount, in the table and in the phone card.
    expect(screen.getAllByText("Not set")).toHaveLength(4);
    expect(screen.queryByText("Rs 350,000 due")).toBeNull();
  });

  it("shows no amount due when a rebate credit has settled the rest of the booking amount", async () => {
    respond = (url) => url.includes("/summary")
      ? { body: counts }
      : { body: { items: [row(1, { bookingAmountReceived: 300_000, bookingAmountRequired: 500_000, bookingAmountRemaining: 0 })], totalCount: 1, page: 1, pageSize: 20, totalPages: 1 } };
    show();
    await screen.findAllByText("BK-000001");
    expect(screen.queryByText(/due$/)).toBeNull();
    expect(screen.getAllByText("Received").length).toBeGreaterThan(0);
  });

  it("filters by a status card, keeps the counts on search and project only, and clears on a second click or Total", async () => {
    show();
    await screen.findAllByText("BK-000001");
    const card = screen.getByRole("button", { name: "Payment plan 34" });
    fireEvent.click(card);
    await waitFor(() => expect(calls.some((url) => url.startsWith("/api/Booking?") && url.includes("status=PaymentPlanActive"))).toBe(true));
    expect(screen.getByTestId("where").textContent).toContain("status=PaymentPlanActive");
    expect(calls.filter((url) => url.includes("/summary")).every((url) => !url.includes("status"))).toBe(true);
    expect(screen.getByRole("button", { name: "Payment plan 34" }).getAttribute("aria-pressed")).toBe("true");
    fireEvent.click(screen.getByRole("button", { name: "Payment plan 34" }));
    await waitFor(() => expect(screen.getByTestId("where").textContent).not.toContain("status="));
    fireEvent.click(screen.getByRole("button", { name: "Sale completed 2" }));
    await waitFor(() => expect(screen.getByTestId("where").textContent).toContain("status=SaleCompleted"));
    fireEvent.click(screen.getAllByRole("button", { name: "Total bookings 46" })[0]!);
    await waitFor(() => expect(screen.getByTestId("where").textContent).not.toContain("status="));
  });

  it("opens the booking when its row is clicked", async () => {
    show();
    fireEvent.click(await screen.findByLabelText("Open BK-000001"));
    expect(screen.getByTestId("where").textContent).toBe("/confirmed-bookings/1");
  });

  it("has Print blank form and New booking", async () => {
    show();
    await screen.findAllByText("BK-000001");
    fireEvent.click(screen.getByRole("button", { name: "Print blank form" }));
    expect(screen.getByTestId("where").textContent).toBe("/application-form");
  });

  it("explains an empty result, with the Reset link in the filter bar and no second button on desktop", async () => {
    respond = (url) => url.includes("/summary")
      ? { body: { ...counts, total: 0 } }
      : { body: { items: [], totalCount: 0, page: 1, pageSize: 20, totalPages: 0 } };
    render(
      <MemoryRouter initialEntries={["/confirmed-bookings?search=BK-0999&status=SaleCompleted"]}>
        <ToastProvider><BookingsPage user={{ userId: 1, role: "Admin", email: "a@b.c", fullName: "A" } as never} /></ToastProvider>
      </MemoryRouter>,
    );
    await screen.findByText("No bookings match these filters");
    expect(screen.queryByRole("button", { name: "Reset filters" })).toBeNull();
    expect(screen.getByRole("button", { name: "Reset" })).toBeTruthy();
  });

  it("gives the phone's empty card a Reset filters button", async () => {
    phone(true);
    respond = (url) => url.includes("/summary")
      ? { body: { ...counts, total: 0 } }
      : { body: { items: [], totalCount: 0, page: 1, pageSize: 20, totalPages: 0 } };
    render(
      <MemoryRouter initialEntries={["/confirmed-bookings?search=BK-0999"]}>
        <ToastProvider><BookingsPage user={{ userId: 1, role: "Admin", email: "a@b.c", fullName: "A" } as never} /></ToastProvider>
        <Where />
      </MemoryRouter>,
    );
    fireEvent.click(await screen.findByRole("button", { name: "Reset filters" }));
    await waitFor(() => expect(screen.getByTestId("where").textContent).toBe("/confirmed-bookings"));
  });

  it("shows 'Bookings could not be loaded' with Try again when the list fails", async () => {
    let fail = true;
    respond = (url) => url.includes("/summary")
      ? { body: counts }
      : fail ? { status: 500, body: { message: "boom" } } : { body: { items: [row(1)], totalCount: 1, page: 1, pageSize: 20, totalPages: 1 } };
    show();
    await screen.findByText("Bookings could not be loaded");
    fail = false;
    fireEvent.click(screen.getByRole("button", { name: "Try again" }));
    await screen.findAllByText("BK-000001");
  });

  it("loads more on a phone, 20 at a time, and shows the count", async () => {
    phone(true);
    show();
    await screen.findAllByText("BK-000001");
    expect(screen.getByText("Showing 2 of 46")).toBeTruthy();
    respond = (url) => url.includes("/summary")
      ? { body: counts }
      : { body: { items: [row(3)], totalCount: 46, page: 2, pageSize: 20, totalPages: 3 } };
    fireEvent.click(screen.getByRole("button", { name: "Load more" }));
    await screen.findAllByText("BK-000003");
    expect(calls.some((url) => url.includes("page=2"))).toBe(true);
    expect(screen.getAllByText("BK-000001").length).toBeGreaterThan(0);
  });

  it("is not for roles without the bookings permission", () => {
    show({ userId: 2, role: "Sales manager", email: "s@b.c", fullName: "S" } as never);
    expect(screen.getByText("Bookings are not part of your role")).toBeTruthy();
    expect(calls).toHaveLength(0);
  });
});
