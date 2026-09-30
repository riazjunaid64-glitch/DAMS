import { describe, expect, it } from "vitest";
import { paymentErrors, referenceRequired } from "./paymentForm.ts";

const valid = { amount: "350000", method: "Cash", accountId: "3", reference: "", paidAt: "2026-09-29" };

describe("paymentErrors", () => {
  it("accepts a cash payment with no reference", () => {
    expect(paymentErrors(valid, 350_000)).toEqual({});
  });

  it("requires a reference unless the method is Cash", () => {
    expect(referenceRequired("Cash")).toBe(false);
    for (const method of ["BankTransfer", "Cheque", "Online"]) {
      expect(referenceRequired(method)).toBe(true);
      expect(paymentErrors({ ...valid, method }, 350_000).reference).toBeDefined();
      expect(paymentErrors({ ...valid, method, reference: "CHQ-1" }, 350_000)).toEqual({});
    }
  });

  it("refuses an amount above the limit, and a missing amount, account or date", () => {
    expect(paymentErrors({ ...valid, amount: "350001" }, 350_000).amount).toBe("Can't be more than Rs 350,000");
    expect(paymentErrors({ ...valid, amount: "" }, 350_000).amount).toBeDefined();
    expect(paymentErrors({ ...valid, amount: "0" }, 350_000).amount).toBeDefined();
    expect(paymentErrors({ ...valid, accountId: "" }, 350_000).accountId).toBeDefined();
    expect(paymentErrors({ ...valid, paidAt: "" }, 350_000).paidAt).toBeDefined();
  });
});
