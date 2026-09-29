import { describe, expect, it } from "vitest";
import {
  addDays,
  addMonths,
  formatPickerDate,
  formatPickerTime,
  fromClockParts,
  isOutOfRange,
  monthGrid,
  monthOutOfRange,
  parseIso,
  parseTypedTime,
  shiftMonth,
  toClockParts,
} from "./pickerFormat.ts";

describe("formatPickerDate", () => {
  it("writes month, day and year", () => {
    expect(formatPickerDate("2026-09-29")).toBe("Sep 29, 2026");
    expect(formatPickerDate("1988-03-04")).toBe("Mar 4, 1988");
  });
  it("is empty for nothing or a non-date", () => {
    expect(formatPickerDate("")).toBe("");
    expect(formatPickerDate("2026-02-30")).toBe("");
    expect(formatPickerDate("29/09/2026")).toBe("");
  });
});

describe("parseIso", () => {
  it("rejects dates the calendar does not have", () => {
    expect(parseIso("2026-13-01")).toBeNull();
    expect(parseIso("2025-02-29")).toBeNull();
    expect(parseIso("2024-02-29")).toEqual({ year: 2024, month: 1, day: 29 });
  });
});

describe("addDays / addMonths", () => {
  it("crosses month and year ends", () => {
    expect(addDays("2026-12-31", 1)).toBe("2027-01-01");
    expect(addDays("2026-03-01", -1)).toBe("2026-02-28");
  });
  it("holds to the last day of a shorter month", () => {
    expect(addMonths("2026-01-31", 1)).toBe("2026-02-28");
    expect(addMonths("2026-01-15", -1)).toBe("2025-12-15");
  });
});

describe("monthGrid", () => {
  it("starts on Monday and always has six weeks", () => {
    const grid = monthGrid({ year: 2026, month: 8 });
    expect(grid).toHaveLength(42);
    expect(grid[0]).toEqual({ iso: "2026-08-31", day: 31, inMonth: false });
    expect(grid[1]).toEqual({ iso: "2026-09-01", day: 1, inMonth: true });
    expect(grid.filter((cell) => cell.inMonth)).toHaveLength(30);
  });
  it("puts a month that starts on Monday in the first cell", () => {
    expect(monthGrid({ year: 2026, month: 5 })[0]).toEqual({ iso: "2026-06-01", day: 1, inMonth: true });
  });
});

describe("range checks", () => {
  it("treats min and max as inclusive", () => {
    expect(isOutOfRange("2026-09-29", undefined, "2026-09-29")).toBe(false);
    expect(isOutOfRange("2026-09-30", undefined, "2026-09-29")).toBe(true);
    expect(isOutOfRange("2026-09-28", "2026-09-29")).toBe(true);
  });
  it("finds months with no pickable day", () => {
    expect(monthOutOfRange({ year: 2026, month: 9 }, undefined, "2026-09-29")).toBe(true);
    expect(monthOutOfRange({ year: 2026, month: 8 }, undefined, "2026-09-29")).toBe(false);
    expect(monthOutOfRange({ year: 2026, month: 7 }, "2026-09-01")).toBe(true);
  });
  it("shifts a view across years", () => {
    expect(shiftMonth({ year: 2026, month: 11 }, 1)).toEqual({ year: 2027, month: 0 });
    expect(shiftMonth({ year: 2026, month: 0 }, -1)).toEqual({ year: 2025, month: 11 });
  });
});

describe("times", () => {
  it("formats 24 hour values as 12 hour", () => {
    expect(formatPickerTime("11:30")).toBe("11:30 AM");
    expect(formatPickerTime("00:00")).toBe("12:00 AM");
    expect(formatPickerTime("12:05")).toBe("12:05 PM");
    expect(formatPickerTime("23:30")).toBe("11:30 PM");
    expect(formatPickerTime("")).toBe("");
    expect(formatPickerTime("25:00")).toBe("");
  });
  it("reads typed times", () => {
    expect(parseTypedTime("11:15 AM")).toBe("11:15");
    expect(parseTypedTime("9pm")).toBe("21:00");
    expect(parseTypedTime("12 am")).toBe("00:00");
    expect(parseTypedTime("12:30pm")).toBe("12:30");
    expect(parseTypedTime("14:30")).toBe("14:30");
    expect(parseTypedTime("930")).toBe("09:30");
    expect(parseTypedTime("13 pm")).toBeNull();
    expect(parseTypedTime("11:75")).toBeNull();
    expect(parseTypedTime("soon")).toBeNull();
  });
  it("round-trips clock parts", () => {
    for (const value of ["00:00", "00:30", "11:30", "12:00", "12:30", "23:30"]) {
      const parts = toClockParts(value);
      expect(parts && fromClockParts(parts)).toBe(value);
    }
  });
});
