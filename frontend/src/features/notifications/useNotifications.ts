import { useCallback, useEffect, useRef, useState } from "react";
import { api } from "../../api/api.ts";
import { fetchCapabilities, fetchSummary, markAllRead, markRead, openNotification } from "./notificationApi.ts";
import { markSummaryCategoryRead, markSummaryItemRead } from "./summaryState.ts";
import type {
  NotificationCapabilities,
  NotificationCategory,
  NotificationItem,
  NotificationSummary,
} from "./types.ts";

interface UseNotificationsResult {
  summary: NotificationSummary;
  capabilities: NotificationCapabilities | null;
  loading: boolean;
  error: string | null;
  /** The newest notification since the last one that was acknowledged, for the toast. */
  incoming: NotificationItem | null;
  dismissIncoming: () => void;
  refresh: () => Promise<void>;
  markOneRead: (id: number) => Promise<void>;
  markEverythingRead: (category?: NotificationCategory | null) => Promise<void>;
  open: (id: number) => Promise<{ allowed: boolean; deepLink: string | null; message: string }>;
}

const EMPTY: NotificationSummary = { unreadCount: 0, unreadByCategory: {}, recent: [] };

/**
 * Keeps one user's bell, badge and drawer in step with the server.
 *
 * Three things feed it, in order of immediacy: a Server-Sent Events stream for instant
 * updates while DAMS is open, a slow poll as a safety net, and a refresh whenever the tab
 * regains focus. The stream only ever carries "something changed" — the actual notifications
 * are re-read through the normal authorised endpoint, so a live connection can never show
 * more than the inbox itself would.
 */
