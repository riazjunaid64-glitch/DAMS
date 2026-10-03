import { describe, expect, it } from "vitest";
import {
  activityCsv,
  activityFileName,
  cashAccountChoices,
  filterLoans,
  loanAccountChoices,
  loanLine,
  movementCaption,
  movementDetail,
  rupees,
  signedFigure,
} from "./rules.ts";
import type { Loan, LoanTransaction } from "./types.ts";

const loan = (over: Partial<Loan> = {}): Loan => ({
  id: 1, name: "Bank Alfalah loan", lenderName: "Bank Alfalah", financeAccountId: 30, financeAccountName: "Bank Alfalah loan", isActive: true,
  openingBalance: 0, drawnPrincipal: 5_000_000, repaidPrincipal: 2_000_000, interestPaid: 355_000, currentBalance: 3_000_000,
  transactionCount: 9, concurrencyToken: "tok", ...over,
});

const row = (over: Partial<LoanTransaction> = {}): LoanTransaction => ({
  id: 1, loanId: 1, type: "Repayment", principalAmount: 400_000, interestAmount: 60_000, totalCashMovement: 460_000, date: "2026-02-05T00:00:00",
  financeAccountId: 8, financeAccountName: "HBL Current", reference: null, note: null, runningBalance: 4_600_000,
  createdAt: "", updatedAt: "", concurrencyToken: "r", attachment: null, ...over,
});

describe("loan rules", () => {
  it("names what a repayment was made of", () => {
    expect(movementCaption(row())).toBe("Principal + interest");
    expect(movementCaption(row({ principalAmount: 0 }))).toBe("Interest only");
    expect(movementCaption(row({ interestAmount: 0 }))).toBe("Principal only");
    expect(movementDetail(row({ type: "Drawdown", interestAmount: 0, reference: "BAL-DD-01" }))).toBe("Drawdown · BAL-DD-01");
  });

  it("signs figures with a real minus and leaves zero for a dash", () => {
    expect(signedFigure(-400_000)).toBe("−400,000");
    expect(signedFigure(5_000_000)).toBe("5,000,000");
    expect(signedFigure(0)).toBeNull();
    expect(rupees(3_000_000)).toBe("Rs 3,000,000");
    expect(rupees(-1_500.5)).toBe("−Rs 1,500.5");
  });

  it("names the lender only when there is one", () => {
    expect(loanLine(loan())).toBe("Lender: Bank Alfalah · Account: Bank Alfalah loan");
    expect(loanLine(loan({ lenderName: null }), "Loan account")).toBe("Loan account: Bank Alfalah loan");
  });

  it("filters by name, lender or account, and by status", () => {
    const director = loan({ id: 2, name: "Director loan", lenderName: "Ayub Satti", financeAccountName: "Director loan account", isActive: false });
    const loans = [loan(), director];
    expect(filterLoans(loans, "ayub", "all")).toEqual([director]);
    expect(filterLoans(loans, "director loan account", "all")).toEqual([director]);
    expect(filterLoans(loans, "", "active")).toEqual([loans[0]]);
    expect(filterLoans(loans, "", "closed")).toEqual([director]);
  });

  it("offers a Liability account no other loan holds, and keeps the loan's own one even when inactive", () => {
    const accounts = [
      { id: 30, name: "Bank Alfalah loan", accountHolderName: "Seven Ventures", isActive: false, linkedLoanId: 1, linkedLoanName: "Bank Alfalah loan" },
      { id: 31, name: "Director loan account", accountHolderName: "Seven Ventures", isActive: true, linkedLoanId: 2, linkedLoanName: "Director loan" },
      { id: 32, name: "Car finance", accountHolderName: "Seven Ventures", isActive: true, linkedLoanId: null, linkedLoanName: null },
    ];
    expect(loanAccountChoices(accounts, null, "").map((choice) => choice.value)).toEqual(["32"]);
    expect(loanAccountChoices(accounts, 1, "30")).toEqual([
      { value: "30", label: "Bank Alfalah loan · Seven Ventures (Inactive)" },
      { value: "32", label: "Car finance · Seven Ventures" },
    ]);
    expect(cashAccountChoices([{ id: 9, name: "Old bank", accountHolderName: "X", isActive: false }], "").length).toBe(0);
  });

  it("exports the same signed figures as the screen, guarding typed text against formulas", () => {
    const csv = activityCsv([
      row({ note: "=HYPERLINK(1)" }),
      row({ id: 2, type: "Drawdown", principalAmount: 5_000_000, interestAmount: 0, totalCashMovement: 5_000_000, runningBalance: 5_000_000, reference: "BAL-DD-01" }),
    ]);
    const lines = csv.slice(1).trim().split("\r\n");
    expect(csv.charCodeAt(0)).toBe(0xfeff);
    expect(lines[1]).toBe(`"2026-02-05","Repayment","Principal + interest","HBL Current","","'=HYPERLINK(1)",-400000.00,60000.00,-460000.00,4600000.00`);
    expect(lines[2]).toBe(`"2026-02-05","Funds received","Drawdown","HBL Current","BAL-DD-01","",5000000.00,0.00,5000000.00,5000000.00`);
    expect(activityFileName("Director loan — Ayub Satti")).toBe("director-loan-ayub-satti-activity.csv");
  });
});
