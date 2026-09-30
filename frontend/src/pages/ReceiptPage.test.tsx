// @vitest-environment happy-dom
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import ReceiptPage from "./ReceiptPage.tsx";
import ApplicationFormPage from "./ApplicationFormPage.tsx";

const calls: string[] = [];
let booking: Record<string, unknown>;

const receipt = {
  paymentId: 9,
  receiptNumber: "RCP-000231",
  paidAt: "2026-08-26T00:00:00",
  receivedByName: "Sana Malik",
  bookingReference: "BK-000013",
  customerName: "Usman Tariq",
  projectName: "Floria Heights",
  unitType: "2 Bed",
  unitNumber: "B08",
  tower: "B",
  isCorner: true,
  floorNumber: 8,
  floorName: "8th floor",
  unitSize: 1180,
  type: "Installment",
  paymentMethod: "BankTransfer",
  paymentReference: "TRX-77120",
  amount: 918_000,
  installmentSequence: 4,
};

vi.mock("../api/api.ts", () => ({
  api: async (url: string) => {
    calls.push(url);
    if (url.endsWith("/receipt")) return new Response(JSON.stringify(receipt), { status: 200 });
    return new Response(JSON.stringify(booking), { status: 200 });
  },
}));

const admin = { userId: "1", role: "Admin", email: "a@b.c", firstName: "Hina Raza" } as never;

function Where() {
  return <output data-testid="where">{useLocation().pathname}</output>;
}

function open(path: string, user = admin) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/receipt/:bookingId/:paymentId" element={<ReceiptPage user={user} />} />
        <Route path="/application-form" element={<ApplicationFormPage user={user} />} />
        <Route path="*" element={<Where />} />
      </Routes>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  calls.length = 0;
  booking = {
    bookingReference: "BK-000013", customerName: "Usman Tariq", unitNumber: "B08", serialNo: "Auto",
    bookingAmountRequired: 500_000, bookingAmountReceived: 500_000, applicationAmountReceived: 500_000,
    applicationPaymentType: "Booking", applicationPaymentMethod: "Cheque",
  };
  vi.stubGlobal("matchMedia", (query: string) => ({ matches: false, media: query, addEventListener: () => {}, removeEventListener: () => {} }));
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("Receipt page", () => {
  it("opens inside the app with the title, the subtitle and one print button", async () => {
    open("/receipt/13/9");
    expect(await screen.findByRole("heading", { name: "Receipt RCP-000231" })).toBeTruthy();
    expect(screen.getByText("Installment 4 · Rs 918,000 · Aug 26, 2026")).toBeTruthy();
    expect(screen.getAllByRole("button", { name: "Print or save PDF" })).toHaveLength(1);
    expect(calls).toEqual(["/api/Booking/13/payments/9/receipt"]);
  });

  it("puts the printing user's name under Prepared by and the recorder's under Received by", async () => {
    open("/receipt/13/9");
    await screen.findByRole("heading", { name: "Receipt RCP-000231" });
    expect(screen.getByText("Hina Raza").nextSibling?.textContent).toBe("Prepared By");
    expect(screen.getByText("Sana Malik").nextSibling?.textContent).toBe("Received By");
  });

  it("goes back to the booking, not to wherever the browser was", async () => {
    open("/receipt/13/9");
    fireEvent.click(await screen.findByRole("button", { name: "Back to booking" }));
    expect((await screen.findByTestId("where")).textContent).toBe("/confirmed-bookings/13");
  });

  it("reads a customer's own receipt from the portal, never the staff endpoint", async () => {
    open("/receipt/13/9", { userId: "5", role: "Customer", email: "c@b.c" } as never);
    await screen.findByRole("heading", { name: "Receipt RCP-000231" });
    expect(calls).toEqual(["/api/MyProjects/13/payments/9/receipt"]);
  });
});

describe("Application form page", () => {
  it("opens a booking's form with the booking in the subtitle and Through = the method", async () => {
    open("/application-form?bookingId=13");
    expect(await screen.findByRole("heading", { name: "Application form" })).toBeTruthy();
    expect(screen.getByText("BK-000013 · Usman Tariq · Unit B08")).toBeTruthy();
    expect(screen.getByText("Cheque")).toBeTruthy();
    expect(calls).toEqual(["/api/Booking/13"]);
  });

  it("prints a blank form, every field empty, and goes back to the list", async () => {
    open("/application-form");
    expect(await screen.findByRole("heading", { name: "Blank application form" })).toBeTruthy();
    expect(calls).toEqual([]);
    fireEvent.click(screen.getByRole("button", { name: "Back to Bookings" }));
    await waitFor(() => expect(screen.getByTestId("where").textContent).toBe("/confirmed-bookings"));
  });

  it("does not open for a user without the bookings permission", async () => {
    open("/application-form?bookingId=13", { userId: "2", role: "Manager", email: "m@b.c" } as never);
    expect(await screen.findByText("You do not have access to bookings.")).toBeTruthy();
    expect(calls).toEqual([]);
  });
});
