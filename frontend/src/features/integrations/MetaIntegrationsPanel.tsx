import { useCallback, useEffect, useState } from "react";
import Button from "../../lib/Button.tsx";
import { CrmModal, ErrorBanner } from "../leads/CrmUi.tsx";
import { formatDateTime } from "../leads/types.ts";
import type { MetaConnection, MetaEvent, MetaEventStatus, MetaResource, MetaResourceGroup } from "./types.ts";
import {
  canSync,
  connectionStatusLabel,
  connectionStatusTone,
  deliverySummary,
  isAwaitingFirstSync,
  isToggleable,
  readCallbackResult,
  summarizeCounts,
} from "./metaIntegrationState.ts";
import {
  disconnectMetaConnection,
  listMetaEvents,
  listMetaConnections,
  listMetaResources,
  retryMetaEvent,
  setMetaResourceEnabled,
  startMetaConnect,
  syncMetaConnection,
} from "./metaIntegrationApi.ts";

/**
 * Connect and manage Meta accounts.
 *
 * Loads its own data rather than joining the settings page's shared load, so a server with
 * no Meta configuration cannot break the rest of CRM settings.
 */
export default function MetaIntegrationsPanel() {
  const [connections, setConnections] = useState<MetaConnection[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [connecting, setConnecting] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setConnections(await listMetaConnections());
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Meta connections could not be loaded.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  // Read the outcome Meta redirected back with, then strip it from the URL so a refresh
  // does not re-announce a connection that happened minutes ago.
  useEffect(() => {
    const result = readCallbackResult(window.location.search);
    if (result.kind === "none") return;

    if (result.kind === "connected") setNotice("Meta account connected. Discovering Pages and forms…");
    else setError(result.message);

    const url = new URL(window.location.href);
    url.searchParams.delete("meta");
    url.searchParams.delete("reason");
    window.history.replaceState({}, "", url.toString());
  }, []);

  const connect = async () => {
    setConnecting(true);
    setError(null);
    try {
      const { authorizationUrl } = await startMetaConnect(`${window.location.pathname}?tab=integrations`);
      // A full navigation, not a fetch: consent happens on Meta's own origin.
      window.location.assign(authorizationUrl);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "The Meta connection could not be started.");
      setConnecting(false);
    }
  };

  return (
    <div>
      <div className="mb-5 flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <h2 className="text-lg font-semibold text-[var(--text-heading)]">Integrations</h2>
          <p className="mt-1 text-sm text-[var(--text-muted)]">
            Connect Meta so Facebook and Instagram lead ads arrive in the normal lead workflow. Leads are
            only received from Pages you enable.
          </p>
        </div>
        <Button size="sm" disabled={connecting} onClick={() => void connect()}>
          {connecting ? "Opening Meta…" : "+ Connect Meta"}
        </Button>
      </div>

      {error && <ErrorBanner message={error} onRetry={() => void load()} />}
      {notice && (
        <p className="mb-4 rounded-xl border border-emerald-500/25 bg-emerald-500/[0.08] px-4 py-3 text-sm text-emerald-200">
          {notice}
        </p>
      )}

      {loading ? (
        <p className="py-16 text-center text-sm text-[var(--text-muted)]">Loading Meta connections…</p>
      ) : connections.length === 0 ? (
        <p className="rounded-xl border border-dashed border-[var(--border)] py-16 text-center text-sm text-[var(--text-muted)]">
          No Meta account is connected yet.
        </p>
      ) : (
        <div className="space-y-4">
          {connections.map((connection) => (
            <ConnectionCard key={connection.id} connection={connection} onChanged={() => void load()} />
          ))}
        </div>
      )}
    </div>
  );
}

