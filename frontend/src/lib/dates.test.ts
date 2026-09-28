import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  daysFromToday,
  formatAppointment,
  formatDateInput,
  formatDay,
  formatDayHeading,
  formatMonthDay,
  formatTime,
  formatWhen,
  fromKarachiInputs,
  isPastServerTime,
  karachiDateInput,
  parseServerDateTime,
  toKarachiInputs,
} from "./dates.ts";

// 11:45 AM on Sep 27, 2026 in Karachi. Server values below carry no zone marker, so they are UTC.
const now = new Date("2026-09-27T06:45:00Z");

describe("server date parsing", () => {
  it("reads a timezone-less server value as UTC rather than local time", () => {
    // SQL Server drops the offset, so this arrives without a Z. Parsed as local time in
    // Pakistan it would land five hours early and a live link would read as expired.
    expect(parseServerDateTime("2026-08-29T14:23:11")?.toISOString())
      .toBe("2026-08-29T14:23:11.000Z");
    expect(parseServerDateTime("2026-08-29T14:23:11Z")?.toISOString())
      .toBe("2026-08-29T14:23:11.000Z");
    expect(parseServerDateTime("2026-08-29T19:23:11+05:00")?.toISOString())
      .toBe("2026-08-29T14:23:11.000Z");
    expect(parseServerDateTime(null)).toBeNull();
    expect(parseServerDateTime("not a date")).toBeNull();
  });

  it("does not flag a time as past before its UTC instant", () => {
    const loggedAt = "2026-09-25T10:15:00";
    expect(isPastServerTime(loggedAt, new Date("2026-09-25T10:14:00Z"))).toBe(false);
    expect(isPastServerTime(loggedAt, new Date("2026-09-25T10:16:00Z"))).toBe(true);
    expect(isPastServerTime(null)).toBe(false);
  });
});

describe("Karachi display formats", () => {
  // A browser far from Pakistan must still see Karachi wall-clock times.
  beforeEach(() => { vi.stubEnv("TZ", "America/Los_Angeles"); });
  afterEach(() => { vi.unstubAllEnvs(); });

  it("names today, tomorrow and yesterday by the Karachi calendar", () => {
    expect(formatWhen("2026-09-27T06:45:00", now)).toBe("Today, 11:45 AM");
    expect(formatWhen("2026-09-28T11:00:00", now)).toBe("Tomorrow, 4:00 PM");
    expect(formatWhen("2026-09-26T12:10:00", now)).toBe("Yesterday, 5:10 PM");
  });

  it("gives other days a date, with the year only outside the current one", () => {
    expect(formatWhen("2026-09-25T10:02:00", now)).toBe("Sep 25, 3:02 PM");
    expect(formatWhen("2025-09-25T10:02:00", now)).toBe("Sep 25, 2025, 3:02 PM");
  });

  it("rolls over at Karachi midnight, not UTC midnight", () => {
    // 19:30 UTC is 00:30 the next day in Karachi.
    expect(formatWhen("2026-09-27T19:30:00", now)).toBe("Tomorrow, 12:30 AM");
    expect(formatWhen("2026-09-27T18:00:00", new Date("2026-09-27T19:30:00Z"))).toBe("Yesterday, 11:00 PM");
    expect(formatDay("2026-09-27T19:30:00")).toBe("Sep 28, 2026");
    expect(formatMonthDay("2026-12-31T10:00:00", new Date("2026-12-31T19:30:00Z"))).toBe("Dec 31, 2026");
  });

  it("formats a day, a month-day and a time", () => {
    expect(formatDay("2026-09-27T06:45:00")).toBe("Sep 27, 2026");
    expect(formatMonthDay("2026-09-27T06:45:00", now)).toBe("Sep 27");
    expect(formatMonthDay("2025-09-27T06:45:00", now)).toBe("Sep 27, 2025");
    expect(formatTime("2026-09-27T05:20:00")).toBe("10:20 AM");
  });

  it("keeps the placeholder for a missing or unreadable time", () => {
    for (const value of [null, undefined, "", "not a date"]) {
      expect(formatWhen(value, now)).toBe("—");
      expect(formatDay(value)).toBe("—");
      expect(formatMonthDay(value, now)).toBe("—");
      expect(formatTime(value)).toBe("—");
    }
  });
});

describe("Karachi day headings and appointments", () => {
  beforeEach(() => { vi.stubEnv("TZ", "America/Los_Angeles"); });
  afterEach(() => { vi.unstubAllEnvs(); });

  it("names today and yesterday, and gives other days their weekday", () => {
    expect(formatDayHeading("2026-09-27T01:00:00", now)).toBe("Today");
    expect(formatDayHeading("2026-09-26T12:00:00", now)).toBe("Yesterday");
    expect(formatDayHeading("2026-09-25T12:00:00", now)).toBe("Fri, Sep 25");
    expect(formatDayHeading("2025-09-25T12:00:00", now)).toBe("Thu, Sep 25, 2025");
    expect(formatDayHeading(null, now)).toBe("—");
  });

  it("writes an appointment with its weekday and time", () => {
    expect(formatAppointment("2026-10-03T06:00:00", now)).toBe("Sat, Oct 3 · 11:00 AM");
    expect(formatAppointment("2027-01-02T06:00:00", now)).toBe("Sat, Jan 2, 2027 · 11:00 AM");
    expect(formatAppointment(undefined, now)).toBe("—");
  });
});

describe("Karachi date and time inputs", () => {
  // The inputs hold Karachi wall-clock values wherever the browser is.
  beforeEach(() => { vi.stubEnv("TZ", "America/Los_Angeles"); });
  afterEach(() => { vi.unstubAllEnvs(); });

  it("turns an instant into the Karachi date and time", () => {
    expect(toKarachiInputs(now)).toEqual({ date: "2026-09-27", time: "11:45" });
    expect(toKarachiInputs("2026-09-27T19:30:00")).toEqual({ date: "2026-09-28", time: "00:30" });
    expect(toKarachiInputs("not a date")).toEqual({ date: "", time: "" });
  });

  it("turns a Karachi date and time back into the same instant", () => {
    expect(fromKarachiInputs("2026-09-28", "16:00")).toBe("2026-09-28T11:00:00.000Z");
    expect(fromKarachiInputs("2026-09-28", "00:30")).toBe("2026-09-27T19:30:00.000Z");
    expect(fromKarachiInputs("2026-09-28", "")).toBeNull();
    expect(fromKarachiInputs("", "16:00")).toBeNull();
  });

  it("counts days on the Karachi calendar", () => {
    expect(karachiDateInput(0, now)).toBe("2026-09-27");
    expect(karachiDateInput(1, now)).toBe("2026-09-28");
    // 20:00 UTC is already the next day in Karachi.
    expect(karachiDateInput(0, new Date("2026-09-27T20:00:00Z"))).toBe("2026-09-28");
    expect(daysFromToday("2026-10-04", now)).toBe(7);
    expect(daysFromToday("2026-09-27", now)).toBe(0);
    expect(formatDateInput("2026-10-03")).toBe("Sat, Oct 3");
    expect(formatDateInput("")).toBe("");
  });
});
