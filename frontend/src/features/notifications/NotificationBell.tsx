import { useCallback, useEffect, useId, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { Button, IconBell, IconClose, useToast } from "../../components/ui";
import { MenuPanel } from "../../components/ui/MenuPanel.tsx";
import { usePopover } from "../../components/ui/usePopover.ts";
import { NotificationRow } from "./NotificationItem.tsx";
import { notificationGroups } from "./notificationPresentation.ts";
import type { NotificationItem } from "./types.ts";
import { useNotifications } from "./useNotifications.ts";

/**
 * The header bell. The panel is the same floating menu the rest of the app uses, and everything
 * in it is also on the notifications page.
 */
export default function NotificationBell({ accountKey }: { accountKey: string | null }) {
  const [open, setOpen] = useState(false);
  const [busyId, setBusyId] = useState<number | null>(null);
  const navigate = useNavigate();
  const toast = useToast();
  const panelId = useId();
  const signedIn = accountKey !== null;

  const { summary, loading, error, incoming, dismissIncoming, markEverythingRead, open: openOne } =
    useNotifications(accountKey);

  const close = useCallback(() => setOpen(false), []);
  const { anchorRef, panelRef, style } = usePopover<HTMLButtonElement, HTMLDivElement>({
    open,
    onClose: close,
    onEscape: () => {
      close();
      anchorRef.current?.focus();
    },
    preferredHeight: 520,
  });

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

  const handleOpen = useCallback(async (item: NotificationItem) => {
    setBusyId(item.id);
    try {
      const result = await openOne(item.id);
      if (result.allowed && result.deepLink) {
        close();
        navigate(result.deepLink);
      } else {
        toast.error(result.message);
      }
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "That notification could not be opened.");
    } finally {
      setBusyId(null);
    }
  }, [close, navigate, openOne, toast]);

  const markRead = async () => {
    try {
      await markEverythingRead();
      toast.success("All notifications marked read");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Notifications could not be marked read.");
    }
  };

  if (!signedIn) return null;

  const unread = summary.unreadCount;
  const groups = notificationGroups(summary.recent);

  return (
    <>
      <button
        ref={anchorRef}
        type="button"
        aria-haspopup="dialog"
        aria-expanded={open}
        aria-controls={open ? panelId : undefined}
        aria-label={unread > 0 ? `Notifications, ${unread} unread` : "Notifications"}
        onClick={() => setOpen((value) => !value)}
        className="relative inline-flex size-10 cursor-pointer items-center justify-center rounded-full border border-line bg-card text-ink transition hover:bg-page focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary"
      >
        <IconBell size={18} />
        {unread > 0 && (
          <span className="absolute -right-0.5 -top-0.5 inline-flex min-w-[1.15rem] items-center justify-center rounded-full bg-danger px-1 text-[10px] font-bold leading-[1.15rem] text-white ring-2 ring-card">
            {unread > 99 ? "99+" : unread}
          </span>
        )}
      </button>

      {open && (
        <MenuPanel
          ref={panelRef}
          id={panelId}
          role="menu"
          label="Notifications"
          style={style}
          className="w-[min(24rem,calc(100vw-1rem))] p-0"
        >
          <div className="flex items-center justify-between gap-3 border-b border-line-soft px-4 py-3">
            <h2 className="m-0 text-body font-extrabold text-ink">Notifications</h2>
            <Button variant="link" disabled={unread === 0} onClick={() => void markRead()}>Mark all read</Button>
          </div>

          <div className="max-h-[22rem] overflow-y-auto overscroll-contain">
            {loading && summary.recent.length === 0 && (
              <div className="flex flex-col gap-2 p-3">
                <div className="h-16 animate-pulse rounded-card bg-track" />
                <div className="h-16 animate-pulse rounded-card bg-track" />
              </div>
            )}
            {error && <p role="alert" className="px-4 py-3 text-small text-danger">{error}</p>}
            {!loading && !error && summary.recent.length === 0 && (
              <p className="px-4 py-10 text-center text-sm font-bold text-ink">Nothing yet</p>
            )}
            {groups.map((group) => (
              <section key={group.label}>
                <p className="px-4 pt-3 text-caption font-bold uppercase tracking-[0.4px] text-ink-muted">{group.label}</p>
                <ul className="m-0 list-none p-0">
                  {group.items.map((item) => (
                    <li key={item.id} className="border-t border-line-soft first:border-t-0">
                      <NotificationRow item={item} busy={busyId === item.id} onOpen={() => void handleOpen(item)} />
                    </li>
                  ))}
                </ul>
              </section>
            ))}
          </div>

          <div className="border-t border-line-soft px-4 py-3 text-center">
            <Link to="/notifications" onClick={close} className="text-sm font-bold text-gold-text no-underline hover:underline">
              See all notifications
            </Link>
          </div>
        </MenuPanel>
      )}

      {incoming && !open && (
        <div role="status" aria-live="polite" className="fixed bottom-4 right-4 z-[70] w-[min(22rem,calc(100vw-2rem))] rounded-card border border-line bg-card p-3 shadow-popup">
          <div className="flex items-start gap-3">
            <div className="min-w-0 flex-1">
              <p className="m-0 truncate text-sm font-extrabold text-ink">{incoming.title}</p>
              <p className="m-0 mt-0.5 line-clamp-2 text-small text-ink-2">{incoming.message}</p>
              <button
                type="button"
                onClick={() => { dismissIncoming(); void handleOpen(incoming); }}
                className="mt-2 cursor-pointer text-small font-bold text-gold-text hover:underline"
              >
                Open
              </button>
            </div>
            <button type="button" aria-label="Dismiss notification" onClick={dismissIncoming} className="cursor-pointer rounded-md p-1 text-ink-muted hover:bg-page">
              <IconClose size={14} />
            </button>
          </div>
        </div>
      )}
    </>
  );
}
