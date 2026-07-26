import {
  fetchPushConfig,
  registerPushSubscription,
  unregisterPushSubscription,
} from "./notificationApi.ts";
import type { PushConfig } from "./types.ts";

/**
 * Where this browser currently stands. The interface is driven entirely by this, which is
 * what keeps DAMS from nagging somebody who has already said no at the browser level.
 */
export type PushPermissionState =
  | "unsupported"   // no service worker or no Push API — nothing to offer
  | "default"       // never asked; safe to offer the DAMS explanation
  | "granted"       // allowed at the browser level
  | "denied";       // blocked; only the browser's own settings can undo this

const SERVICE_WORKER_URL = "/dams-sw.js";

export function pushSupported(): boolean {
  return (
    typeof window !== "undefined" &&
    "serviceWorker" in navigator &&
    "PushManager" in window &&
    "Notification" in window &&
    // A service worker needs a secure context; localhost counts as one.
    (window.isSecureContext || window.location.hostname === "localhost")
  );
}

export function permissionState(): PushPermissionState {
  if (!pushSupported()) return "unsupported";
  const permission = Notification.permission;
  if (permission === "granted") return "granted";
  if (permission === "denied") return "denied";
  return "default";
}

export async function getConfig(): Promise<PushConfig | null> {
  try {
    return await fetchPushConfig();
  } catch {
    return null;
  }
}

async function register(): Promise<ServiceWorkerRegistration> {
  const existing = await navigator.serviceWorker.getRegistration(SERVICE_WORKER_URL);
  if (existing) return existing;
  return navigator.serviceWorker.register(SERVICE_WORKER_URL, { scope: "/" });
}

export interface EnableResult {
  ok: boolean;
  state: PushPermissionState;
  message: string;
}

/**
 * Runs the opt-in. This is only ever called from a deliberate click on "Enable
 * notifications" — the browser prompt is never raised on load or on sign-in, because a
 * prompt somebody did not ask for is the fastest way to get permanently blocked.
 */
export async function enablePush(config: PushConfig): Promise<EnableResult> {
  if (!pushSupported()) {
    return {
      ok: false,
      state: "unsupported",
      message: "This browser does not support notifications. You will still see everything in DAMS and by email.",
    };
  }

  if (!config.enabled || !config.publicKey) {
    return {
      ok: false,
      state: permissionState(),
      message: "Browser notifications are not switched on for this DAMS installation yet.",
    };
  }

  let permission: NotificationPermission;
  try {
    permission = await Notification.requestPermission();
  } catch {
    return { ok: false, state: permissionState(), message: "The browser did not respond to the permission request." };
  }

  if (permission === "denied") {
    return {
      ok: false,
      state: "denied",
      message:
        "Notifications are blocked for this site. You can allow them from the padlock icon in the address bar, " +
        "under Site settings → Notifications.",
    };
  }

  if (permission !== "granted") {
    return { ok: false, state: "default", message: "Notifications were not enabled." };
  }

  try {
    const registration = await register();
    await navigator.serviceWorker.ready;

    // A subscription left over from a previous key or a previous user on this machine must
    // not be reused: drop it and negotiate a fresh one for whoever is signed in now.
    const existing = await registration.pushManager.getSubscription();
    if (existing) await existing.unsubscribe();

    const subscription = await registration.pushManager.subscribe({
      userVisibleOnly: true,
      applicationServerKey: urlBase64ToUint8Array(config.publicKey),
    });

    const payload = subscription.toJSON() as { endpoint?: string; keys?: { p256dh?: string; auth?: string } };
    if (!payload.endpoint || !payload.keys?.p256dh || !payload.keys?.auth) {
      return { ok: false, state: "granted", message: "The browser returned an incomplete subscription." };
    }

    await registerPushSubscription({
      endpoint: payload.endpoint,
      p256dh: payload.keys.p256dh,
      auth: payload.keys.auth,
      deviceLabel: describeDevice(),
    });

    return { ok: true, state: "granted", message: "Browser notifications are on for this device." };
  } catch (error) {
    return {
      ok: false,
      state: permissionState(),
      message: error instanceof Error ? error.message : "This browser could not be subscribed.",
    };
  }
}

