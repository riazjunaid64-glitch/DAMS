import { describe, expect, it } from "vitest";
import { clampPage, countOf, pageRows, paymentsText, PAGE_SIZE } from "./lists.ts";

const rows = (count: number) => Array.from({ length: count }, (_, index) => index + 1);

describe("cutting a list into pages", () => {
  it("shows twenty rows a page", () => {
    expect(PAGE_SIZE).toBe(20);
    expect(pageRows(rows(45), 1)).toEqual(rows(20));
    expect(pageRows(rows(45), 3)).toEqual([41, 42, 43, 44, 45]);
  });

  it("keeps the page it is on, and an empty list is page one", () => {
    expect(clampPage(2, 45)).toBe(2);
    expect(clampPage(1, 0)).toBe(1);
    expect(pageRows([], 1)).toEqual([]);
  });

  it("steps back to the new last page when a delete empties the last one", () => {
    // 21 rows: page 2 holds one. After it is deleted there are 20 and page 2 no longer exists.
    expect(pageRows(rows(21), 2)).toEqual([21]);
    expect(clampPage(2, 20)).toBe(1);
    expect(pageRows(rows(20), 2)).toEqual(rows(20));
  });

  it("never goes below page one", () => {
    expect(clampPage(0, 5)).toBe(1);
    expect(clampPage(-3, 50)).toBe(1);
  });
});

describe("counted wording", () => {
  it("uses the singular for exactly one", () => {
    expect(paymentsText(1)).toBe("1 payment");
    expect(countOf(1, "revenue entry", "revenue entries")).toBe("1 revenue entry");
  });

  it("uses the plural for none and for many, with thousands commas", () => {
    expect(paymentsText(0)).toBe("0 payments");
    expect(paymentsText(18)).toBe("18 payments");
    expect(paymentsText(1_250)).toBe("1,250 payments");
    expect(countOf(4, "revenue entry", "revenue entries")).toBe("4 revenue entries");
  });
});
