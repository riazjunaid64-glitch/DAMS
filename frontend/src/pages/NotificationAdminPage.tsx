import { useCallback, useEffect, useMemo, useState } from "react";
import type { User } from "../App.tsx";
import Button from "../lib/Button.tsx";
import {
  cancelJob,
  compose,
  fetchAudit,
  fetchDeliveries,
  fetchJobs,
  fetchRules,
  fetchSettings,
  fetchSuppressions,
  fetchTemplates,
  generatePushKeys,
  previewAudience,
  previewTemplate,
  removeSuppression,
  retryDelivery,
  saveRule,
  saveSettings,
  saveTemplate,
  sendTestEmail,
} from "../features/notifications/notificationApi.ts";
import {
  CATEGORY_LABELS,
  DELIVERY_STATUS_LABELS,
  formatDateTime,
} from "../features/notifications/types.ts";
import type {
  AudiencePreview,
  AuditRow,
  AudienceType,
  ComposeRequest,
  DeliveryRow,
  JobRow,
  NotificationSettings,
  RuleRow,
  SuppressionRow,
  TemplatePreview,
  TemplateRow,
} from "../features/notifications/types.ts";

type Tab = "settings" | "templates" | "rules" | "compose" | "history";

const SECRET_PLACEHOLDER = "********";

/**
 * The admin console for the notification platform.
 *
 * Secrets are handled carefully throughout: the server sends only a masked hint, the form
 * shows that hint, and submitting it unchanged leaves the stored value alone. A real secret
 * is only ever transmitted when an admin deliberately types a new one.
 */
export default function NotificationAdminPage({ user }: { user: User | null }) {
  const [tab, setTab] = useState<Tab>("settings");

  if (!user) {
    return <Shell><Panel title="Sign in required">Sign in with an administrator account to manage notifications.</Panel></Shell>;
  }

  if (user.role !== "Admin") {
    return (
      <Shell>
        <Panel title="Administrators only">
          Notification settings, templates and broadcasts are restricted to administrators.
        </Panel>
      </Shell>
    );
  }

  const tabs: { id: Tab; label: string }[] = [
    { id: "settings", label: "Settings" },
    { id: "templates", label: "Templates" },
    { id: "rules", label: "Rules" },
    { id: "compose", label: "Send" },
    { id: "history", label: "Delivery history" },
  ];

  return (
    <Shell>
      <div className="mb-5 flex gap-1 overflow-x-auto rounded-xl border border-[var(--border)] bg-[var(--bg-card)] p-1" role="tablist" aria-label="Notification administration">
        {tabs.map((item) => (
          <button
            key={item.id}
            type="button"
            role="tab"
            aria-selected={tab === item.id}
            onClick={() => setTab(item.id)}
            className={`whitespace-nowrap rounded-lg px-3.5 py-2 text-sm font-medium transition ${
              tab === item.id
                ? "bg-[var(--accent)] text-white shadow-sm"
                : "text-[var(--text-muted)] hover:bg-[var(--surface-glass-hover)] hover:text-[var(--text-primary)]"
            }`}
          >
            {item.label}
          </button>
        ))}
      </div>

      {tab === "settings" && <SettingsTab />}
      {tab === "templates" && <TemplatesTab />}
      {tab === "rules" && <RulesTab />}
      {tab === "compose" && <ComposeTab />}
      {tab === "history" && <HistoryTab />}
    </Shell>
  );
}

function Shell({ children }: { children: React.ReactNode }) {
  return (
    <div className="min-h-[70vh] bg-[var(--bg-secondary)]">
      <div className="mx-auto w-full max-w-[1200px] px-4 py-6 sm:px-6 sm:py-8">
        <header className="mb-5">
          <h1 className="text-2xl font-bold text-[var(--text-heading)] sm:text-3xl">Notification settings</h1>
          <p className="mt-1 text-sm text-[var(--text-muted)]">
            Branding, email and browser delivery, templates, rules, broadcasts and delivery history.
          </p>
        </header>
        {children}
      </div>
    </div>
  );
}

// ── Settings ───────────────────────────────────────────────────────────────────

const GENERAL_FIELDS: { key: string; label: string; hint?: string; type?: string }[] = [
  { key: "general.appName", label: "Application name" },
  { key: "general.companyName", label: "Company name" },
  { key: "general.companyLogoUrl", label: "Company logo URL", hint: "An https address or a path inside DAMS." },
  { key: "general.supportEmail", label: "Support email" },
  { key: "general.supportPhone", label: "Support phone" },
  { key: "general.companyAddress", label: "Company address" },
  { key: "general.publicBaseUrl", label: "Public DAMS address", hint: "Used to build links in email and push, e.g. https://dams.example.com" },
  { key: "general.brandPrimaryColor", label: "Header colour", hint: "Hex, e.g. #390217" },
  { key: "general.brandAccentColor", label: "Button colour", hint: "Hex, e.g. #f5d79e" },
  { key: "general.emailHeader", label: "Email header text" },
  { key: "general.emailFooter", label: "Email footer text" },
  { key: "general.copyrightText", label: "Copyright line" },
  { key: "general.socialLinks", label: "Social links" },
  { key: "general.dateFormat", label: "Date format", hint: "e.g. dd MMM yyyy" },
  { key: "general.currencySymbol", label: "Currency label", hint: "e.g. PKR" },
];

const EMAIL_FIELDS: { key: string; label: string; hint?: string; type?: string }[] = [
  { key: "email.senderName", label: "Sender name" },
  { key: "email.senderAddress", label: "Sender email" },
  { key: "email.replyTo", label: "Reply-to email" },
  { key: "email.smtp.host", label: "SMTP host", hint: "Host only, for example smtp.example.com. Do not include https:// or a port." },
  { key: "email.smtp.port", label: "SMTP port", type: "number", hint: "Usually 587 for STARTTLS or 465 for implicit TLS." },
  { key: "email.smtp.username", label: "SMTP username", hint: "Use dedicated SMTP credentials, not a personal account password." },
];

/** Everything this screen may write. Must stay inside the server's own allow-list. */
const EDITABLE_KEYS: string[] = [
  ...GENERAL_FIELDS.map((f) => f.key),
  ...EMAIL_FIELDS.map((f) => f.key),
  "email.enabled",
  "email.provider",
  "email.smtp.password",
  "email.smtp.useSsl",
  "email.attachReceipt",
  "email.testRecipient",
  "push.enabled",
  "push.displayName",
  "push.iconUrl",
  "push.badgeUrl",
  "push.defaultUrl",
  "push.vapid.subject",
];

