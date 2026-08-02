import { useCallback, useEffect, useId, useRef, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { notificationCategoryLabels } from "./capabilities.ts";
import { relativeTime } from "./types.ts";
import type { NotificationItem } from "./types.ts";
import { useNotifications } from "./useNotifications.ts";

/**
 * The header bell, its unread badge and the recent-notifications drawer.
 *
 * The drawer is a dialog: it traps nothing the user cannot escape from, closes on Escape and
 * on an outside click, and returns focus to the bell. Everything it shows is also reachable
 * from the full notifications page, so nothing here is the only route to a message.
 */
export default function NotificationBell({ accountKey }: { accountKey: string | null }) {
  const [open, setOpen] = useState(false);
  const [busyId, setBusyId] = useState<number | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const navigate = useNavigate();
  const panelId = useId();
  const bellRef = useRef<HTMLButtonElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);

  const { summary, capabilities, loading, error, incoming, dismissIncoming, markEverythingRead, open: openOne } =
    useNotifications(accountKey);
  const categoryLabels = notificationCategoryLabels(capabilities);
  const signedIn = accountKey !== null;

  // A push click while DAMS is open arrives as a message from the service worker rather than
  // opening a second tab.
  useEffect(() => {
    if (!signedIn || !("serviceWorker" in navigator)) return;

    const onMessage = (event: MessageEvent) => {
      const data = event.data as { type?: string; url?: string } | null;
      if (data?.type === "dams-notification-click" && typeof data.url === "string" && data.url.startsWith("/")) {
        navigate(data.url);
      }
    };

    navigator.serviceWorker.addEventListener("message", onMessage);
    return () => navigator.serviceWorker.removeEventListener("message", onMessage);
  }, [navigate, signedIn]);

  useEffect(() => {
    if (!open) return;

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        setOpen(false);
        bellRef.current?.focus();
      }
    };

    const onPointerDown = (event: PointerEvent) => {
      const target = event.target as Node;
      if (!panelRef.current?.contains(target) && !bellRef.current?.contains(target)) setOpen(false);
    };

    document.addEventListener("keydown", onKeyDown);
    document.addEventListener("pointerdown", onPointerDown);
    return () => {
      document.removeEventListener("keydown", onKeyDown);
      document.removeEventListener("pointerdown", onPointerDown);
    };
  }, [open]);

  const handleOpen = useCallback(
    async (item: NotificationItem) => {
      setBusyId(item.id);
      setNotice(null);
      try {
        const result = await openOne(item.id);
        if (result.allowed && result.deepLink) {
          setOpen(false);
          navigate(result.deepLink);
        } else {
          setNotice(result.message);
        }
      } catch (err) {
        setNotice(err instanceof Error ? err.message : "That notification could not be opened.");
      } finally {
        setBusyId(null);
      }
    },
    [navigate, openOne]
  );

  if (!signedIn) return null;

  const unread = summary.unreadCount;

  return (
    <>
      <div className="relative">
        <button
          ref={bellRef}
          type="button"
          aria-haspopup="dialog"
          aria-expanded={open}
          aria-controls={open ? panelId : undefined}
          aria-label={unread > 0 ? `Notifications, ${unread} unread` : "Notifications"}
          onClick={() => setOpen((value) => !value)}
          className="relative inline-flex h-10 w-10 items-center justify-center rounded-full border border-white/15 bg-white/10 text-[var(--nav-text)] transition hover:bg-white/20 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--nav-text-active)]"
        >
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            <path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9" />
            <path d="M13.73 21a2 2 0 0 1-3.46 0" />
          </svg>
          {unread > 0 && (
            <span className="absolute -right-0.5 -top-0.5 inline-flex min-w-[1.15rem] items-center justify-center rounded-full bg-[var(--accent-warm)] px-1 text-[10px] font-bold leading-[1.15rem] text-[var(--accent)]">
              {unread > 99 ? "99+" : unread}
            </span>
          )}
        </button>

        {open && (
          <div
            ref={panelRef}
            id={panelId}
            role="dialog"
            aria-modal="false"
            aria-label="Recent notifications"
            className="fixed inset-x-3 top-[calc(var(--site-nav-height)+0.5rem)] z-[60] max-h-[min(32rem,calc(100vh-6rem))] overflow-hidden rounded-2xl border border-[var(--border)] bg-[var(--bg-card)] shadow-2xl sm:absolute sm:inset-x-auto sm:right-0 sm:top-[calc(100%+0.6rem)] sm:w-[26rem]"
          >
            <div className="flex items-center justify-between gap-3 border-b border-[var(--border)] px-4 py-3">
              <div>
                <h2 className="text-sm font-semibold text-[var(--text-heading)]">Notifications</h2>
                <p className="text-xs text-[var(--text-muted)]">
                  {unread > 0 ? `${unread} unread` : "You are all caught up"}
                </p>
              </div>
              {unread > 0 && (
                <button
                  type="button"
                  onClick={() => void markEverythingRead()}
                  className="rounded-lg px-2.5 py-1.5 text-xs font-semibold text-[var(--accent)] transition hover:bg-[var(--surface-glass-hover)]"
                >
                  Mark all read
                </button>
              )}
            </div>

            <div className="max-h-[22rem] overflow-y-auto overscroll-contain">
              {loading && summary.recent.length === 0 && (
                <p className="px-4 py-8 text-center text-sm text-[var(--text-muted)]">Loading notifications…</p>
              )}

              {error && (
                <div role="alert" className="m-3 rounded-lg border border-[var(--accent-rose)]/30 bg-[var(--accent-rose-glow)] px-3 py-2 text-xs text-[var(--accent-rose)]">
                  {error}
                </div>
              )}

              {notice && (
                <div role="status" className="m-3 rounded-lg border border-[var(--border)] bg-[var(--surface-glass)] px-3 py-2 text-xs text-[var(--text-secondary)]">
                  {notice}
                </div>
              )}

              {!loading && !error && summary.recent.length === 0 && (
                <div className="px-4 py-10 text-center">
                  <p className="text-sm font-medium text-[var(--text-heading)]">Nothing yet</p>
                  <p className="mt-1 text-xs text-[var(--text-muted)]">
                    {capabilities?.emptyStateMessage ?? "Your available updates will appear here."}
                  </p>
                </div>
              )}

              <ul className="divide-y divide-[var(--border)]">
                {summary.recent.map((item) => (
                  <li key={item.id}>
                    <button
                      type="button"
                      disabled={busyId === item.id}
                      onClick={() => void handleOpen(item)}
                      className={`flex w-full gap-3 px-4 py-3 text-left transition hover:bg-[var(--bg-card-hover)] focus-visible:outline focus-visible:-outline-offset-2 focus-visible:outline-[var(--accent)] disabled:opacity-60 ${
                        item.isRead ? "" : "bg-[var(--surface-glass)]"
                      }`}
                    >
                      <span
                        aria-hidden="true"
                        className={`mt-1.5 h-2 w-2 shrink-0 rounded-full ${
                          item.isRead ? "bg-transparent" : "bg-[var(--accent)]"
                        }`}
                      />
                      <span className="min-w-0 flex-1">
                        <span className="flex items-center gap-2">
                          <span className="truncate text-sm font-semibold text-[var(--text-heading)]">{item.title}</span>
                          {!item.isRead && <span className="sr-only">Unread</span>}
                        </span>
                        <span className="mt-0.5 line-clamp-2 block text-xs text-[var(--text-secondary)]">
                          {item.message}
                        </span>
                        <span className="mt-1.5 flex flex-wrap items-center gap-2 text-[11px] text-[var(--text-muted)]">
                          <span className="rounded-md bg-[var(--surface-glass)] px-1.5 py-0.5 font-medium">
                            {categoryLabels.get(item.category) ?? item.category}
                          </span>
                          {(item.priority === "High" || item.priority === "Critical") && (
                            <span className="font-semibold text-[var(--accent-rose)]">{item.priority}</span>
                          )}
                          <time dateTime={item.createdAt}>{relativeTime(item.createdAt)}</time>
                        </span>
                      </span>
                    </button>
                  </li>
                ))}
              </ul>
            </div>

            <div className="border-t border-[var(--border)] px-4 py-2.5 text-center">
              <Link
                to="/notifications"
                onClick={() => setOpen(false)}
                className="text-sm font-semibold text-[var(--accent)] hover:underline"
              >
                View all notifications
              </Link>
            </div>
          </div>
        )}
      </div>

      {/* A quiet in-app alert for something that arrived while this page was open. */}
      {incoming && !open && (
        <div
          role="status"
          aria-live="polite"
          className="fixed bottom-4 right-4 z-[70] w-[min(22rem,calc(100vw-2rem))] animate-scale-in rounded-xl border border-[var(--border)] bg-[var(--bg-card)] p-3 shadow-xl"
        >
          <div className="flex items-start gap-3">
            <div className="min-w-0 flex-1">
              <p className="truncate text-sm font-semibold text-[var(--text-heading)]">{incoming.title}</p>
              <p className="mt-0.5 line-clamp-2 text-xs text-[var(--text-secondary)]">{incoming.message}</p>
              <button
                type="button"
                onClick={() => {
                  dismissIncoming();
                  void handleOpen(incoming);
                }}
                className="mt-2 text-xs font-semibold text-[var(--accent)] hover:underline"
              >
                Open
              </button>
            </div>
            <button
              type="button"
              aria-label="Dismiss notification"
              onClick={dismissIncoming}
              className="rounded-md p-1 text-[var(--text-muted)] transition hover:bg-[var(--surface-glass-hover)]"
            >
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true">
                <line x1="18" y1="6" x2="6" y2="18" />
                <line x1="6" y1="6" x2="18" y2="18" />
              </svg>
            </button>
          </div>
        </div>
      )}
    </>
  );
}
