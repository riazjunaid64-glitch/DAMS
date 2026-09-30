import { describe, expect, it } from "vitest";
import { paymentFor } from "./paymentRows.ts";

describe("paymentFor", () => {
  it("says what each payment was for", () => {
    expect(paymentFor({ type: "BookingAmount" })).toBe("Booking amount");
    expect(paymentFor({ type: "Installment", installmentSequence: 4, installmentType: "Regular" })).toBe("Installment 4");
    expect(paymentFor({ type: "Installment", installmentSequence: 0, installmentType: "Possession" })).toBe("Possession");
  });

  it("falls back to plain Installment when the row is gone or unnamed", () => {
    expect(paymentFor({ type: "Installment" })).toBe("Installment");
  });
});