function SettingsTab() {
  const [settings, setSettings] = useState<NotificationSettings | null>(null);
  const [values, setValues] = useState<Record<string, string>>({});
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState<{ tone: "ok" | "bad"; text: string } | null>(null);
  const [testRecipient, setTestRecipient] = useState("");
  const [audit, setAudit] = useState<AuditRow[]>([]);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const next = await fetchSettings();
      setSettings(next);

      const initial: Record<string, string> = {};
      for (const [key, value] of Object.entries(next.values)) initial[key] = value ?? "";
      // Secrets come back masked; the mask is what gets posted unless it is retyped.
      for (const key of Object.keys(next.secrets)) initial[key] = SECRET_PLACEHOLDER;
      setValues(initial);
      setTestRecipient(next.values["email.testRecipient"] ?? "");
      setAudit(await fetchAudit(20));
    } catch (err) {
      setMessage({ tone: "bad", text: err instanceof Error ? err.message : "Settings could not be loaded." });
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const set = (key: string, value: string) => setValues((current) => ({ ...current, [key]: value }));

  const onSave = async () => {
    setSaving(true);
    setMessage(null);
    try {
      // Only the keys this form owns are submitted. The settings response also carries
      // server-managed values (the public push key, the last test result), and posting one
      // back would be refused by the allow-list on the server.
      const payload: Record<string, string | null> = {};
      for (const key of EDITABLE_KEYS) {
        const value = values[key] ?? "";
        payload[key] = value.trim() === "" ? null : value;
      }
      const next = await saveSettings(payload);
      setSettings(next);
      setMessage({ tone: "ok", text: "Settings saved." });
      setAudit(await fetchAudit(20));
    } catch (err) {
      setMessage({ tone: "bad", text: err instanceof Error ? err.message : "Settings could not be saved." });
    } finally {
      setSaving(false);
    }
  };

  const onTestEmail = async () => {
    setSaving(true);
    setMessage(null);
    try {
      const result = await sendTestEmail(testRecipient.trim() === "" ? null : testRecipient.trim());
      setMessage({ tone: "ok", text: result.message });
      setSettings(await fetchSettings());
    } catch (err) {
      setMessage({ tone: "bad", text: err instanceof Error ? err.message : "The test email could not be sent." });
    } finally {
      setSaving(false);
    }
  };

  const onGenerateKeys = async () => {
    if (
      !window.confirm(
        "Generating new push keys signs every browser out of notifications. Everyone will be asked to enable them again. Continue?"
      )
    )
      return;

    setSaving(true);
    setMessage(null);
    try {
      await generatePushKeys();
      setMessage({ tone: "ok", text: "New push keys generated. Existing devices must enable notifications again." });
      await load();
    } catch (err) {
      setMessage({ tone: "bad", text: err instanceof Error ? err.message : "Push keys could not be generated." });
    } finally {
      setSaving(false);
    }
  };

  if (loading) return <p className="py-12 text-center text-sm text-[var(--text-muted)]">Loading settings…</p>;

  const status = settings?.status;

  return (
    <div className="space-y-4">
      {message && <Banner tone={message.tone}>{message.text}</Banner>}

      <div className="grid gap-4 sm:grid-cols-2">
        <StatusCard
          title="Email"
          enabled={status?.emailEnabled ?? false}
          configured={status?.emailConfigured ?? false}
          issue={status?.emailConfigurationIssue ?? null}
          lastTest={status?.emailLastTestAt ?? null}
          lastFailure={status?.emailLastFailure ?? null}
        />
        <StatusCard
          title="Browser push"
          enabled={status?.pushEnabled ?? false}
          configured={status?.pushConfigured ?? false}
          issue={status?.pushConfigurationIssue ?? null}
          lastTest={status?.pushLastTestAt ?? null}
          lastFailure={status?.pushLastFailure ?? null}
          extra={`${status?.activePushSubscriptions ?? 0} active device(s)`}
        />
      </div>

      <Panel title="Company and email branding">
        <div className="grid gap-3 sm:grid-cols-2">
          {GENERAL_FIELDS.map((field) => (
            <Field key={field.key} label={field.label} hint={field.hint}>
              <input
                type={field.type ?? "text"}
                value={values[field.key] ?? ""}
                onChange={(event) => set(field.key, event.target.value)}
                className={inputClass}
              />
            </Field>
          ))}
        </div>
      </Panel>

      <Panel title="Email delivery">
        <div className="grid gap-3 sm:grid-cols-2">
          <Field label="Email enabled" hint="Turning this off stops all outgoing email immediately.">
            <select
              value={values["email.enabled"] ?? "false"}
              onChange={(event) => set("email.enabled", event.target.value)}
              className={inputClass}
            >
              <option value="true">On</option>
              <option value="false">Off</option>
            </select>
          </Field>
          <Field label="Provider">
            <select
              value={values["email.provider"] ?? "smtp"}
              onChange={(event) => set("email.provider", event.target.value)}
              className={inputClass}
            >
              <option value="smtp">SMTP</option>
            </select>
          </Field>
          {EMAIL_FIELDS.map((field) => (
            <Field key={field.key} label={field.label} hint={field.hint}>
              <input
                type={field.type ?? "text"}
                value={values[field.key] ?? ""}
                onChange={(event) => set(field.key, event.target.value)}
                className={inputClass}
              />
            </Field>
          ))}
          <Field label="SMTP password" hint="Stored securely. Leave the masked value to keep the current one.">
            <input
              type="password"
              autoComplete="new-password"
              value={values["email.smtp.password"] ?? ""}
              onChange={(event) => set("email.smtp.password", event.target.value)}
              className={inputClass}
            />
          </Field>
          <Field label="Use TLS" hint="Recommended. Port 465 uses implicit TLS; other ports require STARTTLS.">
            <select
              value={values["email.smtp.useSsl"] ?? "true"}
              onChange={(event) => set("email.smtp.useSsl", event.target.value)}
              className={inputClass}
            >
              <option value="true">Yes</option>
              <option value="false">No</option>
            </select>
          </Field>
          <Field label="Attach receipt PDF" hint="Attaches the official receipt to payment receipt emails.">
            <select
              value={values["email.attachReceipt"] ?? "true"}
              onChange={(event) => set("email.attachReceipt", event.target.value)}
              className={inputClass}
            >
              <option value="true">Yes</option>
              <option value="false">No</option>
            </select>
          </Field>
        </div>

        <div className="mt-4 flex flex-wrap items-end gap-3 border-t border-[var(--border)] pt-4">
          <Field label="Send a test email to">
            <input
              type="email"
              value={testRecipient}
              onChange={(event) => setTestRecipient(event.target.value)}
              placeholder="you@example.com"
              className={inputClass}
            />
          </Field>
          <Button variant="outline" size="sm" onClick={() => void onTestEmail()} disabled={saving}>
            Send test email
          </Button>
        </div>
      </Panel>

      <Panel title="Browser push">
        <div className="grid gap-3 sm:grid-cols-2">
          <Field label="Push enabled">
            <select
              value={values["push.enabled"] ?? "false"}
              onChange={(event) => set("push.enabled", event.target.value)}
              className={inputClass}
            >
              <option value="true">On</option>
              <option value="false">Off</option>
            </select>
          </Field>
          <Field label="Notification name" hint="Shown as the title on a device.">
            <input
              type="text"
              value={values["push.displayName"] ?? ""}
              onChange={(event) => set("push.displayName", event.target.value)}
              className={inputClass}
            />
          </Field>
          <Field label="Default icon URL">
            <input type="text" value={values["push.iconUrl"] ?? ""} onChange={(e) => set("push.iconUrl", e.target.value)} className={inputClass} />
          </Field>
          <Field label="Default badge URL">
            <input type="text" value={values["push.badgeUrl"] ?? ""} onChange={(e) => set("push.badgeUrl", e.target.value)} className={inputClass} />
          </Field>
          <Field label="Default destination" hint="A path inside DAMS, e.g. /notifications">
            <input type="text" value={values["push.defaultUrl"] ?? ""} onChange={(e) => set("push.defaultUrl", e.target.value)} className={inputClass} />
          </Field>
          <Field label="Contact subject" hint="mailto: address or https URL required by push services.">
            <input type="text" value={values["push.vapid.subject"] ?? ""} onChange={(e) => set("push.vapid.subject", e.target.value)} className={inputClass} />
          </Field>
        </div>

        <div className="mt-4 flex flex-wrap items-center gap-3 border-t border-[var(--border)] pt-4">
          <Button variant="outline" size="sm" onClick={() => void onGenerateKeys()} disabled={saving}>
            {settings?.status.pushConfigured ? "Regenerate push keys" : "Generate push keys"}
          </Button>
          <p className="text-xs text-[var(--text-muted)]">
            The private key never leaves the server. Regenerating signs every browser out of notifications.
          </p>
        </div>
      </Panel>

      <div className="flex flex-wrap items-center gap-3">
        <Button onClick={() => void onSave()} disabled={saving}>
          {saving ? "Saving…" : "Save settings"}
        </Button>
        <Button variant="ghost" onClick={() => void load()} disabled={saving}>
          Discard changes
        </Button>
      </div>

      <Panel title="Recent configuration changes">
        {audit.length === 0 ? (
          <p className="text-sm text-[var(--text-muted)]">Nothing recorded yet.</p>
        ) : (
          <ul className="divide-y divide-[var(--border)] text-sm">
            {audit.map((entry) => (
              <li key={entry.id} className="flex flex-wrap items-baseline justify-between gap-2 py-2">
                <span className="text-[var(--text-secondary)]">
                  <span className="font-medium text-[var(--text-heading)]">{entry.area}</span> · {entry.action}
                  {entry.details && <span className="text-[var(--text-muted)]"> — {entry.details}</span>}
                </span>
                <span className="text-xs text-[var(--text-muted)]">
                  {entry.performedByName ?? "—"} · {formatDateTime(entry.occurredAt)}
                </span>
              </li>
            ))}
          </ul>
        )}
      </Panel>
    </div>
  );
}

