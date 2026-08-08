import { describe, expect, it } from "vitest";
import { commissionActions, idempotencyKey, money, prettyEnum, rebateActions } from "./state";

describe("commission and rebate UI state", () => {
  it("shows only lifecycle-valid commission actions", () => {
    expect(commissionActions("Draft").canSubmit).toBe(true);
    expect(commissionActions("Draft").canCancel).toBe(true);
    expect(commissionActions("Draft").canPay).toBe(false);
    expect(commissionActions("PendingApproval").canApprove).toBe(true);
    expect(commissionActions("Approved").canEarn).toBe(true);
    expect(commissionActions("Earned").canMakePayable).toBe(true);
    expect(commissionActions("Payable").canPay).toBe(true);
    expect(commissionActions("PartiallyPaid").canPay).toBe(true);
    expect(commissionActions("PartiallyPaid").canCancel).toBe(false);
    expect(commissionActions("Reversed").canPay).toBe(false);
  });

  it("keeps rebate application separate from approval", () => {
    expect(rebateActions("Draft").canSubmit).toBe(true);
    expect(rebateActions("PendingApproval").canApprove).toBe(true);
    expect(rebateActions("Approved").canDisburse).toBe(true);
    expect(rebateActions("Approved").canCancel).toBe(true);
    expect(rebateActions("PartiallyApplied").canDisburse).toBe(true);
    expect(rebateActions("Paid").canDisburse).toBe(false);
    expect(rebateActions("Paid").canCancel).toBe(false);
    expect(rebateActions("ReversalRequired").canReverse).toBe(true);
  });

  it("generates distinct retry keys and readable financial labels", () => {
    expect(idempotencyKey("payout")).not.toBe(idempotencyKey("payout"));
    expect(prettyEnum("OutstandingBalanceReduction")).toBe("Outstanding Balance Reduction");
    expect(money(1234.5)).toContain("1,234.50");
  });
});
