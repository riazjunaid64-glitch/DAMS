// @vitest-environment happy-dom
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ToastProvider } from "../components/ui/Toast.tsx";
import { uploadProof } from "../features/proof/proofApi.ts";
import BookingDetailPage from "./BookingDetailPage.tsx";

vi.mock("../features/proof/proofApi.ts", () => ({ uploadProof: vi.fn(), openProof: vi.fn() }));

type Call = { url: string; method: string; headers: Headers; body: unknown };
let calls: Call[];
let answers: Record<string, () => { status?: number; body: unknown }>;

vi.mock("../api/api.ts", () => ({
  api: async (url: string, init?: RequestInit) => {
    const method = init?.method ?? "GET";
    calls.push({
      url,
      method,
      headers: new Headers(init?.headers as HeadersInit | undefined),
      body: typeof init?.body === "string" ? JSON.parse(init.body) : undefined,
    });
    const answer = answers[`${method} ${url}`];
    const { status = 200, body } = answer ? answer() : { status: 404, body: { message: "unexpected" } };
    return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
  },
}));

const base = {
  id: 13,
  bookingReference: "BK-000013",
  customerId: 5,
  customerName: "Usman Tariq",
  customerPhone: "03334412987",
  customerCnic: "37405-1234567-1",
  projectName: "Floria Heights",
  unitNumber: "B08",
  unitType: "2 Bed",
  floorName: "8th floor",
  unitSize: 1180,
  status: "PaymentPlanActive",
  listPrice: 12_800_000,
  agreedSalePrice: 12_800_000,
  discountAmount: 0,
  discountPercent: 0,
  bookingDate: "2026-04-20T07:00:00",
  bookingAmountRequired: 500_000,
  bookingAmountReceived: 500_000,
  bookingAmountRemaining: 0,
  rebateCredits: 0,
  totalInstallmentAmount: 12_300_000,
  collected: 4_172_000,
  outstanding: 8_628_000,
  installmentsPaid: 4,
  installmentsTotal: 13,
  hasInstallmentSchedule: true,
  nextInstallment: { id: 55, number: 5, isPossession: false, amount: 918_000, dueDate: "2026-09-26T00:00:00", isOverdue: true },
  convertedFromLead: null,
  payments: [{ id: 1 }, { id: 2 }],
  concurrencyToken: "abc",
};

const accounts = [{ id: 1, name: "Cash in hand", accountHolderName: "Head office", isActive: true }];

const admin = { userId: 1, role: "Admin", email: "a@b.c", fullName: "A" } as never;
const accountant = { userId: 2, role: "Accountant", email: "b@b.c", fullName: "B" } as never;

function Where() {
  const location = useLocation();
  return <output data-testid="where">{location.pathname}{location.search}</output>;
}

function show(over: Record<string, unknown> = {}, user = admin) {
  answers["GET /api/Booking/13"] = () => ({ body: { ...base, ...over } });
  return render(
    <MemoryRouter initialEntries={["/confirmed-bookings/13"]}>
      <ToastProvider>
        <Routes>
          <Route path="/confirmed-bookings/:id" element={<BookingDetailPage user={user} />} />
          <Route path="*" element={null} />
        </Routes>
        <Where />
      </ToastProvider>
    </MemoryRouter>,
  );
}

function phone(on: boolean) {
  window.matchMedia = ((query: string) => ({
    matches: on && query.includes("max-width"),
    media: query,
    addEventListener: () => {},
    removeEventListener: () => {},
  })) as unknown as typeof window.matchMedia;
}

const requested = (method: string, url: string) => calls.filter((c) => c.method === method && c.url === url);

beforeEach(() => {
  vi.mocked(uploadProof).mockReset();
  calls = [];
  answers = { "GET /api/finance/accounts/options": () => ({ body: accounts }) };
  phone(false);
});
afterEach(cleanup);

