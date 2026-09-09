import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { api } from "../api/api.ts";
import Button from "../lib/Button.tsx";
import Field from "../lib/Field.tsx";
import {
  buildVerificationPayload,
  describeVerificationFailure,
  hasValidationErrors,
  messageFromBody,
  readVerificationToken,
  stripTokenFromUrl,
  validateVerification,
  type ActivationValidation,
} from "../features/account/emailVerificationState.ts";

type Props = {
  /** Opens the normal DAMS sign-in modal. Verifying deliberately does not sign anybody in. */
  onSignIn?: () => void;
};

/**
 * Where a customer turns the link from their confirmation email into a usable account.
 *
 * Public by necessity: the person opening it has no account to authenticate with yet, so the link
 * itself is the credential. It is held in memory for exactly as long as it takes to spend it —
 * never stored, never rendered, and wiped out of the address bar on arrival.
 *
 * This page is also where the password is set, and that ordering is the whole security model.
 * Registration takes no password at all, so somebody who signs up with an address they do not own
 * has nothing to sign in with and never will; only whoever can read the mail sent to that address
 * gets to choose the credential.
 */
export default function VerifyEmailPage({ onSignIn }: Props) {
  // Verification emails put the token in the fragment, which never reaches a server; the query
  // string is still accepted so links already sitting in an inbox keep working.
  const [token, setToken] = useState<string | null>(
    () => readVerificationToken(window.location.search, window.location.hash)
  );
  const [password, setPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [invalid, setInvalid] = useState<ActivationValidation>({});
  const [failure, setFailure] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [verified, setVerified] = useState(false);

  useEffect(() => {
    // The token is already in component state by now, so the copy in the address bar is pure
    // exposure: browser history, a shared screenshot, a URL pasted to somebody. Replacing the
    // entry rather than navigating keeps the page mounted and the token in hand. This is the
    // second line of defence, not the first — a fragment was never sent to the server, so there
    // is nothing here that has to win a race against page load.
    const current = `${window.location.pathname}${window.location.search}${window.location.hash}`;
    const scrubbed = stripTokenFromUrl(current);
    if (scrubbed !== current) {
      window.history.replaceState(window.history.state, "", scrubbed);
    }
  }, []);

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    // A second click, or an Enter key while the first request is still open, would spend a
    // single-use link twice and turn a success into "invalid or has expired".
    if (submitting || verified || token === null) return;

    const found = validateVerification(password, confirmPassword);
    setInvalid(found);
    if (hasValidationErrors(found)) return;

    setSubmitting(true);
    setFailure(null);
    try {
      const response = await api(
        "/api/Auth/verify-email",
        {
          method: "POST",
          body: JSON.stringify(buildVerificationPayload(token, password, confirmPassword)),
        },
        false // No access token exists yet, and a 401 here must not trigger the refresh path.
      );

      if (!response.ok) {
        const body = await response.json().catch(() => null);
        setFailure(describeVerificationFailure(response.status, messageFromBody(body)));
        return;
      }

      // Both secrets have done their job. Dropping them also makes the form unreachable, which is
      // what stops a re-submit against a credential that has already been consumed.
      setPassword("");
      setConfirmPassword("");
      setToken(null);
      setVerified(true);
    } catch {
      setFailure(describeVerificationFailure(0, null));
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="mx-auto w-full max-w-md px-4 py-12 sm:px-6 sm:py-16">
      <section className="overflow-hidden rounded-2xl border border-[var(--border)] bg-[var(--bg-card)]">
        <header className="border-b border-[var(--border)] px-6 py-5">
          <div className="mb-1.5 inline-flex items-center gap-2 rounded-full bg-indigo-500/[0.08] px-2.5 py-0.5">
            <span className="h-1.5 w-1.5 rounded-full bg-indigo-500" />
            <span className="text-[10px] font-semibold uppercase tracking-wider text-indigo-500">
              Confirm your account
            </span>
          </div>
          <h1 className="text-lg font-semibold text-[var(--text-heading)]">
            {verified ? "Your account is ready" : "Choose your password"}
          </h1>
          <p className="mt-1.5 text-sm text-[var(--text-muted)]">
            {verified
              ? "Sign in using your email address and the password you just chose."
              : token === null
                ? "This confirmation link cannot be used."
                : "Confirming your email address is the last step. Choose the password you will sign in with — nobody else at DAMS sets it or can see it."}
          </p>
        </header>

        <div className="space-y-4 px-6 py-6">
          {verified ? (
            <>
              <p className="text-sm text-[var(--text-secondary)]">
                Your email address is confirmed and your account is active. Confirming and signing
                in are separate steps, so you are not signed in yet.
              </p>
              <div className="flex flex-wrap items-center gap-3 pt-1">
                {onSignIn && <Button onClick={onSignIn}>Sign in</Button>}
                <Link
                  to="/"
                  className="text-sm font-semibold text-[var(--text-muted)] transition hover:text-[var(--text-primary)]"
                >
                  Go to DAMS home
                </Link>
              </div>
            </>
          ) : token === null ? (
            <>
              {/* No request is made: there is nothing to send, and asking for an email address or
                  a typed-in token here would only invite guessing at somebody else's link. */}
              <p className="text-sm text-[var(--text-secondary)]">
                This confirmation link is missing or invalid. Request a new one from the sign-in
                page and open the newest email we send you.
              </p>
              <p className="text-xs text-[var(--text-muted)]">
                If you reloaded this page after opening it, open the link in your confirmation
                email again. Each link works once.
              </p>
              <div className="pt-1">
                <Link
                  to="/"
                  className="text-sm font-semibold text-[var(--text-muted)] transition hover:text-[var(--text-primary)]"
                >
                  Go to DAMS home
                </Link>
              </div>
            </>
          ) : (
            <form onSubmit={(event) => void submit(event)} className="space-y-4" noValidate>
              <Field
                label="Password"
                type="password"
                autoComplete="new-password"
                autoFocus
                hint="At least 8 characters"
                value={password}
                error={invalid.password}
                onChange={(event) => setPassword(event.target.value)}
                placeholder="••••••••"
              />
              <Field
                label="Confirm password"
                type="password"
                autoComplete="new-password"
                value={confirmPassword}
                error={invalid.confirmPassword}
                onChange={(event) => setConfirmPassword(event.target.value)}
                placeholder="••••••••"
              />

              {failure && (
                <div
                  role="alert"
                  className="flex items-start gap-2 rounded-xl border border-rose-500/20 bg-rose-500/[0.06] px-4 py-3 text-sm text-rose-300"
                >
                  <svg
                    width="16"
                    height="16"
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="2"
                    strokeLinecap="round"
                    className="mt-0.5 shrink-0"
                    aria-hidden="true"
                  >
                    <circle cx="12" cy="12" r="10" />
                    <line x1="12" y1="8" x2="12" y2="12" />
                    <line x1="12" y1="16" x2="12.01" y2="16" />
                  </svg>
                  <span>{failure}</span>
                </div>
              )}

              <Button type="submit" disabled={submitting} className="w-full">
                {submitting ? "Confirming…" : "Confirm and set password"}
              </Button>
            </form>
          )}
        </div>
      </section>
    </div>
  );
}
