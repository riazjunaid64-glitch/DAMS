import { describe, expect, it } from "vitest";
import type { WhtDeposit, WhtVendorLine } from "../whtTypes.ts";
import {
  deleteMessage,
  deleteTitle,
  depositBody,
  depositErrors,
  depositFields,
  depositReady,
  periodCovered,
  sectionLabel,
  supplierKey,
  supplierTaxId,
  suppliersText,
} from "./rules.ts";

const deposit = (over: Partial<WhtDeposit> = {}): WhtDeposit => ({
  id: 7, financeAccountId: 3, financeAccountName: "Meezan Bank", amount: 412_000, depositDate: "2026-09-15T00:00:00",
  challanNumber: "CPR-0923-118", periodFrom: "2026-08-01T00:00:00", periodTo: "2026-08-31T00:00:00", notes: "Monthly",
  createdAt: "2026-09-15T08:00:00", concurrencyToken: "token-7", ...over,
});

const line = (over: Partial<WhtVendorLine> = {}): WhtVendorLine => ({
  vendorId: 1, vendorName: "Al-Noor Steel", ntn: "4410293-1", cnic: null, filerStatus: "Filer", taxSection: "153(1)(a)",
  grossAmount: 4_850_000, whtAmount: 48_500, netPaid: 4_801_500, paymentCount: 3, ...over,
});

describe("period line wording", () => {
  it("counts suppliers in the singular for exactly one and the plural otherwise", () => {
    expect(suppliersText(1)).toBe("1 supplier");
    expect(suppliersText(0)).toBe("0 suppliers");
    expect(suppliersText(6)).toBe("6 suppliers");
  });
});

describe("deposit list wording", () => {
  it("writes the period covered in full, once the year is shared only at the end", () => {
    expect(periodCovered(deposit())).toBe("Aug 1 – Aug 31, 2026");
  });

  it("names both years when the period crosses one", () => {
    expect(periodCovered(deposit({ periodFrom: "2025-12-01T00:00:00", periodTo: "2026-01-31T00:00:00" }))).toBe("Dec 1, 2025 – Jan 31, 2026");
  });

  it("shows a dash unless both ends are known", () => {
    expect(periodCovered(deposit({ periodFrom: null }))).toBe("—");
    expect(periodCovered(deposit({ periodTo: null }))).toBe("—");
    expect(periodCovered(deposit({ periodFrom: null, periodTo: null }))).toBe("—");
  });

  it("asks about the deposit by its own challan number, and says how much goes back", () => {
    expect(deleteTitle(deposit())).toBe("Delete deposit CPR-0923-118?");
    expect(deleteTitle(deposit({ challanNumber: null }))).toBe("Delete this deposit?");
    expect(deleteMessage(deposit())).toBe("Rs 412,000 goes back to being owed to FBR.");
    expect(deleteMessage(deposit({ amount: 1_250.5 }))).toBe("Rs 1,250.5 goes back to being owed to FBR.");
  });
});

describe("supplier list wording", () => {
  it("shows the NTN, else the CNIC, else a dash", () => {
    expect(supplierTaxId(line())).toBe("4410293-1");
    expect(supplierTaxId(line({ ntn: null, cnic: "37405-1122334-5" }))).toBe("37405-1122334-5");
    expect(supplierTaxId(line({ ntn: null, cnic: null }))).toBe("—");
  });

  it("puts the 's.' in front of a section on a phone card, once", () => {
    expect(sectionLabel("153(1)(a)")).toBe("s.153(1)(a)");
    expect(sectionLabel("s.153(1)(b)")).toBe("s.153(1)(b)");
    expect(sectionLabel(null)).toBe("—");
  });

  it("keeps one supplier's lines apart when the section or the status differs", () => {
    const keys = [
      supplierKey(line(), 0),
      supplierKey(line({ taxSection: "153(1)(b)" }), 1),
      supplierKey(line({ filerStatus: "NonFiler" }), 2),
      supplierKey(line({ vendorId: null, vendorName: "Unnamed vendor", ntn: null }), 3),
    ];
    expect(new Set(keys).size).toBe(4);
  });
});

