// @vitest-environment happy-dom
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { api } from "../api/api.ts";
import { ToastProvider } from "../components/ui/Toast.tsx";
import { uploadProof } from "../features/proof/proofApi";
import NewBookingPage from "./NewBookingPage";

vi.mock("../api/api.ts", () => ({ api: vi.fn(), apiUpload: vi.fn() }));
vi.mock("../features/proof/proofApi", () => ({ uploadProof: vi.fn(), openProof: vi.fn() }));

const unit = (id: number, number: string, floor: number, type = "1 Bed", status = "Available") =>
  ({ id, projectId: 1, unitNumber: number, unitType: type, floorNumber: floor, floorName: "", size: 864, price: 14_255_985, status });
// 8th floor: 810..829 (20 units), 9th floor: 901..905 (5 units), one booked unit that must never be listed.
const units = [
  ...Array.from({ length: 20 }, (_, i) => unit(810 + i, String(810 + i), 8)),
  ...Array.from({ length: 5 }, (_, i) => unit(901 + i, String(901 + i), 9, "2 Bed")),
  unit(999, "999", 9, "1 Bed", "Booked"),
];

const json = (body: unknown, status = 200) => Promise.resolve({ ok: status >= 200 && status < 300, status, json: async () => body } as Response);

let bookingCalls: { headers: Headers; body: Record<string, unknown> }[] = [];
let bookingReply: () => Promise<Response> = () => json({ id: 47, bookingReference: "BK-000047", recordedPaymentId: null, customerName: "Hamza Iqbal" });

beforeEach(() => {
  bookingCalls = [];
  bookingReply = () => json({ id: 47, bookingReference: "BK-000047", recordedPaymentId: null, customerName: "Hamza Iqbal" });
  vi.mocked(api).mockImplementation((path: string, options?: RequestInit) => {
    if (path === "/api/Project") return json([{ id: 1, projectName: "Floria Heights" }]);
    if (path === "/api/finance/accounts/options") return json([{ id: 3, name: "Meezan Bank", accountHolderName: "0123", isActive: true }]);
    if (path === "/api/Unit/project/1") return json(units);
    if (path.startsWith("/api/Unit/")) return json(units.find((u) => String(u.id) === path.split("/").pop()));
    if (path.startsWith("/api/Customer?")) {
      return json({ items: [{ id: 5, fullName: "Usman Tariq", phone: "03334412987", cnic: "37405-1234567-1" }, { id: 6, fullName: "Usman Ali", phone: "03219988776", cnic: null }] });
    }
    if (path.startsWith("/api/Customer/")) return json({ id: 5, fullName: "Usman Tariq", phone: "03334412987", cnic: "37405-1234567-1" });
    if (path === "/api/Booking") {
      bookingCalls.push({ headers: new Headers(options?.headers), body: JSON.parse(String(options?.body)) });
      return bookingReply();
    }
    return json({}, 404);
  });
  vi.mocked(uploadProof).mockResolvedValue();
});
afterEach(() => { cleanup(); vi.clearAllMocks(); });

function Where() {
  const location = useLocation();
  return <span data-testid="where">{location.pathname}{location.search}</span>;
}

function show(url = "/confirmed-bookings/new", role = "Admin") {
  return render(
    <MemoryRouter initialEntries={[url]}>
      <ToastProvider>
        <Routes>
          <Route path="/confirmed-bookings/new" element={<NewBookingPage user={{ role } as never} />} />
          <Route path="*" element={<Where />} />
        </Routes>
      </ToastProvider>
    </MemoryRouter>,
  );
}

const cont = () => screen.getByRole("button", { name: "Continue" });
const pick = (label: string | RegExp, option: string) => {
  fireEvent.click(screen.getByRole("combobox", { name: label }));
  fireEvent.click(screen.getByRole("option", { name: option }));
};

