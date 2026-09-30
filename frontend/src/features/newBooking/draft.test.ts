import { describe, expect, it } from "vitest";
import { pakistanToday } from "../../lib/financePeriods.ts";
import { bookingPayload, draftSummary, emptyDraft, firstStepWithErrors, isDirty, stepErrors, withAgreedPrice, withUnit, type BookingDraft } from "./draft";
import type { PickerUnit } from "./units";

const unit: PickerUnit = { id: 810, unitNumber: "810", unitType: "1 Bed", floorNumber: 8, floorName: "", size: 864, price: 14_255_985, status: "Available" };
const ready = (change: Partial<BookingDraft> = {}): BookingDraft => ({
  ...withUnit({ ...emptyDraft(), projectName: "Floria Heights" }, unit),
  fullName: "Hamza Iqbal", mobile: "0334 6120451", ...change,
});

describe("picking a unit", () => {
  it("fills the category, the agreed price and the price per sq ft from it", () => {
    const draft = withUnit(emptyDraft(), unit);
    expect(draft.category).toBe("1 Bed");
    expect(draft.agreedPrice).toBe("14255985");
    expect(draft.pricePerSft).toBe("16499.98");
  });

  it("moves the price per sq ft with the agreed price until it is set by hand", () => {
    const moved = withAgreedPrice(withUnit(emptyDraft(), unit), "13000000");
    expect(moved.pricePerSft).toBe("15046.3");
    const own = withAgreedPrice({ ...withUnit(emptyDraft(), unit), pricePerSft: "15000", pricePerSftEdited: true }, "13000000");
    expect(own.pricePerSft).toBe("15000");
  });
});

describe("step 1 · unit", () => {
  it("cannot continue until a unit is picked", () => {
    expect(stepErrors(0, emptyDraft()).unit).toBeTruthy();
    expect(stepErrors(0, ready())).toEqual({});
  });
});

describe("step 2 · customer", () => {
  it("asks for a name and a valid mobile, with the messages the screens show", () => {
    const errors = stepErrors(1, ready({ fullName: "", mobile: "12345" }));
    expect(errors.fullName).toBeTruthy();
    expect(errors.mobile).toBe("Enter a valid mobile number, e.g. 0300 1234567");
    expect(stepErrors(1, ready({ mobile: "" })).mobile).toBeTruthy();
    expect(stepErrors(1, ready({ mobile: "+92 334 6120451" }))).toEqual({});
  });

  it("accepts a passport number but holds a CNIC-looking entry to the format", () => {
    expect(stepErrors(1, ready({ cnic: "37405-7654321" })).cnic).toBe("Use 00000-0000000-0 or a passport number");
    expect(stepErrors(1, ready({ cnic: "37405-7654321-9" }))).toEqual({});
    expect(stepErrors(1, ready({ cnic: "AB1234567" }))).toEqual({});
  });

  it("checks the email, and WhatsApp when typed", () => {
    expect(stepErrors(1, ready({ email: "nope" })).email).toBeTruthy();
    expect(stepErrors(1, ready({ whatsapp: "12" })).whatsapp).toBeTruthy();
  });

  it("an existing customer only has to be chosen", () => {
    const base = ready({ customerMode: "existing", fullName: "", mobile: "" });
    expect(stepErrors(1, base).customer).toBeTruthy();
    expect(stepErrors(1, { ...base, pickedCustomer: { id: 5, fullName: "Usman Tariq", phone: "03334412987", cnic: null } })).toEqual({});
  });
});

describe("step 3 · next of kin", () => {
  it("is all optional, but what is typed has to be valid", () => {
    expect(stepErrors(2, ready())).toEqual({});
    expect(stepErrors(2, ready({ kinMobile: "1" })).kinMobile).toBeTruthy();
    expect(stepErrors(2, ready({ kinCnic: "12345-12" })).kinCnic).toBeTruthy();
  });
});

