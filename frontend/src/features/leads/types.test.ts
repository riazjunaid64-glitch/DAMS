import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { formatDateTime, isPastServerTime } from "./types.ts";

// The API returns stored UTC times without a zone marker.
const loggedAt = "2026-09-25T10:15:00";

describe("lead server timestamps", () => {
  beforeEach(() => { vi.stubEnv("TZ", "Asia/Karachi"); });
  afterEach(() => { vi.unstubAllEnvs(); });

  it("shows a stored time as the same instant the staff member logged", () => {
    expect(formatDateTime(loggedAt)).toBe(formatDateTime(`${loggedAt}Z`));
    expect(formatDateTime(loggedAt)).toBe(new Date("2026-09-25T15:15:00+05:00").toLocaleString(undefined, {
      year: "numeric", month: "numeric", day: "numeric", hour: "numeric", minute: "2-digit",
    }));
  });

  it("does not flag a next action overdue before its UTC time has passed", () => {
    expect(isPastServerTime(loggedAt, new Date("2026-09-25T10:14:00Z"))).toBe(false);
    expect(isPastServerTime(loggedAt, new Date("2026-09-25T10:16:00Z"))).toBe(true);
    expect(isPastServerTime(null)).toBe(false);
  });

  it("keeps the placeholder for a missing or unreadable time", () => {
    expect(formatDateTime(null)).toBe("—");
    expect(formatDateTime("not a date")).toBe("—");
  });
});
