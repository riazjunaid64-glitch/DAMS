import { describe, expect, it } from "vitest";
import { notificationGroups, notificationVisual, notificationWhen } from "./notificationPresentation.ts";
import type { NotificationItem } from "./types.ts";

const now = new Date("2026-09-28T08:00:00Z");

const item = (overrides: Partial<NotificationItem>): NotificationItem => ({
  id: 1,
  category: "FollowUps",
  type: "FollowUpDue",
  priority: "Normal",
  module: "Leads",
  title: "Follow-up due",
  message: "Call back",
  entityType: "Lead",
  entityId: 1,
  deepLink: "/crm/1",
  isRead: false,
  isEscalation: false,
  createdAt: "2026-09-28T06:00:00Z",
  readAt: null,
  expiresAt: null,
  ...overrides,
});

describe("notification rows", () => {
  it("picks the icon from the kind of event", () => {
    expect(notificationVisual({ type: "FirstContactOverdue" })).toEqual({ kind: "call", tile: "red" });
    expect(notificationVisual({ type: "LeadAssigned" })).toEqual({ kind: "lead", tile: "blue" });
    expect(notificationVisual({ type: "FollowUpDue" })).toEqual({ kind: "followup", tile: "gold" });
    expect(notificationVisual({ type: "SiteVisitReminder" })).toEqual({ kind: "visit", tile: "gold" });
    expect(notificationVisual({ type: "PaymentReceipt" })).toEqual({ kind: "other", tile: "grey" });
  });

  it("says how long ago a notification from today was, and the day after that", () => {
    expect(notificationWhen("2026-09-28T00:00:00Z", now)).toBe("8h ago");
    expect(notificationWhen("2026-09-27T10:00:00Z", now)).toBe("Yesterday");
    expect(notificationWhen("2026-09-25T10:00:00Z", now)).toBe("Sep 25");
  });

  it("groups today apart from everything earlier", () => {
    const groups = notificationGroups([
      item({ id: 1, createdAt: "2026-09-28T06:00:00Z" }),
      item({ id: 2, createdAt: "2026-09-27T10:00:00Z" }),
      item({ id: 3, createdAt: "2026-09-25T10:00:00Z" }),
    ], now);
    expect(groups.map((group) => group.label)).toEqual(["Today", "Earlier"]);
    expect(groups[0]?.items.map((row) => row.id)).toEqual([1]);
    expect(groups[1]?.items.map((row) => row.id)).toEqual([2, 3]);
  });
});
