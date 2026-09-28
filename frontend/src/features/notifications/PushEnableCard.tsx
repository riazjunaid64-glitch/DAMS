import { useCallback, useEffect, useState } from "react";
import { Button, Notice, useToast } from "../../components/ui";
import { enablePush, permissionState, pushSupported, refreshSubscription } from "./push.ts";
import type { PushPermissionState } from "./push.ts";
import { fetchPushConfig } from "./notificationApi.ts";
import type { PushConfig } from "./types.ts";

const DISMISS_KEY = "dams.push.notNow";

/**
 * The browser-notification opt-in, shown only when this phone can receive them and has not
 * turned them on yet. The browser prompt is raised only from the button — never on load.
 */
export default function PushEnableCard() {
  const toast = useToast();
  const [config, setConfig] = useState<PushConfig | null>(null);
  const [state, setState] = useState<PushPermissionState>("default");
  const [busy, setBusy] = useState(false);
  const [dismissed, setDismissed] = useState(() => {
    try {
      return window.localStorage.getItem(DISMISS_KEY) === "1";
    } catch {
      return false;
    }
  });

  const load = useCallback(async () => {
    setState(permissionState());
    if (!pushSupported()) return;
    try {
      const next = await fetchPushConfig();
      setConfig(next);
      if (permissionState() === "granted" && next.enabled) {
        const refreshed = await refreshSubscription(next);
        if (refreshed) setConfig(await fetchPushConfig());
      }
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
    try {
      const result = await enablePush(config);
      setState(result.state);
      if (result.ok) {
        toast.success("Phone notifications are on.");
        setConfig(await fetchPushConfig());
      } else {
        toast.error(result.message);
      }
    } finally {
      setBusy(false);
    }
  }, [config, toast]);

  const onNotNow = useCallback(() => {
    try {
      window.localStorage.setItem(DISMISS_KEY, "1");
    } catch {
      // Storage may be unavailable; the card simply reappears next time.
    }
    setDismissed(true);
  }, []);

  const subscribed = state === "granted" && Boolean(config?.hasActiveSubscription);
  if (!pushSupported() || !config?.enabled || subscribed || dismissed || state === "denied") return null;

  return (
    <Notice
      tone="gold"
      title="Turn on phone notifications"
      message="Hear about leads and follow-ups when DAMS is closed."
      action={(
        <div className="flex gap-2">
          <Button size="sm" loading={busy} onClick={() => void onEnable()}>Turn on</Button>
          <Button variant="ghost" size="sm" disabled={busy} onClick={onNotNow}>Not now</Button>
        </div>
      )}
    />
  );
}
