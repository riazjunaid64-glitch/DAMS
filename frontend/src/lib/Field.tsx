import type { ComponentPropsWithoutRef } from "react";

type BaseProps = {
  label: string;
  hint?: string;
};

type InputProps = BaseProps & Omit<ComponentPropsWithoutRef<"input">, "as"> & {
  as?: "input";
};

type TextareaProps = BaseProps & Omit<ComponentPropsWithoutRef<"textarea">, "as"> & {
  as: "textarea";
};

export type FieldProps = InputProps | TextareaProps;

export default function Field(props: FieldProps) {
  const shared =
    "w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-4 py-3 text-sm text-[var(--text-primary)] placeholder:text-[var(--text-muted)] transition-all duration-200 focus:border-[var(--accent)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-glow)] focus:bg-[var(--input-bg-focus)] hover:border-[var(--border-hover)]";

  if (props.as === "textarea") {
    const { label, as, hint, className, ...rest } = props;
    return (
      <label className="flex flex-col gap-1.5 text-sm font-medium text-[var(--text-secondary)]">
        <span className="flex items-center justify-between">
          <span>{label}</span>
          {hint && <span className="text-xs text-[var(--text-muted)] font-normal">{hint}</span>}
        </span>
        <textarea
          {...rest}
          className={`${shared} min-h-[120px] resize-none ${className ?? ""}`}
        />
      </label>
    );
  }

  const { label, as, hint, className, ...rest } = props;
  return (
    <label className="flex flex-col gap-1.5 text-sm font-medium text-[var(--text-secondary)]">
      <span className="flex items-center justify-between">
        <span>{label}</span>
        {hint && <span className="text-xs text-[var(--text-muted)] font-normal">{hint}</span>}
      </span>
      <input
        {...rest}
        className={`${shared} ${className ?? ""}`}
      />
    </label>
  );
}
