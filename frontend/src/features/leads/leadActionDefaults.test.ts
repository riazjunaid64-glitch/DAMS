import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { initialForm } from "./leadActionDefaults.ts";
import type { FollowUp, Lead, SiteVisit } from "./types.ts";

const now = new Date("2026-09-25T10:15:42.500Z");
// LeadCommunicationService rejects an occurredAt more than five minutes past the server clock.
const serverFutureTolerance = 5 * 60 * 1000;
const lead = { assignedEmployeeId: 4, fullName: "Ayesha Khan" } as Lead;

// Mirrors how LeadActionDialog turns an input value back into the timestamp it posts.
const posted = (inputValue: string | boolean) => new Date(String(inputValue)).toISOString();

describe("lead action form defaults", () => {
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

    const form = initialForm({ type: "communication" }, lead);

    expect(form.occurredAt).toBe("2026-09-25T15:15");
    expect(posted(form.occurredAt)).toBe("2026-09-25T10:15:00.000Z");
  });

  it.each(["Asia/Karachi", "UTC", "America/New_York", "Asia/Kolkata", "Pacific/Chatham"])(
    "never posts a default communication time the server treats as future in %s",
    (timeZone) => {
      vi.stubEnv("TZ", timeZone);

      const sent = new Date(posted(initialForm({ type: "communication" }, lead).occurredAt)).getTime();

      expect(sent).toBeLessThanOrEqual(now.getTime());
      expect(sent).toBeGreaterThan(now.getTime() - 60 * 1000);
      expect(sent).toBeLessThanOrEqual(now.getTime() + serverFutureTolerance);
    });

  it("keeps follow-up and site visit defaults an hour in the future", () => {
    vi.stubEnv("TZ", "Asia/Karachi");

    expect(initialForm({ type: "followUp" }, lead).dueAt).toBe("2026-09-25T16:15");
    expect(initialForm({ type: "siteVisit" }, lead).scheduledAt).toBe("2026-09-25T16:15");
    expect(initialForm({ type: "siteVisit" }, lead).remindAt).toBe("");
  });

  it("prefills a follow-up reschedule with the stored UTC time, unchanged on save", () => {
    vi.stubEnv("TZ", "Asia/Karachi");
    const item = { dueAt: "2026-09-26T07:30:00" } as FollowUp;

    const form = initialForm({ type: "rescheduleFollowUp", item }, lead);

    expect(form.dueAt).toBe("2026-09-26T12:30");
    expect(posted(form.dueAt)).toBe("2026-09-26T07:30:00.000Z");
  });

  it("prefills a site visit reschedule with the stored UTC time, unchanged on save", () => {
    vi.stubEnv("TZ", "Asia/Karachi");
    const item = { scheduledAt: "2026-09-26T07:30:00", remindAt: "2026-09-26T06:30:00", meetingLocation: "Site office" } as SiteVisit;

    const form = initialForm({ type: "rescheduleVisit", item }, lead);

    expect(form.scheduledAt).toBe("2026-09-26T12:30");
    expect(posted(form.scheduledAt)).toBe("2026-09-26T07:30:00.000Z");
    expect(form.remindAt).toBe("2026-09-26T11:30");
    expect(posted(form.remindAt)).toBe("2026-09-26T06:30:00.000Z");
  });
});