function ConnectionCard({ connection, onChanged }: { connection: MetaConnection; onChanged: () => void }) {
  const [groups, setGroups] = useState<MetaResourceGroup[]>([]);
  const [events, setEvents] = useState<MetaEvent[]>([]);
  const [eventFilter, setEventFilter] = useState<MetaEventStatus | "All">("All");
  const [expanded, setExpanded] = useState(false);
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [confirmDisconnect, setConfirmDisconnect] = useState(false);

  const loadResources = useCallback(async () => {
    setError(null);
    const [resourcesResult, eventsResult] = await Promise.allSettled([
      listMetaResources(connection.id),
      listMetaEvents(connection.id, eventFilter === "All" ? 25 : 200, eventFilter === "All" ? undefined : eventFilter),
    ]);
    const failures: string[] = [];
    if (resourcesResult.status === "fulfilled") setGroups(resourcesResult.value);
    else failures.push("Resources could not be loaded.");
    if (eventsResult.status === "fulfilled") setEvents(eventsResult.value);
    else failures.push("Lead events could not be loaded.");
    if (failures.length > 0) setError(failures.join(" "));
  }, [connection.id, eventFilter]);

  useEffect(() => {
    if (expanded) void loadResources();
  }, [expanded, loadResources]);

  const run = async (label: string, action: () => Promise<unknown>) => {
    setBusy(label);
    setError(null);
    try {
      await action();
      await loadResources();
      onChanged();
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : `${label} failed.`);
    } finally {
      setBusy(null);
    }
  };

  return (
    <div className="rounded-2xl border border-[var(--border)] bg-[var(--bg-card)] p-4 sm:p-5">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <div className="flex flex-wrap items-center gap-2">
            <p className="font-semibold text-[var(--text-heading)]">{connection.displayName}</p>
            <span className={`rounded-full border px-2.5 py-0.5 text-xs ${connectionStatusTone(connection.status)}`}>
              {connectionStatusLabel(connection.status)}
            </span>
          </div>
          <p className="mt-1 text-xs text-[var(--text-muted)]">
            Connected {formatDateTime(connection.connectedAt)}
            {connection.connectedByName ? ` by ${connection.connectedByName}` : ""} · Last sync{" "}
            {isAwaitingFirstSync(connection) ? "pending" : formatDateTime(connection.lastSyncedAt)}
          </p>
          <p className="mt-1 text-xs text-[var(--text-secondary)]">{summarizeCounts(connection)}</p>
          <p className="mt-0.5 text-xs text-[var(--text-secondary)]">{deliverySummary(connection)}</p>
          {(connection.failedEventCount || connection.pendingEventCount) ? (
            <div className="mt-2 flex flex-wrap gap-2 text-xs">
              {connection.failedEventCount ? <span className="rounded-full border border-red-500/30 bg-red-500/10 px-2.5 py-0.5 text-red-300">{connection.failedEventCount} failed event{connection.failedEventCount === 1 ? "" : "s"}</span> : null}
              {connection.pendingEventCount ? <span className="rounded-full border border-amber-500/30 bg-amber-500/10 px-2.5 py-0.5 text-amber-200">{connection.pendingEventCount} pending event{connection.pendingEventCount === 1 ? "" : "s"}</span> : null}
            </div>
          ) : null}
        </div>

        <div className="flex flex-wrap gap-2">
          <Button size="sm" variant="outline" onClick={() => setExpanded((v) => !v)}>
            {expanded ? "Hide resources" : "Manage resources"}
          </Button>
          <Button
            size="sm"
            variant="outline"
            disabled={busy !== null || !canSync(connection)}
            onClick={() => void run("Sync", () => syncMetaConnection(connection.id))}
          >
            {busy === "Sync" ? "Syncing…" : "Sync now"}
          </Button>
          {connection.status !== "Disconnected" && (
            <Button size="sm" variant="ghost" disabled={busy !== null} onClick={() => setConfirmDisconnect(true)}>
              Disconnect
            </Button>
          )}
        </div>
      </div>

      {connection.status === "NeedsReauthorization" && (
        <p className="mt-3 rounded-xl border border-amber-500/25 bg-amber-500/[0.08] px-4 py-3 text-sm text-amber-200">
          This account needs to be reconnected before leads can be received. Use Connect Meta above and approve
          all requested permissions.
          {connection.lastError ? <span className="mt-1 block text-xs opacity-80">{connection.lastError}</span> : null}
        </p>
      )}

      {connection.status !== "NeedsReauthorization" && connection.lastError && (
        <p className="mt-3 text-xs text-[var(--text-muted)]">
          Last error {formatDateTime(connection.lastErrorAt)}: {connection.lastError}
        </p>
      )}

      {error && (
        <div className="mt-3">
          <ErrorBanner message={error} />
        </div>
      )}

      {expanded && (
        <div className="mt-4 space-y-4 border-t border-[var(--border)] pt-4">
          {isAwaitingFirstSync(connection) && groups.length === 0 ? (
            <p className="text-sm text-[var(--text-muted)]">
              Discovering Pages and forms from Meta. This runs in the background and usually takes under a minute.
            </p>
          ) : groups.length === 0 ? (
            <p className="text-sm text-[var(--text-muted)]">Nothing has been discovered yet. Try Sync now.</p>
          ) : groups.map((group) => (
            <ResourceGroup
              key={group.resourceType}
              group={group}
              busy={busy}
              onToggle={(resource, isEnabled) =>
                void run(`Resource ${resource.id}`, () =>
                  setMetaResourceEnabled(connection.id, resource.id, isEnabled),
                )
              }
            />
          ))}
          <EventList
            events={events}
            busy={busy}
            filter={eventFilter}
            onFilterChange={setEventFilter}
            onRetry={(eventId) => void run(`Event ${eventId}`, () => retryMetaEvent(connection.id, eventId))}
          />
        </div>
      )}

      {confirmDisconnect && (
        <CrmModal
          open
          title="Disconnect this Meta account?"
          subtitle="Lead delivery stops immediately. Leads already captured, and the record of where they came from, are kept."
          onClose={() => setConfirmDisconnect(false)}
          footer={
            <div className="flex justify-end gap-2">
              <Button variant="ghost" onClick={() => setConfirmDisconnect(false)}>
                Cancel
              </Button>
              <Button
                disabled={busy !== null}
                onClick={() => {
                  setConfirmDisconnect(false);
                  void run("Disconnect", () => disconnectMetaConnection(connection.id));
                }}
              >
                Disconnect
              </Button>
            </div>
          }
        >
          <p className="text-sm text-[var(--text-secondary)]">
            DAMS will stop receiving new leads from every Page on this account and will delete its stored
            credentials. You can reconnect the same account later.
          </p>
        </CrmModal>
      )}
    </div>
  );
}

