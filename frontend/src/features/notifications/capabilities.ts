import type { NotificationCapabilities, NotificationCategory } from "./types.ts";

export function notificationCategoryLabels(
  capabilities: NotificationCapabilities | null
): ReadonlyMap<NotificationCategory, string> {
  return new Map(capabilities?.categories.map((item) => [item.category, item.label]) ?? []);
}

export function notificationCategoryIsAllowed(
  capabilities: NotificationCapabilities | null,
  category: NotificationCategory
): boolean {
  return capabilities?.categories.some((item) => item.category === category) ?? false;
}
