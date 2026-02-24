const API_URL = import.meta.env.VITE_API_URL;

export const api = async (
  endpoint: string,
  options?: RequestInit
) => {
  const token = localStorage.getItem("token");

  const response = await fetch(`${API_URL}${endpoint}`, {
    ...options,
    headers: {
      "Content-Type": "application/json",
      ...(token && { Authorization: `Bearer ${token}` }),
      ...options?.headers,
    },
  });

  return response;
};
