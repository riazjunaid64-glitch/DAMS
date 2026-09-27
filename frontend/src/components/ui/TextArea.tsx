import { useId, type Ref, type TextareaHTMLAttributes } from "react";
import { cx } from "./cx.ts";
import { FieldShell, type FieldBaseProps } from "./FieldShell.tsx";
import { controlBoxClass } from "./styles.ts";

export type TextAreaProps = FieldBaseProps & TextareaHTMLAttributes<HTMLTextAreaElement> & {
  ref?: Ref<HTMLTextAreaElement>;
  /** Classes for the outer frame (label + box). */
  className?: string;
};

/** Multi-line notes field in the shared frame. */
export function TextArea({ ref, label, required, helper, error, className, id, disabled, rows = 4, ...textarea }: TextAreaProps) {
  const autoId = useId();
  const inputId = id ?? `ta-${autoId}`;
  const messageId = `${inputId}-msg`;
  return (
    <FieldShell label={label} required={required} helper={helper} error={error} htmlFor={inputId} messageId={messageId} className={className}>
      <textarea
        ref={ref}
        id={inputId}
        rows={rows}
        required={required}
        disabled={disabled}
        aria-invalid={error ? true : undefined}
        aria-describedby={error || helper ? messageId : undefined}
        className={cx(
          controlBoxClass({ error: !!error, disabled }),
          "h-auto min-h-24 resize-y py-3 outline-none placeholder:text-ink-faint focus:border-primary focus:ring-3 focus:ring-primary-ring",
          !!error && "focus:border-danger focus:ring-danger-soft",
        )}
        {...textarea}
      />
    </FieldShell>
  );
}
