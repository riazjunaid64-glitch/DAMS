import { describe, expect, it, vi } from "vitest";
import { buildPeriodRange, financePeriodLabel, financialYearWindow, pakistanToday } from "./financePeriods";

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

  it("states no financial year until the configured start month is known", () => {
    // July is right for most Pakistani clients and wrong for the rest. Naming a range before the
    // setting has been read would tell an admin with a January year that a figure covers months it
    // does not — so while it is unresolved the presets say only what they can stand behind.
    const now = new Date(2026, 7, 12);
    expect(financePeriodLabel("year", null, now)).toBe("This Year");
    expect(financePeriodLabel("lastYear", null, now)).toBe("Last Year");
  });

  it("refuses to build a financial year range from an unknown start month", () => {
    // An empty range reads as "All" rather than as a silently July-based year, so a filter applied
    // before the setting resolves can never quietly report the wrong twelve months.
    const now = new Date(2026, 7, 12);
    expect(buildPeriodRange("year", null, now)).toEqual({ from: "", to: "" });
    expect(buildPeriodRange("lastYear", null, now)).toEqual({ from: "", to: "" });
  });

  it("keeps the presets that do not depend on the financial year fully usable", () => {
    const now = new Date(2026, 7, 12);
    expect(financePeriodLabel("month", null, now)).toBe("This Month (Aug 2026)");
    expect(financePeriodLabel("today", null, now)).toBe("Today");
    expect(buildPeriodRange("month", null, now)).toEqual({ from: "2026-08-01", to: "2026-08-31" });
    expect(buildPeriodRange("today", null, now)).toEqual({ from: "2026-08-12", to: "2026-08-12" });
  });
});

describe("pakistanToday", () => {
  it("returns the Pakistani calendar date, not the browser's and not UTC", () => {
    // 23:30 UTC on 13 Aug is already 04:30 on 14 Aug in Pakistan. Reading the UTC date would file
    // an entry a day early; the server, which judges against PKT, would accept it silently.
    vi.useFakeTimers();
    try {
      vi.setSystemTime(new Date("2026-08-13T23:30:00Z"));
      expect(pakistanToday()).toBe("2026-08-14");

      // And just before PKT midnight it must still be the earlier day.
      vi.setSystemTime(new Date("2026-08-13T18:00:00Z"));
      expect(pakistanToday()).toBe("2026-08-13");
    } finally {
      vi.useRealTimers();
    }
  });
});
