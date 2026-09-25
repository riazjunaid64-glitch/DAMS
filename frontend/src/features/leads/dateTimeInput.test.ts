import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { oneHourFromNow, toLocalInput } from "./dateTimeInput.ts";

const now = new Date("2026-09-25T10:15:42.500Z");
// LeadCommunicationService rejects an occurredAt more than five minutes past the server clock.
const serverFutureTolerance = 5 * 60 * 1000;

// Mirrors how LeadActionDialog turns the input value back into the timestamp it posts.
const submitted = (inputValue: string) => new Date(inputValue).toISOString();

describe("datetime-local defaults", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(now);
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllEnvs();
  });

  it("defaults a logged communication to the current Pakistan wall-clock minute", () => {
    vi.stubEnv("TZ", "Asia/Karachi");

    const value = toLocalInput(new Date());

    expect(value).toBe("2026-09-25T15:15");
    expect(submitted(value)).toBe("2026-09-25T10:15:00.000Z");
  });

  it.each(["Asia/Karachi", "UTC", "America/New_York", "Asia/Kolkata", "Pacific/Chatham"])(
    "never posts a default communication time the server treats as future in %s",
    (timeZone) => {
      vi.stubEnv("TZ", timeZone);

      const posted = new Date(submitted(toLocalInput(new Date()))).getTime();

      expect(posted).toBeLessThanOrEqual(now.getTime());
      expect(posted).toBeGreaterThan(now.getTime() - 60 * 1000);
      expect(posted).toBeLessThanOrEqual(now.getTime() + serverFutureTolerance);
    });

  it("keeps scheduling defaults in the future", () => {
    vi.stubEnv("TZ", "Asia/Karachi");

    const value = toLocalInput(oneHourFromNow());

    expect(value).toBe("2026-09-25T16:15");
    expect(new Date(submitted(value)).getTime()).toBeGreaterThan(now.getTime());
  });

  it("shows an existing schedule in local time", () => {
    vi.stubEnv("TZ", "Asia/Karachi");

    expect(toLocalInput("2026-09-26T07:30:00Z")).toBe("2026-09-26T12:30");
  });
});
