import { useCallback, useEffect, useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import type { User } from "../App.tsx";
import Button from "../lib/Button.tsx";
import PushEnableCard from "../features/notifications/PushEnableCard.tsx";
import {
  archiveNotification,
  fetchCapabilities,
  fetchNotifications,
  fetchPreferences,
  markAllRead,
  openNotification,
  savePreferences,
} from "../features/notifications/notificationApi.ts";
import { notificationCategoryIsAllowed, notificationCategoryLabels } from "../features/notifications/capabilities.ts";
import { formatDateTime, relativeTime } from "../features/notifications/types.ts";
import type {
  NotificationCapabilities,
  NotificationCategory,
  NotificationItem,
  PreferenceRow,
} from "../features/notifications/types.ts";

type Tab = "inbox" | "preferences";

const PAGE_SIZE = 20;

/**
 * The full notification history and the user's own preferences.
 *
 * Everything on this page is scoped server-side to the signed-in account; there is no
 * identifier here that could be pointed at somebody else's inbox.
 */
export default function NotificationsPage({ user }: { user: User | null }) {
  const [tab, setTab] = useState<Tab>("inbox");

  if (!user) {
    return (
      <Shell>
        <EmptyState
          title="Sign in to see your notifications"
          message="Your notification inbox is private to your account."
        />
      </Shell>
    );
  }

  return (
    <Shell>
      <div className="mb-5 flex gap-1 overflow-x-auto rounded-xl border border-[var(--border)] bg-[var(--bg-card)] p-1" role="tablist" aria-label="Notification sections">
        {(
          [
            { id: "inbox", label: "Inbox" },
            { id: "preferences", label: "Preferences" },
          ] as { id: Tab; label: string }[]
        ).map((item) => (
          <button
            key={item.id}
            type="button"
            role="tab"
            aria-selected={tab === item.id}
            onClick={() => setTab(item.id)}
            className={`whitespace-nowrap rounded-lg px-4 py-2 text-sm font-medium transition ${
              tab === item.id
                ? "bg-[var(--accent)] text-white shadow-sm"
                : "text-[var(--text-muted)] hover:bg-[var(--surface-glass-hover)] hover:text-[var(--text-primary)]"
            }`}
          >
            {item.label}
          </button>
        ))}
      </div>

      {/* Both tabs render the role's categories, so a role change has to rebuild them. */}
      {tab === "inbox" ? (
        <InboxTab key={`${user.userId}:${user.role}`} />
      ) : (
        <PreferencesTab key={`${user.userId}:${user.role}`} />
      )}
    </Shell>
  );
}

function Shell({ children }: { children: React.ReactNode }) {
  return (
    <div className="min-h-[70vh] bg-[var(--bg-secondary)]">
      <div className="mx-auto w-full max-w-4xl px-4 py-6 sm:px-6 sm:py-8">
        <header className="mb-5">
          <h1 className="text-2xl font-bold text-[var(--text-heading)] sm:text-3xl">Notifications</h1>
          <p className="mt-1 text-sm text-[var(--text-muted)]">
            Everything DAMS has told you, and how you would like to hear about it.
          </p>
        </header>
        {children}
      </div>
    </div>
  );
}

// ── Inbox ──────────────────────────────────────────────────────────────────────

function InboxTab() {
  const navigate = useNavigate();

  const [items, setItems] = useState<NotificationItem[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [unreadCount, setUnreadCount] = useState(0);
  const [page, setPage] = useState(1);
  const [category, setCategory] = useState<NotificationCategory | null>(null);
  const [unreadOnly, setUnreadOnly] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<number | null>(null);
  const [capabilities, setCapabilities] = useState<NotificationCapabilities | null>(null);
  const [capabilitiesError, setCapabilitiesError] = useState<string | null>(null);

  const loadCapabilities = useCallback(async () => {
    try {
      setCapabilities(await fetchCapabilities());
      setCapabilitiesError(null);
    } catch (err) {
      setCapabilitiesError(err instanceof Error ? err.message : "Notification filters could not be loaded.");
    }
  }, []);

  useEffect(() => {
    let active = true;
    void fetchCapabilities()
      .then((result) => {
        if (active) {
          setCapabilities(result);
          setCapabilitiesError(null);
        }
      })
      .catch((err: unknown) => {
        if (active) {
          setCapabilitiesError(err instanceof Error ? err.message : "Notification filters could not be loaded.");
        }
      });
    return () => {
      active = false;
    };
  }, []);

  useEffect(() => {
    if (category && capabilities && !notificationCategoryIsAllowed(capabilities, category)) {
      setCategory(null);
    }
  }, [capabilities, category]);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const result = await fetchNotifications({ category, unreadOnly, page, pageSize: PAGE_SIZE });
      setItems(result.items);
      setTotalCount(result.totalCount);
      setUnreadCount(result.unreadCount);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Your notifications could not be loaded.");
    } finally {
      setLoading(false);
    }
  }, [category, page, unreadOnly]);

  useEffect(() => {
    void load();
  }, [load]);

  // Changing a filter starts again from the first page.
  useEffect(() => {
    setPage(1);
  }, [category, unreadOnly]);

  const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));

  const categoryLabels = useMemo(() => notificationCategoryLabels(capabilities), [capabilities]);

  const onOpen = useCallback(
    async (item: NotificationItem) => {
      setBusyId(item.id);
      setNotice(null);
      try {
        const result = await openNotification(item.id);
        if (result.allowed && result.deepLink) {
          navigate(result.deepLink);
          return;
        }
        setNotice(result.message);
        await load();
      } catch (err) {
        setNotice(err instanceof Error ? err.message : "That notification could not be opened.");
      } finally {
        setBusyId(null);
      }
    },
    [load, navigate]
  );

  const onArchive = useCallback(
    async (id: number) => {
      setBusyId(id);
      try {
        await archiveNotification(id);
        await load();
      } catch (err) {
        setNotice(err instanceof Error ? err.message : "That notification could not be dismissed.");
      } finally {
        setBusyId(null);
      }
    },
    [load]
  );

  return (
    <div className="space-y-4">
      <PushEnableCard compact />

      <div className="rounded-2xl border border-[var(--border)] bg-[var(--bg-card)]">
        <div className="flex flex-wrap items-center justify-between gap-3 border-b border-[var(--border)] px-4 py-3">
          <p className="text-sm text-[var(--text-secondary)]">
            <span className="font-semibold text-[var(--text-heading)]">{unreadCount}</span> unread
            <span className="text-[var(--text-muted)]"> · {totalCount} total</span>
          </p>
          <div className="flex flex-wrap gap-2">
            <label className="inline-flex cursor-pointer items-center gap-2 rounded-lg border border-[var(--border)] px-3 py-1.5 text-xs font-medium text-[var(--text-secondary)]">
              <input
                type="checkbox"
                checked={unreadOnly}
                onChange={(event) => setUnreadOnly(event.target.checked)}
                className="h-3.5 w-3.5 accent-[var(--accent)]"
              />
              Unread only
            </label>
            <Button
              variant="outline"
              size="sm"
              disabled={unreadCount === 0}
              onClick={async () => {
                await markAllRead(category);
                await load();
              }}
            >
              Mark all read
            </Button>
          </div>
        </div>

        <div className="flex gap-1.5 overflow-x-auto px-4 py-3" role="group" aria-label="Filter by category">
          <FilterChip active={category === null} onClick={() => setCategory(null)}>
            All
          </FilterChip>
          {capabilities?.categories.map((item) => (
            <FilterChip key={item.category} active={category === item.category} onClick={() => setCategory(item.category)}>
              {item.label}
            </FilterChip>
          ))}
        </div>

        {error && (
          <div role="alert" className="mx-4 mb-4 rounded-lg border border-[var(--accent-rose)]/30 bg-[var(--accent-rose-glow)] px-3 py-2 text-sm text-[var(--accent-rose)]">
            {error}
            <button type="button" onClick={() => void load()} className="ml-2 font-semibold underline">
              Try again
            </button>
          </div>
        )}

        {capabilitiesError && (
          <div role="alert" className="mx-4 mb-4 rounded-lg border border-[var(--accent-rose)]/30 bg-[var(--accent-rose-glow)] px-3 py-2 text-sm text-[var(--accent-rose)]">
            {capabilitiesError}
            <button type="button" onClick={() => void loadCapabilities()} className="ml-2 font-semibold underline">
              Retry filters
            </button>
          </div>
        )}

        {notice && (
          <div role="status" className="mx-4 mb-4 rounded-lg border border-[var(--border)] bg-[var(--surface-glass)] px-3 py-2 text-sm text-[var(--text-secondary)]">
            {notice}
          </div>
        )}

        {loading && items.length === 0 && (
          <p className="px-4 py-12 text-center text-sm text-[var(--text-muted)]">Loading notifications…</p>
        )}

        {!loading && !error && items.length === 0 && (
          <EmptyState
            title={unreadOnly ? "Nothing unread" : "No notifications yet"}
            message={
              unreadOnly
                ? "You have read everything in this view."
                : capabilities?.emptyStateMessage ?? "Your available updates will appear here."
            }
          />
        )}

        <ul className="divide-y divide-[var(--border)]">
          {items.map((item) => (
            <li key={item.id} className={item.isRead ? "" : "bg-[var(--surface-glass)]"}>
              <div className="flex flex-col gap-2 px-4 py-3.5 sm:flex-row sm:items-start sm:gap-3">
                <span
                  aria-hidden="true"
                  className={`mt-2 hidden h-2 w-2 shrink-0 rounded-full sm:block ${
                    item.isRead ? "bg-transparent" : "bg-[var(--accent)]"
                  }`}
                />
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-2">
                    <h3 className="text-sm font-semibold text-[var(--text-heading)]">{item.title}</h3>
                    {!item.isRead && (
                      <span className="rounded-full bg-[var(--accent)] px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide text-white">
                        New
                      </span>
                    )}
                    {item.isEscalation && (
                      <span className="rounded-full border border-[var(--accent-rose)]/30 px-2 py-0.5 text-[10px] font-semibold text-[var(--accent-rose)]">
                        Escalation
                      </span>
                    )}
                  </div>
                  <p className="mt-1 text-sm text-[var(--text-secondary)]">{item.message}</p>
                  <p className="mt-1.5 flex flex-wrap items-center gap-2 text-[11px] text-[var(--text-muted)]">
                    <span className="rounded-md bg-[var(--surface-glass)] px-1.5 py-0.5 font-medium">
                      {categoryLabels.get(item.category) ?? item.category}
                    </span>
                    {(item.priority === "High" || item.priority === "Critical") && (
                      <span className="font-semibold text-[var(--accent-rose)]">{item.priority} priority</span>
                    )}
                    <time dateTime={item.createdAt} title={formatDateTime(item.createdAt)}>
                      {relativeTime(item.createdAt)}
                    </time>
                  </p>
                </div>
                <div className="flex shrink-0 gap-2">
                  <Button
                    variant="outline"
                    size="sm"
                    disabled={busyId === item.id}
                    onClick={() => void onOpen(item)}
                  >
                    Open
                  </Button>
                  <Button
                    variant="ghost"
                    size="sm"
                    disabled={busyId === item.id}
                    onClick={() => void onArchive(item.id)}
                  >
                    Dismiss
                  </Button>
                </div>
              </div>
            </li>
          ))}
        </ul>

        {totalPages > 1 && (
          <nav className="flex items-center justify-between gap-3 border-t border-[var(--border)] px-4 py-3" aria-label="Notification pages">
            <Button variant="ghost" size="sm" disabled={page <= 1} onClick={() => setPage((p) => Math.max(1, p - 1))}>
              Previous
            </Button>
            <span className="text-xs text-[var(--text-muted)]">
              Page {page} of {totalPages}
            </span>
            <Button
              variant="ghost"
              size="sm"
              disabled={page >= totalPages}
              onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
            >
              Next
            </Button>
          </nav>
        )}
      </div>
    </div>
  );
}

