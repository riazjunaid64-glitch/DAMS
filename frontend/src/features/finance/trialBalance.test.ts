import { describe, expect, it } from "vitest";
import {
  applyTrialFilters,
  calendarMonthStart,
  trialDetailsParams,
  trialFilterError,
  trialSummaryParams,
  type TrialBalanceFilters,
} from "./trialBalance.ts";

const filters = (overrides: Partial<TrialBalanceFilters> = {}): TrialBalanceFilters => ({
  mode: "asAt",
  projectId: "",
  asAt: "2026-08-23",
  from: "2026-08-01",
  to: "2026-08-23",
  ...overrides,
});

describe("trial balance filter coupling", () => {
  it("uses the selected month-to-date window for an as-at drill-down", () => {
    expect(calendarMonthStart("2026-08-23")).toBe("2026-08-01");
    expect(applyTrialFilters(filters())).toEqual({
      projectId: "",
      from: "2026-08-01",
      to: "2026-08-23",
    });
  });

  it("keeps an explicitly selected date range unchanged", () => {
    expect(applyTrialFilters(filters({
      mode: "range",
      projectId: "7",
      from: "2026-07-10",
      to: "2026-08-20",
    }))).toEqual({ projectId: "7", from: "2026-07-10", to: "2026-08-20" });
  });

  it("always asks the summary endpoint for one closing-date column", () => {
    const params = trialSummaryParams({ projectId: "7", from: "2026-07-10", to: "2026-08-20" });
    expect(params.get("projectId")).toBe("7");
    expect(params.get("asAt")).toBe("2026-08-20");
    expect(params.get("monthsBack")).toBe("0");
    expect(params.has("from")).toBe(false);
  });

  it("passes the same project and bounds to account details", () => {
    const params = trialDetailsParams("E:office-rent", {
      projectId: "7",
      from: "2026-07-10",
      to: "2026-08-20",
    });
    expect(Object.fromEntries(params)).toEqual({
      accountKey: "E:office-rent",
      from: "2026-07-10",
      to: "2026-08-20",
      projectId: "7",
    });
  });

  it("refuses missing and backwards ranges", () => {
    expect(trialFilterError(filters({ mode: "asAt", asAt: "" }))).toMatch(/As at/i);
    expect(trialFilterError(filters({ mode: "range", from: "", to: "2026-08-20" }))).toMatch(/both/i);
    expect(trialFilterError(filters({ mode: "range", from: "2026-08-21", to: "2026-08-20" }))).toMatch(/cannot be after/i);
    expect(trialFilterError(filters({ mode: "range", from: "2026-08-20", to: "2026-08-20" }))).toBeNull();
  });
});

