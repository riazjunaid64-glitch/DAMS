import { useId, useLayoutEffect, useRef, type InputHTMLAttributes, type ReactNode, type Ref } from "react";
import { FieldShell, type FieldBaseProps } from "./FieldShell.tsx";
import { caretAfter, cleanNumber, groupThousands } from "./numberFormat.ts";
import { bareInputClass, controlBoxClass } from "./styles.ts";

type InputProps = Omit<InputHTMLAttributes<HTMLInputElement>, "size" | "prefix">;

export type TextFieldProps = FieldBaseProps & InputProps & {
  ref?: Ref<HTMLInputElement>;
  /** Bold text or icon before the value, e.g. "Rs". */
  prefix?: ReactNode;
  /** Bold text or icon after the value, e.g. "sq ft". */
  suffix?: ReactNode;
  /** Classes for the outer frame (label + box), e.g. a grid span. */
  className?: string;
};

/** Text input with label, helper, error, and optional prefix / suffix. 48px, radius 10. */
export function TextField({ ref, label, required, helper, error, prefix, suffix, className, id, disabled, ...input }: TextFieldProps) {
  const autoId = useId();
  const inputId = id ?? `tf-${autoId}`;
  const messageId = `${inputId}-msg`;
  return (
    <FieldShell label={label} required={required} helper={helper} error={error} htmlFor={inputId} messageId={messageId} className={className}>
      <div className={controlBoxClass({ error: !!error, disabled })}>
        {prefix && <span className="shrink-0 font-bold text-ink-2">{prefix}</span>}
        <input
          ref={ref}
          id={inputId}
          required={required}
          disabled={disabled}
          aria-invalid={error ? true : undefined}
          aria-describedby={error || helper ? messageId : undefined}
          className={bareInputClass}
          {...input}
        />
        {suffix && <span className="shrink-0 text-small font-bold text-ink-2">{suffix}</span>}
      </div>
    </FieldShell>
  );
}

export type NumberFieldProps = Omit<TextFieldProps, "value" | "onChange" | "type" | "defaultValue"> & {
  /** The raw number as typed, without separators ("1000000.5"). */
  value: string;
  onChange: (value: string) => void;
  /** Digits allowed after the point; 0 for whole numbers. */
  decimals?: number;
};

/**
 * A number shown with thousands separators ("1,000,000") while the caller keeps the raw string.
 * The value is never parsed or rounded here, so amounts stay exact.
 */
export function NumberField({ value, onChange, decimals = 2, ...rest }: NumberFieldProps) {
  const inputRef = useRef<HTMLInputElement>(null);
  const caret = useRef<number | null>(null);
  const shown = groupThousands(value);

  useLayoutEffect(() => {
    if (caret.current === null || document.activeElement !== inputRef.current) return;
    const at = caretAfter(shown, caret.current);
    inputRef.current?.setSelectionRange(at, at);
    caret.current = null;
  }, [shown]);

  return (
    <TextField
      {...rest}
      ref={inputRef}
      inputMode={decimals > 0 ? "decimal" : "numeric"}
      value={shown}
      onChange={(event) => {
        const { value: typed, selectionStart } = event.target;
        const before = typed.slice(0, selectionStart ?? typed.length);
        caret.current = cleanNumber(before, decimals).length;
        onChange(cleanNumber(typed, decimals));
      }}
    />
  );
}

type DateTimeFieldProps = Omit<TextFieldProps, "type">;

/** The platform's own date picker (phone pickers included) in the shared field frame. */
export function DateField(props: DateTimeFieldProps) {
  return <TextField {...props} type="date" />;
}

export function TimeField(props: DateTimeFieldProps) {
  return <TextField {...props} type="time" />;
}