// ── Templates ──────────────────────────────────────────────────────────────────

function TemplatesTab() {
  const [templates, setTemplates] = useState<TemplateRow[]>([]);
  const [selected, setSelected] = useState<TemplateRow | null>(null);
  const [draft, setDraft] = useState<TemplateRow | null>(null);
  const [preview, setPreview] = useState<TemplatePreview | null>(null);
  const [message, setMessage] = useState<{ tone: "ok" | "bad"; text: string } | null>(null);
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const rows = await fetchTemplates();
      setTemplates(rows);
      setSelected((current) => rows.find((r) => r.type === current?.type && r.channel === current?.channel) ?? rows[0] ?? null);
    } catch (err) {
      setMessage({ tone: "bad", text: err instanceof Error ? err.message : "Templates could not be loaded." });
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    setDraft(selected ? { ...selected } : null);
    setPreview(null);
  }, [selected]);

  const onSave = async () => {
    if (!draft) return;
    setBusy(true);
    setMessage(null);
    try {
      const saved = await saveTemplate(draft.type, draft.channel, {
        name: draft.name,
        subject: draft.subject,
        heading: draft.heading,
        body: draft.body,
        actionText: draft.actionText,
        actionUrl: draft.actionUrl,
        footer: draft.footer,
        iconUrl: draft.iconUrl,
        badgeUrl: draft.badgeUrl,
        isEnabled: draft.isEnabled,
      });
      setMessage({ tone: "ok", text: `Saved as version ${saved.version}.` });
      await load();
      setSelected(saved);
    } catch (err) {
      setMessage({ tone: "bad", text: err instanceof Error ? err.message : "The template could not be saved." });
    } finally {
      setBusy(false);
    }
  };

  const onPreview = async () => {
    if (!draft) return;
    setBusy(true);
    setMessage(null);
    try {
      setPreview(await previewTemplate(draft.type, draft));
    } catch (err) {
      setMessage({ tone: "bad", text: err instanceof Error ? err.message : "The preview could not be generated." });
    } finally {
      setBusy(false);
    }
  };

  if (loading) return <p className="py-12 text-center text-sm text-[var(--text-muted)]">Loading templates…</p>;

  return (
    <div className="grid gap-4 lg:grid-cols-[18rem_1fr]">
      <nav className="max-h-[32rem] overflow-y-auto rounded-2xl border border-[var(--border)] bg-[var(--bg-card)] p-2" aria-label="Templates">
        <ul className="space-y-1">
          {templates.map((template) => {
            const active = selected?.type === template.type && selected?.channel === template.channel;
            return (
              <li key={`${template.type}-${template.channel}`}>
                <button
                  type="button"
                  onClick={() => setSelected(template)}
                  aria-current={active ? "true" : undefined}
                  className={`w-full rounded-lg px-3 py-2 text-left text-sm transition ${
                    active ? "bg-[var(--accent)] text-white" : "text-[var(--text-secondary)] hover:bg-[var(--surface-glass-hover)]"
                  }`}
                >
                  <span className="block font-medium">{template.name}</span>
                  <span className={`text-[11px] ${active ? "text-white/75" : "text-[var(--text-muted)]"}`}>
                    {template.channel === "Email" ? "Email" : "Browser push"}
                    {!template.isEnabled && " · disabled"}
                  </span>
                </button>
              </li>
            );
          })}
        </ul>
      </nav>

      <div className="space-y-4">
        {message && <Banner tone={message.tone}>{message.text}</Banner>}

        {draft && (
          <Panel title={`${draft.name} — ${draft.channel === "Email" ? "email" : "browser push"}`}>
            <div className="grid gap-3">
              <Field label={draft.channel === "Email" ? "Subject" : "Push title"}>
                <input value={draft.subject} onChange={(e) => setDraft({ ...draft, subject: e.target.value })} className={inputClass} />
              </Field>

              {draft.channel === "Email" && (
                <Field label="Heading">
                  <input value={draft.heading ?? ""} onChange={(e) => setDraft({ ...draft, heading: e.target.value })} className={inputClass} />
                </Field>
              )}

              <Field
                label={draft.channel === "Email" ? "Body" : "Push message"}
                hint={
                  draft.channel === "Email"
                    ? "Basic formatting is allowed. Scripts and unsafe markup are rejected."
                    : "Keep it short. Push messages can be read on a lock screen, so leave out private details."
                }
              >
                <textarea
                  rows={draft.channel === "Email" ? 8 : 3}
                  value={draft.body}
                  onChange={(e) => setDraft({ ...draft, body: e.target.value })}
                  className={`${inputClass} font-mono text-xs`}
                />
              </Field>

              <div className="grid gap-3 sm:grid-cols-2">
                <Field label="Action button text">
                  <input value={draft.actionText ?? ""} onChange={(e) => setDraft({ ...draft, actionText: e.target.value })} className={inputClass} />
                </Field>
                <Field label="Action destination" hint="A path inside DAMS. Leave blank to use the notification's own link.">
                  <input value={draft.actionUrl ?? ""} onChange={(e) => setDraft({ ...draft, actionUrl: e.target.value })} className={inputClass} />
                </Field>
              </div>

              {draft.channel === "Email" && (
                <Field label="Footer">
                  <textarea rows={2} value={draft.footer ?? ""} onChange={(e) => setDraft({ ...draft, footer: e.target.value })} className={inputClass} />
                </Field>
              )}

              {draft.channel === "WebPush" && (
                <div className="grid gap-3 sm:grid-cols-2">
                  <Field label="Icon URL"><input value={draft.iconUrl ?? ""} onChange={(e) => setDraft({ ...draft, iconUrl: e.target.value })} className={inputClass} /></Field>
                  <Field label="Badge URL"><input value={draft.badgeUrl ?? ""} onChange={(e) => setDraft({ ...draft, badgeUrl: e.target.value })} className={inputClass} /></Field>
                </div>
              )}

              <label className={`flex items-center gap-2 text-sm ${draft.isMandatory ? "opacity-60" : ""}`}>
                <input
                  type="checkbox"
                  checked={draft.isEnabled}
                  disabled={draft.isMandatory}
                  onChange={(e) => setDraft({ ...draft, isEnabled: e.target.checked })}
                  className="h-4 w-4 accent-[var(--accent)]"
                />
                <span className="text-[var(--text-secondary)]">
                  Enabled{draft.isMandatory && " — this is an essential notification and cannot be disabled"}
                </span>
              </label>

              <div className="rounded-lg border border-[var(--border)] bg-[var(--surface-glass)] p-3">
                <p className="text-xs font-semibold text-[var(--text-heading)]">Available variables</p>
                <p className="mt-1 flex flex-wrap gap-1.5">
                  {draft.availableVariables.map((variable) => (
                    <code key={variable} className="rounded bg-[var(--bg-card)] px-1.5 py-0.5 text-[11px] text-[var(--text-secondary)]">
                      {`{{${variable}}}`}
                    </code>
                  ))}
                </p>
                <p className="mt-2 text-[11px] text-[var(--text-muted)]">
                  Only these are accepted. A variable with no value for a particular message is left out cleanly.
                </p>
              </div>

              <div className="flex flex-wrap gap-2">
                <Button size="sm" onClick={() => void onSave()} disabled={busy}>Save template</Button>
                <Button variant="outline" size="sm" onClick={() => void onPreview()} disabled={busy}>Preview</Button>
                {selected && (
                  <Button variant="ghost" size="sm" onClick={() => setDraft({ ...selected })} disabled={busy}>
                    Reset
                  </Button>
                )}
              </div>

              {draft.updatedAt && (
                <p className="text-xs text-[var(--text-muted)]">
                  Version {draft.version} · last edited {formatDateTime(draft.updatedAt)}
                  {draft.updatedByName && ` by ${draft.updatedByName}`}
                </p>
              )}
            </div>
          </Panel>
        )}

        {preview && (
          <Panel title="Preview">
            <p className="text-sm text-[var(--text-secondary)]">
              <span className="font-semibold text-[var(--text-heading)]">Subject:</span> {preview.subject}
            </p>
            {preview.pushTitle && (
              <p className="mt-2 rounded-lg border border-[var(--border)] bg-[var(--surface-glass)] p-3 text-sm">
                <span className="block font-semibold text-[var(--text-heading)]">{preview.pushTitle}</span>
                <span className="text-[var(--text-secondary)]">{preview.pushBody}</span>
              </p>
            )}
            <div className="mt-3 overflow-x-auto rounded-lg border border-[var(--border)]">
              {/* Sandboxed and script-free: the server has already stripped anything executable. */}
              <iframe
                title="Email preview"
                sandbox=""
                srcDoc={preview.html}
                className="h-[28rem] w-full bg-white"
              />
            </div>
          </Panel>
        )}
      </div>
    </div>
  );
}

