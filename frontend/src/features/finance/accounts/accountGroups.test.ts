import { describe, expect, it } from "vitest";
import {
  accountSubline, accountSubtitle, groupAccounts, isCashLike, openingChangeMessage, openingMismatch, oldestFirst, parseOpening,
  sumAccounts, tableRows, takesNoTypedOpening, typeName, withRunningBalances, type Account, type Transaction,
} from "./accountGroups.ts";

const account = (over: Partial<Account>): Account => ({
  id: 1, name: "Cash in office", type: 1, accountHolderName: "Riaz Junaid", openingBalance: 500_000, ledgerCode: "1001", displayOrder: 0,
  systemRole: 0, isSystemAccount: false, bankOrWalletName: null, description: null, isActive: true, revenueReceived: 2_100_000,
  expensesPaid: 1_350_000, whtWithheld: 0, whtDeposited: 0, netMovement: 750_000, currentBalance: 1_250_000, transactionCount: 4, concurrencyToken: "t", ...over,
});

describe("account types", () => {
  it("reads a type sent as a number or as its name", () => {
    expect(typeName(3)).toBe("Mobile wallet");
    expect(typeName("MobileWallet")).toBe("Mobile wallet");
    expect(typeName("StaffFloat")).toBe("Staff float");
    expect(isCashLike("Bank")).toBe(true);
    expect(isCashLike(10)).toBe(false);
  });

  it("locks the typed opening only for customer deposits, customer receivables and commission payable", () => {
    expect([2, 3, 4, 5, 0, 1].map(takesNoTypedOpening)).toEqual([false, true, true, true, false, false]);
    expect(takesNoTypedOpening("CustomerDeposits")).toBe(true);
    expect(takesNoTypedOpening("CommissionPayable")).toBe(true);
    expect(takesNoTypedOpening("CustomerRefundPayable")).toBe(false);
    expect(takesNoTypedOpening("TaxPayable")).toBe(false);
  });
});

describe("groups and subtotals", () => {
  const rows = [
    account({ id: 1 }),
    account({ id: 2, name: "Meezan Bank", type: "Bank", openingBalance: 12_000_000, revenueReceived: 7_282_660, expensesPaid: 882_660, currentBalance: 18_400_000, isActive: true }),
    account({ id: 3, name: "Capital — Riaz", type: 6, openingBalance: -50_000, revenueReceived: 0, expensesPaid: 0, currentBalance: -50_000 }),
    account({ id: 4, name: "Float", type: 10, openingBalance: 0, revenueReceived: 300_000, expensesPaid: 155_000, currentBalance: 145_000 }),
  ];

  it("lists the groups in board order and leaves empty ones out", () => {
    expect(groupAccounts(rows).map((group) => group.label)).toEqual(["Cash & bank", "Cash held by staff", "Capital"]);
  });

  it("adds up opening, increases, decreases and current balance of the rows shown, negatives included", () => {
    const groups = groupAccounts(rows);
    expect(groups[0]!.totals).toEqual({ openingBalance: 12_500_000, revenueReceived: 9_382_660, expensesPaid: 2_232_660, currentBalance: 19_650_000 });
    expect(groups[2]!.totals.openingBalance).toBe(-50_000);
    expect(sumAccounts([])).toEqual({ openingBalance: 0, revenueReceived: 0, expensesPaid: 0, currentBalance: 0 });
  });

  it("keeps the server's order inside a group", () => {
    expect(groupAccounts(rows)[0]!.rows.map((row) => row.id)).toEqual([1, 2]);
  });

  it("builds heading, accounts and a subtotal row on desktop", () => {
    const built = tableRows(groupAccounts(rows), true);
    expect(built.map((row) => row.kind)).toEqual(["group", "row", "row", "subtotal", "group", "row", "subtotal", "group", "row", "subtotal"]);
    expect(built[3]).toMatchObject({ kind: "subtotal", label: "Total cash & bank" });
    expect(built[6]).toMatchObject({ label: "Total cash held by staff" });
  });

  it("puts the group's current balance on its heading, with no subtotal row, for a phone", () => {
    const built = tableRows(groupAccounts(rows), false);
    expect(built.map((row) => row.kind)).toEqual(["group", "row", "row", "group", "row", "group", "row"]);
    expect(built[0]).toMatchObject({ kind: "group", label: "Cash & bank", total: 19_650_000 });
  });
});

