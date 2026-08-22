/**
 * The decisions the activation page makes before and after it talks to the backend, kept out of
 * the component so they can be asserted directly. None of this decides whether an activation is
 * allowed — the token is opaque here and only the backend knows if it is authentic, unspent and
 * unexpired. What lives here is the part the employee can fix themselves, plus the rules that
 * keep the secret out of places it should never reach.
 */

/** The query parameter the invitation email uses. Backend: `StaffInvitationService.ActivationPath`. */
const TOKEN_PARAM = "token";

/** Matches the backend floor. Frontend checks it early; the backend still checks it properly. */
export const MIN_PASSWORD_LENGTH = 8;

/**
 * bcrypt hashes only the first 72 *bytes*, and the backend refuses anything longer rather than
 * silently truncating. Bytes, not characters: "پاسورڈ" is six characters and twelve bytes, so a
 * character count here would accept passwords the backend then rejects.
 */
export const MAX_PASSWORD_BYTES = 72;

/** UTF-8 length, which is what bcrypt's 72-byte boundary is measured in. */
export function utf8ByteLength(value: string): number {
  return new TextEncoder().encode(value).length;
}

/**
 * The token exactly as it arrived, or null when the link carried none. It is an opaque
 * credential: no trimming, no case change, no format check — `URLSearchParams` has already done
 * the only decoding that is ours to do. A frontend that validated the shape would break the day
 * the backend issued a different one.
 */
export function readActivationToken(search: string): string | null {
  const value = new URLSearchParams(search).get(TOKEN_PARAM);
  return value === null || value === "" ? null : value;
}

/**
 * The same location with the token dropped, for `history.replaceState`. Once the page holds the
 * secret in memory there is no reason for it to stay in the address bar, where it survives in
 * history, screenshots and anything the employee copies to a colleague. Everything else about
 * the URL is left alone.
 */
export function stripTokenFromUrl(relativeUrl: string): string {
  let parsed: URL;
  try {
    parsed = new URL(relativeUrl, "http://activation.invalid");
  } catch {
    return relativeUrl;
  }

  if (!parsed.searchParams.has(TOKEN_PARAM)) return relativeUrl;
  parsed.searchParams.delete(TOKEN_PARAM);

  const query = parsed.searchParams.toString();
  return `${parsed.pathname}${query === "" ? "" : `?${query}`}${parsed.hash}`;
}

/** Field-level messages for the two things the employee can correct without a round trip. */
export interface ActivationValidation {
  password?: string;
  confirmPassword?: string;
}

export function validateActivation(password: string, confirmPassword: string): ActivationValidation {
  const found: ActivationValidation = {};

  if (password === "") {
    found.password = "Choose a password.";
  } else if (password.length < MIN_PASSWORD_LENGTH) {
    found.password = `Use at least ${MIN_PASSWORD_LENGTH} characters.`;
  } else if (utf8ByteLength(password) > MAX_PASSWORD_BYTES) {
    found.password =
      "That password is too long. Accented letters and emoji count as more than one character, so shorten it a little.";
  }

  if (confirmPassword === "") {
    found.confirmPassword = "Re-type the password to confirm it.";
  } else if (password !== confirmPassword) {
    found.confirmPassword = "The two passwords do not match.";
  }

  return found;
}

export function hasValidationErrors(validation: ActivationValidation): boolean {
  return validation.password !== undefined || validation.confirmPassword !== undefined;
}

/**
 * Everything the backend accepts and nothing else. The confirmation is a typing check that only
 * the browser can make, so it stops here; naming both fields explicitly is what keeps some later
 * piece of page state from following the password into the request body.
 */
export function buildActivationPayload(token: string, password: string): { token: string; password: string } {
  return { token, password };
}

/** The `{ message }` an Auth endpoint returns, when there is one worth showing. */
export function messageFromBody(body: unknown): string | null {
  if (typeof body !== "object" || body === null) return null;
  const message = (body as { message?: unknown }).message;
  return typeof message === "string" && message.trim() !== "" ? message.trim() : null;
}

/**
 * What to tell somebody whose activation did not go through.
 *
 * The backend deliberately answers "invalid or expired" identically for a guessed token, a spent
 * one, a revoked one and an employee who has since left, so that a stranger holding a link learns
 * nothing from the reply. Repeating its message verbatim preserves that; inventing a more helpful
 * one here would undo it. Server faults get a generic line instead, because a 500 body is not
 * written for strangers to read.
 */
export function describeActivationFailure(status: number, message?: string | null): string {
  if (status === 429) return "Too many activation attempts. Please try again in a few minutes.";

  const safe = typeof message === "string" ? message.trim() : "";
  if (safe !== "" && status >= 400 && status < 500) return safe;

  return "We could not activate the account right now. Please try again.";
}
