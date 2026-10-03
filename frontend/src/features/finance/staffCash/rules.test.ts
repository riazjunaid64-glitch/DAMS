import { describe, expect, it } from "vitest";
import {
  balanceClass,
  canChangeMovement,
  canReturnCash,
  giveAction,
  holderActivity,
  holderStatus,
  movementChoices,
  movementName,
  movementPlace,
  movementRef,
  movementTitle,
  openSince,
  peopleNote,
  returnAmount,
  signedRupees,
  taxLine,
} from "./rules.ts";
import type { Holder, HistoryItem } from "./types.ts";

const holder = (over: Partial<Holder> = {}): Holder => ({
  financeAccountId: 5, personName: "Adeel Satti", accountName: "Adeel Satti — Staff float", isActive: true, openingBalance: 0,
  currentBalance: 145_000, outstandingSince: "2026-09-19T00:00:00", daysOutstanding: 12, lastActivityDate: null, transactionCount: 6, ...over,
});

const row = (over: Partial<HistoryItem> = {}): HistoryItem => ({
  recordType: "Transfer", recordId: 1, kind: "Money received", date: "2026-09-30T00:00:00", description: "Received from Meezan Bank",
  reference: "CHQ-2240", projectName: null, amount: 60_000, grossAmount: 60_000, whtAmount: 0, runningBalance: 153_600,
  movementType: "FundsGiven", counterpartyFinanceAccountId: 7, counterpartyFinanceAccountName: "Meezan Bank", note: null,
  concurrencyToken: "row-1", attachment: null, ...over,
});

const expense = row({
  recordType: "Expense", recordId: 1, kind: "Expense", description: "Construction materials", reference: "City Hardware",
  projectName: "Floria Heights", amount: -48_000, grossAmount: 50_000, whtAmount: 2_000, movementType: null, concurrencyToken: null,
});

describe("people list wording", () => {
  it("names the three states, in the list and on the panel", () => {
    expect(holderStatus(holder())).toBe("Holding company cash");
    expect(holderStatus(holder({ currentBalance: -40_000 }))).toBe("Company owes them");
    expect(holderStatus(holder({ currentBalance: -40_000 }), "panel")).toBe("Company owes this person");
    expect(holderStatus(holder({ currentBalance: 0 }))).toBe("Settled");
    expect(holderStatus(holder({ currentBalance: 0 }), "panel")).toBe("Settled");
  });

  it("colours the balance by its sign", () => {
    expect(balanceClass(1)).toBe("text-success");
    expect(balanceClass(-1)).toBe("text-danger");
    expect(balanceClass(0)).toBe("text-ink-muted");
  });

  it("counts days and movements, singular when there is one, and no days when settled", () => {
    expect(holderActivity(holder())).toBe("12 days open · 6 movements");
    expect(holderActivity(holder({ daysOutstanding: 1, transactionCount: 1 }))).toBe("1 day open · 1 movement");
    expect(holderActivity(holder({ daysOutstanding: null, transactionCount: 14 }))).toBe("14 movements");
    expect(holderActivity(holder({ daysOutstanding: 0, transactionCount: 2 }))).toBe("0 days open · 2 movements");
  });

  it("says how long the float has been open, or that it is settled", () => {
    expect(openSince(holder())).toBe("Open since Sep 19, 2026 · 12 days");
    expect(openSince(holder({ daysOutstanding: 1 }))).toBe("Open since Sep 19, 2026 · 1 day");
    expect(openSince(holder({ outstandingSince: null, daysOutstanding: null }))).toBe("Fully settled");
  });

  it("counts people for the stat cards", () => {
    expect(peopleNote(1)).toBe("1 person");
    expect(peopleNote(2)).toBe("2 people");
    expect(peopleNote(0)).toBe("0 people");
  });
});

describe("person buttons", () => {
  it("offers Give money, or Settle amount owed with what is owed filled in", () => {
    expect(giveAction(holder())).toEqual({ intent: "give", label: "Give money", amount: "" });
    expect(giveAction(holder({ currentBalance: 0 }))).toEqual({ intent: "give", label: "Give money", amount: "" });
    expect(giveAction(holder({ currentBalance: -40_000.5 }))).toEqual({ intent: "settle", label: "Settle amount owed", amount: "40000.5" });
  });

  it("allows cash back only while the person holds some, filling in what they hold", () => {
    expect(canReturnCash(holder())).toBe(true);
    expect(returnAmount(holder())).toBe("145000");
    expect(canReturnCash(holder({ currentBalance: 0 }))).toBe(false);
    expect(canReturnCash(holder({ currentBalance: -40_000 }))).toBe(false);
    expect(returnAmount(holder({ currentBalance: -40_000 }))).toBe("0");
    expect(canReturnCash(holder({ isActive: false }))).toBe(false);
  });

  it("names the popup after the movement chosen", () => {
    expect(movementTitle("FundsGiven", "give", false)).toBe("Give money");
    expect(movementTitle("FundsGiven", "settle", false)).toBe("Settle amount owed");
    expect(movementTitle("FundsReturned", "return", false)).toBe("Record cash returned");
    expect(movementTitle("FundsReturned", "give", false)).toBe("Record cash returned");
    expect(movementTitle("FundsGiven", "return", true)).toBe("Correct movement");
  });
});

describe("movement choices", () => {
  const values = (choices: { value: string }[]) => choices.map((choice) => choice.value);

  it("offers a return only while the person holds cash, so a settle cannot be switched into one", () => {
    expect(values(movementChoices(holder(), null))).toEqual(["FundsGiven", "FundsReturned"]);
    expect(values(movementChoices(holder({ currentBalance: -40_000 }), null))).toEqual(["FundsGiven"]);
    expect(values(movementChoices(holder({ currentBalance: 0 }), null))).toEqual(["FundsGiven"]);
  });

  it("keeps the return option when a return is being corrected", () => {
    expect(values(movementChoices(holder({ currentBalance: 0 }), "FundsReturned"))).toEqual(["FundsGiven", "FundsReturned"]);
  });
});

describe("movement rows", () => {
  it("shows a money move by its kind, its account and reference", () => {
    expect(movementName(row())).toBe("Money received");
    expect(movementPlace(row())).toBe("Received from Meezan Bank");
    expect(movementRef(row())).toBe("Ref: CHQ-2240");
    expect(movementRef(row({ reference: null }))).toBeNull();
    expect(taxLine(row())).toBeNull();
  });

  it("shows an expense by its category, project or General, payee and the tax withheld", () => {
    expect(movementName(expense)).toBe("Construction materials");
    expect(movementPlace(expense)).toBe("Floria Heights");
    expect(movementPlace({ ...expense, projectName: null })).toBe("General");
    expect(movementRef(expense)).toBe("Ref: City Hardware");
    expect(taxLine(expense)).toBe("Gross Rs 50,000 · tax withheld Rs 2,000");
  });

  it("signs amounts", () => {
    expect(signedRupees(60_000)).toBe("+Rs 60,000");
    expect(signedRupees(-48_000)).toBe("−Rs 48,000");
    expect(signedRupees(-1_234.5)).toBe("−Rs 1,234.5");
  });

  it("lets only an active person's money moves be corrected or deleted", () => {
    expect(canChangeMovement(holder(), row())).toBe(true);
    expect(canChangeMovement(holder(), expense)).toBe(false);
    expect(canChangeMovement(holder({ isActive: false }), row())).toBe(false);
  });
});
