const API_URL = import.meta.env.VITE_API_URL;

let refreshInFlight: Promise<boolean> | null = null;

async function refreshAccessToken(): Promise<boolean> {
  const storedRefresh = localStorage.getItem("refreshToken");
  if (!storedRefresh) return false;

  if (refreshInFlight === null) {
    refreshInFlight = (async (): Promise<boolean> => {
      try {
        const res = await fetch(`${API_URL}/api/Auth/refresh`, {
          method: "POST",
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

  const response = await fetch(`${API_URL}${endpoint}`, {
    ...options,
    headers,
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
