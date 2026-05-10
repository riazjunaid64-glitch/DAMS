import { useState } from "react";
import { api } from "../api/api.ts";
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
          setError("Invalid email or password. Please try again.");
          return;
        }

        const data = await res.json();
        localStorage.setItem("token", data.accessToken);
        localStorage.setItem("refreshToken", data.refreshToken);
        onSuccess?.();
        onClose();
      }

      if (mode === "signup") {
        const res = await api(
          "/api/Auth/register",
          {
            method: "POST",
            body: JSON.stringify({
              fullName,
              email,
              password,
              roleId: 1,
            }),
          },
          false
        );

        if (!res.ok) {
          const text = await res.text();
          setError(text || "Signup failed. Please try again.");
          return;
        }

        setError(null);
        alert("Account created successfully! Please login.");
        onClose();
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

          <Field
            label="Password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            placeholder="••••••••"
            type="password"
          />

          {error && (
            <div className="rounded-xl border border-rose-500/20 bg-rose-500/[0.06] px-4 py-3 text-sm text-rose-300 flex items-center gap-2 animate-scale-in">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                <circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/>
              </svg>
              {error}
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="relative flex items-center justify-between border-t border-[var(--border)] px-6 py-4 bg-[var(--surface-glass)]">
          <Button type="button" variant="ghost" onClick={onClose} disabled={loading}>
            Cancel
          </Button>
          <Button type="submit" disabled={loading}>
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
