import { useCallback, useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import type { User } from "../App.tsx";
import {
  Button,
  EmptyState,
  IconBell,
  LoadMore,
  PageHeader,
  Tabs,
  useToast,
} from "../components/ui";
import PushEnableCard from "../features/notifications/PushEnableCard.tsx";
import { NotificationRow } from "../features/notifications/NotificationItem.tsx";
import { fetchNotifications, fetchSummary, markAllRead, openNotification } from "../features/notifications/notificationApi.ts";
import { notificationGroups } from "../features/notifications/notificationPresentation.ts";
import type { NotificationItem } from "../features/notifications/types.ts";

const PAGE_SIZE = 20;
const PAGE = "mx-auto flex w-full max-w-[1500px] flex-col gap-4 px-4 py-5 md:gap-5 md:px-8 md:py-7";

/**
 * Every notification for the signed-in account. The server scopes the list; this page never
 * asks for somebody else's inbox.
 */
export default function NotificationsPage({ user }: { user: User | null }) {
  if (!user) {
    return (
      <div className={PAGE}>
        <EmptyState icon={<IconBell size={26} />} title="Sign in to see your notifications" message="Your notification inbox is private to your account." />
      </div>
    );
  }
  return <Inbox key={`${user.userId}:${user.role}`} />;
}

function Inbox() {
  const navigate = useNavigate();
  const toast = useToast();
  const [tab, setTab] = useState("all");
  const [items, setItems] = useState<NotificationItem[]>([]);
  const [total, setTotal] = useState(0);
  const [unread, setUnread] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [marking, setMarking] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<number | null>(null);
  const unreadOnly = tab === "unread";
  const request = useRef(0);

  const load = useCallback(async (nextPage: number, replace: boolean, keepVisible = false) => {
    const id = ++request.current;
    if (replace && !keepVisible) {
      setItems([]);
      setLoading(true);
    } else if (!replace) {
      setLoadingMore(true);
    }
    if (!keepVisible) setError(null);
    try {
      const result = await fetchNotifications({ unreadOnly, page: nextPage, pageSize: PAGE_SIZE });
      if (id !== request.current) return;
      setItems((current) => (replace ? result.items : [...current, ...result.items]));
      setTotal(result.totalCount);
      setUnread(result.unreadCount);
      setPage(nextPage);
      setError(null);
    } catch (err) {
      if (id !== request.current) return;
      const message = err instanceof Error ? err.message : "Your notifications could not be loaded.";
      if (replace && !keepVisible) setError(message);
      else toast.error(message);
    } finally {
      if (id === request.current) {
        setLoading(false);
        setLoadingMore(false);
      }
    }
  }, [toast, unreadOnly]);

  useEffect(() => {
    void load(1, true);
  }, [load]);

  // The bell already follows the live stream. While this page is open, refresh the list in place
  // so the Unread count moves without blanking what the person is reading.
  useEffect(() => {
    const tick = () => {
      if (document.visibilityState !== "visible") return;
      if (page === 1) void load(1, true, true);
      else void fetchSummary(1).then((summary) => setUnread(summary.unreadCount)).catch(() => undefined);
    };
    const timer = window.setInterval(tick, 30_000);
    window.addEventListener("focus", tick);
    return () => {
      window.clearInterval(timer);
      window.removeEventListener("focus", tick);
    };
  }, [load, page]);

  const markRead = async () => {
    setMarking(true);
    try {
      await markAllRead();
      toast.success("All notifications marked read");
      await load(1, true);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Notifications could not be marked read.");
    } finally {
      setMarking(false);
    }
  };

  const openOne = async (item: NotificationItem) => {
    setBusyId(item.id);
    try {
      const result = await openNotification(item.id);
      if (result.allowed && result.deepLink) {
        navigate(result.deepLink);
        return;
      }
      toast.error(result.message);
      await load(1, true);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "That notification could not be opened.");
    } finally {
      setBusyId(null);
    }
  };

  const groups = notificationGroups(items);

  return (
    <div className={PAGE}>
      <PageHeader
        title="Notifications"
        actions={<Button variant="outline" loading={marking} disabled={unread === 0} onClick={() => void markRead()}>Mark all read</Button>}
      />
      <PushEnableCard />
      <Tabs
        aria-label="Notifications"
        value={tab}
        onChange={setTab}
        items={[{ id: "all", label: "All" }, { id: "unread", label: "Unread", count: unread }]}
      />

      {loading && items.length === 0 ? (
        <div className="flex flex-col gap-2.5">{Array.from({ length: 4 }, (_, index) => <div key={index} className="h-16 animate-pulse rounded-card bg-track" />)}</div>
      ) : error && items.length === 0 ? (
        <EmptyState icon={<IconBell size={26} />} title="Notifications could not be loaded" action={<Button variant="outline" onClick={() => void load(1, true)}>Try again</Button>} />
      ) : items.length === 0 ? (
        <EmptyState icon={<IconBell size={26} />} title={unreadOnly ? "No unread notifications" : "Nothing yet"} message={unreadOnly ? undefined : "Your updates will appear here."} />
      ) : (
        <>
          <div className="overflow-hidden rounded-card border border-line bg-card">
            {groups.map((group) => (
              <section key={group.label}>
                <p className="px-4 pt-3 text-caption font-bold uppercase tracking-[0.4px] text-ink-muted">{group.label}</p>
                <ul className="m-0 list-none p-0">
                  {group.items.map((item) => (
                    <li key={item.id} className="border-t border-line-soft">
                      <NotificationRow item={item} busy={busyId === item.id} onOpen={() => void openOne(item)} />
                    </li>
                  ))}
                </ul>
              </section>
            ))}
          </div>
          <LoadMore shown={items.length} total={total} loading={loadingMore} onLoadMore={() => void load(page + 1, false)} />
        </>
      )}
    </div>
  );
}
