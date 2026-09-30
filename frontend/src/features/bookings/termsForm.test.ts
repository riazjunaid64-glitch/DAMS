import { describe, expect, it } from "vitest";
import { canSaveTerms, chipAmount, chipFor, termsErrors, termsFigures } from "./termsForm.ts";

const fields = (over: Partial<Parameters<typeof termsFigures>[0]> = {}) => ({
  agreedSalePrice: "14255985", discountPercent: "0", chip: "10", customAmount: "", ...over,
});

describe("terms figures", () => {
  it("works a chip out from the agreed price, to the whole rupee", () => {
    expect(chipAmount(14_255_985, 10)).toBe(1_425_599);
    const figures = termsFigures(fields());
    expect(figures).toMatchObject({ net: 14_255_985, bookingAmount: 1_425_599, leftForInstallments: 12_830_386 });
  });

  it("takes the discount off the price but keeps the chip on the agreed price", () => {
    const figures = termsFigures(fields({ discountPercent: "5" }));
    expect(figures.discount).toBe(712_799.25);
    expect(figures.net).toBe(13_543_185.75);
    expect(figures.bookingAmount).toBe(1_425_599);
  });

  it("uses the typed amount on Custom", () => {
    expect(termsFigures(fields({ chip: "custom", customAmount: "100000" })).bookingAmount).toBe(100_000);
    expect(termsFigures(fields({ chip: "custom", customAmount: "" })).bookingAmount).toBe(0);
  });

  it("finds the chip a saved amount belongs to, else Custom", () => {
    expect(chipFor(1_425_599, 14_255_985)).toBe("10");
    expect(chipFor(150_000, 11_400_000)).toBe("custom");
    expect(chipFor(0, 11_400_000)).toBe("custom");
  });
});

describe("terms errors", () => {
  it("refuses an amount above the net price", () => {
    const figures = termsFigures(fields({ chip: "custom", customAmount: "15000000" }));
    const errors = termsErrors(figures, 0);
    expect(errors.amount).toBe("Can't be more than the net price, Rs 14,255,985");
    expect(canSaveTerms(figures, errors)).toBe(false);
  });

  it("refuses an amount below what is already received", () => {
    const figures = termsFigures(fields({ agreedSalePrice: "11400000", discountPercent: "2", chip: "custom", customAmount: "100000" }));
    const errors = termsErrors(figures, 150_000);
    expect(errors.amount).toBe("Can't be less than Rs 150,000 already received");
    expect(canSaveTerms(figures, errors)).toBe(false);
  });

  it("refuses a discount outside 0-100", () => {
    const figures = termsFigures(fields({ discountPercent: "101" }));
    expect(termsErrors(figures, 0).discount).toBeDefined();
    expect(canSaveTerms(figures, termsErrors(figures, 0))).toBe(false);
  });

  it("allows saving a valid form, and not one without a price or an amount", () => {
    const ok = termsFigures(fields());
    expect(canSaveTerms(ok, termsErrors(ok, 0))).toBe(true);
    const noPrice = termsFigures(fields({ agreedSalePrice: "" }));
    expect(canSaveTerms(noPrice, termsErrors(noPrice, 0))).toBe(false);
    const noAmount = termsFigures(fields({ chip: "custom", customAmount: "" }));
    expect(canSaveTerms(noAmount, termsErrors(noAmount, 0))).toBe(false);
  });
});
