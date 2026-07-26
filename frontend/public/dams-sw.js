/*
 * DAMS service worker — browser push only.
 *
 * It deliberately does nothing else: no caching, no fetch interception, no offline shell.
 * The only jobs are to show a notification that arrived while DAMS was closed, and to take
 * the person to the right place when they click it.
 *
 * The payload is written by the server and is already stripped of anything that should not
 * be readable on a lock screen or over somebody's shoulder.
 */

self.addEventListener("install", (event) => {
  // Take over straight away so a newly enabled subscription works without a reload.
  event.waitUntil(self.skipWaiting());
});

self.addEventListener("activate", (event) => {
  event.waitUntil(self.clients.claim());
});

self.addEventListener("push", (event) => {
  let payload = {};
  try {
    payload = event.data ? event.data.json() : {};
  } catch {
    payload = {};
  }

  const title = typeof payload.title === "string" && payload.title.trim() !== "" ? payload.title : "DAMS";
  const body =
    typeof payload.body === "string" && payload.body.trim() !== ""
      ? payload.body
      : "Open DAMS to see the details.";

  // Only same-origin paths are followed on click; anything else falls back to the inbox so a
  // tampered payload cannot send somebody to another site.
  const url = typeof payload.url === "string" && payload.url.startsWith("/") && !payload.url.startsWith("//")
    ? payload.url
    : "/notifications";

  const options = {
    body,
    // Same tag collapses repeats of one event instead of stacking a banner per retry.
    tag: typeof payload.tag === "string" ? payload.tag : "dams",
    renotify: false,
    data: { url },
    timestamp: Date.now(),
  };

  if (typeof payload.icon === "string" && payload.icon !== "") options.icon = payload.icon;
  if (typeof payload.badge === "string" && payload.badge !== "") options.badge = payload.badge;

  event.waitUntil(self.registration.showNotification(title, options));
});

self.addEventListener("notificationclick", (event) => {
  event.notification.close();

  const target = (event.notification.data && event.notification.data.url) || "/notifications";

  event.waitUntil(
    (async () => {
      const clientList = await self.clients.matchAll({ type: "window", includeUncontrolled: true });

      // Prefer an existing DAMS window: focus it and ask it to navigate, rather than piling
      // up tabs. If the app decides the person is signed out, it handles the sign-in and
      // returns them here afterwards.
      for (const client of clientList) {
        if (new URL(client.url).origin === self.location.origin) {
          await client.focus();
          client.postMessage({ type: "dams-notification-click", url: target });
          return;
        }
      }

      await self.clients.openWindow(target);
    })()
  );
});

// The browser can retire a subscription on its own (key rotation, storage clean-up). When it
// does, the page re-registers on its next visit; nothing here can reach the API without an
// access token, so the row is simply left for the server to deactivate on its next failure.
self.addEventListener("pushsubscriptionchange", () => {
  // Intentionally empty — re-subscription happens in the page, where a token is available.
});
