import { describe, expect, it } from "vitest";
import { caretAfter, cleanNumber, groupThousands } from "./numberFormat.ts";

describe("cleanNumber", () => {
  it("strips separators and letters", () => {
    expect(cleanNumber("1,000,000", 2)).toBe("1000000");
    expect(cleanNumber("Rs 25a0", 2)).toBe("250");
  });
  it("keeps one point and caps the fraction without rounding", () => {
    expect(cleanNumber("12.349", 2)).toBe("12.34");
    expect(cleanNumber("1.2.3", 2)).toBe("1.23");
    expect(cleanNumber("5.", 2)).toBe("5.");
  });
  it("drops the point when decimals are not allowed", () => {
    expect(cleanNumber("12.5", 0)).toBe("12");
  });
  it("trims leading zeros but keeps a lone zero", () => {
    expect(cleanNumber("007", 2)).toBe("7");
    expect(cleanNumber("0", 2)).toBe("0");
    expect(cleanNumber("0.5", 2)).toBe("0.5");
  });
  it("refuses a minus unless the field asks for one", () => {
    expect(cleanNumber("-1200000", 0)).toBe("1200000");
    expect(cleanNumber("-1,200,000", 0, true)).toBe("-1200000");
    expect(cleanNumber("-", 2, true)).toBe("-");
    expect(cleanNumber("--12", 0, true)).toBe("-12");
    expect(cleanNumber("12-3", 0, true)).toBe("123");
    expect(cleanNumber("-0.5", 2, true)).toBe("-0.5");
  });
});

describe("groupThousands", () => {
  it("groups the whole part only", () => {
    expect(groupThousands("1000000")).toBe("1,000,000");
    expect(groupThousands("1234.5")).toBe("1,234.5");
    expect(groupThousands("999")).toBe("999");
    expect(groupThousands("")).toBe("");
    expect(groupThousands("-1200000")).toBe("-1,200,000");
    expect(groupThousands("-")).toBe("-");
  });
});

describe("caretAfter", () => {
  it("skips the separators the formatting added", () => {
    expect(caretAfter("1,000,000", 4)).toBe(5);
    expect(caretAfter("1,000", 1)).toBe(1);
    expect(caretAfter("1,000", 0)).toBe(0);
    expect(caretAfter("1,000", 9)).toBe(5);
    expect(caretAfter("-1,200,000", 5)).toBe(6);
    expect(caretAfter("-", 1)).toBe(1);
  });
});
