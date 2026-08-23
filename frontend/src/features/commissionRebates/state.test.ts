import { describe, expect, it } from "vitest";
import { commissionActions, idempotencyKey, money, prettyEnum, rebateActions, statusLabel } from "./state";

describe("commission and rebate UI state", () => {
  it("keeps a commission payable until it is fully paid", () => {
    expect(commissionActions("Payable").canPay).toBe(true);
    expect(commissionActions("PartiallyPaid").canPay).toBe(true);
    expect(commissionActions("Paid").canPay).toBe(false);
    expect(commissionActions("Reversed").canPay).toBe(false);
    // Correcting or cancelling stops as soon as any money has gone out; reversing takes over.
    expect(commissionActions("Payable").canEdit).toBe(true);
    expect(commissionActions("Payable").canCancel).toBe(true);
    expect(commissionActions("PartiallyPaid").canCancel).toBe(false);
    expect(commissionActions("PartiallyPaid").canEdit).toBe(false);
    expect(commissionActions("PartiallyPaid").canReverse).toBe(true);
  });

  it("keeps a rebate open until it has all reached the customer", () => {
    expect(rebateActions("Approved").canDisburse).toBe(true);
    expect(rebateActions("PartiallyApplied").canDisburse).toBe(true);
    expect(rebateActions("Applied").canDisburse).toBe(false);
    expect(rebateActions("Paid").canDisburse).toBe(false);
    expect(rebateActions("Approved").canCancel).toBe(true);
    expect(rebateActions("PartiallyApplied").canCancel).toBe(false);
    expect(rebateActions("ReversalRequired").canReverse).toBe(true);
  });

  it("still works records left on the retired approval ladder", () => {
    for (const status of ["Draft","PendingApproval","Approved","Earned"] as const) {
      expect(commissionActions(status).canPay).toBe(true);
      expect(statusLabel(status)).toBe("Pending");
    }
    expect(rebateActions("Draft").canDisburse).toBe(true);
    expect(rebateActions("PendingApproval").canDisburse).toBe(true);
  });

  it("shows one pending label and names the end states", () => {
    expect(statusLabel("Payable")).toBe("Pending");
    expect(statusLabel("PartiallyPaid")).toBe("Pending");
    expect(statusLabel("PartiallyApplied")).toBe("Pending");
    expect(statusLabel("Paid")).toBe("Paid");
    expect(statusLabel("Applied")).toBe("Applied");
    expect(statusLabel("Cancelled")).toBe("Cancelled");
    expect(statusLabel("ReversalRequired")).toBe("Reversal Required");
  });

  it("generates distinct retry keys and readable financial labels", () => {
    expect(idempotencyKey("payout")).not.toBe(idempotencyKey("payout"));
    expect(prettyEnum("OutstandingBalanceReduction")).toBe("Outstanding Balance Reduction");
    expect(money(1234.5)).toContain("1,234.50");
  });
});
