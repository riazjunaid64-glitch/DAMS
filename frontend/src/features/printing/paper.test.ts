import { describe, expect, it } from "vitest";
import { bookingToApplicationForm } from "../../utils/bookingToApplicationForm.ts";
import { modeOfPayment, paymentMethodLabel, receiptSubtitle } from "./paper.ts";

describe("the receipt paper", () => {
  it("ticks the box for the real payment method", () => {
    expect(modeOfPayment("Cash")).toBe("Cash");
    expect(modeOfPayment("Cheque")).toBe("Cheque");
    expect(modeOfPayment("BankTransfer")).toBe("Transfer");
    expect(modeOfPayment("Online")).toBe("Transfer");
  });

  it("says what was paid under the title", () => {
    const base = { amount: 918_000, paidAt: "2026-08-26T00:00:00" };
    expect(receiptSubtitle({ ...base, type: "Installment", installmentSequence: 4 })).toBe("Installment 4 · Rs 918,000 · Aug 26, 2026");
    expect(receiptSubtitle({ ...base, type: "BookingAmount" })).toBe("Booking amount · Rs 918,000 · Aug 26, 2026");
    expect(receiptSubtitle({ ...base, type: "Installment", installmentType: "Possession" })).toBe("Possession payment · Rs 918,000 · Aug 26, 2026");
  });
});

describe("the application form paper", () => {
  const booking = {
    bookingReference: "BK-000013",
    bookingDate: "2026-04-20T00:00:00",
    bookingAmountRequired: 500_000,
    bookingAmountReceived: 350_000,
    applicationAmountReceived: 200_000,
    applicationPaymentType: "Booking",
    applicationPaymentMethod: "BankTransfer",
  };

  it("shows the booking number when the serial was left as Auto", () => {
    expect(bookingToApplicationForm({ ...booking, serialNo: "Auto" }).serialNo).toBe("BK-000013");
    expect(bookingToApplicationForm({ ...booking, serialNo: " auto " }).serialNo).toBe("BK-000013");
    expect(bookingToApplicationForm({ ...booking, serialNo: null }).serialNo).toBe("BK-000013");
    expect(bookingToApplicationForm({ ...booking, serialNo: "FH-77" }).serialNo).toBe("FH-77");
  });

  it("works the remaining down payment out from everything received toward the booking amount", () => {
    const form = bookingToApplicationForm(booking);
    expect(form.totalDownPayment).toBe(500_000);
    expect(form.remainingDownPayment).toBe(150_000);
    expect(bookingToApplicationForm({ ...booking, bookingAmountReceived: 600_000 }).remainingDownPayment).toBe(0);
  });

  it("says Through with the real payment method, not Cash", () => {
    expect(bookingToApplicationForm(booking).through).toBe("Bank transfer");
    expect(bookingToApplicationForm({ ...booking, applicationPaymentMethod: "Cheque" }).through).toBe("Cheque");
    expect(bookingToApplicationForm({ ...booking, applicationPaymentMethod: null }).through).toBe("");
    expect(paymentMethodLabel("Online")).toBe("Online");
  });
});
