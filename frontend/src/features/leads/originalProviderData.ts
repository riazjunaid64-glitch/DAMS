/**
 * The original provider payload holds the enquirer's personal details and every answer
 * verbatim, so only the roles the API serves it to are offered it at all.
 */
export function canViewOriginalProviderData(role: string | null | undefined) {
  return role === "Admin" || role === "Manager";
}

// A bare number too long to survive JSON.parse exactly. Meta sends its ids as strings, but one
// sent as a number would otherwise be shown silently rounded — no longer the original.
const unsafeIntegerLiteral = /(?<![\w."])-?\d{16,}/;

/**
 * Stored payloads are compact JSON; indented is what a person can actually read. Anything that
 * is not valid JSON, or could not be re-indented without changing a value, is shown exactly as
 * stored rather than hidden, since it is still the original data.
 */
export function formatProviderPayload(json: string | null | undefined): string | null {
  if (json == null || json.trim() === "") return null;
  if (unsafeIntegerLiteral.test(json)) return json;
  try {
    return JSON.stringify(JSON.parse(json), null, 2);
  } catch {
    return json;
  }
}