describe("the deposit form", () => {
  const today = "2026-10-01";

  it("opens a new deposit on what is owed and today", () => {
    expect(depositFields(null, 184_350, today)).toEqual({
      accountId: "", amount: "184350", date: "2026-10-01", challan: "", periodFrom: "", periodTo: "", notes: "",
    });
  });

  it("leaves the amount empty when nothing is owed", () => {
    expect(depositFields(null, 0, today).amount).toBe("");
    expect(depositFields(null, -20, today).amount).toBe("");
  });

  it("opens an edit on what was saved, not on what is owed", () => {
    expect(depositFields(deposit(), 999, today)).toEqual({
      accountId: "3", amount: "412000", date: "2026-09-15", challan: "CPR-0923-118", periodFrom: "2026-08-01", periodTo: "2026-08-31", notes: "Monthly",
    });
  });

  it("keeps Save off until the account, a positive amount and the date are there", () => {
    const ready = { ...depositFields(null, 184_350, today), accountId: "3" };
    expect(depositReady(ready, today)).toBe(true);
    expect(depositReady({ ...ready, accountId: "" }, today)).toBe(false);
    expect(depositReady({ ...ready, amount: "" }, today)).toBe(false);
    expect(depositReady({ ...ready, amount: "0" }, today)).toBe(false);
    expect(depositReady({ ...ready, amount: "0.00" }, today)).toBe(false);
    expect(depositReady({ ...ready, date: "" }, today)).toBe(false);
  });

  it("does not need the optional fields", () => {
    const ready = { ...depositFields(null, 100, today), accountId: "3" };
    expect(depositReady({ ...ready, challan: "", periodFrom: "", periodTo: "", notes: "" }, today)).toBe(true);
  });

  it("refuses a future date and a period that ends before it starts", () => {
    const ready = { ...depositFields(null, 100, today), accountId: "3" };
    expect(depositErrors({ ...ready, date: "2026-10-02" }, today).date).toBe("The date cannot be in the future.");
    expect(depositReady({ ...ready, date: "2026-10-02" }, today)).toBe(false);
    expect(depositErrors({ ...ready, periodFrom: "2026-09-01", periodTo: "2026-08-31" }, today).periodTo).toBe("The period end cannot be before its start.");
    expect(depositReady({ ...ready, periodFrom: "2026-09-01", periodTo: "2026-08-31" }, today)).toBe(false);
  });

  it("accepts a one-day period and a period with only one end", () => {
    const ready = { ...depositFields(null, 100, today), accountId: "3" };
    expect(depositReady({ ...ready, periodFrom: "2026-09-30", periodTo: "2026-09-30" }, today)).toBe(true);
    expect(depositReady({ ...ready, periodFrom: "2026-09-01" }, today)).toBe(true);
    expect(depositReady({ ...ready, periodTo: "2026-09-30" }, today)).toBe(true);
  });

  it("sends blank optional fields as null and the version only when editing", () => {
    const fields = { ...depositFields(null, 184_350.5, today), accountId: "3", challan: "  ", notes: "" };
    expect(depositBody(fields, null)).toEqual({
      financeAccountId: 3, amount: 184_350.5, depositDate: "2026-10-01", challanNumber: null, periodFrom: null, periodTo: null, notes: null, concurrencyToken: null,
    });
    expect(depositBody(depositFields(deposit(), 0, today), "token-7")).toEqual({
      financeAccountId: 3, amount: 412_000, depositDate: "2026-09-15", challanNumber: "CPR-0923-118", periodFrom: "2026-08-01", periodTo: "2026-08-31", notes: "Monthly", concurrencyToken: "token-7",
    });
  });
});
