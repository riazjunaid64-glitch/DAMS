import { useCallback, useEffect, useState } from "react";
import {
  Button,
  Card,
  ConfirmDialog,
  DateField,
  EmptyState,
  IconLink,
  Modal,
  Notice,
  StatusBadge,
  Toggle,
  useToast,
} from "../../components/ui";
import { formatMonthDay, formatWhen, karachiDateInput } from "../../lib/dates.ts";
import LeadFormMappingDialog from "./LeadFormMappingDialog.tsx";
import {
  activeConnections,
  connectionStatusLabel,
  formDetailLine,
  formSetupState,
  importOutcomeLine,
  instagramForPage,
  isAwaitingFirstSync,
  MAX_IMPORT_DAYS,
  pageChannelLine,
  pageLeadLine,
  readCallbackResult,
} from "./metaIntegrationState.ts";
import {
  disconnectMetaConnection,
  importMetaLeads,
  listMetaConnections,
  listMetaResources,
  retryFailedMetaEvents,
  setMetaResourceEnabled,
  startMetaConnect,
  syncMetaConnection,
} from "./metaIntegrationApi.ts";
import type { MetaConnection, MetaLeadImportResult, MetaResource } from "./types.ts";

const STEPS = [
  { title: "Click Connect with Facebook", text: "A Facebook window opens." },
  { title: "Log in and approve", text: "Use the Facebook account that manages the Floria Heights Page. You never type a password into DAMS." },
  { title: "Turn on your Page", text: "Pick the Page here. New leads start arriving within a minute." },
];

/**
 * Facebook and Instagram lead ads. Loads on its own, so a Meta problem cannot take the
 * staff accounts tab down with it.
 */