export function useNotifications(accountKey: string | null): UseNotificationsResult {
  const [summary, setSummary] = useState<NotificationSummary>(EMPTY);
  const [capabilities, setCapabilities] = useState<NotificationCapabilities | null>(null);
  const [loading, setLoading] = useState(false);
  const [notificationError, setNotificationError] = useState<string | null>(null);
  const [capabilitiesError, setCapabilitiesError] = useState<string | null>(null);
  const [incoming, setIncoming] = useState<NotificationItem | null>(null);

  // Guards against a stale response overwriting a newer one after a fast sequence of events.
  const requestId = useRef(0);
  const lastSeenId = useRef<number | null>(null);

  const refresh = useCallback(async () => {
    if (!accountKey) return;

    const id = ++requestId.current;
    try {
      const next = await fetchSummary(12);
      if (id !== requestId.current) return;

      setSummary(next);
      setNotificationError(null);

      const newest = next.recent.find((item) => !item.isRead);
      if (newest && lastSeenId.current !== null && newest.id > lastSeenId.current) {
        setIncoming(newest);
      }
      // On the very first load there is nothing "new" — everything is history.
      lastSeenId.current = next.recent.length > 0 ? Math.max(...next.recent.map((n) => n.id)) : 0;
    } catch (err) {
      if (id !== requestId.current) return;
      setNotificationError(err instanceof Error ? err.message : "Notifications could not be loaded.");
    }
  }, [accountKey]);

  // Initial load.
  useEffect(() => {
    requestId.current += 1;
    setSummary(EMPTY);
    setCapabilities(null);
    setIncoming(null);
    setNotificationError(null);
    setCapabilitiesError(null);
    lastSeenId.current = null;

    if (!accountKey) {
      setLoading(false);
      return;
    }

    let cancelled = false;
    setLoading(true);
    const id = ++requestId.current;
    void Promise.allSettled([fetchSummary(12), fetchCapabilities()])
      .then(([summaryResult, capabilitiesResult]) => {
        if (cancelled) return;

        if (capabilitiesResult.status === "fulfilled") {
          setCapabilities(capabilitiesResult.value);
          setCapabilitiesError(null);
        } else {
          setCapabilitiesError(
            capabilitiesResult.reason instanceof Error
              ? capabilitiesResult.reason.message
              : "Notification capabilities could not be loaded."
          );
        }

        // A live refresh may have completed while this initial request was in flight.
        if (id !== requestId.current) return;
        if (summaryResult.status === "fulfilled") {
          setSummary(summaryResult.value);
          setNotificationError(null);
          lastSeenId.current = summaryResult.value.recent.length > 0
            ? Math.max(...summaryResult.value.recent.map((item) => item.id))
            : 0;
        } else {
          setNotificationError(
            summaryResult.reason instanceof Error
              ? summaryResult.reason.message
              : "Notifications could not be loaded."
          );
        }
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
      requestId.current += 1;
    };
  }, [accountKey]);

  // Live stream, with reconnection. A failure here is never surfaced as an error: the poll
  // below keeps the inbox correct, just less promptly.
  useEffect(() => {
    if (!accountKey) return;

    let cancelled = false;
    let attempt = 0;
    const controller = new AbortController();

    const run = async () => {
      while (!cancelled) {
        try {
          const response = await api("/api/notifications/stream", {
            signal: controller.signal,
            headers: { Accept: "text/event-stream" },
            cache: "no-store",
          });

          if (!response.ok || !response.body) throw new Error("stream unavailable");

          attempt = 0;
          const reader = response.body.getReader();
          const decoder = new TextDecoder();
          let buffer = "";

          while (!cancelled) {
            const { done, value } = await reader.read();
            if (done) break;

            buffer += decoder.decode(value, { stream: true });
            const frames = buffer.split("\n\n");
            buffer = frames.pop() ?? "";

            for (const frame of frames) {
              // Both a real event and the keep-alive comment are treated as "go and look":
              // that is what makes a missed signal cost one heartbeat instead of a message.
              if (frame.includes("data:") || frame.startsWith(": ping")) {
                await refresh();
              }
            }
          }
        } catch {
          if (cancelled) return;
        }

        if (cancelled) return;

        // Back off so a server restart does not turn into a reconnect storm.
        attempt = Math.min(attempt + 1, 6);
        const delay = Math.min(2000 * 2 ** (attempt - 1), 60_000);
        await new Promise((resolve) => setTimeout(resolve, delay));
      }
    };

    void run();

    return () => {
      cancelled = true;
      controller.abort();
    };
  }, [accountKey, refresh]);

  // Safety net: a slow poll while the tab is visible, plus a refresh when it regains focus.
  useEffect(() => {
    if (!accountKey) return;

    const tick = () => {
      if (document.visibilityState === "visible") void refresh();
    };

    const interval = window.setInterval(tick, 90_000);
    document.addEventListener("visibilitychange", tick);
    window.addEventListener("focus", tick);

    return () => {
      window.clearInterval(interval);
      document.removeEventListener("visibilitychange", tick);
      window.removeEventListener("focus", tick);
    };
  }, [accountKey, refresh]);

  const markOneRead = useCallback(
    async (id: number) => {
      // Optimistic: the badge should never lag behind the click.
      setSummary((current) => markSummaryItemRead(current, id));

      try {
        await markRead(id);
      } finally {
        await refresh();
      }
    },
    [refresh]
  );

  const markEverythingRead = useCallback(
    async (category?: NotificationCategory | null) => {
      setSummary((current) => markSummaryCategoryRead(current, category));

      try {
        await markAllRead(category ?? null);
      } finally {
        await refresh();
      }
    },
    [refresh]
  );

  const open = useCallback(
    async (id: number) => {
      const result = await openNotification(id);
      await refresh();
      return { allowed: result.allowed, deepLink: result.deepLink, message: result.message };
    },
    [refresh]
  );

  return {
    summary,
    capabilities,
    loading,
    error: notificationError ?? capabilitiesError,
    incoming,
    dismissIncoming: () => setIncoming(null),
    refresh,
    markOneRead,
    markEverythingRead,
    open,
  };
}
