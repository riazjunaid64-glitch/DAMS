import { describe, expect, it } from "vitest";
import { computeRetained, idempotencyKey, isStaleCancellationError, refundDecisionLabel, refundStatusLabel, validateCancellationDecision } from "./state";

describe("booking cancellation settlement UI state", () => {
  it("computes retained amount as paid minus refund, clamped at zero", () => {
    expect(computeRetained(500_000, 450_000)).toBe(50_000);
    expect(computeRetained(500_000, 500_000)).toBe(0);
    expect(computeRetained(500_000, 0)).toBe(500_000);
    expect(computeRetained(0, 0)).toBe(0);
  });

  it("requires a non-negative refund that never exceeds cash received", () => {
    expect(validateCancellationDecision({ cashReceived: 500_000, refundAmount: -1, decision: "" })).toContain("negative");
    expect(validateCancellationDecision({ cashReceived: 500_000, refundAmount: 600_000, decision: "PayLater" })).toContain("cannot exceed");
  });

  it("requires RefundDecision None when refund is zero, and rejects a stray decision", () => {
    expect(validateCancellationDecision({ cashReceived: 500_000, refundAmount: 0, decision: "None" })).toBeNull();
    expect(validateCancellationDecision({ cashReceived: 500_000, refundAmount: 0, decision: "PayNow" })).toContain("must be None");
  });

  it("never defaults a paid customer's refund to zero without an explicit decision", () => {
    // Money was received but the Admin hasn't chosen anything yet — must block, not silently retain 100%.
    expect(validateCancellationDecision({ cashReceived: 500_000, refundAmount: 0, decision: "" })).toContain("Confirm the refund decision");
    // Nothing was ever paid — there is nothing to decide, so an unmade decision is fine.
    expect(validateCancellationDecision({ cashReceived: 0, refundAmount: 0, decision: "" })).toBeNull();
  });

  it("requires an explicit Pay now / Pay later choice once a refund is entered", () => {
    expect(validateCancellationDecision({ cashReceived: 500_000, refundAmount: 450_000, decision: "" })).toContain("Choose whether");
    expect(validateCancellationDecision({ cashReceived: 500_000, refundAmount: 450_000, decision: "PayLater" })).toBeNull();
  });

  it("requires an account and method for Pay now, and a reference for non-cash", () => {
    expect(validateCancellationDecision({ cashReceived: 500_000, refundAmount: 450_000, decision: "PayNow" }))
      .toContain("source account is required");
    expect(validateCancellationDecision({
      cashReceived: 500_000, refundAmount: 450_000, decision: "PayNow", refundFinanceAccountId: 1, refundPaymentMethod: "Cash",
    })).toBeNull();
    expect(validateCancellationDecision({
      cashReceived: 500_000, refundAmount: 450_000, decision: "PayNow", refundFinanceAccountId: 1,
      refundPaymentMethod: "BankTransfer", refundPaymentReference: "",
    })).toContain("reference is required");
    expect(validateCancellationDecision({
      cashReceived: 500_000, refundAmount: 450_000, decision: "PayNow", refundFinanceAccountId: 1,
      refundPaymentMethod: "BankTransfer", refundPaymentReference: "TXN-1",
    })).toBeNull();
  });

  it("labels statuses and decisions for display", () => {
    expect(refundStatusLabel("NotRequired")).toBe("No refund required");
    expect(refundStatusLabel("Pending")).toBe("Pending");
    expect(refundStatusLabel("Paid")).toBe("Paid");
    expect(refundDecisionLabel("PayNow")).toBe("Pay now");
    expect(refundDecisionLabel("PayLater")).toBe("Pay later");
    expect(refundDecisionLabel("None")).toBe("None");
  });

  it("generates distinct idempotency keys per cancellation operation", () => {
    expect(idempotencyKey("cancel")).not.toBe(idempotencyKey("cancel"));
  });

  it("recognizes every stale/concurrency error message the backend can return", () => {
    expect(isStaleCancellationError("Customer payments changed while you were cancelling this booking. Reload and review the settlement again.")).toBe(true);
    expect(isStaleCancellationError("The booking changed while you were cancelling it. Refresh and review the settlement again.")).toBe(true);
    expect(isStaleCancellationError("The booking version is missing. Refresh and try again.")).toBe(true);
    expect(isStaleCancellationError("The booking version is invalid. Refresh and try again.")).toBe(true);
    expect(isStaleCancellationError("A refund source account is required when paying the refund now.")).toBe(false);
  });
});
