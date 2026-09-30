import { describe, expect, it } from "vitest";
import { applyRebateErrors, commissionErrors, commissionPreview, commissionSummary, isMissingBankDetails, newMovementId, paymentOutErrors, rebateErrors, rebatePreview, remainingAfterReversal } from "./forms";
import type { CommissionFormState, RebateFormState } from "./state";
import type { Commission, MoneyMovement, Rebate } from "./types";

const figures = { agreedSalePrice: 13_000_000, netSalePrice: 12_800_000, amountCollected: 4_000_000 };
const commissionForm: CommissionFormState = { partnerId: "7", calculationType: "Percentage", calculationBasis: "NetSalePriceAfterDiscount", percentageRate: "2", fixedAmount: "", notes: "" };
const rebateForm: RebateFormState = { calculationType: "FixedAmount", calculationBasis: "NetSalePriceAfterDiscount", percentageRate: "", fixedAmount: "200000", reason: "", method: "CashOrBankPayment" };

describe("commission popup", () => {
  it("works a percentage out live on the chosen basis", () => {
    expect(commissionPreview(commissionForm, null, figures)).toBe(256_000);
    expect(commissionPreview({ ...commissionForm, calculationBasis: "AmountActuallyCollected" }, null, figures)).toBe(80_000);
    expect(commissionPreview({ ...commissionForm, calculationType: "FixedAmount", fixedAmount: "50000" }, null, figures)).toBe(50_000);
  });

  it("asks for a partner and for the figure of the chosen type", () => {
    expect(commissionErrors({ ...commissionForm, partnerId: "" }, null, figures).partnerId).toBeTruthy();
    expect(commissionErrors({ ...commissionForm, percentageRate: "" }, null, figures).percentageRate).toBeTruthy();
    expect(commissionErrors({ ...commissionForm, percentageRate: "101" }, null, figures).percentageRate).toBeTruthy();
    expect(commissionErrors({ ...commissionForm, calculationType: "FixedAmount" }, null, figures).fixedAmount).toBeTruthy();
    expect(commissionErrors(commissionForm, null, figures)).toEqual({});
  });

  it("never lets the total fall below what is already paid", () => {
    const paid = { partnerId: 7, isManual: true, paidAmount: 100_000, adjustmentAmount: 0, allocationPercent: 100, attributionId: null, calculationBasis: "NetSalePriceAfterDiscount" } as unknown as Commission;
    expect(commissionErrors({ ...commissionForm, percentageRate: "0.5" }, paid, figures).percentageRate).toContain("Rs 100,000");
    expect(commissionErrors({ ...commissionForm, percentageRate: "2" }, paid, figures)).toEqual({});
  });

  it("leaves a rule-driven commission's figures alone", () => {
    const ruleDriven = { partnerId: 7, isManual: false, paidAmount: 0 } as unknown as Commission;
    expect(commissionErrors({ ...commissionForm, percentageRate: "" }, ruleDriven, figures)).toEqual({});
  });

  it("reads 'Agency · 2% of net sale price' and 'Referral partner · Fixed amount'", () => {
    expect(commissionSummary({ partnerType: "Agency", calculationType: "Percentage", percentageRate: 2, calculationBasis: "NetSalePriceAfterDiscount" })).toBe("Agency · 2% of net sale price");
    expect(commissionSummary({ partnerType: "Referral Partner", calculationType: "FixedAmount", percentageRate: null, calculationBasis: "NetSalePriceAfterDiscount" })).toBe("Referral partner · Fixed amount");
  });
});

