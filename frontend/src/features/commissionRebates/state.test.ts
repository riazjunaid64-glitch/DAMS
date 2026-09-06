import { describe, expect, it } from "vitest";
import { commissionActions, commissionRequestBody, idempotencyKey, isPendingStatus, money, prettyEnum, rebateActions, rebateRequestBody } from "./state";
import type { Commission, Rebate } from "./types";

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

  // A payout that has been recorded and then fully reversed leaves a movement row behind and a net
  // of zero. Editing is off — the server refuses it because the payout's figures were struck against
  // the amount being rewritten — but cancelling is on, and the card used to hide both, leaving the
  // commission with no way to be closed at all.
  it("keeps Cancel available after a payout is fully reversed, and still refuses Edit", () => {
    const afterFullReversal = commissionActions("Pending", 0, 1);
    expect(afterFullReversal.canCancel).toBe(true);
    expect(afterFullReversal.canEdit).toBe(false);
    expect(commissionActions("Pending", 0, 0).canEdit).toBe(true);

    const rebateAfterFullReversal = rebateActions("Pending", 0, 1);
    expect(rebateAfterFullReversal.canCancel).toBe(true);
    expect(rebateAfterFullReversal.canEdit).toBe(false);
    expect(rebateActions("Pending", 0, 0).canEdit).toBe(true);
  });
});

describe("commission and rebate update bodies", () => {
  const form = {
    partnerId: "7",
    calculationType: "FixedAmount" as const,
    calculationBasis: "NetSalePriceAfterDiscount" as const,
    percentageRate: "",
    fixedAmount: "1000",
    notes: "Corrected note",
    changeReason: "Note only",
  };

  const attributed = {
    id: 3, partnerId: 7, attributionId: 11, ruleId: null, isManual: true,
    allocationPercent: 25, adjustmentAmount: -50, adjustmentReason: "Agreed haircut",
    concurrencyToken: "tok",
  } as unknown as Commission;

  it("keeps the attribution and the adjustment when a commission is edited", () => {
    const body = commissionRequestBody(form, attributed);
    // The bug: sending attributionId null dropped the 25% allocation, so a Rs 250 quarter-share
    // silently became the full Rs 1,000 on an edit that only changed a note.
    expect(body.attributionId).toBe(11);
    expect(body.adjustmentAmount).toBe(-50);
    expect(body.adjustmentReason).toBe("Agreed haircut");
    expect(body).toMatchObject({ concurrencyToken: "tok", changeReason: "Note only" });
  });

  it("does not rewrite a rule-driven commission as a manual one", () => {
    const ruleDriven = { ...attributed, isManual: false, ruleId: 4 } as unknown as Commission;
    const body = commissionRequestBody(form, ruleDriven);
    expect(body.isManual).toBe(false);
    expect(body.ruleId).toBe(4);
    // The rule owns the figures; nothing manual is sent for the server to try to honour.
    expect(body.manualCalculationType).toBeNull();
    expect(body.manualFixedAmount).toBeNull();
    expect(body.manualReason).toBeNull();
  });

  it("creates a manual commission with no attribution, rule or adjustment", () => {
    const body = commissionRequestBody(form, null);
    expect(body.isManual).toBe(true);
    expect(body.attributionId).toBeNull();
    expect(body.ruleId).toBeNull();
    expect(body.adjustmentAmount).toBe(0);
    expect(body.manualFixedAmount).toBe(1000);
    expect(body).not.toHaveProperty("concurrencyToken");
  });

  it("keeps a rebate's adjustment, notes and method when it is edited", () => {
    const rebateForm = {
      calculationType: "FixedAmount" as const,
      calculationBasis: "AgreedSalePrice" as const,
      percentageRate: "", fixedAmount: "5000", reason: "Goodwill", changeReason: "Typo",
    };
    const existing = {
      adjustmentAmount: 250, adjustmentReason: "Agreed uplift", notes: "Approved by MD",
      method: "CreditNote", concurrencyToken: "rtok",
    } as unknown as Rebate;

    const body = rebateRequestBody(rebateForm, existing);
    expect(body.adjustmentAmount).toBe(250);
    expect(body.adjustmentReason).toBe("Agreed uplift");
    expect(body.notes).toBe("Approved by MD");
    expect(body.method).toBe("CreditNote");

    const created = rebateRequestBody(rebateForm, null);
    expect(created.adjustmentAmount).toBe(0);
    expect(created.notes).toBeNull();
    expect(created.method).toBe("OutstandingBalanceReduction");
  });
});
