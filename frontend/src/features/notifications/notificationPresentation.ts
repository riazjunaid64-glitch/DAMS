import { formatDayHeading, formatMonthDay, parseServerDateTime } from "../../lib/dates.ts";
import type { NotificationItem } from "./types.ts";

export type NotificationKind = "call" | "lead" | "followup" | "visit" | "other";
export type NotificationTile = "red" | "blue" | "gold" | "grey";

/** Which icon and colour a notification row uses. */
export function notificationVisual(item: { type: string }): { kind: NotificationKind; tile: NotificationTile } {
  if (item.type.startsWith("FirstContact")) return { kind: "call", tile: "red" };
  if (item.type.startsWith("SiteVisit")) return { kind: "visit", tile: "gold" };
  if (item.type.startsWith("FollowUp")) return { kind: "followup", tile: "gold" };
  if (item.type.startsWith("Lead")) return { kind: "lead", tile: "blue" };
  return { kind: "other", tile: "grey" };
}

/**
 * Time on a notification row. Today stays relative ("8h ago"); yesterday and older days are
 * words, still on the Karachi clock. This is not formatWhen — a row does not repeat the clock time.
 */
export function notificationWhen(iso: string, now: Date = new Date()): string {
  const date = parseServerDateTime(iso);
  if (!date) return "";
  const heading = formatDayHeading(iso, now);
  if (heading === "Today") {
    const seconds = Math.round((now.getTime() - date.getTime()) / 1000);
    if (seconds < 45) return "just now";
    if (seconds < 3600) return `${Math.max(1, Math.round(seconds / 60))}m ago`;
    return `${Math.max(1, Math.round(seconds / 3600))}h ago`;
  }
  if (heading === "Yesterday") return "Yesterday";
  return formatMonthDay(iso, now);
}

/** TODAY, then everything else under EARLIER. Order inside each group is the list's order. */
export function notificationGroups(items: NotificationItem[], now: Date = new Date()): { label: string; items: NotificationItem[] }[] {
  const today: NotificationItem[] = [];
  const earlier: NotificationItem[] = [];
  for (const item of items) {
    if (formatDayHeading(item.createdAt, now) === "Today") today.push(item);
    else earlier.push(item);
  }
  return [
    ...(today.length > 0 ? [{ label: "Today", items: today }] : []),
    ...(earlier.length > 0 ? [{ label: "Earlier", items: earlier }] : []),
  ];
}
