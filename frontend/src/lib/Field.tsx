import type { InputHTMLAttributes, TextareaHTMLAttributes } from "react";

type InputProps = InputHTMLAttributes<HTMLInputElement> & {
  label: string;
  as?: "input";
  hint?: string;
};

type TextareaProps = TextareaHTMLAttributes<HTMLTextAreaElement> & {
  label: string;
  as: "textarea";
  hint?: string;
};

type FieldProps = InputProps | TextareaProps;

export default function Field(props: FieldProps) {
  const { label, as = "input", hint } = props;
  const shared =
    "w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-4 py-3 text-sm text-[var(--text-primary)] placeholder:text-[var(--text-muted)] transition-all duration-200 focus:border-[var(--accent)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-glow)] focus:bg-[var(--input-bg-focus)] hover:border-[var(--border-hover)]";

  return (
    <label className="flex flex-col gap-1.5 text-sm font-medium text-[var(--text-secondary)]">
      <span className="flex items-center justify-between">
        <span>{label}</span>
        {hint && <span className="text-xs text-[var(--text-muted)] font-normal">{hint}</span>}
      </span>
      {as === "textarea" ? (
        <textarea
          {...(props as TextareaProps)}
          className={`${shared} min-h-[120px] resize-none ${props.className ?? ""}`}
        />
      ) : (
        <input {...(props as InputProps)} className={`${shared} ${props.className ?? ""}`} />
      )}
    </label>
  );
}
