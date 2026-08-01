import type { NotificationCategory, NotificationSummary } from "./types.ts";

export function markSummaryItemRead(summary: NotificationSummary, id: number): NotificationSummary {
  const item = summary.recent.find((notification) => notification.id === id && !notification.isRead);
  const unreadByCategory = { ...summary.unreadByCategory };
  if (item) unreadByCategory[item.category] = Math.max(0, (unreadByCategory[item.category] ?? 0) - 1);

  return {
    ...summary,
    unreadCount: Math.max(0, summary.unreadCount - (item ? 1 : 0)),
    unreadByCategory,
    recent: summary.recent.map((notification) =>
      notification.id === id ? { ...notification, isRead: true } : notification
    ),
  };
}

export function markSummaryCategoryRead(
  summary: NotificationSummary,
  category?: NotificationCategory | null
): NotificationSummary {
  const categoryUnread = category ? summary.unreadByCategory[category] ?? 0 : summary.unreadCount;
  return {
    ...summary,
    unreadCount: Math.max(0, summary.unreadCount - categoryUnread),
    unreadByCategory: category ? { ...summary.unreadByCategory, [category]: 0 } : {},
    recent: summary.recent.map((notification) =>
      !category || notification.category === category ? { ...notification, isRead: true } : notification
    ),
  };
}
