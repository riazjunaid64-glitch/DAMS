import { describe, expect, it } from "vitest";
import { exactAmount, shortAmountParts, type AmountSystem } from "./financeAmounts";

const short = (value: number, system?: AmountSystem) => {
  const { sign, parts } = shortAmountParts(value, system);
  return `${sign}Rs ${parts.map((part) => (part.unit ? `${part.value} ${part.unit}` : part.value)).join(" ")}`;
};

const UNIT_SIZE: Record<string, number> = { Cr: 10_000_000, L: 100_000, B: 1_000_000_000, M: 1_000_000 };

describe("shortAmountParts", () => {
  it("states the largest whole unit and the remainder", () => {
    expect(short(90_040_000)).toBe("Rs 9 Cr 40,000");
    expect(short(450_000)).toBe("Rs 4 L 50,000");
    expect(short(212_111)).toBe("Rs 2 L 12,111");
  });

  it("gives a large remainder its own unit instead of dumping the digits", () => {
    expect(short(9_999_787_889)).toBe("Rs 999 Cr 98 L");
    expect(short(1_301_000)).toBe("Rs 13 L 1,000");
    expect(short(15_500_000)).toBe("Rs 1 Cr 55 L");
  });

  it("never states more than two units", () => {
    expect(shortAmountParts(9_999_787_889).parts).toHaveLength(2);
    expect(shortAmountParts(123_456_789).parts).toHaveLength(2);
  });

  it("carries instead of printing a hundred lakh, or a thousand million", () => {
    // The remainder rounds to 100 L, which is a crore — and no reader says "999 Cr 100 L".
    expect(short(9_999_999_999)).toBe("Rs 1,000 Cr");
    expect(short(9_999_787_889, "million-billion")).toBe("Rs 10 B");
  });

  it("states a remainder below the smallest unit exactly, however close to the next unit", () => {
    expect(short(9_999_999)).toBe("Rs 99 L 99,999");
  });

  it("drops the remainder when the figure divides exactly", () => {
    expect(short(10_000_000_000)).toBe("Rs 1,000 Cr");
    expect(short(2_000_000_000)).toBe("Rs 200 Cr");
    expect(short(100_000)).toBe("Rs 1 L");
  });

  it("leaves figures below one lakh alone, paisa included", () => {
    expect(short(0)).toBe("Rs 0");
    expect(short(40_000)).toBe("Rs 40,000");
    expect(short(212.5)).toBe("Rs 212.5");
    expect(short(99_999)).toBe("Rs 99,999");
  });

  it("picks the unit from the rounded figure, so near-misses read as the unit they are", () => {
    expect(short(9_999_999.7)).toBe("Rs 1 Cr");
    expect(short(99_999.6)).toBe("Rs 1 L");
  });

  it("never mixes systems: a billion is stated in crore", () => {
    expect(shortAmountParts(2_000_000_000).parts.map((p) => p.unit)).toEqual(["Cr"]);
    expect(shortAmountParts(1_301_000).parts.map((p) => p.unit)).toEqual(["L", null]);
  });

  it("follows the international scale when that system is asked for", () => {
    expect(short(1_301_000, "million-billion")).toBe("Rs 1 M 301,000");
    expect(short(9_400_000_000, "million-billion")).toBe("Rs 9 B 400 M");
    expect(short(212_111, "million-billion")).toBe("Rs 212,111");
  });

  it("keeps the sign in front of the whole amount", () => {
    expect(short(-90_040_000)).toBe("−Rs 9 Cr 40,000");
    expect(short(-5_000)).toBe("−Rs 5,000");
  });

  it("stays within half of the smallest unit it states", () => {
    const values = [90_040_000, 450_000, 212_111, 9_999_787_889, 15_500_000, 123_456_789, 0, -5_000, 99_999.6];
    for (const value of values) {
      const { sign, parts } = shortAmountParts(value);
      const stated = parts.reduce(
        (sum, part) => sum + Number(part.value.replaceAll(",", "")) * (part.unit ? UNIT_SIZE[part.unit] : 1),
        0,
      ) * (sign === "−" ? -1 : 1);
      const smallest = parts[parts.length - 1].unit;
      const tolerance = smallest ? UNIT_SIZE[smallest] / 2 : 0.5;
      expect(Math.abs(stated - value)).toBeLessThanOrEqual(tolerance);
    }
  });
});

describe("exactAmount", () => {
  it("prints the full figure to the paisa", () => {
    expect(exactAmount(90_040_000)).toBe("Rs 90,040,000");
    expect(exactAmount(9_999_787_889)).toBe("Rs 9,999,787,889");
    expect(exactAmount(212_111.25)).toBe("Rs 212,111.25");
    expect(exactAmount(-5_000)).toBe("−Rs 5,000");
  });
});
