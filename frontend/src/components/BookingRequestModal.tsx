import { useState } from "react";
import { api } from "../api/api.ts";
import Button from "../lib/Button.tsx";
import Field from "../lib/Field.tsx";
import ModalPortal from "../lib/ModalPortal.tsx";
import { formatPkr } from "../utils/currency.ts";
import {
  PLACEHOLDERS,
  formatCnic,
  formatPkMobile,
  isValidCnic,
  isValidEmail,
  isValidPkMobile,
} from "../utils/validation.ts";

interface Unit {
  id: number;
  unitNumber: string;
  unitType: string;
  price: number;
}

interface Project {
  id: number;
  projectName: string;
  location: string;
}

type Props = {
  unit: Unit;
  project: Project | null;
  onClose: () => void;
  onSuccess?: () => void;
};

export default function BookingRequestModal({ unit, project, onClose, onSuccess }: Props) {
  const [form, setForm] = useState({
    fullName: "",
    phone: "",
    email: "",
    cnic: "",
    address: "",
    notes: "",
  });
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState(false);

  const handleChange = (field: keyof typeof form) => (
    e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>
  ) => {
    let value = e.target.value;
    if (field === "cnic") value = formatCnic(value);
    if (field === "phone") value = formatPkMobile(value);
    setForm((prev) => ({ ...prev, [field]: value }));
    setError(null);
  };

  // Inline, per-field validation messages (shown only once the user has typed).
  const fieldError = (field: "phone" | "email" | "cnic"): string | undefined => {
    const v = form[field].trim();
    if (!v) return undefined;
    if (field === "email" && !isValidEmail(v))
      return "Enter a valid email address (e.g. name@example.com).";
    if (field === "phone" && !isValidPkMobile(v))
      return "Enter a valid Pakistani mobile number (e.g. 0300-1234567).";
    if (field === "cnic" && !isValidCnic(v))
      return "CNIC must be 13 digits in the format 00000-0000000-0.";
    return undefined;
  };

  const validateForm = (): string | null => {
    if (!form.fullName.trim() || form.fullName.trim().length < 2)
      return "Please enter your full name (at least 2 characters).";
    if (!isValidPkMobile(form.phone))
      return "Please enter a valid Pakistani mobile number (e.g. 0300-1234567).";
    if (!isValidEmail(form.email))
      return "Please enter a valid email address.";
    if (!isValidCnic(form.cnic))
      return "Please enter a valid CNIC in the format 00000-0000000-0.";
    if (!form.address.trim() || form.address.trim().length < 10)
      return "Please enter your complete address (at least 10 characters).";
    return null;
  };

  const handleSubmit = async (e?: React.FormEvent) => {
    e?.preventDefault();

    const validationError = validateForm();
    if (validationError) {
      setError(validationError);
      return;
    }

    setLoading(true);
    setError(null);

    try {
      const res = await api("/api/BookingRequest", {
        method: "POST",
        body: JSON.stringify({
          unitId: unit.id,
          fullName: form.fullName.trim(),
          phone: form.phone.trim(),
          email: form.email.trim(),
          cnic: form.cnic.trim(),
          address: form.address.trim(),
          notes: form.notes.trim() || null,
        }),
      }, false);

      if (!res.ok) {
        const data = await res.json().catch(() => ({}));
        setError(data.message || "Unable to submit booking request. Please try again.");
        return;
      }

      setSuccess(true);
      onSuccess?.();
    } catch {
      setError("Something went wrong. Please try again.");
    } finally {
      setLoading(false);
    }
  };

  if (success) {
    return (
      <ModalPortal>
      <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
        <div
          className="absolute inset-0 bg-black/60 backdrop-blur-sm animate-fade-in"
          onClick={onClose}
        />
        <div className="relative z-10 w-[480px] max-w-[92vw] animate-scale-in overflow-hidden rounded-2xl border border-[var(--border)] bg-[var(--modal-bg)] shadow-2xl">
          <div className="absolute right-0 top-0 h-32 w-32 rounded-full bg-emerald-500/[0.08] blur-[60px]" />
          <div className="absolute bottom-0 left-0 h-24 w-24 rounded-full bg-emerald-500/[0.06] blur-[40px]" />

          <div className="relative px-8 py-10 text-center">
            <div className="mx-auto mb-5 flex h-16 w-16 items-center justify-center rounded-2xl bg-emerald-500/10 border border-emerald-500/20">
              <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" className="text-emerald-400">
                <path d="M22 11.08V12a10 10 0 11-5.93-9.14"/>
                <polyline points="22 4 12 14.01 9 11.01"/>
              </svg>
            </div>

            <h3 className="mb-2 text-xl font-semibold text-[var(--text-heading)]">
              Request Submitted Successfully
            </h3>
            <p className="mb-6 text-sm text-[var(--text-muted)] leading-relaxed">
              Your booking request for <span className="font-medium text-[var(--text-secondary)]">{unit.unitNumber}</span> has been received and is now under review. Our team will contact you shortly.
            </p>

            <div className="rounded-xl border border-[var(--border)] bg-[var(--surface-glass)] p-4 mb-6">
              <div className="flex items-center justify-between text-sm">
                <span className="text-[var(--text-muted)]">Unit</span>
                <span className="font-medium text-[var(--text-primary)]">{unit.unitNumber}</span>
              </div>
              {project && (
                <div className="flex items-center justify-between text-sm mt-2">
                  <span className="text-[var(--text-muted)]">Project</span>
                  <span className="font-medium text-[var(--text-primary)]">{project.projectName}</span>
                </div>
              )}
              <div className="flex items-center justify-between text-sm mt-2">
                <span className="text-[var(--text-muted)]">Status</span>
                <span className="inline-flex items-center gap-1.5 rounded-full bg-amber-500/10 border border-amber-500/20 px-2.5 py-0.5 text-xs font-medium text-amber-400">
                  <span className="h-1.5 w-1.5 rounded-full bg-amber-400 animate-pulse" />
                  Under Review
                </span>
              </div>
            </div>

            <Button onClick={onClose} className="w-full">
              Close
            </Button>
          </div>
        </div>
      </div>
      </ModalPortal>
    );
  }

  return (
    <ModalPortal>
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
      <div
        className="absolute inset-0 bg-black/60 backdrop-blur-sm animate-fade-in"
        onClick={onClose}
      />

      <form
        onSubmit={handleSubmit}
        className="relative z-10 w-[540px] max-w-[92vw] max-h-[90vh] overflow-y-auto animate-scale-in rounded-2xl border border-[var(--border)] bg-[var(--modal-bg)] shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="absolute right-0 top-0 h-32 w-32 rounded-full bg-indigo-500/[0.08] blur-[60px]" />
        <div className="absolute bottom-0 left-0 h-24 w-24 rounded-full bg-violet-500/[0.06] blur-[40px]" />

        {/* Header */}
        <div className="sticky top-0 z-10 border-b border-[var(--border)] bg-[var(--modal-bg)] px-6 py-5">
          <div className="flex items-center justify-between">
            <div>
              <div className="mb-1 inline-flex items-center gap-2 rounded-full bg-indigo-500/[0.08] px-2.5 py-0.5">
                <span className="h-1.5 w-1.5 rounded-full bg-indigo-500" />
                <span className="text-[10px] font-semibold uppercase tracking-wider text-indigo-500">
                  Booking Request
                </span>
              </div>
              <h3 className="text-lg font-semibold text-[var(--text-heading)]">
                Request to Book Unit
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
            Fill in your details to submit a booking request for this unit.
          </p>
        </div>

        {/* Unit Info Card */}
        <div className="relative px-6 pt-5">
          <div className="rounded-xl border border-[var(--border)] bg-[var(--surface-glass)] p-4">
            <div className="flex items-center gap-4">
              <div className="flex h-12 w-12 items-center justify-center rounded-xl bg-indigo-500/10 border border-indigo-500/20">
                <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" className="text-indigo-400" strokeLinecap="round">
                  <path d="M3 9l9-7 9 7v11a2 2 0 01-2 2H5a2 2 0 01-2-2V9z"/>
                  <polyline points="9 22 9 12 15 12 15 22"/>
                </svg>
              </div>
              <div className="flex-1 min-w-0">
                <div className="flex items-center gap-2 flex-wrap">
                  <span className="font-semibold text-[var(--text-heading)]">{unit.unitNumber}</span>
                  <span className="rounded-md bg-[var(--accent-glow)] px-2 py-0.5 text-[10px] font-medium text-[var(--accent)] uppercase tracking-wider">
                    {unit.unitType}
                  </span>
                </div>
                {project && (
                  <p className="text-sm text-[var(--text-muted)] truncate">{project.projectName} • {project.location}</p>
                )}
              </div>
              <div className="text-right">
                <p className="text-lg font-bold text-[var(--text-heading)]">{formatPkr(unit.price)}</p>
              </div>
            </div>
          </div>
        </div>

        {/* Form Fields */}
        <div className="relative space-y-4 px-6 py-5">
          <div className="grid gap-4 sm:grid-cols-2">
            <Field
              label="Full Name"
              value={form.fullName}
              onChange={handleChange("fullName")}
              placeholder="Enter your full name"
              required
            />
            <Field
              label="Phone Number"
              value={form.phone}
              onChange={handleChange("phone")}
              placeholder={PLACEHOLDERS.mobile}
              hint="Pakistani mobile"
              inputMode="tel"
              type="tel"
              error={fieldError("phone")}
              required
            />
          </div>

          <Field
            label="Email Address"
            value={form.email}
            onChange={handleChange("email")}
            placeholder={PLACEHOLDERS.email}
            type="email"
            error={fieldError("email")}
            required
          />

          <Field
            label="CNIC Number"
            value={form.cnic}
            onChange={handleChange("cnic")}
            placeholder={PLACEHOLDERS.cnic}
            hint="00000-0000000-0"
            inputMode="numeric"
            error={fieldError("cnic")}
            required
          />

          <Field
            label="Address"
            value={form.address}
            onChange={handleChange("address")}
            placeholder="Enter your complete address"
            required
          />

          <Field
            as="textarea"
            label="Additional Notes"
            hint="Optional"
            value={form.notes}
            onChange={handleChange("notes")}
            placeholder="Any special requirements or questions..."
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
        <div className="sticky bottom-0 z-10 flex items-center justify-between border-t border-[var(--border)] px-6 py-4 bg-[var(--surface-glass)]">
          <Button type="button" variant="ghost" onClick={onClose} disabled={loading}>
            Cancel
          </Button>
          <Button type="submit" disabled={loading}>
            {loading ? (
              <>
                <svg className="animate-spin" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                  <path d="M21 12a9 9 0 11-6.219-8.56"/>
                </svg>
                Submitting...
              </>
            ) : (
              <>
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                  <path d="M22 11.08V12a10 10 0 11-5.93-9.14"/>
                  <polyline points="22 4 12 14.01 9 11.01"/>
                </svg>
                Submit Request
              </>
            )}
          </Button>
        </div>
      </form>
    </div>
    </ModalPortal>
  );
}