describe("step 4 · price and payment", () => {
  it("is fine with nothing received", () => {
    expect(stepErrors(3, ready())).toEqual({});
  });

  it("needs a price and a booking amount, and keeps the amount within the net price", () => {
    expect(stepErrors(3, ready({ agreedPrice: "" })).agreedPrice).toBeTruthy();
    expect(stepErrors(3, ready({ chip: "custom", customAmount: "" })).bookingAmount).toBeTruthy();
    expect(stepErrors(3, ready({ chip: "custom", customAmount: "99999999" })).bookingAmount).toContain("net price");
    expect(stepErrors(3, ready({ discountPercent: "120" })).discount).toBeTruthy();
  });

  it("asks for the account, method, reference and date once something is received", () => {
    const errors = stepErrors(3, ready({ received: "500000", accountId: "", method: "BankTransfer", reference: "", paidOn: "" }));
    expect(errors.accountId).toBeTruthy();
    expect(errors.reference).toBeTruthy();
    expect(errors.paidOn).toBeTruthy();
    expect(stepErrors(3, ready({ received: "500000", accountId: "1", method: "Cash", reference: "" }))).toEqual({});
  });

  it("cannot receive more than the booking amount, or be dated in the future", () => {
    expect(stepErrors(3, ready({ received: "2000000", accountId: "1" })).received).toContain("booking amount");
    expect(stepErrors(3, ready({ received: "100", accountId: "1", paidOn: "2999-01-01" })).paidOn).toBeTruthy();
  });

  it("jumps back to the first step that has something wrong", () => {
    expect(firstStepWithErrors(ready())).toBeNull();
    expect(firstStepWithErrors(ready({ mobile: "" }))).toBe(1);
    expect(firstStepWithErrors({ ...ready({ kinMobile: "1" }), fullName: "" })).toBe(1);
  });
});

describe("the request", () => {
  it("sends the real payment method and reference, and the label only as the application-form label", () => {
    const body = bookingPayload(ready({ received: "500000", accountId: "3", method: "BankTransfer", paymentFor: "Confirmation", reference: "TRX-90117", paidOn: "2026-09-29" }));
    expect(body).toMatchObject({
      applicationAmountReceived: 500_000, applicationFinanceAccountId: 3, applicationPaymentMethod: "BankTransfer",
      applicationPaymentType: "Confirmation", paymentThrough: "TRX-90117", applicationDate: "2026-09-29",
      unitId: 810, customerId: null, bookingAmountRequired: 1_425_599,
    });
  });

  it("sends no payment at all when nothing is received", () => {
    const body = bookingPayload(ready({ accountId: "3", reference: "TRX-1", method: "BankTransfer" }));
    expect(body).toMatchObject({ applicationAmountReceived: null, applicationFinanceAccountId: null, applicationPaymentMethod: null, paymentThrough: null, applicationDate: null });
  });

  it("books under an existing customer by id, with no new-customer details", () => {
    const body = bookingPayload(ready({ customerMode: "existing", pickedCustomer: { id: 5, fullName: "Usman Tariq", phone: "03334412987", cnic: null } }));
    expect(body.customerId).toBe(5);
    expect(body.newCustomer).toBeNull();
  });

  it("carries the discount reason only while there is a discount", () => {
    expect(bookingPayload(ready({ discountPercent: "5", discountReason: "Early booking" })).discountReason).toBe("Early booking");
    expect(bookingPayload(ready({ discountPercent: "0", discountReason: "stale" })).discountReason).toBeNull();
  });
});

describe("leaving and the summary", () => {
  it("counts as dirty only once something changed from where the form started", () => {
    const start = withUnit({ ...emptyDraft(), projectName: "Floria Heights" }, unit);
    expect(isDirty(start, start)).toBe(false);
    expect(isDirty({ ...start, fullName: "H" }, start)).toBe(true);
  });

  it("shows — until a value is known", () => {
    expect(draftSummary(emptyDraft()).every((row) => row.value === "—")).toBe(true);
    const rows = Object.fromEntries(draftSummary(ready()).map((row) => [row.label, row.value]));
    expect(rows).toMatchObject({ Project: "Floria Heights", Unit: "Unit 810 · 1 Bed", Customer: "Hamza Iqbal", "Agreed price": "Rs 14,255,985", "Booking amount": "Rs 1,425,599" });
  });

  it("starts the payment date at today in Pakistan", () => {
    expect(emptyDraft().paidOn).toBe(pakistanToday());
  });
});

describe("Payment for", () => {
  it("keeps the canonical value the printed form ticks, and only shows a friendlier label", async () => {
    const { PAYMENT_FOR } = await import("./draft.ts");
    const { bookingToApplicationForm } = await import("../../utils/bookingToApplicationForm.ts");
    const lump = PAYMENT_FOR.find((option) => option.label === "Lump sum")!;
    expect(lump.value).toBe("LumSum");
    expect(bookingToApplicationForm({ applicationPaymentType: lump.value }).paymentType).toBe("LumSum");
  });
});