// ── Preferences ────────────────────────────────────────────────────────────────

function PreferencesTab() {
  const [rows, setRows] = useState<PreferenceRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const result = await fetchPreferences();
        if (!cancelled) setRows(result);
      } catch (err) {
        if (!cancelled) setError(err instanceof Error ? err.message : "Your preferences could not be loaded.");
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  const update = (category: NotificationCategory, field: "emailEnabled" | "pushEnabled", value: boolean) => {
    setSaved(false);
    setRows((current) => current.map((row) => (row.category === category ? { ...row, [field]: value } : row)));
  };

  const onSave = async () => {
    setSaving(true);
    setError(null);
    try {
      const next = await savePreferences(
        rows
          .filter((row) => !row.isMandatory)
          .map((row) => ({ category: row.category, emailEnabled: row.emailEnabled, pushEnabled: row.pushEnabled }))
      );
      setRows(next);
      setSaved(true);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Your preferences could not be saved.");
    } finally {
      setSaving(false);
    }
  };

  if (loading) return <p className="py-12 text-center text-sm text-[var(--text-muted)]">Loading preferences…</p>;

  return (
    <div className="space-y-4">
      <PushEnableCard />

      <section className="rounded-2xl border border-[var(--border)] bg-[var(--bg-card)]">
        <div className="border-b border-[var(--border)] px-4 py-3 sm:px-5">
          <h2 className="text-base font-semibold text-[var(--text-heading)]">What you hear about</h2>
          <p className="mt-1 text-sm text-[var(--text-muted)]">
            Everything always appears in your DAMS inbox. These switches control email and browser notifications only.
          </p>
        </div>

        {error && (
          <div role="alert" className="mx-4 mt-4 rounded-lg border border-[var(--accent-rose)]/30 bg-[var(--accent-rose-glow)] px-3 py-2 text-sm text-[var(--accent-rose)]">
            {error}
          </div>
        )}

        <ul className="divide-y divide-[var(--border)]">
          {rows.map((row) => (
            <li key={row.category} className="flex flex-col gap-3 px-4 py-4 sm:flex-row sm:items-center sm:justify-between sm:px-5">
              <div className="min-w-0">
                <p className="flex flex-wrap items-center gap-2 text-sm font-semibold text-[var(--text-heading)]">
                  {row.label}
                  {row.isMandatory && (
                    <span className="rounded-md bg-[var(--surface-glass)] px-1.5 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-[var(--text-muted)]">
                      Always sent
                    </span>
                  )}
                </p>
                <p className="mt-0.5 text-xs text-[var(--text-muted)]">{row.description}</p>
              </div>

              <div className="flex shrink-0 gap-4">
                <Toggle
                  label="Email"
                  checked={row.emailEnabled}
                  disabled={row.isMandatory || !row.emailAvailable}
                  hint={row.isMandatory ? "Essential message" : row.emailAvailable ? undefined : "Email is off"}
                  onChange={(value) => update(row.category, "emailEnabled", value)}
                />
                <Toggle
                  label="Browser"
                  checked={row.pushEnabled}
                  disabled={row.isMandatory || !row.pushAvailable}
                  hint={row.isMandatory ? "Essential message" : row.pushAvailable ? undefined : "Push is off"}
                  onChange={(value) => update(row.category, "pushEnabled", value)}
                />
              </div>
            </li>
          ))}
        </ul>

        <div className="flex flex-wrap items-center gap-3 border-t border-[var(--border)] px-4 py-3 sm:px-5">
          <Button size="sm" onClick={() => void onSave()} disabled={saving}>
            {saving ? "Saving…" : "Save preferences"}
          </Button>
          {saved && (
            <span role="status" className="text-sm text-[var(--accent-emerald)]">
              Preferences saved.
            </span>
          )}
        </div>
      </section>
    </div>
  );
}