describe("Step 1 · Unit", () => {
  it("lists only Available units grouped by floor, 20 at a time, and keeps Continue off until one is picked", async () => {
    show();
    await screen.findByRole("combobox", { name: /Project/ });
    expect((cont() as HTMLButtonElement).disabled).toBe(true);
    pick(/Project/, "Floria Heights");

    const eighth = await screen.findByRole("region", { name: "8th floor" });
    expect(within(eighth).getByText("20 available")).toBeTruthy();
    expect(screen.getByText("Showing", { exact: false }).textContent).toContain("20");
    expect(screen.getByText("Show more")).toBeTruthy();
    expect(screen.queryByText("Unit 999")).toBeNull();

    fireEvent.click(screen.getByText("Show more"));
    expect(await screen.findByRole("region", { name: "9th floor" })).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: /Unit 905/ }));
    expect(await screen.findAllByText("Unit 905 · 2 Bed")).toBeTruthy();
    expect((cont() as HTMLButtonElement).disabled).toBe(false);
  });

  it("filters on the device as a number is typed, and says how many match", async () => {
    show();
    await screen.findByRole("combobox", { name: /Project/ });
    pick(/Project/, "Floria Heights");
    await screen.findByRole("region", { name: "8th floor" });

    fireEvent.change(screen.getByLabelText("Unit number"), { target: { value: "90" } });
    expect(await screen.findByText("5 units match “90”")).toBeTruthy();
    expect(screen.queryByText("Showing", { exact: false })).toBeNull();
  });

  it("opens with the unit already picked when it comes from a unit page", async () => {
    show("/confirmed-bookings/new?unitId=905");
    expect(await screen.findAllByText("Unit 905 · 2 Bed")).toBeTruthy();
    const summary = screen.getByRole("complementary", { name: "Summary" });
    expect(within(summary).getByText("Floria Heights")).toBeTruthy();
    expect(within(summary).getByText("Unit 905 · 2 Bed")).toBeTruthy();
    expect((cont() as HTMLButtonElement).disabled).toBe(false);
  });

  it("says so when the unit it was opened with is no longer available", async () => {
    show("/confirmed-bookings/new?unitId=999");
    expect(await screen.findByText("Unit 999 is no longer available. Pick another unit.")).toBeTruthy();
    expect((cont() as HTMLButtonElement).disabled).toBe(true);
  });
});

async function toCustomerStep() {
  show("/confirmed-bookings/new?unitId=905");
  await screen.findAllByText("Unit 905 · 2 Bed");
  fireEvent.click(cont());
  await screen.findByRole("radio", { name: "New customer" });
}

describe("Step 2 · Customer", () => {
  it("stays on the step and shows the errors under the fields", async () => {
    await toCustomerStep();
    fireEvent.change(screen.getByLabelText(/Mobile/), { target: { value: "123" } });
    fireEvent.change(screen.getByLabelText(/CNIC/), { target: { value: "37405-7654321" } });
    fireEvent.click(cont());
    expect(screen.getByText("Enter the customer's full name.")).toBeTruthy();
    expect(screen.getByText("Enter a valid mobile number, e.g. 0300 1234567")).toBeTruthy();
    expect(screen.getByText("Use 00000-0000000-0 or a passport number")).toBeTruthy();
    expect(screen.getByRole("radio", { name: "New customer" })).toBeTruthy();
  });

  it("searches the server as the user types, and picks the customer", async () => {
    await toCustomerStep();
    fireEvent.click(screen.getByRole("radio", { name: "Existing customer" }));
    fireEvent.change(screen.getByLabelText("Search customers"), { target: { value: "usman" } });

    const list = await screen.findByRole("list", { name: "Customers" });
    expect(within(list).getByText("0333 4412987 · 37405-1234567-1")).toBeTruthy();
    expect(vi.mocked(api)).toHaveBeenCalledWith("/api/Customer?search=usman&pageSize=20");

    fireEvent.click(within(list).getByRole("button", { name: /Usman Tariq/ }));
    expect(within(list).getByRole("button", { name: /Usman Tariq/ }).getAttribute("aria-pressed")).toBe("true");
    fireEvent.click(cont());
    expect(await screen.findByLabelText(/Mailing address/)).toBeTruthy();
  });

  it("opens with a customer already chosen, as Existing customer, when it comes from a customer page", async () => {
    show("/confirmed-bookings/new?unitId=905&customerId=5");
    await screen.findAllByText("Unit 905 · 2 Bed");
    const summary = screen.getByRole("complementary", { name: "Summary" });
    expect(within(summary).getByText("Usman Tariq")).toBeTruthy();
    fireEvent.click(cont());
    expect(screen.getByRole("radio", { name: "Existing customer" }).getAttribute("aria-checked")).toBe("true");
    expect(await screen.findByRole("button", { name: /Usman Tariq/ })).toBeTruthy();
  });
});