export default function MetaIntegrationsPanel() {
  const toast = useToast();
  const [connections, setConnections] = useState<MetaConnection[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [connecting, setConnecting] = useState(false);

  const load = useCallback(async () => {
    setError(null);
    try {
      setConnections(await listMetaConnections());
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Facebook could not be loaded.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  useEffect(() => {
    const result = readCallbackResult(window.location.search);
    if (result.kind === "none") return;
    if (result.kind === "connected") toast.success("Facebook connected. Discovering Pages and forms…");
    else toast.error(result.message);
    const url = new URL(window.location.href);
    url.searchParams.delete("meta");
    url.searchParams.delete("reason");
    window.history.replaceState({}, "", url.toString());
  }, [toast]);

  const connect = async () => {
    setConnecting(true);
    try {
      const { authorizationUrl } = await startMetaConnect(`${window.location.pathname}?tab=integrations`);
      window.location.assign(authorizationUrl);
    } catch (caught) {
      toast.error(caught instanceof Error ? caught.message : "Facebook could not be opened.");
      setConnecting(false);
    }
  };

  if (loading) {
    return <div className="flex flex-col gap-2.5">{Array.from({ length: 3 }, (_, index) => <div key={index} className="h-16 animate-pulse rounded-card bg-track" />)}</div>;
  }
  if (error && connections.length === 0) {
    return <EmptyState icon={<IconLink size={26} />} title="Facebook could not be loaded" action={<Button variant="outline" onClick={() => { setLoading(true); void load(); }}>Try again</Button>} />;
  }

  const active = activeConnections(connections);
  if (active.length === 0) return <DisconnectedCard connecting={connecting} onConnect={() => void connect()} />;

  return (
    <div className="flex flex-col gap-4">
      {active.map((connection) => (
        <ConnectedAccount key={connection.id} connection={connection} connecting={connecting} onConnect={() => void connect()} onChanged={() => void load()} />
      ))}
    </div>
  );
}

function DisconnectedCard({ connecting, onConnect }: { connecting: boolean; onConnect: () => void }) {
  return (
    <Card>
      <div className="flex items-start gap-3">
        <span className="flex size-10 shrink-0 items-center justify-center rounded-field bg-page text-ink-2"><IconLink size={18} /></span>
        <div>
          <h2 className="m-0 text-body font-extrabold text-ink">Facebook & Instagram lead ads</h2>
          <p className="m-0 mt-1 text-small text-ink-2">Leads from your ad forms arrive in DAMS automatically, with their answers filled in.</p>
        </div>
      </div>
      <ol className="mt-4 flex flex-col gap-3 rounded-card bg-page p-4">
        {STEPS.map((step, index) => (
          <li key={step.title} className="flex gap-3">
            <span className="flex size-7 shrink-0 items-center justify-center rounded-full bg-primary text-label font-bold text-white">{index + 1}</span>
            <span>
              <span className="block text-sm font-extrabold text-ink">{step.title}</span>
              <span className="block text-small text-ink-2">{step.text}</span>
            </span>
          </li>
        ))}
      </ol>
      <Button className="mt-4" loading={connecting} onClick={onConnect}>Connect with Facebook</Button>
    </Card>
  );
}

function ConnectedAccount({ connection, connecting, onConnect, onChanged }: {
  connection: MetaConnection;
  connecting: boolean;
  onConnect: () => void;
  onChanged: () => void;
}) {
  const toast = useToast();
  const [resources, setResources] = useState<MetaResource[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [confirmOff, setConfirmOff] = useState(false);
  const [importing, setImporting] = useState<MetaResource | null>(null);
  const [mapping, setMapping] = useState<MetaResource | null>(null);

  const loadResources = useCallback(async () => {
    setError(null);
    try {
      const groups = await listMetaResources(connection.id);
      setResources(groups.flatMap((group) => group.items).filter((item) => item.isActive));
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Pages could not be loaded.");
    } finally {
      setLoading(false);
    }
  }, [connection.id]);

  useEffect(() => { void loadResources(); }, [loadResources]);

  const run = async (label: string, action: () => Promise<unknown>) => {
    setBusy(label);
    try {
      await action();
      await loadResources();
      onChanged();
    } catch (caught) {
      toast.error(caught instanceof Error ? caught.message : `${label} failed.`);
    } finally {
      setBusy(null);
    }
  };

  const pages = resources.filter((item) => item.resourceType === "facebook_page");
  const forms = resources.filter((item) => item.resourceType === "lead_form");
  const expired = connection.status === "NeedsReauthorization";
  const failed = connection.recentFailedCount ?? 0;
  const pageName = (form: MetaResource) => pages.find((page) => page.externalId === form.parentExternalId)?.name;

  const retry = async () => {
    setBusy("Retry");
    try {
      const result = await retryFailedMetaEvents(connection.id);
      if (result.requeued === 0) toast.error(result.skippedReason ?? "No leads were sent again.");
      else toast.success(`${result.requeued} ${result.requeued === 1 ? "lead" : "leads"} sent again`);
      await loadResources();
      onChanged();
    } catch (caught) {
      toast.error(caught instanceof Error ? caught.message : "Leads could not be sent again.");
    } finally {
      setBusy(null);
    }
  };

  const refresh = async () => {
    setBusy("Refresh");
    try {
      const result = await syncMetaConnection(connection.id);
      if (result.warning) toast.error(result.warning);
      else toast.success("Forms refreshed");
      await loadResources();
      onChanged();
    } catch (caught) {
      toast.error(caught instanceof Error ? caught.message : "Forms could not be refreshed.");
    } finally {
      setBusy(null);
    }
  };

  return (
    <div className="flex flex-col gap-4">
      <Card>
        <div className="flex flex-col gap-3 md:flex-row md:items-center md:justify-between">
          <div className="flex items-start gap-3">
            <span className="flex size-10 shrink-0 items-center justify-center rounded-field bg-page text-ink-2"><IconLink size={18} /></span>
            <div>
              <div className="flex flex-wrap items-center gap-2">
                <h2 className="m-0 text-body font-extrabold text-ink">Facebook & Instagram</h2>
                <StatusBadge status={connectionStatusLabel(connection.status)} tone={expired ? "orange" : "green"} />
              </div>
              <p className="m-0 mt-1 text-small text-ink-muted">{connectedLine(connection)}</p>
            </div>
          </div>
          <div className="flex gap-2">
            <Button variant="outline" className="flex-1 md:flex-none" loading={connecting} onClick={onConnect}>Reconnect</Button>
            <Button variant="danger" className="flex-1 md:flex-none" disabled={busy !== null} onClick={() => setConfirmOff(true)}>Disconnect</Button>
          </div>
        </div>
      </Card>

      {expired ? (
        <Notice
          tone="red"
          title="Facebook sign-in expired — Reconnect to keep receiving leads"
          action={<Button variant="outline" size="sm" onClick={onConnect}>Reconnect</Button>}
        />
      ) : failed > 0 ? (
        <Notice
          tone="red"
          title={`${failed} ${failed === 1 ? "lead" : "leads"} could not be received`}
          message={connection.lastFailedAt ? `Last try ${lowerWhen(connection.lastFailedAt)}` : undefined}
          action={<Button variant="outline" size="sm" loading={busy === "Retry"} onClick={() => void retry()}>Retry now</Button>}
        />
      ) : (
        <div className="flex items-center gap-2 rounded-card border border-line bg-card px-4 py-3.5 text-sm text-ink">
          <span className="size-2 shrink-0 rounded-full bg-success" />
          All leads delivered — no problems in the last 7 days
        </div>
      )}

      <section className="flex flex-col gap-3">
        <h3 className="m-0 text-caption font-bold uppercase tracking-[0.4px] text-ink-muted">Your pages</h3>
        {loading ? <div className="h-16 animate-pulse rounded-card bg-track" /> : error ? (
          <EmptyState title="Pages could not be loaded" action={<Button variant="outline" onClick={() => { setLoading(true); void loadResources(); }}>Try again</Button>} />
        ) : pages.length === 0 ? (
          <p className="m-0 text-small text-ink-muted">{isAwaitingFirstSync(connection) ? "Discovering Pages and forms. This usually takes under a minute." : "No Pages yet."}</p>
        ) : pages.map((page) => (
          <PageCard
            key={page.id}
            page={page}
            instagram={instagramForPage(resources, page)}
            busy={busy === `page-${page.id}`}
            onToggle={(enabled) => void run(`page-${page.id}`, () => setMetaResourceEnabled(connection.id, page.id, enabled))}
            onImport={() => setImporting(page)}
          />
        ))}
      </section>

      <Card
        title="Lead forms"
        action={{ label: busy === "Refresh" ? "Refreshing…" : "Refresh forms", onClick: () => void refresh() }}
      >
        {forms.length === 0 ? (
          <p className="m-0 text-small text-ink-muted">No lead forms yet.</p>
        ) : (
          <div>
            {forms.map((form) => (
              <FormRow key={form.id} form={form} pageName={pageName(form)} onSetup={() => setMapping(form)} />
            ))}
          </div>
        )}
      </Card>

      <ConfirmDialog
        open={confirmOff}
        danger
        loading={busy === "Disconnect"}
        title="Disconnect Facebook?"
        message="New leads stop arriving until you connect again."
        confirmLabel="Disconnect"
        onClose={() => setConfirmOff(false)}
        onConfirm={() => {
          setConfirmOff(false);
          void run("Disconnect", async () => {
            await disconnectMetaConnection(connection.id);
            toast.success("Facebook disconnected");
          });
        }}
      />
      {importing && (
        <ImportLeadsDialog
          connectionId={connection.id}
          page={importing}
          onClose={() => setImporting(null)}
          onDone={() => { setImporting(null); void loadResources(); }}
        />
      )}
      {mapping && (
        <LeadFormMappingDialog
          form={mapping}
          connectionId={connection.id}
          onClose={() => setMapping(null)}
          onSaved={() => { setMapping(null); void loadResources(); }}
        />
      )}
    </div>
  );
}

function connectedLine(connection: MetaConnection): string {
  const who = connection.connectedByName ? ` by ${connection.connectedByName}` : "";
  const renews = connection.tokenExpiresAt ? ` · sign-in renews by ${formatMonthDay(connection.tokenExpiresAt)}` : "";
  return `Connected${who} on ${formatMonthDay(connection.connectedAt)}${renews}`;
}

function lowerWhen(iso: string): string {
  return formatWhen(iso).replace(/^(Today|Tomorrow|Yesterday)/, (word) => word.toLowerCase());
}

function PageCard({ page, instagram, busy, onToggle, onImport }: {
  page: MetaResource;
  instagram?: MetaResource;
  busy: boolean;
  onToggle: (enabled: boolean) => void;
  onImport: () => void;
}) {
  const leadLine = pageLeadLine(page);
  return (
    <div className="rounded-card border border-line bg-card p-4">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="m-0 text-body font-extrabold text-ink">{page.name ?? "Facebook Page"}</p>
          <p className="m-0 mt-0.5 text-small text-ink-muted">{pageChannelLine(page, instagram)}</p>
        </div>
        <Toggle checked={page.isEnabled} disabled={busy} onChange={onToggle} label={<span className="sr-only">{page.isEnabled ? `Stop receiving leads from ${page.name}` : `Receive leads from ${page.name}`}</span>} />
      </div>
      {page.isEnabled && (
        <div className="mt-3 flex flex-col gap-3 border-t border-line-soft pt-3 md:flex-row md:items-center md:justify-between">
          <div className="flex flex-wrap items-center gap-2">
            <StatusBadge status="Receiving leads" tone="green" />
            {leadLine && <span className="text-small text-ink-2">{leadLine}</span>}
          </div>
          <Button variant="outline" size="sm" onClick={onImport}>Import old leads</Button>
        </div>
      )}
    </div>
  );
}

function FormRow({ form, pageName, onSetup }: { form: MetaResource; pageName?: string | null; onSetup: () => void }) {
  const state = formSetupState(form);
  const badge = state === "ready"
    ? { label: "Answers set up", tone: "green" as const }
    : state === "missing"
      ? { label: "Not set up", tone: "orange" as const }
      : state === "empty"
        ? { label: "Nothing to set up", tone: "grey" as const }
        : { label: "Refresh forms first", tone: "grey" as const };
  return (
    <div className="flex flex-col gap-3 border-t border-line-soft py-3 first:border-t-0 first:pt-0 md:flex-row md:items-center md:justify-between">
      <div className="min-w-0">
        <p className="m-0 text-sm font-extrabold text-ink">{form.name ?? "Lead form"}</p>
        <p className="m-0 mt-0.5 text-small text-ink-muted">{formDetailLine(form, pageName)}</p>
      </div>
      <div className="flex flex-col gap-2 md:flex-row md:items-center">
        <StatusBadge status={badge.label} tone={badge.tone} />
        {state === "ready" && <Button variant="outline" size="sm" onClick={onSetup}>Edit answers</Button>}
        {state === "missing" && <Button size="sm" onClick={onSetup}>Set up answers</Button>}
      </div>
    </div>
  );
}

function ImportLeadsDialog({ connectionId, page, onClose, onDone }: {
  connectionId: number;
  page: MetaResource;
  onClose: () => void;
  onDone: () => void;
}) {
  const toast = useToast();
  const today = karachiDateInput();
  const earliest = karachiDateInput(-(MAX_IMPORT_DAYS - 1));
  const [since, setSince] = useState(() => karachiDateInput(-7));
  const [saving, setSaving] = useState(false);
  const [result, setResult] = useState<MetaLeadImportResult | null>(null);
  const outOfRange = since < earliest || since > today || since === "";

  const save = async () => {
    setSaving(true);
    setResult(null);
    try {
      const imported = await importMetaLeads(connectionId, { resourceId: page.id, since });
      setResult(imported);
      toast.success(importOutcomeLine(imported));
    } catch (caught) {
      toast.error(caught instanceof Error ? caught.message : "Leads could not be imported.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      open
      onClose={result ? onDone : onClose}
      title={`Import leads from ${page.name ?? "this Page"}`}
      size="sm"
      phoneLayout="popup"
      busy={saving}
      primaryAction={result
        ? { label: "Done", onClick: onDone }
        : { label: "Import", onClick: () => void save(), disabled: outOfRange, loading: saving }}
      cancelLabel={result ? null : "Cancel"}
    >
      <div className="flex flex-col gap-3">
        <DateField
          label="Leads submitted since"
          required
          value={since}
          min={earliest}
          max={today}
          onChange={(event) => { setSince(event.target.value); setResult(null); }}
        />
        {result && <p className="m-0 text-sm font-bold text-ink">{importOutcomeLine(result)}</p>}
      </div>
    </Modal>
  );
}
