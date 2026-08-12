import { describe, expect, it } from "vitest";
import { buildPeriodRange, financialYearWindow } from "./financePeriods";

describe("finance period ranges", () => {
  it("uses the configured financial year for the annual preset", () => {
    const window = financialYearWindow(new Date("2026-08-12T00:00:00Z"), 7);

    expect(window.from).toEqual(new Date(2026, 6, 1, 0, 0, 0, 0));
    expect(window.toExclusive).toEqual(new Date(2027, 6, 1, 0, 0, 0, 0));
    expect(buildPeriodRange("year", 7, new Date("2026-08-12T00:00:00Z"))).toEqual({
      from: "2026-07-01",
      to: "2027-06-30",
    });
  });

  it("falls back to the calendar year when the financial year starts in January", () => {
    const window = financialYearWindow(new Date("2026-08-12T00:00:00Z"), 1);

    expect(window.from).toEqual(new Date(2026, 0, 1, 0, 0, 0, 0));
    expect(window.toExclusive).toEqual(new Date(2027, 0, 1, 0, 0, 0, 0));
    expect(buildPeriodRange("year", 1, new Date("2026-08-12T00:00:00Z"))).toEqual({
      from: "2026-01-01",
      to: "2026-12-31",
    });
  });
});
