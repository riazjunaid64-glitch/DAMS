/**
 * The decisions the client email-verification page makes before and after it talks to the
 * backend, kept out of the component so they can be asserted directly.
 *
 * None of this decides whether a verification is allowed — the token is opaque here and only the
 * backend knows if it is authentic, unspent and unexpired. What lives here is the part the
 * customer can fix themselves, plus the rules that keep the secret out of places it should never
 * reach.
 *
 * The token mechanics are shared with staff activation rather than copied. Both links carry the
 * secret in the fragment for the same reason, and both scrub the address bar the same way; a
 * second implementation of that scrub is one more place for it to quietly stop working.
 */

import {
  MAX_PASSWORD_BYTES,
  MIN_PASSWORD_LENGTH,
  hasValidationErrors,
  messageFromBody,
  readActivationToken,
  stripTokenFromUrl,
  utf8ByteLength,
  validateActivation,
  type ActivationValidation,
} from "../staff/staffActivationState.ts";

export {
  MAX_PASSWORD_BYTES,
  MIN_PASSWORD_LENGTH,
  hasValidationErrors,
  messageFromBody,
  stripTokenFromUrl,
  utf8ByteLength,
  type ActivationValidation,
};

/**
 * The token exactly as it arrived, or null when the link carried none. Named for this flow so the
 * page does not read as if it were activating a staff account, but the reading is identical:
 * fragment first, because that is where the verification email puts it and a fragment never
 * reaches a server, then the query string so a link already sitting in an inbox keeps working.
 */
export function readVerificationToken(search: string, hash: string): string | null {
  return readActivationToken(search, hash);
}

/** The same two things the customer can correct without a round trip. */
export function validateVerification(password: string, confirmPassword: string): ActivationValidation {
  return validateActivation(password, confirmPassword);
}

/**
 * Everything the backend accepts and nothing else.
 *
 * Unlike staff activation, the confirmation travels to the server too. The endpoint compares the
 * two before spending the token, so a mistyped confirmation cannot burn a single-use credential
 * on a password the customer never meant to set — which would lock them out of an account they
 * had just proved they own. Naming all three fields explicitly is what keeps some later piece of
 * page state from following the password into the request body.
 */
export function buildVerificationPayload(
  token: string,
  password: string,
  confirmPassword: string
): { token: string; password: string; confirmPassword: string } {
  return { token, password, confirmPassword };
}

/**
 * What to tell somebody whose verification did not go through.
 *
 * The backend deliberately answers "invalid or has expired" identically for a guessed token, a
 * spent one, a superseded one and a revoked one, so that a stranger holding a link learns nothing
 * from the reply. Repeating its message verbatim preserves that; inventing a more helpful one
 * here would undo it. Server faults get a generic line instead, because a 500 body is not written
 * for strangers to read.
 */
export function describeVerificationFailure(status: number, message?: string | null): string {
  if (status === 429) return "Too many attempts. Please try again in a few minutes.";

  const safe = typeof message === "string" ? message.trim() : "";
  if (safe !== "" && status >= 400 && status < 500) return safe;

  return "We could not confirm your email address right now. Please try again.";
}
