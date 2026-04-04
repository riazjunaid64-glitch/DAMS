const API_URL = import.meta.env.VITE_API_URL;

export const api = async (
  endpoint: string,
  options?: RequestInit,
  includeAuth: boolean = true
) => {
  const token = localStorage.getItem("token");
  const hasBody = options?.body !== undefined && options?.body !== null;
  const isFormData = typeof FormData !== "undefined" && options?.body instanceof FormData;

  const headers: HeadersInit = {
    ...(includeAuth && token && { Authorization: `Bearer ${token}` }),
    ...options?.headers,
  };

  // Avoid forcing non-simple GET requests; this removes unnecessary CORS preflight.
  if (hasBody && !isFormData && !(headers as Record<string, string>)["Content-Type"]) {
    (headers as Record<string, string>)["Content-Type"] = "application/json";
  }

  const response = await fetch(`${API_URL}${endpoint}`, {
    ...options,
    headers,
  });

  return response;
};
