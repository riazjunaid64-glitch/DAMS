import { describe, expect, it } from "vitest";
import { commissionActions, idempotencyKey, isPendingStatus, money, prettyEnum, rebateActions } from "./state";

describe("commission and rebate UI state", () => {
  it("keeps a commission pending until it is fully paid", () => {
    expect(commissionActions("Pending").canPay).toBe(true);
    expect(commissionActions("Pending", 500).canPay).toBe(true);
    expect(commissionActions("Paid").canPay).toBe(false);
    expect(commissionActions("Reversed").canPay).toBe(false);
    expect(commissionActions("Cancelled").canPay).toBe(false);
  });

  it("stops correcting and cancelling a commission once any of it has been paid", () => {
    expect(commissionActions("Pending").canEdit).toBe(true);
    expect(commissionActions("Pending").canCancel).toBe(true);
    expect(commissionActions("Pending").canReverse).toBe(false);
    // Part paid: the agreement is now history, and reversing is the way back.
    expect(commissionActions("Pending", 500).canEdit).toBe(false);
    expect(commissionActions("Pending", 500).canCancel).toBe(false);
    expect(commissionActions("Pending", 500).canReverse).toBe(true);
    expect(commissionActions("Paid", 1000).canReverse).toBe(true);
    expect(commissionActions("ReversalRequired").canReverse).toBe(true);
  });

  it("keeps a rebate pending until it has all reached the customer", () => {
    expect(rebateActions("Pending").canDisburse).toBe(true);
    expect(rebateActions("Pending", 250).canDisburse).toBe(true);
    expect(rebateActions("Applied").canDisburse).toBe(false);
    expect(rebateActions("Paid").canDisburse).toBe(false);
    expect(rebateActions("Pending").canCancel).toBe(true);
    expect(rebateActions("Pending", 250).canCancel).toBe(false);
    expect(rebateActions("Pending", 250).canReverse).toBe(true);
    expect(rebateActions("ReversalRequired").canReverse).toBe(true);
  });

  it("reads every status back as a plain label", () => {
    expect(isPendingStatus("Pending")).toBe(true);
    expect(isPendingStatus("Paid")).toBe(false);
    expect(prettyEnum("Pending")).toBe("Pending");
    expect(prettyEnum("ReversalRequired")).toBe("Reversal Required");
  });

  it("generates distinct retry keys and readable financial labels", () => {
    expect(idempotencyKey("payout")).not.toBe(idempotencyKey("payout"));
    expect(prettyEnum("OutstandingBalanceReduction")).toBe("Outstanding Balance Reduction");
    expect(money(1234.5)).toContain("1,234.50");
  });
});