// ── Small shared pieces ────────────────────────────────────────────────────────

function FilterChip({
  active,
  onClick,
  children,
}: {
  active: boolean;
  onClick: () => void;
  children: React.ReactNode;
}) {
  return (
    <button
      type="button"
      aria-pressed={active}
      onClick={onClick}
      className={`whitespace-nowrap rounded-full border px-3 py-1.5 text-xs font-medium transition ${
        active
          ? "border-[var(--accent)] bg-[var(--accent)] text-white"
          : "border-[var(--border)] text-[var(--text-secondary)] hover:bg-[var(--surface-glass-hover)]"
      }`}
    >
      {children}
    </button>
  );
}

function Toggle({
  label,
  checked,
  disabled,
  hint,
  onChange,
}: {
  label: string;
  checked: boolean;
  disabled?: boolean;
  hint?: string;
  onChange: (value: boolean) => void;
}) {
  return (
    <label className={`flex w-20 flex-col items-start gap-1 text-xs ${disabled ? "opacity-60" : "cursor-pointer"}`}>
      <span className="font-medium text-[var(--text-secondary)]">{label}</span>
      <input
        type="checkbox"
        checked={checked}
        disabled={disabled}
        onChange={(event) => onChange(event.target.checked)}
        className="h-4 w-4 accent-[var(--accent)]"
      />
      {/* Status in words as well as a control state, so it does not depend on colour alone. */}
      <span className="text-[10px] text-[var(--text-muted)]">{hint ?? (checked ? "On" : "Off")}</span>
    </label>
  );
}

function EmptyState({ title, message }: { title: string; message: string }) {
  return (
    <div className="px-4 py-14 text-center">
      <p className="text-base font-semibold text-[var(--text-heading)]">{title}</p>
      <p className="mx-auto mt-1 max-w-md text-sm text-[var(--text-muted)]">{message}</p>
    </div>
  );
}
