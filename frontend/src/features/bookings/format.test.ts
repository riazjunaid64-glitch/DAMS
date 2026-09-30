import { describe, expect, it } from "vitest";
import { formatPhone } from "./format.ts";

describe("formatPhone", () => {
  it("writes a mobile as 0333 4412987 whatever way it was stored", () => {
    for (const stored of ["03334412987", "0333-4412987", "0333 4412987", "+92 333 4412987", "+923334412987", "923334412987", "0092 333 4412987"]) {
      expect(formatPhone(stored)).toBe("0333 4412987");
    }
  });
  it("leaves other numbers alone, and copes with nothing", () => {
    expect(formatPhone("021-1234567")).toBe("021-1234567");
    expect(formatPhone("+44 20 7946 0958")).toBe("+44 20 7946 0958");
    expect(formatPhone("")).toBe("");
    expect(formatPhone(null)).toBe("");
  });
});