describe("the header", () => {
  it("shows the customer line and the buttons of a Payment plan booking with a plan", async () => {
    show();
    expect(await screen.findByRole("heading", { name: "BK-000013" })).toBeTruthy();
    expect(screen.getByText("Payment plan")).toBeTruthy();
    expect(screen.getByRole("link", { name: "0333 4412987" }).getAttribute("href")).toBe("tel:03334412987");
    expect(screen.getByText(/Booked Apr 20, 2026/)).toBeTruthy();
    for (const name of ["Print application form", "Cancel booking", "Give possession"]) {
      expect(screen.getByRole("button", { name })).toBeTruthy();
    }
  });

  it("offers Give possession only once a plan exists (or nothing is left to pay)", async () => {
    show({ hasInstallmentSchedule: false, installmentsTotal: 0, nextInstallment: null });
    await screen.findByRole("heading", { name: "BK-000013" });
    expect(screen.queryByRole("button", { name: "Give possession" })).toBeNull();
    expect(screen.getByRole("button", { name: "Cancel booking" })).toBeTruthy();
  });

  it("Awaiting booking amount: Record payment, and Cancel booking", async () => {
    show({ status: "AwaitingBookingAmount", bookingAmountRequired: 500_000, bookingAmountReceived: 150_000, bookingAmountRemaining: 350_000, hasInstallmentSchedule: false, installmentsTotal: 0, nextInstallment: null });
    await screen.findByRole("heading", { name: "BK-000013" });
    expect(screen.getByRole("button", { name: "Record payment" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Cancel booking" })).toBeTruthy();
  });

  it("Possession given: Complete sale (no Cancel), disabled while anything is owed", async () => {
    show({ status: "PossessionGiven" });
    await screen.findByRole("heading", { name: "BK-000013" });
    expect((screen.getByRole("button", { name: "Complete sale" }) as HTMLButtonElement).disabled).toBe(true);
    expect(screen.queryByRole("button", { name: "Cancel booking" })).toBeNull();
    expect(screen.getByRole("button", { name: "Print application form" })).toBeTruthy();
  });

  it("Sale completed: Print only", async () => {
    show({ status: "SaleCompleted", outstanding: 0 });
    await screen.findByRole("heading", { name: "BK-000013" });
    expect(screen.getAllByRole("button").map((b) => b.textContent).filter((t) => /Print|Cancel|Complete|Give|Record/.test(t ?? ""))).toEqual(["Print application form"]);
  });

  it("Print application form opens the form for this booking", async () => {
    show();
    fireEvent.click(await screen.findByRole("button", { name: "Print application form" }));
    expect(screen.getByTestId("where").textContent).toBe("/application-form?bookingId=13");
  });

  it("phone: one main button and ⋯, whose sheet holds Print and Cancel", async () => {
    phone(true);
    show();
    await screen.findByRole("heading", { name: "BK-000013" });
    expect(screen.getByRole("button", { name: "Give possession" })).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Print application form" })).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "More actions" }));
    const sheet = await screen.findByRole("dialog");
    expect(within(sheet).getByRole("button", { name: "Print application form" })).toBeTruthy();
    expect(within(sheet).getByRole("button", { name: "Cancel booking" })).toBeTruthy();
  });

  it("phone: only a printer icon when Print is all that is left", async () => {
    phone(true);
    show({ status: "SaleCompleted", outstanding: 0 });
    await screen.findByRole("heading", { name: "BK-000013" });
    expect(screen.queryByRole("button", { name: "More actions" })).toBeNull();
    expect(screen.getByRole("button", { name: "Print application form" })).toBeTruthy();
  });

  it("phone: no main button and only ⋯ while the terms are not set", async () => {
    phone(true);
    show({ status: "AwaitingBookingAmount", bookingAmountRequired: 0, bookingAmountReceived: 0, hasInstallmentSchedule: false, installmentsTotal: 0, nextInstallment: null, payments: [] });
    await screen.findByRole("heading", { name: "BK-000013" });
    expect(screen.getByRole("button", { name: "More actions" })).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Record payment" })).toBeNull();
  });
});

describe("tabs", () => {
  it("counts installments and payments from the booking, and reads a tab's data only when it is opened", async () => {
    answers["GET /api/Booking/13/installments"] = () => ({ body: { hasSchedule: true, canGenerate: false, canRegenerate: false, items: [], scheduleTotal: 0, schedulePaid: 0, scheduleRemaining: 0 } });
    show();
    const plan = await screen.findByRole("tab", { name: /Installment plan/ });
    expect(plan.textContent).toContain("13");
    expect(screen.getByRole("tab", { name: /Payments/ }).textContent).toContain("2");
    expect(calls.map((c) => c.url)).not.toContain("/api/Booking/13/installments");
    expect(calls.map((c) => c.url)).not.toContain("/api/Booking/13/payments");

    fireEvent.click(plan);
    await waitFor(() => expect(requested("GET", "/api/Booking/13/installments")).toHaveLength(1));
    expect(calls.map((c) => c.url)).not.toContain("/api/Booking/13/payments");
  });

  it("phone tabs are short names without counts", async () => {
    phone(true);
    show();
    await screen.findByRole("heading", { name: "BK-000013" });
    expect(screen.getAllByRole("tab").map((t) => t.textContent)).toEqual(["Summary", "Plan", "Payments", "Commission"]);
  });
});

describe("Summary", () => {
  it("Payment plan: the four cards and the price statement come from the server's figures", async () => {
    show();
    await screen.findByRole("heading", { name: "BK-000013" });
    expect(screen.getAllByText("Rs 12,800,000").length).toBeGreaterThan(0);
    expect(screen.getByText("Rs 4,172,000", { selector: "p" })).toBeTruthy();
    expect(screen.getByText("33% of the price")).toBeTruthy();
    expect(screen.getByText("Rs 918,000")).toBeTruthy();
    expect(screen.getByText("Overdue · Sep 26, 2026")).toBeTruthy();
    expect(screen.getByText("4 of 13 paid")).toBeTruthy();
    expect(screen.getByText("Unit B08 · 2 Bed")).toBeTruthy();
    expect(screen.getByRole("link", { name: "Open customer" }).getAttribute("href")).toBe("/customers/5");
  });

  it("Payment plan with no plan: the next installment reads No plan yet", async () => {
    show({ hasInstallmentSchedule: false, installmentsTotal: 0, installmentsPaid: 0, nextInstallment: null });
    await screen.findByRole("heading", { name: "BK-000013" });
    expect(screen.getAllByText("No plan yet").length).toBeGreaterThan(0);
  });

  it("Possession given: an orange notice says what is still due", async () => {
    show({ status: "PossessionGiven" });
    expect(await screen.findByText("Rs 8,628,000 still due · 9 installments")).toBeTruthy();
  });

  it("Awaiting: what has been received of what is required, and what is due by when", async () => {
    show({ status: "AwaitingBookingAmount", agreedSalePrice: 11_400_000, discountAmount: 228_000, discountPercent: 2, bookingAmountReceived: 150_000, bookingAmountRequired: 500_000, bookingAmountRemaining: 350_000, bookingAmountDueDate: "2026-10-05T00:00:00", hasInstallmentSchedule: false, installmentsTotal: 0, nextInstallment: null });
    expect(await screen.findByText("received of Rs 500,000")).toBeTruthy();
    expect(screen.getByText("Rs 350,000")).toBeTruthy();
    expect(screen.getByText("Oct 5, 2026")).toBeTruthy();
    expect(screen.getByText("Discount (2%)")).toBeTruthy();
    expect(screen.getByText("− Rs 228,000")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Edit terms" })).toBeTruthy();
  });

  it("Terms not set: Set the terms to start, with Not set for the agreed price and booking amount", async () => {
    show({ status: "AwaitingBookingAmount", bookingAmountRequired: 0, bookingAmountReceived: 0, listPrice: 14_255_985, agreedSalePrice: 0, hasInstallmentSchedule: false, installmentsTotal: 0, nextInstallment: null, payments: [] });
    expect(await screen.findByText("Set the terms to start")).toBeTruthy();
    expect(screen.getAllByText("Not set")).toHaveLength(2);
    expect(screen.getByText("Rs 14,255,985")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Set terms" })).toBeTruthy();
  });

  it("From lead is a link for Admin and plain text for an Accountant", async () => {
    const converted = { status: "AwaitingBookingAmount", bookingAmountRequired: 0, bookingAmountReceived: 0, convertedFromLead: { leadId: 9, leadReference: "LD-000437" } };
    show(converted);
    expect((await screen.findByRole("link", { name: "LD-000437" })).getAttribute("href")).toBe("/crm/leads/9");
    cleanup();
    show(converted, accountant);
    expect(await screen.findByText("LD-000437")).toBeTruthy();
    expect(screen.queryByRole("link", { name: "LD-000437" })).toBeNull();
  });

  it("someone without the bookings permission sees no booking", async () => {
    show({}, { userId: 3, role: "Manager", email: "m@b.c", fullName: "M" } as never);
    expect(await screen.findByText("You do not have access to bookings.")).toBeTruthy();
    expect(calls).toHaveLength(0);
  });
});

describe("Set terms", () => {
  const terms = { status: "AwaitingBookingAmount", bookingAmountRequired: 0, bookingAmountReceived: 0, listPrice: 14_255_985, agreedSalePrice: 14_255_985, hasInstallmentSchedule: false, installmentsTotal: 0, nextInstallment: null, payments: [], bookingReference: "BK-000045", unitNumber: "B09" };

  async function open() {
    show(terms);
    fireEvent.click(await screen.findByRole("button", { name: "Set terms" }));
    return screen.findByRole("dialog");
  }

  it("works a chip out from the agreed price and locks the amount box", async () => {
    const dialog = await open();
    expect(within(dialog).getByText("BK-000045 · Unit B09")).toBeTruthy();
    fireEvent.click(within(dialog).getByRole("radio", { name: "10%" }));
    const amount = within(dialog).getByLabelText("Booking amount in rupees") as HTMLInputElement;
    expect(amount.value).toBe("1,425,599");
    expect(amount.disabled).toBe(true);
    expect(within(dialog).getByText("Left for installments")).toBeTruthy();
    expect(within(dialog).getByText("Rs 12,830,386")).toBeTruthy();

    fireEvent.click(within(dialog).getByRole("radio", { name: "Custom" }));
    expect((within(dialog).getByLabelText("Booking amount in rupees") as HTMLInputElement).disabled).toBe(false);
  });

  it("asks for a discount reason only while there is a discount, and shows the discount in rupees", async () => {
    const dialog = await open();
    expect(within(dialog).queryByLabelText("Discount reason")).toBeNull();
    fireEvent.change(within(dialog).getByLabelText("Discount %"), { target: { value: "5" } });
    expect(within(dialog).getByLabelText("Discount reason")).toBeTruthy();
    expect(within(dialog).getByText("Discount Rs 712,799.25")).toBeTruthy();
  });

  it("hides the summary strip and disables Save while the amount is too high", async () => {
    const dialog = await open();
    fireEvent.click(within(dialog).getByRole("radio", { name: "Custom" }));
    fireEvent.change(within(dialog).getByLabelText("Booking amount in rupees"), { target: { value: "15000000" } });
    expect(within(dialog).getByText("Can't be more than the net price, Rs 14,255,985")).toBeTruthy();
    expect(within(dialog).queryByText("Left for installments")).toBeNull();
    expect((within(dialog).getByRole("button", { name: "Save terms" }) as HTMLButtonElement).disabled).toBe(true);
  });

  it("saves with a retry key, thanks the user, and reloads the booking", async () => {
    answers["PUT /api/Booking/13/financials"] = () => ({ body: { ...base, ...terms, status: "AwaitingBookingAmount" } });
    const dialog = await open();
    fireEvent.click(within(dialog).getByRole("radio", { name: "10%" }));
    fireEvent.click(within(dialog).getByRole("button", { name: "Save terms" }));

    await waitFor(() => expect(requested("PUT", "/api/Booking/13/financials")).toHaveLength(1));
    const [saved] = requested("PUT", "/api/Booking/13/financials");
    expect(saved!.headers.get("Idempotency-Key")).toMatch(/^terms-/);
    expect(saved!.body).toEqual({
      agreedSalePrice: 14_255_985, discountPercent: 0, discountReason: "", bookingAmountRequired: 1_425_599, bookingAmountDueDate: null,
    });
    expect(await screen.findByText("Terms saved.")).toBeTruthy();
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(requested("GET", "/api/Booking/13").length).toBeGreaterThan(1);
  });

  it("keeps the popup open with what was typed when the server refuses", async () => {
    answers["PUT /api/Booking/13/financials"] = () => ({ status: 400, body: { message: "This booking was just changed by someone else. Refresh and try again." } });
    const dialog = await open();
    fireEvent.click(within(dialog).getByRole("radio", { name: "15%" }));
    fireEvent.click(within(dialog).getByRole("button", { name: "Save terms" }));

    expect(await within(dialog).findByText("This booking was just changed by someone else. Refresh and try again.")).toBeTruthy();
    expect(screen.getByRole("dialog")).toBeTruthy();
    expect(within(dialog).getByRole("radio", { name: "15%" }).getAttribute("aria-checked")).toBe("true");
  });

  it("Edit terms when money is received: the amount cannot go below it", async () => {
    show({ ...terms, bookingReference: "BK-000044", bookingAmountRequired: 500_000, bookingAmountReceived: 150_000, bookingAmountRemaining: 350_000, payments: [{ id: 1 }] });
    fireEvent.click(await screen.findByRole("button", { name: "Edit terms" }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText("Edit terms")).toBeTruthy();
    expect(within(dialog).getByText("BK-000044 · Rs 150,000 already received")).toBeTruthy();
    fireEvent.change(within(dialog).getByLabelText("Booking amount in rupees"), { target: { value: "100000" } });
    expect(within(dialog).getByText("Can't be less than Rs 150,000 already received")).toBeTruthy();
    expect((within(dialog).getByRole("button", { name: "Save terms" }) as HTMLButtonElement).disabled).toBe(true);
  });
});

describe("Record booking amount", () => {
  const awaiting = { status: "AwaitingBookingAmount", bookingAmountRequired: 500_000, bookingAmountReceived: 150_000, bookingAmountRemaining: 350_000, hasInstallmentSchedule: false, installmentsTotal: 0, nextInstallment: null, bookingReference: "BK-000044", payments: [{ id: 1, amount: 150_000 }] };

  async function open() {
    show(awaiting);
    fireEvent.click(await screen.findByRole("button", { name: "Record payment" }));
    return screen.findByRole("dialog");
  }

  it("starts from what is due, with the method, account and date filled in", async () => {
    const dialog = await open();
    expect(within(dialog).getByText("Record booking amount")).toBeTruthy();
    expect(within(dialog).getByText("BK-000044 · Rs 350,000 due")).toBeTruthy();
    expect((within(dialog).getByLabelText(/Amount/) as HTMLInputElement).value).toBe("350,000");
    expect(within(dialog).getByText("Up to Rs 350,000")).toBeTruthy();
    expect(within(dialog).getByText("Cash in hand — Head office")).toBeTruthy();
  });

  it("needs a reference for a cheque and records nothing until it is given", async () => {
    answers["POST /api/Booking/13/booking-amount-payment"] = () => ({ body: { ...base, ...awaiting } });
    const dialog = await open();
    fireEvent.click(within(dialog).getByRole("combobox", { name: /Payment method/ }));
    fireEvent.click(await screen.findByRole("option", { name: "Cheque" }));
    fireEvent.click(within(dialog).getByRole("button", { name: "Record payment" }));

    expect(await within(dialog).findByText("Enter the cheque or transfer number.")).toBeTruthy();
    expect(requested("POST", "/api/Booking/13/booking-amount-payment")).toHaveLength(0);

    fireEvent.change(within(dialog).getByLabelText(/Reference/), { target: { value: "CHQ-1001" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Record payment" }));
    await waitFor(() => expect(requested("POST", "/api/Booking/13/booking-amount-payment")).toHaveLength(1));
    const [sent] = requested("POST", "/api/Booking/13/booking-amount-payment");
    expect(sent!.body).toMatchObject({ amount: 350_000, paymentMethod: "Cheque", financeAccountId: 1, paymentReference: "CHQ-1001", notes: null });
    expect(sent!.headers.get("Idempotency-Key")).toMatch(/^booking-amount-payment-/);
    expect(await screen.findByText("Payment recorded.")).toBeTruthy();
  });

  it("takes cash without a reference", async () => {
    answers["POST /api/Booking/13/booking-amount-payment"] = () => ({ body: { ...base, ...awaiting } });
    const dialog = await open();
    fireEvent.click(within(dialog).getByRole("button", { name: "Record payment" }));
    await waitFor(() => expect(requested("POST", "/api/Booking/13/booking-amount-payment")).toHaveLength(1));
    expect(requested("POST", "/api/Booking/13/booking-amount-payment")[0]!.body).toMatchObject({ paymentMethod: "Cash", paymentReference: null });
  });

  it("refuses more than is due, before anything is sent", async () => {
    const dialog = await open();
    fireEvent.change(within(dialog).getByLabelText(/Amount/), { target: { value: "350001" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Record payment" }));
    expect(await within(dialog).findByText("Can't be more than Rs 350,000")).toBeTruthy();
    expect(requested("POST", "/api/Booking/13/booking-amount-payment")).toHaveLength(0);
  });

  it("shows the server's message and keeps the popup open when the payment is refused", async () => {
    answers["POST /api/Booking/13/booking-amount-payment"] = () => ({ status: 400, body: { message: "Payment exceeds the remaining booking amount. Remaining is 300000.00." } });
    const dialog = await open();
    fireEvent.click(within(dialog).getByRole("button", { name: "Record payment" }));
    expect(await within(dialog).findByText("Payment exceeds the remaining booking amount. Remaining is 300000.00.")).toBeTruthy();
    expect(screen.getByRole("dialog")).toBeTruthy();
  });
});

describe("proof on saved payments", () => {
  it("Payment History shows the proof file name where there is one and Attach proof where there is not", async () => {
    answers["GET /api/Booking/13/payments"] = () => ({ body: [
      { id: 1, type: "BookingAmount", amount: 500_000, paymentMethod: "Cash", receiptNumber: "RCP-000001", paidAt: "2026-04-20T07:00:00", proof: { id: 8, fileName: "cash-slip.pdf", fileSize: 10 } },
      { id: 2, type: "BookingAmount", amount: 100_000, paymentMethod: "Cash", receiptNumber: "RCP-000002", paidAt: "2026-04-21T07:00:00", proof: null },
    ] });
    show();
    fireEvent.click(await screen.findByRole("tab", { name: /Payments/ }));
    expect(await screen.findByRole("button", { name: "cash-slip.pdf" })).toBeTruthy();
    expect(screen.getAllByRole("button", { name: "Attach proof" })).toHaveLength(1);
  });
});

describe("proof on a recorded payment", () => {
  const awaiting = { status: "AwaitingBookingAmount", bookingAmountRequired: 500_000, bookingAmountReceived: 150_000, bookingAmountRemaining: 350_000, hasInstallmentSchedule: false, installmentsTotal: 0, nextInstallment: null, bookingReference: "BK-000044", payments: [{ id: 1, amount: 150_000 }] };
  const slip = new File(["x"], "slip.pdf", { type: "application/pdf" });

  async function openWithFile() {
    show(awaiting);
    fireEvent.click(await screen.findByRole("button", { name: "Record payment" }));
    const dialog = await screen.findByRole("dialog");
    const input = dialog.querySelector("input[type=file]") as HTMLInputElement;
    fireEvent.change(input, { target: { files: [slip] } });
    return dialog;
  }

  it("goes onto the payment the server says it created, not one guessed from the list", async () => {
    answers["POST /api/Booking/13/booking-amount-payment"] = () => ({ body: { ...base, ...awaiting, recordedPaymentId: 77 } });
    vi.mocked(uploadProof).mockResolvedValue();
    const dialog = await openWithFile();
    fireEvent.click(within(dialog).getByRole("button", { name: "Record payment" }));

    await waitFor(() => expect(uploadProof).toHaveBeenCalledWith("CustomerPayment", 77, slip, expect.any(Function), expect.any(AbortSignal)));
    expect(await screen.findByText("Payment recorded.")).toBeTruthy();
    expect(calls.map((c) => c.url)).not.toContain("/api/Booking/13/payments");
  });

  it("refreshes the booking only after the proof is stored, so the list already shows it", async () => {
    answers["POST /api/Booking/13/booking-amount-payment"] = () => ({ body: { ...base, ...awaiting, recordedPaymentId: 77 } });
    let uploaded = false;
    vi.mocked(uploadProof).mockImplementation(async () => { uploaded = true; });
    const dialog = await openWithFile();
    const reloadsBefore = requested("GET", "/api/Booking/13").length;
    let uploadedBeforeReload: boolean | null = null;
    const original = answers["GET /api/Booking/13"]!;
    answers["GET /api/Booking/13"] = () => { uploadedBeforeReload ??= uploaded; return original(); };
    fireEvent.click(within(dialog).getByRole("button", { name: "Record payment" }));

    await waitFor(() => expect(requested("GET", "/api/Booking/13").length).toBeGreaterThan(reloadsBefore));
    expect(uploadedBeforeReload).toBe(true);
  });

  it("closes with a warning when the upload fails, having recorded the payment once", async () => {
    answers["POST /api/Booking/13/booking-amount-payment"] = () => ({ body: { ...base, ...awaiting, recordedPaymentId: 77 } });
    vi.mocked(uploadProof).mockRejectedValue(new Error("Network down"));
    const dialog = await openWithFile();
    fireEvent.click(within(dialog).getByRole("button", { name: "Record payment" }));

    expect(await screen.findByText("Payment recorded, but the proof did not upload. Attach it from Payment History.")).toBeTruthy();
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(requested("POST", "/api/Booking/13/booking-amount-payment")).toHaveLength(1);
  });
});

describe("Give possession and Complete sale", () => {
  it("asks for the date, says what is still to be paid, and gives possession with a retry key", async () => {
    answers["POST /api/Booking/13/possession"] = () => ({ body: { ...base, status: "PossessionGiven" } });
    show();
    fireEvent.click(await screen.findByRole("button", { name: "Give possession" }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText("Give possession of Unit B08?")).toBeTruthy();
    expect(within(dialog).getByText("Rs 8,628,000 is still to be paid on the plan.")).toBeTruthy();

    fireEvent.click(within(dialog).getByRole("button", { name: "Give possession" }));
    await waitFor(() => expect(requested("POST", "/api/Booking/13/possession")).toHaveLength(1));
    const [sent] = requested("POST", "/api/Booking/13/possession");
    expect(sent!.headers.get("Idempotency-Key")).toMatch(/^possession-/);
    expect((sent!.body as { possessionDate: string }).possessionDate).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    expect(await screen.findByText("Possession of Unit B08 given.")).toBeTruthy();
  });

  it("leaves the still-to-be-paid line out when nothing is owed", async () => {
    show({ outstanding: 0, installmentsPaid: 13 });
    fireEvent.click(await screen.findByRole("button", { name: "Give possession" }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).queryByText(/still to be paid/)).toBeNull();
  });

  it("shows the server's message when possession is refused", async () => {
    answers["POST /api/Booking/13/possession"] = () => ({ status: 400, body: { message: "Possession date cannot be before the booking date (20 Apr 2026)." } });
    show();
    fireEvent.click(await screen.findByRole("button", { name: "Give possession" }));
    const dialog = await screen.findByRole("dialog");
    fireEvent.click(within(dialog).getByRole("button", { name: "Give possession" }));
    expect(await within(dialog).findByText("Possession date cannot be before the booking date (20 Apr 2026).")).toBeTruthy();
  });

  it("Complete sale is enabled once everything is paid, and confirms first", async () => {
    answers["POST /api/Booking/13/complete"] = () => ({ body: { ...base, status: "SaleCompleted" } });
    show({ status: "PossessionGiven", outstanding: 0, installmentsPaid: 13 });
    const button = await screen.findByRole("button", { name: "Complete sale" });
    expect((button as HTMLButtonElement).disabled).toBe(false);
    fireEvent.click(button);
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText("Complete the sale?")).toBeTruthy();
    expect(within(dialog).getByText("Unit B08 becomes Sold.")).toBeTruthy();
    expect(requested("POST", "/api/Booking/13/complete")).toHaveLength(0);

    fireEvent.click(within(dialog).getByRole("button", { name: "Complete sale" }));
    await waitFor(() => expect(requested("POST", "/api/Booking/13/complete")).toHaveLength(1));
    expect(requested("POST", "/api/Booking/13/complete")[0]!.headers.get("Idempotency-Key")).toMatch(/^complete-/);
  });
});

// ── Installment plan and Payments tabs (KAN-74) ───────────────────────────────────────────────

const dueOn = (month: number) => new Date(Date.UTC(2026, 9 + month - 1, 26)).toISOString().slice(0, 19);

function installment(number: number, over: Record<string, unknown> = {}) {
  return {
    id: 100 + number, sequenceNumber: number, type: "Regular", dueDate: dueOn(number), amount: 918_000, status: "Pending",
    amountPaid: 0, remainingBalance: 918_000, isOverdue: false, ...over,
  };
}

const paidRow = (number: number) => installment(number, { status: "Paid", amountPaid: 918_000, remainingBalance: 0 });
const possessionRow = () => ({
  id: 200, sequenceNumber: 0, type: "Possession", dueDate: "2027-11-01T00:00:00", amount: 1_284_000, status: "Pending",
  amountPaid: 0, remainingBalance: 1_284_000, isOverdue: false,
});

function planOf(items: unknown[], over: Record<string, unknown> = {}) {
  return {
    hasSchedule: true, canGenerate: true, canRegenerate: true, agreedSalePrice: 12_800_000, discountPercent: 0, discountAmount: 0,
    bookingAmountReceived: 500_000, possessionAmount: 1_284_000, installmentPool: 11_016_000, frequency: "Monthly",
    numberOfInstallments: 12, installmentStartDate: "2026-10-26T00:00:00", possessionDueDate: "2027-11-01T00:00:00",
    scheduleTotal: 12_300_000, schedulePaid: 0, scheduleRemaining: 12_300_000, unscheduledBalance: 0, items, ...over,
  };
}

// The Summary tab stays mounted behind the others, so text is looked up inside the tab that is showing.
const tab = () => within(screen.getByRole("tabpanel"));

const thirteen = () => [...Array.from({ length: 12 }, (_, index) => installment(index + 1)), possessionRow()];

async function openPlan(schedule: unknown, over: Record<string, unknown> = {}) {
  answers["GET /api/Booking/13/installments"] = () => ({ body: schedule });
  show(over);
  fireEvent.click(await screen.findByRole("tab", { name: /Installment plan|Plan/ }));
}

describe("Installment plan: no plan yet", () => {
  const empty = { hasSchedule: false, canGenerate: true, canRegenerate: false, installmentPool: 12_300_000, possessionAmount: 0, items: [] };

  it("shows one empty card with what is to be scheduled and a Create plan button", async () => {
    await openPlan(empty, { hasInstallmentSchedule: false, installmentsTotal: 0, nextInstallment: null });
    expect(await screen.findByText("No installment plan yet")).toBeTruthy();
    expect(screen.getByText("Rs 12,300,000 to schedule")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Create plan" })).toBeTruthy();
  });

  it("says the booking amount comes first when the plan cannot be created yet", async () => {
    await openPlan({ ...empty, canGenerate: false }, { hasInstallmentSchedule: false, installmentsTotal: 0, nextInstallment: null });
    expect(await screen.findByText("The booking amount must be fully received before the installment plan unlocks.")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Create plan" })).toBeNull();
  });
});

describe("Create plan", () => {
  const empty = { hasSchedule: false, canGenerate: true, canRegenerate: false, installmentPool: 12_300_000, possessionAmount: 0, items: [] };
  const none = { hasInstallmentSchedule: false, installmentsTotal: 0, nextInstallment: null };

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(new Date("2026-09-29T07:00:00Z"));
    Element.prototype.scrollIntoView = () => {};
  });
  afterEach(() => vi.useRealTimers());

  async function open() {
    await openPlan(empty, none);
    fireEvent.click(await screen.findByRole("button", { name: "Create plan" }));
    return screen.findByRole("dialog", { name: /Create installment plan/ });
  }

  function pickFirstDue(dialog: HTMLElement) {
    fireEvent.click(within(dialog).getByRole("button", { name: /First due/ }));
    fireEvent.click(screen.getByRole("button", { name: "Next month" }));
    fireEvent.click(screen.getByRole("button", { name: "October 26, 2026" }));
  }

  it("opens on the booking's terms and works out each installment as it is typed", async () => {
    const dialog = await open();
    expect(within(dialog).getByText("BK-000013")).toBeTruthy();
    expect((within(dialog).getByLabelText(/Agreed sale price/) as HTMLInputElement).value).toBe("12,800,000");
    expect(within(dialog).getByText("To schedule")).toBeTruthy();
    expect(within(dialog).getByText("Rs 12,300,000")).toBeTruthy();
    expect(within(dialog).getByText("Rs 1,025,000")).toBeTruthy();

    fireEvent.change(within(dialog).getByLabelText(/Possession amount/), { target: { value: "1284000" } });
    expect(within(dialog).getByText("Rs 918,000")).toBeTruthy();
    expect(within(dialog).getByLabelText(/Possession due/, { selector: "button" })).toBeTruthy();
  });

  it("asks for the first due date before anything is sent", async () => {
    const dialog = await open();
    fireEvent.click(within(dialog).getByRole("button", { name: "Create plan" }));
    expect(await within(dialog).findByText("Choose when installment 1 is due.")).toBeTruthy();
    expect(requested("POST", "/api/Booking/13/installment-plan/generate")).toHaveLength(0);
  });

  it("saves with a retry key, thanks the user and reloads the plan", async () => {
    answers["POST /api/Booking/13/installment-plan/generate"] = () => ({ body: planOf(thirteen()) });
    const dialog = await open();
    pickFirstDue(dialog);
    fireEvent.click(within(dialog).getByRole("button", { name: "Create plan" }));

    await waitFor(() => expect(requested("POST", "/api/Booking/13/installment-plan/generate")).toHaveLength(1));
    const [saved] = requested("POST", "/api/Booking/13/installment-plan/generate");
    expect(saved!.headers.get("Idempotency-Key")).toMatch(/^plan-/);
    expect(saved!.body).toEqual({
      agreedSalePrice: 12_800_000, discountPercent: 0, frequency: "Monthly", numberOfInstallments: 12,
      installmentStartDate: "2026-10-26", possessionAmount: 0, possessionDueDate: null, regenerate: false,
    });
    expect(await screen.findByText("Plan created.")).toBeTruthy();
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(requested("GET", "/api/Booking/13/installments").length).toBeGreaterThan(1);
  });

  it("keeps the popup open with what was typed when the server refuses", async () => {
    answers["POST /api/Booking/13/installment-plan/generate"] = () => ({ status: 400, body: { message: "Installment pool is too small for this number of installments." } });
    const dialog = await open();
    pickFirstDue(dialog);
    fireEvent.change(within(dialog).getByLabelText(/Installments/, { selector: "input" }), { target: { value: "24" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Create plan" }));

    expect(await within(dialog).findByText("Installment pool is too small for this number of installments.")).toBeTruthy();
    expect((within(dialog).getByLabelText(/Installments/, { selector: "input" }) as HTMLInputElement).value).toBe("24");
    expect(screen.getByRole("dialog")).toBeTruthy();
  });
});

describe("Installment plan: nothing paid yet", () => {
  it("shows three stat cards, the schedule heading, Change plan and a Record payment on every unpaid row", async () => {
    await openPlan(planOf(thirteen()));
    expect(await screen.findByText("12 monthly installments from Oct 26, 2026")).toBeTruthy();
    expect(tab().getByText("Plan total")).toBeTruthy();
    expect(tab().getByText("0 of 13 paid")).toBeTruthy();
    expect(tab().getByText("Next due")).toBeTruthy();
    expect(tab().queryByText("Overdue")).toBeNull();
    expect(screen.getByRole("button", { name: "Change plan" })).toBeTruthy();
    expect(screen.getAllByRole("button", { name: "Record payment" })).toHaveLength(13);
    expect(screen.getByText("Possession").className).toContain("text-gold-text");
  });

  it("Record payment on a row opens the payment popup for that installment", async () => {
    await openPlan(planOf(thirteen()));
    fireEvent.click((await screen.findAllByRole("button", { name: "Record payment" }))[2]!);
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText("BK-000013 · Installment 3")).toBeTruthy();
  });

  it("has no Change plan once the server says the plan cannot be replaced", async () => {
    await openPlan(planOf(thirteen(), { canGenerate: false, canRegenerate: false }));
    await screen.findByText("12 monthly installments from Oct 26, 2026");
    expect(screen.queryByRole("button", { name: "Change plan" })).toBeNull();
  });
});

describe("Installment plan: with payments", () => {
  const paidOn = () => [paidRow(1), paidRow(2), paidRow(3), paidRow(4),
    installment(5, { status: "Overdue", isOverdue: true }), ...Array.from({ length: 7 }, (_, index) => installment(index + 6)), possessionRow()];
  const withPayments = () => planOf(paidOn(), { canGenerate: false, canRegenerate: false, schedulePaid: 3_672_000, scheduleRemaining: 8_628_000 });

  it("shows four stat cards with the overdue amount, and no Change plan", async () => {
    await openPlan(withPayments());
    expect(await screen.findByText("4 of 13 paid")).toBeTruthy();
    expect(tab().getByText("Rs 3,672,000")).toBeTruthy();
    // The stat card and the column header both say Remaining.
    expect(tab().getAllByText("Remaining")).toHaveLength(2);
    // The stat card and the overdue row's badge.
    expect(tab().getAllByText("Overdue")).toHaveLength(2);
    expect(tab().getByText("1 installment")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Change plan" })).toBeNull();
  });

  it("writes an overdue due date in red, and puts Record payment on every unpaid row but none on paid ones", async () => {
    await openPlan(withPayments());
    const overdue = await screen.findByText("Feb 26, 2027");
    expect(overdue.className).toContain("text-danger");
    expect(screen.getByText("Jan 26, 2027").className).not.toContain("text-danger");
    expect(screen.getAllByRole("button", { name: "Record payment" })).toHaveLength(9);
  });

  it("pages a long plan at 20 rows", async () => {
    const long = Array.from({ length: 25 }, (_, index) => installment(index + 1));
    await openPlan(planOf(long, { numberOfInstallments: 25 }));
    await screen.findByText("25 monthly installments from Oct 26, 2026");
    expect(screen.getAllByRole("button", { name: "Record payment" })).toHaveLength(20);
    fireEvent.click(screen.getByRole("button", { name: "Page 2" }));
    expect(screen.getAllByRole("button", { name: "Record payment" })).toHaveLength(5);
  });

  it("starts again on page 1 when a changed plan is shorter than the page being viewed", async () => {
    const rows = (count: number) => Array.from({ length: count }, (_, index) => installment(index + 1));
    answers["POST /api/Booking/13/installment-plan/generate"] = () => ({ body: planOf(rows(21)) });
    await openPlan(planOf(rows(45), { numberOfInstallments: 45, generatedAt: "2026-09-01T00:00:00" }));
    await screen.findByText("45 monthly installments from Oct 26, 2026");
    fireEvent.click(screen.getByRole("button", { name: "Page 3" }));
    expect(screen.getAllByRole("button", { name: "Record payment" })).toHaveLength(5);

    // The server now holds the shorter plan, built later.
    answers["GET /api/Booking/13/installments"] = () => ({ body: planOf(rows(21), { numberOfInstallments: 21, generatedAt: "2026-09-02T00:00:00" }) });
    fireEvent.click(screen.getByRole("button", { name: "Change plan" }));
    const dialog = await screen.findByRole("dialog", { name: /Change plan/ });
    fireEvent.click(within(dialog).getByRole("button", { name: "Change plan" }));

    expect(await screen.findByText("21 monthly installments from Oct 26, 2026")).toBeTruthy();
    await waitFor(() => expect(screen.getAllByRole("button", { name: "Record payment" })).toHaveLength(20));
    expect(screen.getByRole("button", { name: "Page 1" }).getAttribute("aria-current")).toBe("page");
  });

  it("phone: the last two paid and the next unpaid ones, then Show all", async () => {
    phone(true);
    await openPlan(withPayments());
    expect(await screen.findByText("Installment 3")).toBeTruthy();
    expect(screen.getByText("Installment 4")).toBeTruthy();
    expect(screen.queryByText("Installment 2")).toBeNull();
    expect(screen.getByText("Installment 7")).toBeTruthy();
    expect(screen.queryByText("Installment 8")).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Show all 13" }));
    expect(screen.getByText("Installment 1")).toBeTruthy();
    expect(screen.getByText("Possession")).toBeTruthy();
  });
});

describe("Installment plan: money outside the plan", () => {
  const short = () => planOf(thirteen(), { unscheduledBalance: 50_000 });

  it("warns with the amount, hides Record payment, and offers Change plan", async () => {
    await openPlan(short());
    expect(await screen.findByText("Rs 50,000 is not in the plan. Change the plan before recording more payments.")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Record payment" })).toBeNull();
    expect(screen.getAllByRole("button", { name: "Change plan" }).length).toBeGreaterThan(0);
  });

  it("Change plan opens on the current plan and replaces every installment", async () => {
    answers["POST /api/Booking/13/installment-plan/generate"] = () => ({ body: planOf(thirteen()) });
    await openPlan(short());
    fireEvent.click((await screen.findAllByRole("button", { name: "Change plan" }))[0]!);
    const dialog = await screen.findByRole("dialog", { name: /Change plan/ });
    expect(within(dialog).getByText("Replaces all 13 installments")).toBeTruthy();
    expect((within(dialog).getByLabelText(/Installments/, { selector: "input" }) as HTMLInputElement).value).toBe("12");
    expect(within(dialog).getByRole("button", { name: /First due/ }).textContent).toContain("Oct 26, 2026");

    fireEvent.click(within(dialog).getByRole("button", { name: "Change plan" }));
    await waitFor(() => expect(requested("POST", "/api/Booking/13/installment-plan/generate")).toHaveLength(1));
    const [saved] = requested("POST", "/api/Booking/13/installment-plan/generate");
    expect(saved!.body).toMatchObject({ regenerate: true, installmentStartDate: "2026-10-26", possessionAmount: 1_284_000, possessionDueDate: "2027-11-01" });
    expect(await screen.findByText("Plan changed.")).toBeTruthy();
  });
});

describe("Payments tab", () => {
  const rows = [
    { id: 2, type: "Installment", installmentId: 104, installmentSequence: 4, installmentType: "Regular", amount: 918_000, paymentMethod: "BankTransfer", paymentReference: "TT-2201", receiptNumber: "RCP-000231", paidAt: "2026-09-20T07:00:00", accountName: "Cash in hand", proof: { id: 8, fileName: "slip.pdf", fileSize: 10 } },
    { id: 1, type: "BookingAmount", installmentId: null, amount: 500_000, paymentMethod: "Cash", paymentReference: null, receiptNumber: "RCP-000001", paidAt: "2026-04-20T07:00:00", proof: null },
  ];
  const openPayments = async () => {
    answers["GET /api/Booking/13/payments"] = () => ({ body: rows });
    show({ collected: 4_172_000, rebateCredits: 0, outstanding: 8_628_000, payments: rows });
    fireEvent.click(await screen.findByRole("tab", { name: /Payments/ }));
  };

  it("shows Collected with its payment count, Rebate credits and Outstanding", async () => {
    await openPayments();
    expect(await tab().findByText("Collected")).toBeTruthy();
    expect(tab().getByText("Rs 4,172,000")).toBeTruthy();
    expect(tab().getByText("2 payments")).toBeTruthy();
    expect(tab().getByText("Rebate credits")).toBeTruthy();
    expect(tab().getByText("Outstanding")).toBeTruthy();
    expect(tab().getByText("Rs 8,628,000")).toBeTruthy();
  });

  it("lists each payment with what it was for, its method, reference and amount", async () => {
    await openPayments();
    expect(await tab().findByText("RCP-000231")).toBeTruthy();
    expect(tab().getByText("Installment 4")).toBeTruthy();
    expect(tab().getByText("Booking amount")).toBeTruthy();
    expect(tab().getByText("Bank transfer")).toBeTruthy();
    expect(tab().getByText("TT-2201")).toBeTruthy();
    expect(tab().getByText("Sep 20, 2026")).toBeTruthy();
    expect(tab().getByText("Rs 918,000")).toBeTruthy();
    expect(tab().getByRole("button", { name: "slip.pdf" })).toBeTruthy();
    expect(tab().getAllByRole("button", { name: "Attach proof" })).toHaveLength(1);
  });

  it("Receipt opens the receipt page inside the app, not a new tab", async () => {
    const open = vi.spyOn(window, "open");
    await openPayments();
    fireEvent.click((await screen.findAllByRole("button", { name: "Receipt" }))[0]!);
    expect(screen.getByTestId("where").textContent).toBe("/receipt/13/2");
    expect(open).not.toHaveBeenCalled();
    open.mockRestore();
  });

  it("phone: a card per payment with the reference only when there is one", async () => {
    phone(true);
    await openPayments();
    expect(await screen.findByText("RCP-000231")).toBeTruthy();
    expect(screen.getAllByText("Reference")).toHaveLength(1);
    expect(screen.getAllByRole("button", { name: "Receipt" })).toHaveLength(2);
  });

  it("says No payments yet, and not that the payments could not be read, when there are none", async () => {
    answers["GET /api/Booking/13/payments"] = () => ({ body: [] });
    show({ payments: [], collected: 0 });
    fireEvent.click(await screen.findByRole("tab", { name: /Payments/ }));
    expect(await screen.findByText("No payments yet")).toBeTruthy();
  });
});
