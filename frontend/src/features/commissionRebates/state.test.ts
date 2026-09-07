import { describe, expect, it } from "vitest";
import { commissionActions, commissionAllocationPercent, commissionAttributionFor, commissionBases, commissionBasisFor, commissionRequestBody, commissionRuleFor, idempotencyKey, isPendingStatus, money, prettyEnum, rebateActions, rebateBases, rebateRequestBody, usesStoredBasisAmount } from "./state";
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

  // The form offers three of the five bases. Substituting one of them for a stored
  // ManuallyApprovedAmount turned a 10%-of-Rs-500,000 commission into 10% of the whole net sale
  // price on an edit that only changed a note.
  it("keeps a calculation basis the form cannot offer, and carries its amount with it", () => {
    const manual = {
      ...attributed, calculationType: "Percentage", percentageRate: 10,
      calculationBasis: "ManuallyApprovedAmount", basisAmount: 500_000,
    } as unknown as Commission;
    const percentageForm = { ...form, calculationType: "Percentage" as const, percentageRate: "10" };

    expect(commissionBasisFor(percentageForm, manual)).toBe("ManuallyApprovedAmount");
    const body = commissionRequestBody(percentageForm, manual);
    expect(body.manualCalculationBasis).toBe("ManuallyApprovedAmount");
    expect(body.manualBasisAmount).toBe(500_000);

    const manualRebate = {
      calculationBasis: "ManuallyApprovedAmount", basisAmount: 400_000, method: "CreditNote",
    } as unknown as Rebate;
    const rebateForm = {
      calculationType: "Percentage" as const, calculationBasis: "AgreedSalePrice" as const,
      percentageRate: "5", fixedAmount: "", reason: "Goodwill", changeReason: "Typo",
    };
    const rebateBody = rebateRequestBody(rebateForm, manualRebate);
    expect(rebateBody.calculationBasis).toBe("ManuallyApprovedAmount");
    expect(rebateBody.manualBasisAmount).toBe(400_000);
  });

  it("still lets the operator change a basis the form does offer", () => {
    const supported = {
      ...attributed, calculationType: "Percentage", percentageRate: 2,
      calculationBasis: "NetSalePriceAfterDiscount", basisAmount: 10_000_000,
    } as unknown as Commission;
    const changed = {
      ...form, calculationType: "Percentage" as const, percentageRate: "2",
      calculationBasis: "AgreedSalePrice" as const,
    };
    expect(commissionBasisFor(changed, supported)).toBe("AgreedSalePrice");
    expect(commissionRequestBody(changed, supported).manualBasisAmount).toBeNull();
  });

  // An attribution belongs to one partner. Resubmitting it after an explicit partner change is what
  // the server's ownership check rejects, so the save simply failed.
  it("drops the attribution when the partner is deliberately changed", () => {
    expect(commissionAttributionFor(form, attributed)).toBe(11);
    expect(commissionAllocationPercent(form, attributed)).toBe(25);

    const otherPartner = { ...form, partnerId: "8" };
    expect(commissionAttributionFor(otherPartner, attributed)).toBeNull();
    expect(commissionAllocationPercent(otherPartner, attributed)).toBe(100);
    expect(commissionRequestBody(otherPartner, attributed).attributionId).toBeNull();

    // A new commission never carries one.
    expect(commissionAllocationPercent(form, null)).toBe(100);
  });

  // A rule belongs to the partner it was chosen for. Carrying it across a partner change either
  // fails the server's "does not apply to this booking and partner" check, or pays the new partner
  // on the old one's terms — and skips the ranking that would have found the right rule for them.
  it("drops the rule when the partner is deliberately changed", () => {
    const ruleDriven = {
      id: 4, partnerId: 7, attributionId: null, ruleId: 42, isManual: false,
      allocationPercent: 100, adjustmentAmount: 0, adjustmentReason: null,
      calculationBasis: "NetSalePriceAfterDiscount", basisAmount: 1_000_000, concurrencyToken: "tok",
    } as unknown as Commission;

    expect(commissionRuleFor(form, ruleDriven)).toBe(42);
    expect(commissionRequestBody(form, ruleDriven).ruleId).toBe(42);

    const otherPartner = { ...form, partnerId: "8" };
    expect(commissionRuleFor(otherPartner, ruleDriven)).toBeNull();
    expect(commissionRequestBody(otherPartner, ruleDriven).ruleId).toBeNull();

    // A new commission has no rule to carry, so the server ranks one for the partner chosen.
    expect(commissionRuleFor(form, null)).toBeNull();
  });

  // The preview and the save have to agree about WHICH basis amount is used, or the operator reads
  // one number and stores another. The server re-derives only for a basis the form can re-pick.
  it("previews the stored basis amount for a basis the form cannot re-pick", () => {
    // A rebate on AmountActuallyCollected: not in rebateBases, so the server keeps what was agreed.
    expect(usesStoredBasisAmount("AmountActuallyCollected", "AmountActuallyCollected", rebateBases)).toBe(true);
    // The same basis on a COMMISSION is offered by that form, so the server re-derives it and the
    // preview must follow the live figure.
    expect(usesStoredBasisAmount("AmountActuallyCollected", "AmountActuallyCollected", commissionBases)).toBe(false);
    // Never frozen where the operator genuinely re-picked the basis...
    expect(usesStoredBasisAmount("AgreedSalePrice", "AmountActuallyCollected", rebateBases)).toBe(false);
    // ...and never for a new record, which has no stored amount to freeze.
    expect(usesStoredBasisAmount("AmountActuallyCollected", null, rebateBases)).toBe(false);
    // ManuallyApprovedAmount is in neither list: the amount IS the basis, always its own.
    expect(usesStoredBasisAmount("ManuallyApprovedAmount", "ManuallyApprovedAmount", commissionBases)).toBe(true);
    expect(usesStoredBasisAmount("ManuallyApprovedAmount", "ManuallyApprovedAmount", rebateBases)).toBe(true);
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
