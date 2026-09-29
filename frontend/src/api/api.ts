/** Base URL for the DAMS API (no trailing slash). In dev, defaults to same-origin so Vite can proxy `/api`. */
function apiBaseUrl(): string {
  const fromEnv = import.meta.env.VITE_API_URL as string | undefined;
  if (fromEnv != null && String(fromEnv).trim() !== "") {
    return String(fromEnv).replace(/\/$/, "");
  }
  if (import.meta.env.DEV) {
    return "";
  }
  console.warn(
    "VITE_API_URL is not set. Set it in frontend/.env (e.g. VITE_API_URL=http://localhost:5219) for production builds."
  );
  return "";
}

// Access token lives in memory only — never in localStorage/sessionStorage.
// The refresh token lives in an httpOnly cookie set by the backend.
let _accessToken: string | null = null;

/** Fired once when a signed-in request is rejected and the refresh cookie cannot replace it. */
export const ACCESS_CHANGED_EVENT = "dams-access-changed";

let accessChangeNotified = false;

export function setAccessToken(token: string | null): void {
  _accessToken = token;
  if (token) accessChangeNotified = false;
}

function notifyAccessChanged(): void {
  if (accessChangeNotified || typeof window === "undefined") return;
  accessChangeNotified = true;
  window.dispatchEvent(new Event(ACCESS_CHANGED_EVENT));
}

let refreshInFlight: Promise<boolean> | null = null;

export async function refreshAccessToken(): Promise<boolean> {
  if (refreshInFlight === null) {
    refreshInFlight = (async (): Promise<boolean> => {
      try {
        const res = await fetch(`${apiBaseUrl()}/api/Auth/refresh`, {
          method: "POST",
          credentials: "include",
          cache: "no-store",
          // Harmless outside ngrok; when tunneled through ngrok's free tier this stops it
          // intercepting the request with an HTML click-through page in place of JSON.
          headers: { "Content-Type": "application/json", "ngrok-skip-browser-warning": "true" },
        });
        if (!res.ok) {
          _accessToken = null;
          return false;
        }
        const data = (await res.json()) as { accessToken: string };
        _accessToken = data.accessToken;
        accessChangeNotified = false;
        return true;
      } catch {
        _accessToken = null;
        return false;
      } finally {
        refreshInFlight = null;
      }
    })();
  }

  return refreshInFlight;
}

export const api = async (
  endpoint: string,
  options?: RequestInit,
  includeAuth: boolean = true,
  isRetry: boolean = false
): Promise<Response> => {
  const headers = new Headers(options?.headers as HeadersInit | undefined);

  if (includeAuth && _accessToken) {
    headers.set("Authorization", `Bearer ${_accessToken}`);
  }

  const isFormData = options?.body instanceof FormData;
  if (!isFormData && !headers.has("Content-Type")) {
    headers.set("Content-Type", "application/json");
  }

  if (!headers.has("Cache-Control")) {
    headers.set("Cache-Control", "no-cache");
  }
  if (!headers.has("Pragma")) {
    headers.set("Pragma", "no-cache");
  }
  // Harmless outside ngrok; when tunneled through ngrok's free tier this stops it
  // intercepting the request with an HTML click-through page in place of JSON.
  if (!headers.has("ngrok-skip-browser-warning")) {
    headers.set("ngrok-skip-browser-warning", "true");
  }

  const response = await fetch(`${apiBaseUrl()}${endpoint}`, {
    ...options,
    headers,
    credentials: "include",
    cache: options?.cache ?? "no-store",
  });

  if (
    response.status === 401 &&
    includeAuth &&
    !isRetry &&
    endpoint !== "/api/Auth/refresh"
  ) {
    const hadSession = _accessToken != null;
    const refreshed = await refreshAccessToken();
    if (refreshed) {
      return api(endpoint, options, includeAuth, true);
    }
    _accessToken = null;
    if (hadSession) notifyAccessChanged();
  }

  return response;
};

/** Resolves stored paths like `/uploads/...` for `<img src>` / `<video src>`. In dev, `/uploads` is proxied to the API; with `VITE_API_URL` set, paths are prefixed so static files load from the API host. */
export function resolveMediaUrl(path: string | null | undefined): string {
  if (path == null || path === "") return "";
  const p = path.trim();
  if (/^https?:\/\//i.test(p)) return p;
  const slug = p.startsWith("/") ? p : `/${p}`;
  const base = apiBaseUrl();
  return base ? `${base}${slug}` : slug;
}

/**
 * POSTs a form (a file upload) and reports how much has been sent, which fetch cannot. Signs in
 * the same way `api` does, and on a rejected token refreshes it and sends once more. Resolves to a
 * Response so callers read it like any other API call; rejects with an AbortError when `signal` fires.
 */
export function apiUpload(
  endpoint: string,
  body: FormData,
  onProgress?: (percent: number) => void,
  signal?: AbortSignal,
  isRetry: boolean = false
): Promise<Response> {
  const send = () =>
    new Promise<Response>((resolve, reject) => {
      const request = new XMLHttpRequest();
      request.open("POST", `${apiBaseUrl()}${endpoint}`);
      request.withCredentials = true;
      request.responseType = "blob";
      if (_accessToken) request.setRequestHeader("Authorization", `Bearer ${_accessToken}`);
      request.setRequestHeader("ngrok-skip-browser-warning", "true");
      request.upload.onprogress = (event) => {
        if (event.lengthComputable) onProgress?.(Math.round((event.loaded / event.total) * 100));
      };
      request.onload = () =>
        resolve(new Response(request.response as Blob, { status: request.status, headers: { "Content-Type": request.getResponseHeader("Content-Type") ?? "application/json" } }));
      request.onerror = () => reject(new TypeError("The upload could not reach the server."));
      request.onabort = () => reject(new DOMException("The upload was stopped.", "AbortError"));
      signal?.addEventListener("abort", () => request.abort(), { once: true });
      if (signal?.aborted) {
        reject(new DOMException("The upload was stopped.", "AbortError"));
        return;
      }
      request.send(body);
    });

  return send().then(async (response) => {
    if (response.status === 401 && !isRetry) {
      const hadSession = _accessToken != null;
      if (await refreshAccessToken()) return apiUpload(endpoint, body, onProgress, signal, true);
      _accessToken = null;
      if (hadSession) notifyAccessChanged();
    }
    return response;
  });
}
