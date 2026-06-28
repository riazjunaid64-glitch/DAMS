import type { InputHTMLAttributes, TextareaHTMLAttributes } from "react";

type BaseProps = {
  label: string;
  hint?: string;
  /** Validation message shown in red below the field. When set, the field gets an error style. */
  error?: string;
};

type InputProps = BaseProps &
  InputHTMLAttributes<HTMLInputElement> & {
    as?: "input";
  };

type TextareaProps = BaseProps &
  TextareaHTMLAttributes<HTMLTextAreaElement> & {
    as: "textarea";
  };

type FieldProps = InputProps | TextareaProps;

const baseShared =
  "w-full rounded-xl border bg-[var(--input-bg)] px-4 py-3 text-sm text-[var(--text-primary)] placeholder:text-[var(--text-muted)] transition-all duration-200 focus:outline-none focus:ring-2";

const okBorder =
  "border-[var(--border)] hover:border-[var(--border-hover)] focus:border-[var(--accent)] focus:ring-[var(--accent-glow)] focus:bg-[var(--input-bg-focus)]";

const errorBorder =
  "border-rose-500/70 hover:border-rose-500 focus:border-rose-500 focus:ring-rose-500/25";

function Header({ label, hint, required }: { label: string; hint?: string; required?: boolean }) {
  return (
    <span className="flex items-center justify-between">
      <span>
        {label}
        {required && <span className="ml-0.5 text-rose-400">*</span>}
      </span>
      {hint && (
        <span className="text-xs text-[var(--text-muted)] font-normal">{hint}</span>
      )}
    </span>
  );
}

function ErrorText({ error }: { error?: string }) {
  if (!error) return null;
  return (
    <span className="flex items-center gap-1 text-xs font-normal text-rose-400" role="alert">
      <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
        <circle cx="12" cy="12" r="10" /><line x1="12" y1="8" x2="12" y2="12" /><line x1="12" y1="16" x2="12.01" y2="16" />
      </svg>
      {error}
    </span>
  );
}

export default function Field(props: FieldProps) {
  if (props.as === "textarea") {
    const { label, hint, error, className, required, ...rest } = props;
    return (
      <label className="flex flex-col gap-1.5 text-sm font-medium text-[var(--text-secondary)]">
        <Header label={label} hint={hint} required={required} />
        <textarea
          {...rest}
          required={required}
          aria-invalid={error ? true : undefined}
          className={`${baseShared} ${error ? errorBorder : okBorder} min-h-[120px] resize-none ${className ?? ""}`}
        />
        <ErrorText error={error} />
      </label>
    );
  }

  const { label, hint, error, className, required, ...rest } = props;
  return (
    <label className="flex flex-col gap-1.5 text-sm font-medium text-[var(--text-secondary)]">
      <Header label={label} hint={hint} required={required} />
      <input
        {...rest}
        required={required}
        aria-invalid={error ? true : undefined}
        className={`${baseShared} ${error ? errorBorder : okBorder} ${className ?? ""}`}
      />
      <ErrorText error={error} />
    </label>
  );
}