/** Turns push off for this browser only; other devices keep working. */
export async function disablePush(): Promise<void> {
  if (!pushSupported()) return;

  const registration = await navigator.serviceWorker.getRegistration(SERVICE_WORKER_URL);
  const subscription = await registration?.pushManager.getSubscription();
  if (!subscription) return;

  const endpoint = subscription.endpoint;
  await subscription.unsubscribe().catch(() => undefined);
  await unregisterPushSubscription(endpoint).catch(() => undefined);
}

/**
 * Called on sign-out. Both halves matter on a shared computer: the browser stops holding a
 * subscription, and the server stops considering this device to belong to that account.
 */
export async function detachPushOnLogout(): Promise<void> {
  if (!pushSupported()) return;

  try {
    const registration = await navigator.serviceWorker.getRegistration(SERVICE_WORKER_URL);
    const subscription = await registration?.pushManager.getSubscription();
    if (!subscription) return;

    // Detach only this browser. Removing every server-side device here would silently turn
    // push off on the user's phone and other signed-in computers.
    await unregisterPushSubscription(subscription.endpoint).catch(() => undefined);
    await subscription.unsubscribe();
  } catch {
    // The browser may already have discarded it; nothing more to do.
  }
}

/**
 * Re-registers a browser that already has permission — after a sign-in, or after the server
 * retired its subscriptions. Silent: it never raises a prompt.
 */
export async function refreshSubscription(config: PushConfig): Promise<boolean> {
  if (!pushSupported() || permissionState() !== "granted" || !config.enabled || !config.publicKey) return false;

  try {
    const registration = await register();
    await navigator.serviceWorker.ready;

    const existing = await registration.pushManager.getSubscription();
    const subscription =
      existing ??
      (await registration.pushManager.subscribe({
        userVisibleOnly: true,
        applicationServerKey: urlBase64ToUint8Array(config.publicKey),
      }));

    const payload = subscription.toJSON() as { endpoint?: string; keys?: { p256dh?: string; auth?: string } };
    if (!payload.endpoint || !payload.keys?.p256dh || !payload.keys?.auth) return false;

    // Re-sending the same endpoint is what moves a shared browser to the person who is
    // signed in now, instead of leaving it attached to the previous user.
    await registerPushSubscription({
      endpoint: payload.endpoint,
      p256dh: payload.keys.p256dh,
      auth: payload.keys.auth,
      deviceLabel: describeDevice(),
    });

    return true;
  } catch {
    return false;
  }
}

/** A short, non-identifying label so somebody can tell their devices apart. */
function describeDevice(): string {
  const agent = navigator.userAgent;
  const browser = /Edg\//.test(agent)
    ? "Edge"
    : /OPR\//.test(agent)
      ? "Opera"
      : /Chrome\//.test(agent)
        ? "Chrome"
        : /Firefox\//.test(agent)
          ? "Firefox"
          : /Safari\//.test(agent)
            ? "Safari"
            : "Browser";

  const platform = /Windows/.test(agent)
    ? "Windows"
    : /Android/.test(agent)
      ? "Android"
      : /iPhone|iPad/.test(agent)
        ? "iOS"
        : /Mac OS X/.test(agent)
          ? "macOS"
          : /Linux/.test(agent)
            ? "Linux"
            : "this device";

  return `${browser} on ${platform}`;
}

/**
 * The VAPID key arrives as base64url; PushManager wants raw bytes. The buffer is allocated
 * explicitly so the result is typed as a plain ArrayBuffer view, which is what the
 * applicationServerKey signature requires.
 */
function urlBase64ToUint8Array(base64Url: string): Uint8Array<ArrayBuffer> {
  const padding = "=".repeat((4 - (base64Url.length % 4)) % 4);
  const base64 = (base64Url + padding).replace(/-/g, "+").replace(/_/g, "/");
  const raw = window.atob(base64);
  const output = new Uint8Array(new ArrayBuffer(raw.length));
  for (let i = 0; i < raw.length; i += 1) output[i] = raw.charCodeAt(i);
  return output;
}
