import { useState } from "react";
import { api } from "../api/api";
import Button from "../lib/Button";
import Field from "../lib/Field";

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

  const handleSubmit = async (e?: React.FormEvent) => {
    e?.preventDefault();
    setLoading(true);

    try {
      if (mode === "login") {
        const res = await api("/api/Auth/login", {
          method: "POST",
          body: JSON.stringify({ email, password }),
        });

        if (!res.ok) {
          alert("Login failed");
          return;
        }

        const data = await res.json();
        localStorage.setItem("token", data.accessToken);
        localStorage.setItem("refreshToken", data.refreshToken);
        onSuccess?.();
        onClose();
      }

      if (mode === "signup") {
        const res = await api("/api/Auth/register", {
          method: "POST",
          body: JSON.stringify({
            fullName,
            email,
            password,
            roleId: 1,
          }),
        });

        if (!res.ok) {
          const text = await res.text();
          alert("Signup failed: " + text);
          return;
        }

        alert("Signup successful. Please login.");
        onClose();
      }
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center">
      <div className="absolute inset-0 bg-slate-950/80 backdrop-blur-sm" onClick={onClose} />

      <form
        onSubmit={handleSubmit}
        className="relative z-10 w-[420px] max-w-[90vw] overflow-hidden rounded-3xl border border-white/10 bg-slate-950/90 p-8 shadow-[0_40px_120px_-60px_rgba(15,23,42,0.9)]"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="absolute right-[-40%] top-[-40%] h-64 w-64 rounded-full bg-amber-400/10 blur-3xl" />
        <div className="absolute left-[-30%] bottom-[-40%] h-56 w-56 rounded-full bg-cyan-400/10 blur-3xl" />

        <div className="relative">
          <p className="text-xs font-semibold uppercase tracking-[0.3em] text-amber-300">
            {mode === "login" ? "Welcome back" : "Create account"}
          </p>
          <h3 className="mt-3 text-2xl font-semibold text-white">
            {mode === "login" ? "Login to your dashboard" : "Sign up for access"}
          </h3>
          <p className="mt-2 text-sm text-slate-300">
            {mode === "login"
              ? "Enter your credentials to continue."
              : "Fill in the details below and we will set up your account."}
          </p>

          <div className="mt-6 grid gap-4">
            {mode === "signup" && (
              <Field
                label="Full Name"
                value={fullName}
                onChange={(e) => setFullName(e.target.value)}
                placeholder="Full name"
              />
            )}

            <Field
              label="Email Address"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="Email"
              type="email"
            />

            <Field
              label="Password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              placeholder="Password"
              type="password"
            />
          </div>

          <div className="mt-8 flex flex-col gap-3 sm:flex-row sm:justify-between">
            <Button type="button" variant="outline" onClick={onClose} disabled={loading}>
              Cancel
            </Button>
            <Button type="submit" disabled={loading} className="sm:min-w-[140px]">
              {loading ? "Please wait..." : mode === "login" ? "Login" : "Sign Up"}
            </Button>
          </div>
        </div>
      </form>
    </div>
  );
}
