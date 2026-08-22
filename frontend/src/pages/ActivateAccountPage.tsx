import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { api } from "../api/api.ts";
import Button from "../lib/Button.tsx";
import Field from "../lib/Field.tsx";
import {
  buildActivationPayload,
  describeActivationFailure,
  hasValidationErrors,
  messageFromBody,
  readActivationToken,
  stripTokenFromUrl,
  validateActivation,
  type ActivationValidation,
} from "../features/staff/staffActivationState.ts";

type Props = {
  /** Opens the normal DAMS sign-in modal. Activation deliberately does not sign anybody in. */
  onSignIn?: () => void;
};

/**
 * Where an invited employee turns their emailed link into a password of their own.
 *
 * Public by necessity: the person opening it has no account to authenticate with yet, so the
 * link itself is the credential. It is held in memory for exactly as long as it takes to spend
 * it — never stored, never rendered, and wiped out of the address bar on arrival. Activating is
 * not signing in; when it succeeds the employee goes to the ordinary sign-in control like
 * everyone else.
 */
export default function ActivateAccountPage({ onSignIn }: Props) {
  // Invitation emails put the token in the fragment, which never reaches a server; the query
  // string is still accepted so links already sitting in an inbox keep working.
  const [token, setToken] = useState<string | null>(
    () => readActivationToken(window.location.search, window.location.hash)
  );
  const [password, setPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [invalid, setInvalid] = useState<ActivationValidation>({});
  const [failure, setFailure] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [activated, setActivated] = useState(false);

  useEffect(() => {
    // The token is already in component state by now, so the copy in the address bar is pure
    // exposure: browser history, a shared screenshot, a URL pasted to a colleague. Replacing the
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
    // single-use link twice and turn a success into "invalid or expired".
    if (submitting || activated || token === null) return;

    const found = validateActivation(password, confirmPassword);
    setInvalid(found);
    if (hasValidationErrors(found)) return;

    setSubmitting(true);
    setFailure(null);
    try {
      const response = await api(
        "/api/Auth/activate-staff",
        { method: "POST", body: JSON.stringify(buildActivationPayload(token, password)) },
        false // No access token exists yet, and a 401 here must not trigger the refresh path.
      );

      if (!response.ok) {
        const body = await response.json().catch(() => null);
        setFailure(describeActivationFailure(response.status, messageFromBody(body)));
        return;
      }

      // Both secrets have done their job. Dropping them also makes the form unreachable, which
      // is what stops a re-submit against an invitation that has already been consumed.
      setPassword("");
      setConfirmPassword("");
      setToken(null);
      setActivated(true);
    } catch {
      setFailure(describeActivationFailure(0, null));
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
              DAMS staff access
            </span>
          </div>
          <h1 className="text-lg font-semibold text-[var(--text-heading)]">
            {activated ? "Your DAMS account is ready" : "Set your password"}
          </h1>
          <p className="mt-1.5 text-sm text-[var(--text-muted)]">
            {activated
              ? "Sign in using your email address and the password you just chose."
              : token === null
                ? "This activation link cannot be used."
                : "Choose the password you will use to sign in. Nobody else at DAMS sets it or can see it."}
          </p>
        </header>

        <div className="space-y-4 px-6 py-6">
          {activated ? (
            <>
              <p className="text-sm text-[var(--text-secondary)]">
                Your login is now active. Activation and signing in are separate steps, so you are not
                signed in yet.
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
                  a typed-in token would only invite guessing at somebody else's invitation. */}
              <p className="text-sm text-[var(--text-secondary)]">
                This activation link is missing or invalid. Ask your administrator to send a new
                invitation.
              </p>
              <p className="text-xs text-[var(--text-muted)]">
                If you reloaded this page after opening it, open the link in your invitation email
                again.
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
                label="New password"
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
                {submitting ? "Setting your password…" : "Activate account"}
              </Button>
            </form>
          )}
        </div>
      </section>
    </div>
  );
}
