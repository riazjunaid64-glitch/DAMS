/** The message a failed API call carries: `{ message }`, the first validation error, a title, or short plain text. */
export async function readError(response: Response, fallback: string): Promise<string> {
  const text = await response.text();
  if (!text) return fallback;
  try {
    const body = JSON.parse(text) as { message?: string; title?: string; errors?: Record<string, string[]> };
    if (body.message) return body.message;
    const first = body.errors && Object.values(body.errors).flat()[0];
    if (first) return first;
    if (body.title) return body.title;
  } catch {
    // The server sent plain text.
  }
  return text.length < 300 ? text : fallback;
}