describe("account texts", () => {
  it("writes the ledger code and the bank name only when they are set", () => {
    expect(accountSubline({ ledgerCode: "1002", bankOrWalletName: "Meezan Bank" })).toBe("GL 1002 · Meezan Bank");
    expect(accountSubline({ ledgerCode: null, bankOrWalletName: "Meezan Bank" })).toBe("Meezan Bank");
    expect(accountSubline({ ledgerCode: "1002", bankOrWalletName: null })).toBe("GL 1002");
    expect(accountSubline({ ledgerCode: null, bankOrWalletName: "" })).toBe("");
  });

  it("writes the popup subtitle, with Inactive last", () => {
    expect(accountSubtitle({ type: 2, accountHolderName: "Seven Ventures", ledgerCode: "1002", isActive: true })).toBe("Bank · Seven Ventures · GL 1002");
    expect(accountSubtitle({ type: 2, accountHolderName: "Seven Ventures", ledgerCode: null, isActive: false })).toBe("Bank · Seven Ventures · Inactive");
  });
});

describe("opening balances", () => {
  it("says which side is larger, and by how much", () => {
    expect(openingMismatch({ openingDebitTotal: 920_000, openingCreditTotal: 500_000 })).toBe("Opening balances don't match: debits are Rs 420,000 more than credits.");
    expect(openingMismatch({ openingDebitTotal: 500_000, openingCreditTotal: 920_000 })).toBe("Opening balances don't match: credits are Rs 420,000 more than debits.");
  });

  it("is silent when they match, also across float noise", () => {
    expect(openingMismatch({ openingDebitTotal: 100, openingCreditTotal: 100 })).toBeNull();
    expect(openingMismatch({ openingDebitTotal: 0.1 + 0.2, openingCreditTotal: 0.3 })).toBeNull();
    expect(openingMismatch({ openingDebitTotal: 10.01, openingCreditTotal: 10 })).toBe("Opening balances don't match: debits are Rs 0.01 more than credits.");
  });

  it("reads an empty or half-typed box as 0", () => {
    expect(["", "-", ".", "-.", "12000000", "-250.5"].map(parseOpening)).toEqual([0, 0, 0, 0, 12_000_000, -250.5]);
  });

  it("words the confirm with the go-live date, or without one", () => {
    expect(openingChangeMessage("Meezan Bank", 12_000_000, 12_500_000, "2026-08-15T00:00:00"))
      .toBe("Every report from the go-live date, Aug 15, 2026, will change. Meezan Bank: Rs 12,000,000 → Rs 12,500,000.");
    expect(openingChangeMessage("Meezan Bank", 12_000_000, -50_000, null)).toBe("Every report will change. Meezan Bank: Rs 12,000,000 → -Rs 50,000.");
  });
});

describe("running balance", () => {
  const line = (over: Partial<Transaction>): Transaction => ({
    kind: "Revenue", recordId: 1, date: "2026-09-02T00:00:00", label: "Surcharge", reference: null, projectName: "General", amount: 30_000, grossAmount: 30_000, whtAmount: 0, ...over,
  });

  it("starts from the opening figure and adds each cash effect", () => {
    const lines = withRunningBalances([line({}), line({ recordId: 2, amount: -92_160 }), line({ recordId: 3, amount: 6_852_660 })], 12_000_000);
    expect(lines.map((entry) => entry.balance)).toEqual([12_030_000, 11_937_840, 18_790_500]);
  });

  it("orders oldest first, and by record number inside one day", () => {
    const ordered = oldestFirst([line({ recordId: 5, date: "2026-09-10T00:00:00" }), line({ recordId: 9, date: "2026-09-02T00:00:00" }), line({ recordId: 4, date: "2026-09-02T00:00:00" })]);
    expect(ordered.map((entry) => entry.recordId)).toEqual([4, 9, 5]);
  });
});
