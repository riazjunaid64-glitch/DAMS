import { describe, expect, it } from "vitest";
import {
  accountAndReference, activeShareTotal, capitalAccountChoices, formatShare, noteToShow, runningBalance, serverDay,
  sharesAddUp, showDay, statementRangeError, statementSubtitle, toNumber, typeLabel,
} from "./rules.ts";
import type { AccountOption, Partner, Transaction } from "./types.ts";

const move = (over: Partial<Transaction>): Transaction => ({
  id: 1, type: "Contribution", amount: 100, date: "2026-07-01T00:00:00", financeAccountId: null, reference: null, note: null,
  profitSharePercentSnapshot: null, attachment: null, ...over,
});
const partner = (over: Partial<Partner>): Partner => ({
  id: 1, name: "Riaz", cnic: null, ntn: null, profitSharePercent: 40, financeAccountId: null, financeAccountName: null, isActive: true,
  joinedDate: null, exitedDate: null, openingBalance: 0, contributions: 0, withdrawals: 0, profitShare: 0, lossShare: 0, closingBalance: 0,
  concurrencyToken: "t", ...over,
});
const account = (over: Partial<AccountOption>): AccountOption => ({ id: 1, name: "Capital — A", accountHolderName: "A", isActive: true, type: "Capital", ...over });

describe("share total", () => {
  it("adds up only the partners that are switched on", () => {
    expect(activeShareTotal([
      { isActive: true, share: 40 }, { isActive: true, share: 25 }, { isActive: true, share: 20 }, { isActive: true, share: 10 }, { isActive: false, share: 5 },
    ])).toBe(95);
  });

  it("does not show float dust", () => {
    expect(activeShareTotal([{ isActive: true, share: 0.1 }, { isActive: true, share: 0.2 }])).toBe(0.3);
  });

  it("is exactly 100 within the server's 0.01 tolerance and not beyond it", () => {
    expect(sharesAddUp(100)).toBe(true);
    expect(sharesAddUp(99.99)).toBe(true);
    expect(sharesAddUp(100.01)).toBe(true);
    expect(sharesAddUp(99.9899)).toBe(false);
    expect(sharesAddUp(100.0101)).toBe(false);
    expect(sharesAddUp(95)).toBe(false);
  });

  it("reads an empty or half-typed box as 0", () => {
    expect(toNumber("")).toBe(0);
    expect(toNumber(".")).toBe(0);
    expect(toNumber("14.9999")).toBe(14.9999);
  });

  it("shows a share with up to 4 decimals", () => {
    expect(formatShare(40)).toBe("40%");
    expect(formatShare(14.9999)).toBe("14.9999%");
    expect(formatShare(33.33333)).toBe("33.3333%");
  });
});

describe("running balance", () => {
  it("adds contributions and profit share and subtracts withdrawals and loss share", () => {
    const lines = runningBalance(0, [
      move({ id: 1, type: "Contribution", amount: 3_000_000 }),
      move({ id: 2, type: "Contribution", amount: 1_000_000 }),
      move({ id: 3, type: "Withdrawal", amount: 100_000 }),
      move({ id: 4, type: "ProfitShare", amount: 416_660 }),
      move({ id: 5, type: "LossShare", amount: 60 }),
    ]);
    expect(lines.map((line) => line.balance)).toEqual([3_000_000, 4_000_000, 3_900_000, 4_316_660, 4_316_600]);
    expect(lines.map((line) => line.signed)).toEqual([3_000_000, 1_000_000, -100_000, 416_660, -60]);
  });

  it("starts from the opening figure, including a negative one", () => {
    expect(runningBalance(-50, [move({ amount: 20 })])[0]!.balance).toBe(-30);
  });

  it("rounds to 2 decimals after every step so the last line equals the closing balance", () => {
    const lines = runningBalance(0, [move({ amount: 0.1 }), move({ id: 2, amount: 0.2 }), move({ id: 3, type: "Withdrawal", amount: 0.3 })]);
    expect(lines.map((line) => line.balance)).toEqual([0.1, 0.3, 0]);
  });

  it("treats the go-live Opening balance row as money in", () => {
    expect(runningBalance(0, [move({ type: "OpeningBalance", amount: 500 })])[0]!.balance).toBe(500);
  });

  it("is empty for no movements", () => {
    expect(runningBalance(10, [])).toEqual([]);
  });
});

