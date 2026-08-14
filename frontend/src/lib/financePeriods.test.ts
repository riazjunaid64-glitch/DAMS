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

  it("names the month the monthly preset covers", () => {
    expect(financePeriodLabel("month", 7, new Date(2026, 7, 12))).toBe("This Month (Aug 2026)");
    expect(financePeriodLabel("month", 7, new Date(2027, 0, 31))).toBe("This Month (Jan 2027)");
  });

  it("keeps the monthly label on the calendar month whatever the financial year start", () => {
    // The month preset is a calendar month, so moving the year start must not shift its label.
    const now = new Date(2026, 7, 12);
    expect(financePeriodLabel("month", 1, now)).toBe(financePeriodLabel("month", 7, now));
  });

  it("names the presets that have no fixed range to state", () => {
    const now = new Date(2026, 7, 12);
    expect(financePeriodLabel("today", 7, now)).toBe("Today");
    expect(financePeriodLabel("all", 7, now)).toBe("All");
    expect(financePeriodLabel("custom", 7, now)).toBe("Custom");
  });

  it("states the range it filters on", () => {
    // The label is what an admin reads before trusting a figure, so it must not be able to drift
    // from the dates actually sent to the server.
    const now = new Date(2026, 7, 12);
    for (const preset of ["month", "year", "lastYear"] as const) {
      const range = buildPeriodRange(preset, 7, now);
      const label = financePeriodLabel(preset, 7, now);
      expect(label).toContain(`${new Date(`${range.from}T00:00:00`).getFullYear()}`);
      expect(label).toMatch(/\(.+\)$/);
    }
  });
});
