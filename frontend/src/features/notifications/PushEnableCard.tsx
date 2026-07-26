import { useCallback, useEffect, useState } from "react";
import Button from "../../lib/Button.tsx";
import { disablePush, enablePush, permissionState, pushSupported, refreshSubscription } from "./push.ts";
import type { PushPermissionState } from "./push.ts";
import { fetchPushConfig, fetchPushDevices, sendTestPush } from "./notificationApi.ts";
import type { PushConfig, PushDevice } from "./types.ts";
import { formatDateTime } from "./types.ts";

const DISMISS_KEY = "dams.push.notNow";

/**
 * The browser-notification opt-in.
 *
 * The browser's own permission prompt is never raised on load or on sign-in — only when
 * somebody deliberately chooses "Enable notifications" here. That ordering matters: a prompt
 * nobody asked for is the quickest way to get permanently blocked, and a block cannot be
 * undone from inside the page.
 */
export default function PushEnableCard({ compact = false }: { compact?: boolean }) {
  const [config, setConfig] = useState<PushConfig | null>(null);
  const [devices, setDevices] = useState<PushDevice[]>([]);
  const [state, setState] = useState<PushPermissionState>("default");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [tone, setTone] = useState<"info" | "success" | "warning">("info");
  const [dismissed, setDismissed] = useState(() => {
    try {
      return window.localStorage.getItem(DISMISS_KEY) === "1";
    } catch {
      return false;
    }
  });

  const load = useCallback(async () => {
    setState(permissionState());
    try {
      const next = await fetchPushConfig();
      setConfig(next);

      // Already allowed at the browser level but the server has no live subscription for
      // this login — a new device, a fresh sign-in on a shared computer, or rotated keys.
      // Re-registering silently is correct here: permission was already given.
      if (permissionState() === "granted" && next.enabled) {
        const refreshed = await refreshSubscription(next);
        if (refreshed) setConfig(await fetchPushConfig());
      }

      setDevices(await fetchPushDevices());
    } catch {
      setConfig(null);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const onEnable = useCallback(async () => {
    if (!config) return;
    setBusy(true);
    setMessage(null);
    try {
      const result = await enablePush(config);
      setState(result.state);
      setTone(result.ok ? "success" : result.state === "denied" ? "warning" : "info");
      setMessage(result.message);
      if (result.ok) {
        setConfig(await fetchPushConfig());
        setDevices(await fetchPushDevices());
      }
    } finally {
      setBusy(false);
    }
  }, [config]);

  const onDisable = useCallback(async () => {
    setBusy(true);
    setMessage(null);
    try {
      await disablePush();
      setConfig(await fetchPushConfig());
      setDevices(await fetchPushDevices());
      setTone("info");
      setMessage("Browser notifications are off for this device. You will still see everything in DAMS and by email.");
    } finally {
      setBusy(false);
    }
  }, []);

  const onTest = useCallback(async () => {
    setBusy(true);
    setMessage(null);
    try {
      const result = await sendTestPush();
      setTone("success");
      setMessage(`Test notification sent to ${result.delivered} device(s).`);
    } catch (err) {
      setTone("warning");
      setMessage(err instanceof Error ? err.message : "The test notification could not be sent.");
    } finally {
      setBusy(false);
    }
  }, []);

  const onNotNow = useCallback(() => {
    try {
      window.localStorage.setItem(DISMISS_KEY, "1");
    } catch {
      // Storage may be unavailable in private mode; the card simply reappears next time.
    }
    setDismissed(true);
  }, []);

  // Nothing to offer: no browser support, or push is not configured for this installation.
  if (!pushSupported() || !config?.enabled) {
    if (compact) return null;
    return (
      <Panel title="Browser notifications">
        <p className="text-sm text-[var(--text-secondary)]">
          {!pushSupported()
            ? "This browser does not support notifications outside DAMS. You will still see everything in your inbox here, and by email."
            : "Browser notifications are not switched on for this DAMS installation yet. Your administrator can enable them in notification settings."}
        </p>
      </Panel>
    );
  }

  const subscribedHere = state === "granted" && config.hasActiveSubscription;

  if (compact && (subscribedHere || dismissed || state === "denied")) return null;

  return (
    <Panel title="Browser notifications">
      {state === "denied" ? (
        <>
          <p className="text-sm text-[var(--text-secondary)]">
            Notifications are blocked for this site in your browser settings. DAMS cannot ask again from here.
          </p>
          <ol className="mt-3 list-decimal space-y-1 pl-5 text-sm text-[var(--text-secondary)]">
            <li>Click the padlock or info icon to the left of the address bar.</li>
            <li>Open <span className="font-medium text-[var(--text-heading)]">Site settings</span> → <span className="font-medium text-[var(--text-heading)]">Notifications</span>.</li>
            <li>Change it to <span className="font-medium text-[var(--text-heading)]">Allow</span>, then reload DAMS.</li>
          </ol>
          <p className="mt-3 text-sm text-[var(--text-muted)]">
            Until then you will still receive everything in your DAMS inbox and by email.
          </p>
        </>
      ) : subscribedHere ? (
        <>
          <p className="text-sm text-[var(--text-secondary)]">
            This device will receive important payment, booking, lead, follow-up and site-visit updates even when DAMS is
            closed.
          </p>
          {devices.length > 0 && (
            <ul className="mt-3 space-y-1.5">
              {devices.map((device) => (
                <li key={device.id} className="flex flex-wrap items-center gap-x-2 gap-y-0.5 text-xs text-[var(--text-muted)]">
                  <span className="font-medium text-[var(--text-secondary)]">{device.deviceLabel ?? "Unnamed device"}</span>
                  <span aria-hidden="true">·</span>
                  <span>{device.isActive ? "Active" : "Inactive"}</span>
                  <span aria-hidden="true">·</span>
                  <span>last used {formatDateTime(device.lastSuccessAt ?? device.lastSeenAt)}</span>
                </li>
              ))}
            </ul>
          )}
          <div className="mt-4 flex flex-wrap gap-2">
            <Button variant="outline" size="sm" onClick={() => void onTest()} disabled={busy}>
              Send a test notification
            </Button>
            <Button variant="ghost" size="sm" onClick={() => void onDisable()} disabled={busy}>
              Turn off on this device
            </Button>
          </div>
        </>
      ) : (
        <>
          <p className="text-sm text-[var(--text-secondary)]">
            Enable DAMS notifications to receive important payment, booking, lead, follow-up, and site-visit updates even
            when DAMS is closed.
          </p>
          <p className="mt-2 text-xs text-[var(--text-muted)]">
            Notifications never show private details on your lock screen — just what happened and where to look.
          </p>
          <div className="mt-4 flex flex-wrap gap-2">
            <Button size="sm" onClick={() => void onEnable()} disabled={busy}>
              {busy ? "Enabling…" : "Enable notifications"}
            </Button>
            <Button variant="ghost" size="sm" onClick={onNotNow} disabled={busy}>
              Not now
            </Button>
          </div>
        </>
      )}

      {message && (
        <p
          role="status"
          className={`mt-3 rounded-lg border px-3 py-2 text-xs ${
            tone === "success"
              ? "border-[var(--accent-emerald)]/30 bg-[var(--accent-emerald-glow)] text-[var(--accent-emerald)]"
              : tone === "warning"
                ? "border-[var(--accent-rose)]/30 bg-[var(--accent-rose-glow)] text-[var(--accent-rose)]"
                : "border-[var(--border)] bg-[var(--surface-glass)] text-[var(--text-secondary)]"
          }`}
        >
          {message}
        </p>
      )}
    </Panel>
  );
}

function Panel({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="rounded-2xl border border-[var(--border)] bg-[var(--bg-card)] p-4 sm:p-5">
      <h2 className="text-base font-semibold text-[var(--text-heading)]">{title}</h2>
      <div className="mt-2">{children}</div>
    </section>
  );
}
