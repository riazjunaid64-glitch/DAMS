import { describe, expect, it } from "vitest";
import { pageItems, pageRange } from "./pageItems.ts";

describe("pageItems", () => {
  it("lists every page when there are few", () => {
    expect(pageItems(1, 1)).toEqual([1]);
    expect(pageItems(3, 7)).toEqual([1, 2, 3, 4, 5, 6, 7]);
    expect(pageItems(1, 0)).toEqual([]);
  });
  it("matches the design near the start, middle and end", () => {
    expect(pageItems(1, 22)).toEqual([1, 2, 3, 4, 5, "gap", 22]);
    expect(pageItems(4, 22)).toEqual([1, 2, 3, 4, 5, "gap", 22]);
    expect(pageItems(5, 22)).toEqual([1, "gap", 4, 5, 6, "gap", 22]);
    expect(pageItems(5, 10)).toEqual([1, "gap", 4, 5, 6, "gap", 10]);
    expect(pageItems(19, 22)).toEqual([1, "gap", 18, 19, 20, 21, 22]);
    expect(pageItems(22, 22)).toEqual([1, "gap", 18, 19, 20, 21, 22]);
  });
  it("clamps an out-of-range page", () => {
    expect(pageItems(99, 22)).toEqual([1, "gap", 18, 19, 20, 21, 22]);
  });
});

describe("pageRange", () => {
  it("gives the shown range", () => {
    expect(pageRange(1, 20, 437)).toEqual([1, 20]);
    expect(pageRange(22, 20, 437)).toEqual([421, 437]);
    expect(pageRange(1, 20, 0)).toEqual([0, 0]);
  });
});