describe("statement range", () => {
  it("refuses a From date after the To date", () => {
    expect(statementRangeError("2026-10-02", "2026-10-01")).toBe("The From date cannot be after the To date.");
  });

  it("accepts equal dates, and an empty side", () => {
    expect(statementRangeError("2026-10-01", "2026-10-01")).toBeNull();
    expect(statementRangeError("2026-10-01", "")).toBeNull();
    expect(statementRangeError("", "2026-10-01")).toBeNull();
    expect(statementRangeError("", "")).toBeNull();
  });
});

describe("statement dates", () => {
  it("reads the day out of the server text without a time-zone parse", () => {
    expect(serverDay("2026-09-30T00:00:00")).toBe("2026-09-30");
    expect(showDay("2026-09-30T00:00:00")).toBe("Sep 30, 2026");
    expect(showDay("2026-01-01")).toBe("Jan 1, 2026");
  });

  it("is empty when there is no date", () => {
    expect(serverDay(null)).toBeNull();
    expect(showDay(undefined)).toBe("");
    expect(showDay("not a date")).toBe("");
  });
});

describe("statement lines", () => {
  const names = new Map([[7, "Meezan Bank"]]);

  it("joins the account name and the reference, leaving out what is empty", () => {
    expect(accountAndReference(move({ financeAccountId: 7, reference: "CH-1001" }), names)).toBe("Meezan Bank · CH-1001");
    expect(accountAndReference(move({ financeAccountId: 7 }), names)).toBe("Meezan Bank");
    expect(accountAndReference(move({ reference: "CH-1" }), names)).toBe("CH-1");
    expect(accountAndReference(move({ financeAccountId: 99, reference: " " }), names)).toBe("");
  });

  it("hides a note that only repeats the type name", () => {
    expect(noteToShow(move({ type: "Contribution", note: "contribution" }))).toBeNull();
    expect(noteToShow(move({ type: "ProfitShare", note: "Profit share" }))).toBeNull();
    expect(noteToShow(move({ type: "ProfitShare", note: "ProfitShare" }))).toBeNull();
    expect(noteToShow(move({ type: "LossShare", note: " loss  share " }))).toBeNull();
    expect(noteToShow(move({ note: "Second instalment of agreed capital" }))).toBe("Second instalment of agreed capital");
    expect(noteToShow(move({ note: "  " }))).toBeNull();
  });

  it("names the types", () => {
    expect(typeLabel("ProfitShare")).toBe("Profit share");
    expect(typeLabel("LossShare")).toBe("Loss share");
    expect(typeLabel("OpeningBalance")).toBe("Opening balance");
    expect(typeLabel("Contribution")).toBe("Contribution");
  });

  it("says the share and the account in the header, and the share alone on a phone", () => {
    const riaz = partner({ profitSharePercent: 40, financeAccountName: "Capital — Riaz Junaid" });
    expect(statementSubtitle(riaz, false)).toBe("40% share · Capital — Riaz Junaid");
    expect(statementSubtitle(riaz, true)).toBe("40% share");
    expect(statementSubtitle(partner({ profitSharePercent: 14.9999 }), false)).toBe("14.9999% share");
  });
});

describe("capital account choices", () => {
  const accounts = [
    account({ id: 1, name: "Capital — Riaz" }),
    account({ id: 2, name: "Capital — Adeel" }),
    account({ id: 3, name: "Capital — Old", isActive: false }),
    account({ id: 4, name: "Capital — Numeric", type: 6 }),
    account({ id: 5, name: "Cash box", type: "Cash" }),
  ];

  it("lists active Capital accounts that no other partner uses", () => {
    const partners = [partner({ id: 10, financeAccountId: 1 })];
    expect(capitalAccountChoices(accounts, partners, null).map((choice) => choice.label)).toEqual(["Capital — Adeel", "Capital — Numeric"]);
  });

  it("keeps the partner's own account, marked Inactive when it is", () => {
    const own = partner({ id: 10, financeAccountId: 3 });
    const choices = capitalAccountChoices(accounts, [own, partner({ id: 11, financeAccountId: 1 })], own);
    expect(choices.map((choice) => choice.label)).toEqual(["Capital — Adeel", "Capital — Old (Inactive)", "Capital — Numeric"]);
  });

  it("is empty when there is no Capital account at all", () => {
    expect(capitalAccountChoices([account({ id: 5, type: "Cash" })], [], null)).toEqual([]);
  });
});
