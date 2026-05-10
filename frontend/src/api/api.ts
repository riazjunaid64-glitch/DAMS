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

let refreshInFlight: Promise<boolean> | null = null;

async function refreshAccessToken(): Promise<boolean> {
  const storedRefresh = localStorage.getItem("refreshToken");
  if (!storedRefresh) return false;

  if (refreshInFlight === null) {
    refreshInFlight = (async (): Promise<boolean> => {
      try {
        const res = await fetch(`${apiBaseUrl()}/api/Auth/refresh`, {
          method: "POST",
          cache: "no-store",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ refreshToken: storedRefresh }),
        });
        if (!res.ok) return false;
        const data = (await res.json()) as {
          accessToken: string;
          refreshToken: string;
        };
        localStorage.setItem("token", data.accessToken);
        localStorage.setItem("refreshToken", data.refreshToken);
        return true;
      } catch {
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
  const token = localStorage.getItem("token");
  const headers = new Headers(options?.headers as HeadersInit | undefined);

  if (includeAuth && token) {
    headers.set("Authorization", `Bearer ${token}`);
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
    // Avoid cached 304s: fetch treats 304 as !ok and the body is empty, which breaks res.json().
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
    localStorage.removeItem("token");
    localStorage.removeItem("refreshToken");
  }

  return response;
};
