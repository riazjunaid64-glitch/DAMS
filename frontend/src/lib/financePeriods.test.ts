import { describe, expect, it } from "vitest";
import { buildPeriodRange, financePeriodLabel, financialYearWindow } from "./financePeriods";

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

  it("backs off to the prior financial year before the start month", () => {
    expect(buildPeriodRange("year", 7, new Date(2026, 2, 15))).toEqual({
      from: "2025-07-01",
      to: "2026-06-30",
    });
  });

  it("treats 1 July as the first day of the current financial year", () => {
    expect(buildPeriodRange("year", 7, new Date(2026, 6, 1))).toEqual({
      from: "2026-07-01",
      to: "2027-06-30",
    });
  });

  it("treats 30 June as the final day of the prior financial year", () => {
    expect(buildPeriodRange("year", 7, new Date(2026, 5, 30))).toEqual({
      from: "2025-07-01",
      to: "2026-06-30",
    });
  });

  it("falls back to July when the configured month is invalid", () => {
    expect(buildPeriodRange("year", 0, new Date("2026-08-12T00:00:00Z"))).toEqual({
      from: "2026-07-01",
      to: "2027-06-30",
    });

    expect(buildPeriodRange("year", 13, new Date("2026-08-12T00:00:00Z"))).toEqual({
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

  it("builds and labels the prior configured financial year", () => {
    const now = new Date(2026, 7, 12);
    expect(buildPeriodRange("lastYear", 7, now)).toEqual({ from: "2025-07-01", to: "2026-06-30" });
    expect(financePeriodLabel("year", 7, now)).toBe("This Year (Jul 2026 – Jun 2027)");
    expect(financePeriodLabel("lastYear", 7, now)).toBe("Last Year (Jul 2025 – Jun 2026)");
  });
});
