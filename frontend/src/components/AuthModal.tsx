import { useState } from "react";
import { api, setAccessToken } from "../api/api.ts";
import Button from "../lib/Button.tsx";
import Field from "../lib/Field.tsx";

type Props = {
  mode: "login" | "signup";
  onClose: () => void;
  onSuccess?: () => void;
};

export default function AuthModal({ mode, onClose, onSuccess }: Props) {
  const [fullName, setFullName] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // What the server said after a signup or a resend. Always the same neutral sentence, whether or
  // not the address is already registered — showing it verbatim is what keeps this form from
  // becoming a way to test which addresses have DAMS accounts.
  const [notice, setNotice] = useState<string | null>(null);
  // Shown only after a failed sign-in, because that is the moment somebody who never confirmed
  // their address discovers something is wrong. Offering it unprompted would hint that unconfirmed
  // accounts are a thing that happens to particular addresses.
  const [offerResend, setOfferResend] = useState(false);

  /**
   * Asks for a fresh confirmation email. The server answers identically whether or not the
   * address is registered and whether or not it is awaiting confirmation, so this can be offered
   * to anybody without telling them anything.
   */
  const resendVerification = async () => {
    if (loading || email.trim() === "") return;

    setLoading(true);
    setError(null);
    try {
      const res = await api(
        "/api/Auth/resend-verification",
        { method: "POST", body: JSON.stringify({ email }) },
        false
      );
      const body = await res.json().catch(() => null);
      const message =
        typeof body?.message === "string" && body.message.trim() !== "" ? body.message.trim() : null;

      if (!res.ok) {
        setError(
          res.status === 429
            ? "Too many attempts. Please try again in a few minutes."
            : message ?? "We could not send that right now. Please try again."
        );
        return;
      }

      setOfferResend(false);
      setNotice(message ?? "If the address can be registered, we have sent confirmation instructions.");
    } catch {
      setError("Something went wrong. Please try again.");
    } finally {
      setLoading(false);
    }
  };

  const handleSubmit = async (e?: React.FormEvent) => {
    e?.preventDefault();
    setLoading(true);
    setError(null);

    try {
      if (mode === "login") {
        const res = await api(
          "/api/Auth/login",
          {
            method: "POST",
            body: JSON.stringify({ email, password }),
          },
          false
        );

        if (!res.ok) {
          // One message for every reason sign-in failed — wrong password, unknown address, an
          // account still awaiting confirmation, a disabled one. The server already answers them
          // identically, and naming the reason here would put that back.
          setError("Invalid email or password. Please try again.");
          setOfferResend(true);
          return;
        }

        const data = await res.json();
        setAccessToken(data.accessToken);
        onSuccess?.();
        onClose();
      }

      if (mode === "signup") {
        // No password, and no roleId. Neither was ever the caller's to choose: registration only
        // reserves the address, and the password is set on the confirmation page by whoever can
        // actually read the mail sent to it. Sending one here is what used to let a stranger sign
        // up with somebody else's address and keep the credential.
        const res = await api(
          "/api/Auth/register",
          {
            method: "POST",
            body: JSON.stringify({ fullName, email }),
          },
          false
        );

        const body = await res.json().catch(() => null);
        const message =
          typeof body?.message === "string" && body.message.trim() !== "" ? body.message.trim() : null;

        if (!res.ok) {
          setError(
            res.status === 429
              ? "Too many attempts. Please try again in a few minutes."
              : message ?? "Signup failed. Please try again."
          );
          return;
        }

        // The modal stays open on the neutral message rather than closing on an alert. There is
        // nothing to sign in with yet, so sending them to the login form would be misleading.
        setError(null);
        setNotice(message ?? "Check your inbox to confirm your address and choose your password.");
      }
    } catch {
      setError("Something went wrong. Please try again.");
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
      {/* Backdrop */}
      <div
        className="absolute inset-0 bg-black/60 backdrop-blur-sm animate-fade-in"
        onClick={onClose}
      />

      {/* Modal */}
      <form
        onSubmit={handleSubmit}
        className="relative z-10 w-[440px] max-w-[92vw] animate-scale-in overflow-hidden rounded-2xl border border-[var(--border)] bg-[var(--modal-bg)] shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        {/* Decorative gradient */}
        <div className="absolute right-0 top-0 h-32 w-32 rounded-full bg-indigo-500/[0.08] blur-[60px]" />
        <div className="absolute bottom-0 left-0 h-24 w-24 rounded-full bg-violet-500/[0.06] blur-[40px]" />

        {/* Header */}
        <div className="relative border-b border-[var(--border)] px-6 py-5">
          <div className="flex items-center justify-between">
            <div>
              <div className="mb-1 inline-flex items-center gap-2 rounded-full bg-indigo-500/[0.08] px-2.5 py-0.5">
                <span className="h-1.5 w-1.5 rounded-full bg-indigo-500" />
                <span className="text-[10px] font-semibold uppercase tracking-wider text-indigo-500">
                  {mode === "login" ? "Welcome back" : "Get started"}
                </span>
              </div>
              <h3 className="text-lg font-semibold text-[var(--text-heading)]">
                {mode === "login" ? "Sign in to your account" : "Create your account"}
              </h3>
            </div>
            <button
              type="button"
              onClick={onClose}
              className="flex h-8 w-8 items-center justify-center rounded-lg text-[var(--text-muted)] transition hover:bg-[var(--surface-glass-hover)] hover:text-[var(--text-primary)]"
            >
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                <line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>
              </svg>
            </button>
          </div>
          <p className="mt-1.5 text-sm text-[var(--text-muted)]">
            {mode === "login"
              ? "Enter your credentials to continue."
              : "Fill in the details below to set up your account."}
          </p>
        </div>

        {/* Body */}
        <div className="relative space-y-4 px-6 py-5">
          {mode === "signup" && (
            <Field
              label="Full Name"
              value={fullName}
              onChange={(e) => setFullName(e.target.value)}
              placeholder="John Doe"
            />
          )}

          <Field
            label="Email Address"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            placeholder="you@example.com"
            type="email"
          />

          {/* Signup asks for no password: there is nothing yet for one to protect, and the
              account it would protect is not proven to belong to whoever is typing. */}
          {mode === "login" && (
            <Field
              label="Password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              placeholder="••••••••"
              type="password"
            />
          )}

          {notice && (
            <div
              role="status"
              className="rounded-xl border border-emerald-500/20 bg-emerald-500/[0.06] px-4 py-3 text-sm text-emerald-300 animate-scale-in"
            >
              {notice}
            </div>
          )}

          {error && (
            <div className="rounded-xl border border-rose-500/20 bg-rose-500/[0.06] px-4 py-3 text-sm text-rose-300 flex items-center gap-2 animate-scale-in">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                <circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/>
              </svg>
              {error}
            </div>
          )}

          {mode === "login" && offerResend && notice === null && (
            <p className="text-xs text-[var(--text-muted)]">
              New here and never confirmed your email address?{" "}
              <button
                type="button"
                onClick={() => void resendVerification()}
                disabled={loading || email.trim() === ""}
                className="font-semibold text-indigo-400 underline-offset-2 transition hover:underline disabled:opacity-50"
              >
                Send the confirmation email again
              </button>
              .
            </p>
          )}
        </div>

        {/* Footer */}
        <div className="relative flex items-center justify-between border-t border-[var(--border)] px-6 py-4 bg-[var(--surface-glass)]">
          <Button type="button" variant="ghost" onClick={onClose} disabled={loading}>
            {notice ? "Close" : "Cancel"}
          </Button>
          <Button type="submit" disabled={loading || notice !== null}>
            {loading ? (
              <>
                <svg className="animate-spin" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                  <path d="M21 12a9 9 0 11-6.219-8.56"/>
                </svg>
                Please wait...
              </>
            ) : mode === "login" ? (
              "Sign In"
            ) : (
              "Create Account"
            )}
          </Button>
        </div>
      </form>
    </div>
  );
}