function ResourceGroup({
  group,
  busy,
  onToggle,
}: {
  group: MetaResourceGroup;
  busy: string | null;
  onToggle: (resource: MetaResource, isEnabled: boolean) => void;
}) {
  return (
    <div>
      <h3 className="mb-2 text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)]">{group.label}</h3>
      <div className="space-y-2">
        {group.items.map((resource) => (
          <div
            key={resource.id}
            className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-[var(--border)] px-4 py-3"
          >
            <div className="min-w-0">
              <p className="truncate text-sm text-[var(--text-primary)]">{resource.name ?? resource.externalId}</p>
              <p className="text-xs text-[var(--text-muted)]">
                {resource.externalId}
                {resource.externalStatus ? ` · ${resource.externalStatus}` : ""}
                {!resource.isActive ? " · no longer returned by Meta" : ""}
              </p>
            </div>

            {isToggleable(resource) ? (
              <label className="flex shrink-0 items-center gap-2 text-sm text-[var(--text-secondary)]">
                <input
                  type="checkbox"
                  checked={resource.isEnabled}
                  disabled={busy !== null || (!resource.isActive && !resource.isEnabled)}
                  onChange={(e) => onToggle(resource, e.target.checked)}
                />
                {resource.isEnabled ? "Receiving leads" : "Enable"}
              </label>
            ) : (
              <span className="shrink-0 text-xs text-[var(--text-muted)]">Discovered for attribution</span>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}

function EventList({ events, busy, filter, onFilterChange, onRetry }: {
  events: MetaEvent[];
  busy: string | null;
  filter: MetaEventStatus | "All";
  onFilterChange: (filter: MetaEventStatus | "All") => void;
  onRetry: (eventId: number) => void;
}) {
  return (
    <div>
      <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
        <h3 className="text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)]">Lead events</h3>
        <select
          aria-label="Filter lead events"
          value={filter}
          onChange={(event) => onFilterChange(event.target.value as MetaEventStatus | "All")}
          className="rounded-lg border border-[var(--border)] bg-[var(--input-bg)] px-2 py-1 text-xs text-[var(--text-secondary)]"
        >
          <option value="All">Recent</option>
          <option value="Failed">Failed</option>
          <option value="Pending">Pending</option>
          <option value="Retry">Retrying</option>
          <option value="Processing">Processing</option>
        </select>
      </div>
      {events.length === 0 ? (
        <p className="text-sm text-[var(--text-muted)]">No webhook events recorded for this connection.</p>
      ) : (
        <div className="space-y-2">
          {events.map((event) => (
            <div key={event.id} className="rounded-xl border border-[var(--border)] px-4 py-3">
              <div className="flex flex-wrap items-center justify-between gap-3">
                <div className="min-w-0">
                  <p className="text-sm text-[var(--text-primary)]">{event.eventType} · #{event.id}</p>
                  <p className="text-xs text-[var(--text-muted)]">
                    Received {formatDateTime(event.receivedAt)} · Attempts {event.attempts}
                    {event.retryCount > 0 ? ` · Requeued ${event.retryCount} time${event.retryCount === 1 ? "" : "s"}` : ""}
                  </p>
                </div>
                <div className="flex items-center gap-2">
                  <span className={`rounded-full border px-2.5 py-0.5 text-xs ${event.status === "Failed" ? "border-red-500/30 bg-red-500/10 text-red-300" : event.status === "Pending" || event.status === "Retry" ? "border-amber-500/30 bg-amber-500/10 text-amber-200" : "border-[var(--border)] text-[var(--text-muted)]"}`}>
                    {event.status}
                  </span>
                  {event.status === "Failed" && (
                    <Button size="sm" variant="outline" disabled={busy !== null} onClick={() => onRetry(event.id)}>
                      {busy === `Event ${event.id}` ? "Retrying…" : "Retry"}
                    </Button>
                  )}
                </div>
              </div>
              {event.lastError && <p className="mt-2 text-xs text-red-300">{event.lastError}</p>}
              {event.lastRetriedAt && <p className="mt-1 text-xs text-[var(--text-muted)]">Last retry {formatDateTime(event.lastRetriedAt)}{event.lastRetriedByName ? ` by ${event.lastRetriedByName}` : ""}</p>}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
