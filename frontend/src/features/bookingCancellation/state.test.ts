import { describe, expect, it } from "vitest";
import { cancelErrors, computeRetained, isStaleCancellationError, payoutErrors, type CancelFields } from "./state";

const filled: CancelFields = { reason: "Moved abroad", decision: "PayNow", refundAmount: "450000", method: "BankTransfer", accountId: "3", reference: "TRX-1" };

describe("booking cancellation form rules", () => {
  it("computes retained amount as paid minus refund, clamped at zero", () => {
    expect(computeRetained(500_000, 450_000)).toBe(50_000);
    expect(computeRetained(500_000, 500_000)).toBe(0);
    expect(computeRetained(500_000, 0)).toBe(500_000);
    expect(computeRetained(0, 0)).toBe(0);
  });

  it("always asks for the reason", () => {
    expect(cancelErrors({ ...filled, reason: "  " }, 500_000).reason).toBeTruthy();
    expect(cancelErrors({ ...filled, reason: "", decision: "None" }, 500_000).reason).toBeTruthy();
    expect(cancelErrors({ ...filled, reason: "", decision: "" }, 0).reason).toBeTruthy();
  });

  it("never defaults a paid customer's refund to zero without an explicit choice", () => {
    expect(cancelErrors({ ...filled, decision: "" }, 500_000).decision).toBeTruthy();
    // Nothing was ever paid: there is nothing to decide.
    expect(cancelErrors({ ...filled, decision: "" }, 0)).toEqual({});
  });

  it("No refund needs nothing more than the reason", () => {
    expect(cancelErrors({ ...filled, decision: "None", refundAmount: "", accountId: "", reference: "" }, 500_000)).toEqual({});
  });

  it("Pay now / Pay later need an amount between 1 and what was paid; zero must be No refund", () => {
    expect(cancelErrors({ ...filled, decision: "PayLater", refundAmount: "" }, 500_000).refundAmount).toBeTruthy();
    expect(cancelErrors({ ...filled, decision: "PayLater", refundAmount: "0" }, 500_000).refundAmount).toBeTruthy();
    expect(cancelErrors({ ...filled, decision: "PayLater", refundAmount: "500001" }, 500_000).refundAmount).toContain("Rs 500,000");
    expect(cancelErrors({ ...filled, decision: "PayLater", refundAmount: "500000", accountId: "", reference: "" }, 500_000)).toEqual({});
  });

  it("Pay now needs an account, and a reference for anything but Cash", () => {
    expect(cancelErrors({ ...filled, accountId: "" }, 500_000).accountId).toBeTruthy();
    expect(cancelErrors({ ...filled, reference: "" }, 500_000).reference).toBeTruthy();
    expect(cancelErrors({ ...filled, method: "Cash", reference: "" }, 500_000)).toEqual({});
    expect(cancelErrors(filled, 500_000)).toEqual({});
  });

  it("payout rules are the same for the Pay refund popup", () => {
    expect(payoutErrors({ method: "Cheque", accountId: "", reference: "" })).toEqual({
      accountId: expect.any(String), reference: expect.any(String),
    });
    expect(payoutErrors({ method: "Cash", accountId: "1", reference: "" })).toEqual({});
  });

  it("recognizes every stale/concurrency error message the backend can return", () => {
    expect(isStaleCancellationError("Customer payments changed while you were cancelling this booking. Reload and review the settlement again.")).toBe(true);
    expect(isStaleCancellationError("The booking changed while you were cancelling it. Refresh and review the settlement again.")).toBe(true);
    expect(isStaleCancellationError("The booking version is missing. Refresh and try again.")).toBe(true);
    expect(isStaleCancellationError("The booking version is invalid. Refresh and try again.")).toBe(true);
    expect(isStaleCancellationError("A refund source account is required when paying the refund now.")).toBe(false);
  });
});