// ── Rules ──────────────────────────────────────────────────────────────────────

function RulesTab() {
  const [rules, setRules] = useState<RuleRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [busyType, setBusyType] = useState<string | null>(null);
  const [message, setMessage] = useState<{ tone: "ok" | "bad"; text: string } | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setRules(await fetchRules());
    } catch (err) {
      setMessage({ tone: "bad", text: err instanceof Error ? err.message : "Rules could not be loaded." });
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const update = async (rule: RuleRow, patch: Partial<RuleRow>) => {
    const next = { ...rule, ...patch };

    // Weakening an essential notification is a deliberate act, so it is confirmed here as
    // well as being refused by the server without acknowledgement.
    let confirmEssentialChange = false;
    if (rule.isMandatory) {
      const weakening = (!next.emailEnabled && rule.emailEnabled) || (!next.pushEnabled && rule.pushEnabled);
      if (weakening) {
        confirmEssentialChange = window.confirm(
          `"${rule.name}" is an essential notification. Reducing how it is delivered can mean a customer never receives a receipt or a security message. Continue?`
        );
        if (!confirmEssentialChange) return;
      }
    }

    setBusyType(rule.type);
    setMessage(null);
    try {
      const saved = await saveRule(rule.type, {
        isEnabled: next.isEnabled,
        inAppEnabled: next.inAppEnabled,
        emailEnabled: next.emailEnabled,
        pushEnabled: next.pushEnabled,
        priority: next.priority,
        delayMinutes: next.delayMinutes,
        reminderLeadDays: next.reminderLeadDays,
        remindOnDueDate: next.remindOnDueDate,
        repeatWhenOverdue: next.repeatWhenOverdue,
        escalateToSupervisors: next.escalateToSupervisors,
        confirmEssentialChange,
      });
      setRules((current) => current.map((r) => (r.type === saved.type ? saved : r)));
      setMessage({ tone: "ok", text: `${saved.name} updated.` });
    } catch (err) {
      setMessage({ tone: "bad", text: err instanceof Error ? err.message : "That rule could not be saved." });
      await load();
    } finally {
      setBusyType(null);
    }
  };

  if (loading) return <p className="py-12 text-center text-sm text-[var(--text-muted)]">Loading rules…</p>;

  const grouped = rules.reduce<Record<string, RuleRow[]>>((acc, rule) => {
    (acc[rule.category] ??= []).push(rule);
    return acc;
  }, {});

  return (
    <div className="space-y-4">
      {message && <Banner tone={message.tone}>{message.text}</Banner>}

      {Object.entries(grouped).map(([category, group]) => (
        <Panel key={category} title={CATEGORY_LABELS[category as keyof typeof CATEGORY_LABELS] ?? category}>
          <div className="overflow-x-auto">
            <table className="w-full min-w-[46rem] text-sm">
              <thead>
                <tr className="text-left text-xs uppercase tracking-wide text-[var(--text-muted)]">
                  <th scope="col" className="pb-2 pr-3 font-semibold">Notification</th>
                  <th scope="col" className="pb-2 px-2 font-semibold">On</th>
                  <th scope="col" className="pb-2 px-2 font-semibold">In-app</th>
                  <th scope="col" className="pb-2 px-2 font-semibold">Email</th>
                  <th scope="col" className="pb-2 px-2 font-semibold">Push</th>
                  <th scope="col" className="pb-2 px-2 font-semibold">Reminder</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-[var(--border)]">
                {group.map((rule) => (
                  <tr key={rule.type} className={busyType === rule.type ? "opacity-60" : ""}>
                    <th scope="row" className="py-2.5 pr-3 text-left font-medium text-[var(--text-heading)]">
                      {rule.name}
                      {rule.isMandatory && (
                        <span className="ml-2 rounded bg-[var(--surface-glass)] px-1.5 py-0.5 text-[10px] font-semibold uppercase text-[var(--text-muted)]">
                          Essential
                        </span>
                      )}
                    </th>
                    <Cell>
                      <Check
                        label={`${rule.name} enabled`}
                        checked={rule.isEnabled}
                        disabled={rule.isMandatory || busyType === rule.type}
                        onChange={(v) => void update(rule, { isEnabled: v })}
                      />
                    </Cell>
                    <Cell>
                      <Check
                        label={`${rule.name} in-app`}
                        checked={rule.inAppEnabled}
                        disabled={rule.isMandatory || busyType === rule.type}
                        onChange={(v) => void update(rule, { inAppEnabled: v })}
                      />
                    </Cell>
                    <Cell>
                      <Check
                        label={`${rule.name} email`}
                        checked={rule.emailEnabled}
                        disabled={busyType === rule.type}
                        onChange={(v) => void update(rule, { emailEnabled: v })}
                      />
                    </Cell>
                    <Cell>
                      <Check
                        label={`${rule.name} push`}
                        checked={rule.pushEnabled}
                        disabled={busyType === rule.type}
                        onChange={(v) => void update(rule, { pushEnabled: v })}
                      />
                    </Cell>
                    <Cell>
                      {rule.type === "InstallmentDue" || rule.type === "SiteVisitReminder" ? (
                        <label className="flex items-center gap-1.5 text-xs text-[var(--text-muted)]">
                          <input
                            type="number"
                            min={0}
                            max={60}
                            value={rule.reminderLeadDays}
                            disabled={busyType === rule.type}
                            onChange={(e) =>
                              setRules((current) =>
                                current.map((r) =>
                                  r.type === rule.type ? { ...r, reminderLeadDays: Number(e.target.value) } : r
                                )
                              )
                            }
                            onBlur={() => void update(rule, { reminderLeadDays: rule.reminderLeadDays })}
                            className="w-14 rounded-md border border-[var(--border)] bg-[var(--input-bg)] px-1.5 py-1 text-xs"
                            aria-label={`${rule.name} days before`}
                          />
                          days before
                        </label>
                      ) : rule.type === "InstallmentOverdue" ? (
                        <Check
                          label={`${rule.name} repeat daily`}
                          checked={rule.repeatWhenOverdue}
                          disabled={busyType === rule.type}
                          onChange={(v) => void update(rule, { repeatWhenOverdue: v })}
                          text="repeat daily"
                        />
                      ) : (
                        <span className="text-xs text-[var(--text-muted)]">—</span>
                      )}
                    </Cell>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Panel>
      ))}
    </div>
  );
}

// ── Compose and schedule ───────────────────────────────────────────────────────

const AUDIENCES: { value: AudienceType; label: string }[] = [
  { value: "AllCustomers", label: "All customers" },
  { value: "AllSalesEmployees", label: "All sales employees" },
  { value: "AllManagers", label: "All managers" },
  { value: "AllInternalStaff", label: "All internal staff" },
  { value: "CustomersWithOverdueInstallments", label: "Customers with overdue installments" },
  { value: "CustomersInProject", label: "Customers in a project" },
  { value: "Team", label: "One team" },
  { value: "SelectedUsers", label: "Selected users" },
];

function ComposeTab() {
  const [form, setForm] = useState({
    title: "",
    message: "",
    actionUrl: "",
    audience: "AllCustomers" as AudienceType,
    projectId: "",
    teamId: "",
    userIds: "",
    sendEmail: true,
    sendPush: true,
    scheduledAt: "",
    priority: "Normal" as ComposeRequest["priority"],
  });
  const [preview, setPreview] = useState<AudiencePreview | null>(null);
  const [jobs, setJobs] = useState<JobRow[]>([]);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ tone: "ok" | "bad"; text: string } | null>(null);
  // Regenerated after each successful send, so a second click cannot repeat a broadcast.
  const [requestKey, setRequestKey] = useState(() => crypto.randomUUID());

  const loadJobs = useCallback(async () => {
    try {
      setJobs(await fetchJobs(25));
    } catch {
      // The composer still works without the history list.
    }
  }, []);

  useEffect(() => {
    void loadJobs();
  }, [loadJobs]);

  const request = useMemo<ComposeRequest>(
    () => ({
      title: form.title,
      message: form.message,
      actionUrl: form.actionUrl.trim() === "" ? null : form.actionUrl.trim(),
      type: "AdminAnnouncement",
      priority: form.priority,
      sendEmail: form.sendEmail,
      sendPush: form.sendPush,
      audience: {
        type: form.audience,
        userIds: form.userIds
          .split(/[\s,]+/)
          .map((value) => Number(value))
          .filter((value) => Number.isFinite(value) && value > 0),
        teamId: form.teamId.trim() === "" ? null : Number(form.teamId),
        projectId: form.projectId.trim() === "" ? null : Number(form.projectId),
        bookingIds: [],
        leadIds: [],
      },
      // datetime-local gives a local wall-clock value; converting here means the schedule
      // means the same instant regardless of where the server runs.
      scheduledAt: form.scheduledAt === "" ? null : new Date(form.scheduledAt).toISOString(),
      requestKey,
    }),
    [form, requestKey]
  );

  const onPreview = async () => {
    setBusy(true);
    setMessage(null);
    try {
      setPreview(await previewAudience(request));
    } catch (err) {
      setMessage({ tone: "bad", text: err instanceof Error ? err.message : "The audience could not be worked out." });
    } finally {
      setBusy(false);
    }
  };

  const onSend = async () => {
    if (!preview) {
      setMessage({ tone: "bad", text: "Check the audience before sending." });
      return;
    }

    const confirmText = form.scheduledAt
      ? `Schedule this for ${preview.totalRecipients} recipient(s)?`
      : `Send this now to ${preview.totalRecipients} recipient(s)? This cannot be undone.`;

    if (!window.confirm(confirmText)) return;

    setBusy(true);
    setMessage(null);
    try {
      const job = await compose({ ...request, confirmLargeAudience: true });
      setMessage({
        tone: "ok",
        text: job.scheduledAt
          ? `Scheduled for ${formatDateTime(job.scheduledAt)} — ${job.recipientCount} recipient(s).`
          : `Queued for ${job.recipientCount} recipient(s).`,
      });
      setRequestKey(crypto.randomUUID());
      setForm((current) => ({ ...current, title: "", message: "", actionUrl: "", scheduledAt: "" }));
      setPreview(null);
      await loadJobs();
    } catch (err) {
      setMessage({ tone: "bad", text: err instanceof Error ? err.message : "The notification could not be sent." });
    } finally {
      setBusy(false);
    }
  };

  const onCancel = async (id: number) => {
    if (!window.confirm("Cancel this scheduled notification?")) return;
    try {
      await cancelJob(id);
      await loadJobs();
    } catch (err) {
      setMessage({ tone: "bad", text: err instanceof Error ? err.message : "It could not be cancelled." });
    }
  };

  return (
    <div className="space-y-4">
      {message && <Banner tone={message.tone}>{message.text}</Banner>}

      <Panel title="Compose a notification">
        <div className="grid gap-3">
          <Field label="Title">
            <input value={form.title} onChange={(e) => setForm({ ...form, title: e.target.value })} className={inputClass} maxLength={200} />
          </Field>
          <Field label="Message" hint="Written to customers and staff exactly as typed. Never paste internal CRM notes here.">
            <textarea rows={4} value={form.message} onChange={(e) => setForm({ ...form, message: e.target.value })} className={inputClass} maxLength={2000} />
          </Field>
          <div className="grid gap-3 sm:grid-cols-2">
            <Field label="Destination" hint="Optional path inside DAMS, e.g. /projects/3">
              <input value={form.actionUrl} onChange={(e) => setForm({ ...form, actionUrl: e.target.value })} className={inputClass} />
            </Field>
            <Field label="Priority">
              <select value={form.priority} onChange={(e) => setForm({ ...form, priority: e.target.value as ComposeRequest["priority"] })} className={inputClass}>
                <option value="Low">Low</option>
                <option value="Normal">Normal</option>
                <option value="High">High</option>
              </select>
            </Field>
            <Field label="Audience">
              <select value={form.audience} onChange={(e) => { setForm({ ...form, audience: e.target.value as AudienceType }); setPreview(null); }} className={inputClass}>
                {AUDIENCES.map((option) => (
                  <option key={option.value} value={option.value}>{option.label}</option>
                ))}
              </select>
            </Field>
            {form.audience === "CustomersInProject" && (
              <Field label="Project id">
                <input value={form.projectId} onChange={(e) => { setForm({ ...form, projectId: e.target.value }); setPreview(null); }} className={inputClass} inputMode="numeric" />
              </Field>
            )}
            {form.audience === "Team" && (
              <Field label="Team id">
                <input value={form.teamId} onChange={(e) => { setForm({ ...form, teamId: e.target.value }); setPreview(null); }} className={inputClass} inputMode="numeric" />
              </Field>
            )}
            {form.audience === "SelectedUsers" && (
              <Field label="User ids" hint="Comma or space separated.">
                <input value={form.userIds} onChange={(e) => { setForm({ ...form, userIds: e.target.value }); setPreview(null); }} className={inputClass} />
              </Field>
            )}
            <Field label="Send at" hint="Leave blank to send immediately. Your local time.">
              <input type="datetime-local" value={form.scheduledAt} onChange={(e) => setForm({ ...form, scheduledAt: e.target.value })} className={inputClass} />
            </Field>
          </div>

          <fieldset className="flex flex-wrap gap-4">
            <legend className="mb-1 text-xs font-semibold uppercase tracking-wide text-[var(--text-muted)]">Channels</legend>
            <label className="flex items-center gap-2 text-sm text-[var(--text-secondary)]">
              <input type="checkbox" checked disabled className="h-4 w-4 accent-[var(--accent)]" />
              In-app (always)
            </label>
            <label className="flex items-center gap-2 text-sm text-[var(--text-secondary)]">
              <input type="checkbox" checked={form.sendEmail} onChange={(e) => setForm({ ...form, sendEmail: e.target.checked })} className="h-4 w-4 accent-[var(--accent)]" />
              Email
            </label>
            <label className="flex items-center gap-2 text-sm text-[var(--text-secondary)]">
              <input type="checkbox" checked={form.sendPush} onChange={(e) => setForm({ ...form, sendPush: e.target.checked })} className="h-4 w-4 accent-[var(--accent)]" />
              Browser push
            </label>
          </fieldset>

          <div className="flex flex-wrap gap-2">
            <Button variant="outline" size="sm" onClick={() => void onPreview()} disabled={busy}>
              Check audience
            </Button>
            <Button size="sm" onClick={() => void onSend()} disabled={busy || !preview || form.title.trim() === "" || form.message.trim() === ""}>
              {form.scheduledAt ? "Schedule" : "Send now"}
            </Button>
          </div>

          {preview && (
            <div
              className={`rounded-lg border p-3 text-sm ${
                preview.requiresConfirmation
                  ? "border-[var(--accent-rose)]/30 bg-[var(--accent-rose-glow)]"
                  : "border-[var(--border)] bg-[var(--surface-glass)]"
              }`}
              role="status"
            >
              <p className="font-semibold text-[var(--text-heading)]">
                {preview.description}: {preview.totalRecipients} recipient(s)
              </p>
              <ul className="mt-1 space-y-0.5 text-xs text-[var(--text-secondary)]">
                <li>{preview.withEmail} have a valid email address{form.sendEmail && preview.emailOptedOut > 0 ? `, ${preview.emailOptedOut} have turned email off for this category` : ""}</li>
                <li>{preview.withPushDevices} have browser notifications on ({preview.pushDeviceCount} device(s)){form.sendPush && preview.pushOptedOut > 0 ? `, ${preview.pushOptedOut} have turned push off for this category` : ""}</li>
                {preview.sampleRecipients.length > 0 && <li>For example: {preview.sampleRecipients.join(", ")}</li>}
              </ul>
              {preview.requiresConfirmation && (
                <p className="mt-2 text-xs font-semibold text-[var(--accent-rose)]">
                  This is a large audience. Read the message once more before sending.
                </p>
              )}
            </div>
          )}
        </div>
      </Panel>

      <Panel title="Sent and scheduled">
        {jobs.length === 0 ? (
          <p className="text-sm text-[var(--text-muted)]">Nothing has been sent from here yet.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[44rem] text-sm">
              <thead>
                <tr className="text-left text-xs uppercase tracking-wide text-[var(--text-muted)]">
                  <th scope="col" className="pb-2 pr-3 font-semibold">Message</th>
                  <th scope="col" className="pb-2 px-2 font-semibold">Audience</th>
                  <th scope="col" className="pb-2 px-2 font-semibold">When</th>
                  <th scope="col" className="pb-2 px-2 font-semibold">Status</th>
                  <th scope="col" className="pb-2 pl-2 font-semibold"><span className="sr-only">Actions</span></th>
                </tr>
              </thead>
              <tbody className="divide-y divide-[var(--border)]">
                {jobs.map((job) => (
                  <tr key={job.id}>
                    <th scope="row" className="max-w-[16rem] py-2.5 pr-3 text-left font-medium text-[var(--text-heading)]">
                      <span className="block truncate">{job.title}</span>
                      <span className="block truncate text-xs font-normal text-[var(--text-muted)]">{job.message}</span>
                    </th>
                    <td className="px-2 py-2.5 text-[var(--text-secondary)]">
                      {job.audienceDescription}
                      <span className="block text-xs text-[var(--text-muted)]">{job.recipientCount} recipient(s)</span>
                    </td>
                    <td className="px-2 py-2.5 text-xs text-[var(--text-secondary)]">
                      {job.scheduledAt ? formatDateTime(job.scheduledAt) : formatDateTime(job.createdAt)}
                    </td>
                    <td className="px-2 py-2.5">
                      <StatusPill status={job.status} />
                      {job.failureReason && <span className="block text-xs text-[var(--accent-rose)]">{job.failureReason}</span>}
                    </td>
                    <td className="py-2.5 pl-2 text-right">
                      {job.status === "Scheduled" && (
                        <Button variant="ghost" size="sm" onClick={() => void onCancel(job.id)}>Cancel</Button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Panel>
    </div>
  );
}

// ── Delivery history ───────────────────────────────────────────────────────────

function HistoryTab() {
  const [rows, setRows] = useState<DeliveryRow[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [failuresOnly, setFailuresOnly] = useState(false);
  const [channel, setChannel] = useState<string>("");
  const [search, setSearch] = useState("");
  const [loading, setLoading] = useState(true);
  const [message, setMessage] = useState<{ tone: "ok" | "bad"; text: string } | null>(null);
  const [suppressions, setSuppressions] = useState<SuppressionRow[]>([]);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const result = await fetchDeliveries({ page, pageSize: 25, failuresOnly, channel: channel || null, search });
      setRows(result.items);
      setTotal(result.totalCount);
      setSuppressions(await fetchSuppressions());
    } catch (err) {
      setMessage({ tone: "bad", text: err instanceof Error ? err.message : "Delivery history could not be loaded." });
    } finally {
      setLoading(false);
    }
  }, [channel, failuresOnly, page, search]);

  useEffect(() => {
    void load();
  }, [load]);

  const onRetry = async (id: number) => {
    try {
      await retryDelivery(id);
      setMessage({ tone: "ok", text: "Queued for another attempt." });
      await load();
    } catch (err) {
      setMessage({ tone: "bad", text: err instanceof Error ? err.message : "It could not be retried." });
    }
  };

  const totalPages = Math.max(1, Math.ceil(total / 25));

  return (
    <div className="space-y-4">
      {message && <Banner tone={message.tone}>{message.text}</Banner>}

      <Panel title="Delivery history">
        <div className="mb-3 flex flex-wrap items-end gap-3">
          <Field label="Channel">
            <select value={channel} onChange={(e) => { setChannel(e.target.value); setPage(1); }} className={inputClass}>
              <option value="">All</option>
              <option value="InApp">In-app</option>
              <option value="Email">Email</option>
              <option value="WebPush">Browser push</option>
            </select>
          </Field>
          <Field label="Search">
            <input
              value={search}
              onChange={(e) => { setSearch(e.target.value); setPage(1); }}
              placeholder="Title, address or reason"
              className={inputClass}
            />
          </Field>
          <label className="mb-1 inline-flex cursor-pointer items-center gap-2 rounded-lg border border-[var(--border)] px-3 py-2 text-xs font-medium text-[var(--text-secondary)]">
            <input type="checkbox" checked={failuresOnly} onChange={(e) => { setFailuresOnly(e.target.checked); setPage(1); }} className="h-3.5 w-3.5 accent-[var(--accent)]" />
            Problems only
          </label>
        </div>

        {loading && rows.length === 0 && <p className="py-10 text-center text-sm text-[var(--text-muted)]">Loading…</p>}
        {!loading && rows.length === 0 && <p className="py-10 text-center text-sm text-[var(--text-muted)]">Nothing matches these filters.</p>}

        {rows.length > 0 && (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[54rem] text-sm">
              <thead>
                <tr className="text-left text-xs uppercase tracking-wide text-[var(--text-muted)]">
                  <th scope="col" className="pb-2 pr-3 font-semibold">Notification</th>
                  <th scope="col" className="pb-2 px-2 font-semibold">Recipient</th>
                  <th scope="col" className="pb-2 px-2 font-semibold">Channel</th>
                  <th scope="col" className="pb-2 px-2 font-semibold">Status</th>
                  <th scope="col" className="pb-2 px-2 font-semibold">Timeline</th>
                  <th scope="col" className="pb-2 pl-2 font-semibold"><span className="sr-only">Actions</span></th>
                </tr>
              </thead>
              <tbody className="divide-y divide-[var(--border)]">
                {rows.map((row) => (
                  <tr key={row.id}>
                    <th scope="row" className="max-w-[18rem] py-2.5 pr-3 text-left font-medium text-[var(--text-heading)]">
                      <span className="block truncate">{row.title}</span>
                      <span className="block text-xs font-normal text-[var(--text-muted)]">
                        {CATEGORY_LABELS[row.category] ?? row.category}
                        {row.jobId && ` · broadcast #${row.jobId}`}
                      </span>
                    </th>
                    <td className="px-2 py-2.5 text-[var(--text-secondary)]">
                      <span className="block">{row.recipientName ?? "—"}</span>
                      <span className="block text-xs text-[var(--text-muted)]">{row.target ?? "—"}</span>
                    </td>
                    <td className="px-2 py-2.5 text-[var(--text-secondary)]">
                      {row.channel === "WebPush" ? "Browser" : row.channel === "InApp" ? "In-app" : "Email"}
                    </td>
                    <td className="px-2 py-2.5">
                      <StatusPill status={row.status} />
                      {row.failureReason && (
                        <span className="mt-0.5 block max-w-[16rem] text-xs text-[var(--accent-rose)]">{row.failureReason}</span>
                      )}
                    </td>
                    <td className="px-2 py-2.5 text-xs text-[var(--text-secondary)]">
                      <span className="block">Created {formatDateTime(row.createdAt)}</span>
                      {row.sentAt && <span className="block">Sent {formatDateTime(row.sentAt)}</span>}
                      {row.deliveredAt && <span className="block">Delivered {formatDateTime(row.deliveredAt)}</span>}
                      {row.nextAttemptAt && <span className="block">Next try {formatDateTime(row.nextAttemptAt)}</span>}
                      <span className="block text-[var(--text-muted)]">{row.attemptCount} attempt(s)</span>
                    </td>
                    <td className="py-2.5 pl-2 text-right">
                      {row.canRetry && (
                        <Button variant="ghost" size="sm" onClick={() => void onRetry(row.id)}>Retry</Button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {totalPages > 1 && (
          <nav className="mt-3 flex items-center justify-between gap-3 border-t border-[var(--border)] pt-3" aria-label="Delivery pages">
            <Button variant="ghost" size="sm" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>Previous</Button>
            <span className="text-xs text-[var(--text-muted)]">Page {page} of {totalPages}</span>
            <Button variant="ghost" size="sm" disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)}>Next</Button>
          </nav>
        )}
      </Panel>

      <Panel title="Blocked email addresses">
        {suppressions.length === 0 ? (
          <p className="text-sm text-[var(--text-muted)]">No addresses are currently blocked.</p>
        ) : (
          <ul className="divide-y divide-[var(--border)]">
            {suppressions.map((row) => (
              <li key={row.id} className="flex flex-wrap items-center justify-between gap-2 py-2 text-sm">
                <span>
                  <span className="font-medium text-[var(--text-heading)]">{row.email}</span>
                  <span className="block text-xs text-[var(--text-muted)]">{row.reason} · {formatDateTime(row.createdAt)}</span>
                </span>
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={async () => {
                    await removeSuppression(row.id);
                    await load();
                  }}
                >
                  Unblock
                </Button>
              </li>
            ))}
          </ul>
        )}
      </Panel>
    </div>
  );
}

// ── Small shared pieces ────────────────────────────────────────────────────────

const inputClass =
  "w-full rounded-lg border border-[var(--border)] bg-[var(--input-bg)] px-3 py-2 text-sm text-[var(--text-primary)] outline-none transition focus:border-[var(--border-active)] focus:bg-[var(--input-bg-focus)]";

function Panel({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="rounded-2xl border border-[var(--border)] bg-[var(--bg-card)] p-4 sm:p-5">
      <h2 className="mb-3 text-base font-semibold text-[var(--text-heading)]">{title}</h2>
      {children}
    </section>
  );
}

function Field({ label, hint, children }: { label: string; hint?: string; children: React.ReactNode }) {
  return (
    <label className="block">
      <span className="mb-1 block text-xs font-semibold uppercase tracking-wide text-[var(--text-muted)]">{label}</span>
      {children}
      {hint && <span className="mt-1 block text-[11px] text-[var(--text-muted)]">{hint}</span>}
    </label>
  );
}

function Banner({ tone, children }: { tone: "ok" | "bad"; children: React.ReactNode }) {
  return (
    <p
      role={tone === "bad" ? "alert" : "status"}
      className={`rounded-lg border px-3 py-2 text-sm ${
        tone === "ok"
          ? "border-[var(--accent-emerald)]/30 bg-[var(--accent-emerald-glow)] text-[var(--accent-emerald)]"
          : "border-[var(--accent-rose)]/30 bg-[var(--accent-rose-glow)] text-[var(--accent-rose)]"
      }`}
    >
      {children}
    </p>
  );
}

function StatusCard({
  title,
  enabled,
  configured,
  issue,
  lastTest,
  lastFailure,
  extra,
}: {
  title: string;
  enabled: boolean;
  configured: boolean;
  issue: string | null;
  lastTest: string | null;
  lastFailure: string | null;
  extra?: string;
}) {
  const state = !enabled ? "Off" : configured ? "Ready" : "Needs setup";
  return (
    <section className="rounded-2xl border border-[var(--border)] bg-[var(--bg-card)] p-4">
      <div className="flex items-center justify-between gap-2">
        <h2 className="text-base font-semibold text-[var(--text-heading)]">{title}</h2>
        {/* Word plus colour, so status never depends on colour alone. */}
        <span
          className={`rounded-full px-2.5 py-1 text-xs font-semibold ${
            state === "Ready"
              ? "bg-[var(--accent-emerald-glow)] text-[var(--accent-emerald)]"
              : state === "Off"
                ? "bg-[var(--surface-glass)] text-[var(--text-muted)]"
                : "bg-[var(--accent-rose-glow)] text-[var(--accent-rose)]"
          }`}
        >
          {state}
        </span>
      </div>
      {issue && <p className="mt-2 text-sm text-[var(--accent-rose)]">{issue}</p>}
      {extra && <p className="mt-1 text-sm text-[var(--text-secondary)]">{extra}</p>}
      <p className="mt-1 text-xs text-[var(--text-muted)]">Last successful test: {formatDateTime(lastTest)}</p>
      {lastFailure && <p className="mt-1 break-words text-xs text-[var(--accent-rose)]">Last failure: {lastFailure}</p>}
    </section>
  );
}

function StatusPill({ status }: { status: string }) {
  const label = DELIVERY_STATUS_LABELS[status as keyof typeof DELIVERY_STATUS_LABELS] ?? status;
  const good = status === "Sent" || status === "Delivered";
  const bad = status === "Failed" || status === "Bounced" || status === "Expired";
  return (
    <span
      className={`inline-flex rounded-full px-2 py-0.5 text-[11px] font-semibold ${
        good
          ? "bg-[var(--accent-emerald-glow)] text-[var(--accent-emerald)]"
          : bad
            ? "bg-[var(--accent-rose-glow)] text-[var(--accent-rose)]"
            : "bg-[var(--surface-glass)] text-[var(--text-secondary)]"
      }`}
    >
      {label}
    </span>
  );
}

function Cell({ children }: { children: React.ReactNode }) {
  return <td className="px-2 py-2.5">{children}</td>;
}

function Check({
  label,
  checked,
  disabled,
  onChange,
  text,
}: {
  label: string;
  checked: boolean;
  disabled?: boolean;
  onChange: (value: boolean) => void;
  text?: string;
}) {
  return (
    <label className={`inline-flex items-center gap-1.5 text-xs ${disabled ? "opacity-50" : "cursor-pointer"}`}>
      <input
        type="checkbox"
        checked={checked}
        disabled={disabled}
        aria-label={label}
        onChange={(event) => onChange(event.target.checked)}
        className="h-4 w-4 accent-[var(--accent)]"
      />
      <span className="text-[var(--text-muted)]">{text ?? (checked ? "On" : "Off")}</span>
    </label>
  );
}
