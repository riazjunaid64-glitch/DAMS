import { describe, expect, it } from "vitest";
import {
  calculationKey,
  effectiveTax,
  fromAmount,
  fromPreview,
  fromRate,
  isOverridden,
  netPaid,
  seededKey,
  showsFiledFigures,
} from "./whtFormState.ts";
import type { WhtCalculation, WhtFormValue } from "./whtTypes.ts";

const preview = (overrides: Partial<WhtCalculation> = {}): WhtCalculation => ({
  isWhtApplicable: true,
  rate: 1,
  whtAmount: 10_000,
  netPaid: 990_000,
  whtApplied: true,
  filerStatus: "Filer",
  taxSection: "153(1)(a)",
  belowThreshold: false,
  annualThreshold: 75_000,
  yearToDateTotal: 0,
  financialYear: "2026-27",
  notice: null,
  ...overrides,
});

const value = (overrides: Partial<WhtFormValue> = {}): WhtFormValue =>
  ({ rate: "", amount: "", overrideReason: "", ...overrides });

describe("seededKey — protects a saved override from being recalculated away", () => {
  it("treats an expense opened for editing as already applied", () => {
    // The regression this guards: opening an expense whose tax was overridden to 15,000 and
    // saving it untouched must not silently write back the calculated 10,000.
    const key = calculationKey({ categoryId: "1", vendorId: "1", grossAmount: "1000000", date: "2026-08-11" });
    const editing = value({ rate: "1.5", amount: "15000", overrideReason: "Figure from the vendor invoice." });

    expect(seededKey(editing, key)).toBe(key);
  });

  it("leaves a blank form open to being prefilled", () => {
    const key = calculationKey({ categoryId: "1", vendorId: "", grossAmount: "1000", date: "" });
    expect(seededKey(value(), key)).toBeNull();
  });

  it("changing any input produces a different key, so figures are recalculated", () => {
    const base = { categoryId: "1", vendorId: "1", grossAmount: "1000", date: "2026-08-11" };
    const key = calculationKey(base);

    expect(calculationKey({ ...base, categoryId: "2" })).not.toBe(key);
    expect(calculationKey({ ...base, vendorId: "2" })).not.toBe(key);
    expect(calculationKey({ ...base, grossAmount: "2000" })).not.toBe(key);
    expect(calculationKey({ ...base, date: "2026-08-12" })).not.toBe(key);
    // Formatting of the same amount must not count as a change, or every keystroke resets.
    expect(calculationKey({ ...base, grossAmount: "1000.00" })).toBe(key);
  });
});

describe("effectiveTax — a blank field means calculated, not zero", () => {
  it("falls back to the calculated figure when nothing was typed", () => {
    expect(effectiveTax(value(), preview())).toBe(10_000);
    expect(isOverridden(value(), preview())).toBe(false);
    expect(netPaid("1000000", value(), preview())).toBe(990_000);
  });

  it("uses the typed figure when there is one", () => {
    const typed = value({ amount: "15000" });
    expect(effectiveTax(typed, preview())).toBe(15_000);
    expect(isOverridden(typed, preview())).toBe(true);
    expect(netPaid("1000000", typed, preview())).toBe(985_000);
  });

  it("treats a deliberate zero as an override, not as a blank", () => {
    const zero = value({ amount: "0" });
    expect(effectiveTax(zero, preview())).toBe(0);
    expect(isOverridden(zero, preview())).toBe(true);
  });

  it("is never an override on a head that carries no withholding", () => {
    const none = preview({ isWhtApplicable: false, rate: 0, whtAmount: 0 });
    expect(isOverridden(value({ amount: "500" }), none)).toBe(false);
    expect(effectiveTax(value(), none)).toBe(0);
  });

  it("is never an override before the calculation has arrived", () => {
    expect(isOverridden(value({ amount: "15000" }), null)).toBe(false);
  });

  it("matches a below-threshold calculation of zero without flagging an override", () => {
    const below = preview({ whtAmount: 0, whtApplied: false, belowThreshold: true });
    expect(isOverridden(value({ amount: "0" }), below)).toBe(false);
    expect(netPaid("50000", value({ amount: "0" }), below)).toBe(50_000);
  });
});

describe("rate and amount stay consistent with each other", () => {
  it("editing the rate recomputes the tax, rounded to paisa", () => {
    expect(fromRate(value(), "1", "1000000").amount).toBe("10000");
    expect(fromRate(value(), "7.5", "1234567.89").amount).toBe("92592.59");
    expect(fromRate(value(), "2.5", "1").amount).toBe("0.03");
  });

  it("editing the tax back-computes the effective rate", () => {
    expect(fromAmount(value(), "10000", "1000000").rate).toBe("1");
    expect(fromAmount(value(), "10000", "210000").rate).toBe("4.7619");
  });

  it("clearing either field leaves the other alone", () => {
    const current = value({ rate: "1", amount: "10000" });
    expect(fromRate(current, "", "1000000")).toEqual({ ...current, rate: "" });
    expect(fromAmount(current, "", "1000000")).toEqual({ ...current, amount: "" });
  });

  it("does not divide by a missing or zero gross amount", () => {
    const current = value({ rate: "1", amount: "10000" });
    expect(fromAmount(current, "500", "0").rate).toBe("1");
    expect(fromAmount(current, "500", "").rate).toBe("1");
    expect(fromRate(current, "5", "").amount).toBe("10000");
  });
});

describe("fromPreview", () => {
  it("adopts the calculated figures and drops a reason written against the old basis", () => {
    const adopted = fromPreview(preview());
    expect(adopted).toEqual({ rate: "1", amount: "10000", overrideReason: "" });
  });

  it("blanks both fields when the head carries no withholding", () => {
    expect(fromPreview(preview({ isWhtApplicable: false, rate: 0, whtAmount: 0 })))
      .toEqual({ rate: "", amount: "", overrideReason: "" });
  });

  it("drops trailing zeros so a rate reads as 7.5%, not 7.5000%", () => {
    expect(fromPreview(preview({ rate: 7.5 })).rate).toBe("7.5");
  });
});

describe("showsFiledFigures", () => {
  const opened = calculationKey({ categoryId: "1", vendorId: "1", grossAmount: "40000", date: "2026-01-10" });

  it("holds while an edit leaves the tax basis alone", () => {
    // Fixing a typo in the description does not touch category, vendor, amount or date, so the
    // figure on screen is still the one that was filed — no override prompt belongs here.
    expect(showsFiledFigures(opened, opened)).toBe(true);
  });

  it("releases as soon as the basis moves", () => {
    const raised = calculationKey({ categoryId: "1", vendorId: "1", grossAmount: "50000", date: "2026-01-10" });
    expect(showsFiledFigures(opened, raised)).toBe(false);
  });

  it("never applies to a new expense, which has nothing filed yet", () => {
    expect(showsFiledFigures(null, opened)).toBe(false);
  });
});