describe("rebate popup", () => {
  it("shows the net price after the rebate", () => {
    expect(rebatePreview(rebateForm, null, figures)).toEqual({ amount: 200_000, final: 200_000 });
    expect(rebatePreview({ ...rebateForm, calculationType: "Percentage", percentageRate: "5" }, null, figures).final).toBe(640_000);
  });

  it("asks for the figure, caps it at the net price, and keeps it at or above what is given", () => {
    expect(rebateErrors({ ...rebateForm, fixedAmount: "" }, null, figures).fixedAmount).toBeTruthy();
    expect(rebateErrors({ ...rebateForm, fixedAmount: "13000000" }, null, figures).fixedAmount).toContain("net sale price");
    const given = { appliedOrPaidAmount: 50_000, adjustmentAmount: 0, calculationBasis: "NetSalePriceAfterDiscount" } as unknown as Rebate;
    expect(rebateErrors({ ...rebateForm, fixedAmount: "40000" }, given, figures).fixedAmount).toContain("Rs 50,000");
    expect(rebateErrors(rebateForm, given, figures)).toEqual({});
  });
});

describe("paying out", () => {
  const fields = { amount: "156000", method: "BankTransfer", accountId: "3", reference: "TRX-1", date: "2026-09-29" };

  it("needs an amount within what remains, an account, a date, and a reference unless Cash", () => {
    expect(paymentOutErrors(fields, 156_000)).toEqual({});
    expect(paymentOutErrors({ ...fields, amount: "156001" }, 156_000).amount).toBeTruthy();
    expect(paymentOutErrors({ ...fields, accountId: "" }, 156_000).accountId).toBeTruthy();
    expect(paymentOutErrors({ ...fields, reference: "" }, 156_000).reference).toBeTruthy();
    expect(paymentOutErrors({ ...fields, method: "Cash", reference: "" }, 156_000)).toEqual({});
    expect(paymentOutErrors({ ...fields, date: "" }, 156_000).date).toBeTruthy();
  });

  it("asks only for what the way the customer gets the rebate needs", () => {
    const apply = { ...fields, amount: "150000", installmentId: "", notes: "" };
    expect(applyRebateErrors("CashOrBankPayment", apply, 150_000)).toEqual({});
    expect(applyRebateErrors("CashOrBankPayment", { ...apply, accountId: "" }, 150_000).accountId).toBeTruthy();
    // Off an installment: the installment, an amount and a date; no account.
    const off = { ...apply, accountId: "", reference: "" };
    expect(applyRebateErrors("InstallmentAdjustment", off, 150_000).installmentId).toBeTruthy();
    expect(applyRebateErrors("InstallmentAdjustment", { ...off, installmentId: "5" }, 150_000)).toEqual({});
    // Off the balance: an amount and a date only, never more than the balance allows.
    expect(applyRebateErrors("OutstandingBalanceReduction", off, 150_000)).toEqual({});
    expect(applyRebateErrors("OutstandingBalanceReduction", { ...off, amount: "200000" }, 150_000).amount).toBeTruthy();
    expect(applyRebateErrors("CreditNote", off, 150_000).reference).toBeTruthy();
    expect(applyRebateErrors("Other", off, 150_000).notes).toBeTruthy();
  });
});

describe("after a save", () => {
  const row = (id: number) => ({ id, amount: 0, reversedAmount: 0 }) as unknown as MoneyMovement;

  it("finds the payment that was just created, so the proof lands on it", () => {
    expect(newMovementId([row(1)], [row(2), row(1)])).toBe(2);
    expect(newMovementId([row(1)], [row(1)])).toBeNull();
  });

  it("says what will remain once a payment is reversed", () => {
    expect(remainingAfterReversal(156_000, { amount: 100_000, reversedAmount: 0 })).toBe(256_000);
    expect(remainingAfterReversal(156_000, { amount: 100_000, reversedAmount: 40_000 })).toBe(216_000);
  });

  it("recognises the server's missing-bank-details message", () => {
    expect(isMissingBankDetails("Bank-transfer payouts require the partner's bank name, account title, and account number or IBAN.")).toBe(true);
    expect(isMissingBankDetails("This payment reference is already recorded against the selected finance account.")).toBe(false);
  });
});
