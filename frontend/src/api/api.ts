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

export function setAccessToken(token: string | null): void {
  _accessToken = token;
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
          headers: { "Content-Type": "application/json" },
        });
        if (!res.ok) {
          _accessToken = null;
          return false;
        }
        const data = (await res.json()) as { accessToken: string };
        _accessToken = data.accessToken;
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
    const refreshed = await refreshAccessToken();
    if (refreshed) {
      return api(endpoint, options, includeAuth, true);
    }
    _accessToken = null;
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