/** Walks a new customer through to Review: one click per step, nothing received. */
async function toReview() {
  await toCustomerStep();
  fireEvent.change(screen.getByLabelText(/Full name/), { target: { value: "Hamza Iqbal" } });
  fireEvent.change(screen.getByLabelText(/Mobile/), { target: { value: "0334 6120451" } });
  fireEvent.click(cont());
  await screen.findByLabelText(/Mailing address/);
  fireEvent.click(cont()); // next of kin is all optional
  await screen.findByLabelText(/Agreed sale price/);
  fireEvent.click(cont());
  await screen.findByRole("button", { name: "Create booking" });
}

describe("Steps 4 and 5 · Price, payment and Review", () => {
  it("shows the payment fields only once an amount is received", async () => {
    await toCustomerStep();
    fireEvent.change(screen.getByLabelText(/Full name/), { target: { value: "Hamza Iqbal" } });
    fireEvent.change(screen.getByLabelText(/Mobile/), { target: { value: "0334 6120451" } });
    fireEvent.click(cont());
    await screen.findByLabelText(/Mailing address/);
    fireEvent.click(cont());
    await screen.findByLabelText(/Agreed sale price/);

    expect(screen.queryByLabelText(/Received in account/)).toBeNull();
    fireEvent.change(screen.getByLabelText(/Received today/), { target: { value: "500000" } });
    expect(screen.getByLabelText(/Received in account/)).toBeTruthy();
    expect(screen.getByRole("radio", { name: "Booking" })).toBeTruthy();
    fireEvent.click(cont());
    expect(screen.getByText("Choose the account it was received in.")).toBeTruthy();
  });

  it("creates the booking with a retry key, the real payment method, and the money received", async () => {
    await toCustomerStep();
    fireEvent.change(screen.getByLabelText(/Full name/), { target: { value: "Hamza Iqbal" } });
    fireEvent.change(screen.getByLabelText(/Mobile/), { target: { value: "0334 6120451" } });
    fireEvent.click(cont());
    await screen.findByLabelText(/Mailing address/);
    fireEvent.click(cont());
    await screen.findByLabelText(/Agreed sale price/);
    fireEvent.change(screen.getByLabelText(/Received today/), { target: { value: "500000" } });
    pick(/Received in account/, "Meezan Bank — 0123");
    pick(/Payment method/, "Bank transfer");
    fireEvent.click(screen.getByRole("radio", { name: "Confirmation" }));
    fireEvent.change(screen.getByLabelText(/Reference no\./), { target: { value: "TRX-90117" } });
    fireEvent.click(cont());
    await screen.findByRole("button", { name: "Create booking" });

    bookingReply = () => json({ id: 47, bookingReference: "BK-000047", recordedPaymentId: 88, customerName: "Hamza Iqbal" });
    fireEvent.click(screen.getByRole("button", { name: "Create booking" }));

    expect(await screen.findByText("Booking BK-000047 created")).toBeTruthy();
    expect(bookingCalls).toHaveLength(1);
    expect(bookingCalls[0]!.headers.get("Idempotency-Key")).toBeTruthy();
    expect(bookingCalls[0]!.body).toMatchObject({
      unitId: 905, applicationAmountReceived: 500_000, applicationFinanceAccountId: 3, applicationPaymentMethod: "BankTransfer",
      applicationPaymentType: "Confirmation", paymentThrough: "TRX-90117",
    });
    expect(screen.getByRole("button", { name: "Print receipt" })).toBeTruthy();
  });

  it("offers no receipt when nothing was received, and opens the booking", async () => {
    await toReview();
    fireEvent.click(screen.getByRole("button", { name: "Create booking" }));
    expect(await screen.findByText("Booking BK-000047 created")).toBeTruthy();
    expect(screen.getByText("Unit 905 is now booked for Hamza Iqbal.")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Print receipt" })).toBeNull();
    expect(bookingCalls[0]!.body).toMatchObject({ applicationAmountReceived: null, applicationPaymentMethod: null });

    fireEvent.click(screen.getByRole("button", { name: "Open booking" }));
    expect(await screen.findByTestId("where")).toHaveProperty("textContent", "/confirmed-bookings/47");
  });

  it("a retry after a dropped connection sends the same key, so it can never book twice", async () => {
    await toReview();
    bookingReply = () => Promise.reject(new TypeError("network down"));
    fireEvent.click(screen.getByRole("button", { name: "Create booking" }));
    expect(await screen.findByText(/same booking will not be created twice/)).toBeTruthy();

    bookingReply = () => json({ id: 47, bookingReference: "BK-000047", recordedPaymentId: null, customerName: "Hamza Iqbal" });
    fireEvent.click(screen.getByRole("button", { name: "Create booking" }));
    await screen.findByText("Booking BK-000047 created");
    expect(bookingCalls).toHaveLength(2);
    expect(bookingCalls[1]!.headers.get("Idempotency-Key")).toBe(bookingCalls[0]!.headers.get("Idempotency-Key"));
  });

  it("shows the server's message when the unit was just taken, and keeps everything typed", async () => {
    await toReview();
    bookingReply = () => json({ message: "Unit 905 was just booked by someone else. Pick another unit." }, 409);
    fireEvent.click(screen.getByRole("button", { name: "Create booking" }));

    expect(await screen.findByText("Unit 905 was just booked by someone else. Pick another unit.")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Edit Customer" }));
    expect((await screen.findByLabelText(/Full name/) as HTMLInputElement).value).toBe("Hamza Iqbal");
  });

  it("each Edit link goes back to its step", async () => {
    await toReview();
    fireEvent.click(screen.getByRole("button", { name: "Edit Price & payment" }));
    expect(await screen.findByLabelText(/Agreed sale price/)).toBeTruthy();
  });
});

describe("Leaving", () => {
  it("asks before discarding what was typed, and leaves on Discard", async () => {
    await toCustomerStep();
    fireEvent.change(screen.getByLabelText(/Full name/), { target: { value: "H" } });
    fireEvent.click(screen.getByRole("button", { name: "Back" }));
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText("Discard this booking?")).toBeTruthy();
    fireEvent.click(within(dialog).getByRole("button", { name: "Keep editing" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());

    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    fireEvent.click(within(await screen.findByRole("dialog")).getByRole("button", { name: "Discard" }));
    expect(await screen.findByTestId("where")).toHaveProperty("textContent", "/confirmed-bookings");
  });

  it("leaves at once when nothing was entered", async () => {
    show();
    await screen.findByRole("combobox", { name: /Project/ });
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(await screen.findByTestId("where")).toHaveProperty("textContent", "/confirmed-bookings");
  });
});

describe("Who can use it", () => {
  it("turns away sales staff", async () => {
    show("/confirmed-bookings/new", "Manager");
    expect(await screen.findByText("You do not have access to bookings.")).toBeTruthy();
  });
});
