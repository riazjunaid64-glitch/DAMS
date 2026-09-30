import { useId, useRef, type KeyboardEvent, type ReactNode } from "react";
import { cx } from "./cx.ts";
import { FieldShell, type FieldBaseProps } from "./FieldShell.tsx";
import type { Option } from "./types.ts";

type SingleChoiceProps = FieldBaseProps & {
  options: readonly Option[];
  value: string;
  onChange: (value: string) => void;
  /** Accessible name when there is no visible label. */
  "aria-label"?: string;
  className?: string;
  /**
   * `chips` (default): separate bordered chips. `segmented`: one grey track with the selected option as a
   * white pill, for a two- or three-way form choice (Fixed amount | Percentage).
   */
  variant?: "chips" | "segmented";
};

/**
 * Single choice from a few options shown as chips (Studio / 1 Bed / 2 Bed). Behaves as a radio
 * group: one Tab stop, arrow keys move and select.
 */
export function ChoiceChips({ options, value, onChange, label, required, helper, error, className, variant = "chips", "aria-label": ariaLabel }: SingleChoiceProps) {
  const segmented = variant === "segmented";
  const refs = useRef<(HTMLButtonElement | null)[]>([]);
  const selectedIndex = options.findIndex((option) => option.value === value);
  const tabStop = selectedIndex >= 0 ? selectedIndex : options.findIndex((option) => !option.disabled);

  const onKeyDown = (event: KeyboardEvent, index: number) => {
    const step = event.key === "ArrowRight" || event.key === "ArrowDown" ? 1 : event.key === "ArrowLeft" || event.key === "ArrowUp" ? -1 : 0;
    if (!step) return;
    event.preventDefault();
    for (let i = 1; i <= options.length; i += 1) {
      const next = (index + step * i + options.length) % options.length;
      if (!options[next]?.disabled) {
        refs.current[next]?.focus();
        onChange(options[next]!.value);
        return;
      }
    }
  };

  return (
    <FieldShell as="fieldset" label={label} required={required} helper={helper} error={error} className={className}>
      <div
        role="radiogroup"
        aria-label={label ? undefined : ariaLabel}
        className={segmented ? "flex gap-1 rounded-field bg-track p-1" : "flex flex-wrap gap-2"}
      >
        {options.map((option, index) => {
          const checked = option.value === value;
          return (
            <button
              key={option.value}
              ref={(el) => { refs.current[index] = el; }}
              type="button"
              role="radio"
              aria-checked={checked}
              disabled={option.disabled}
              tabIndex={index === tabStop ? 0 : -1}
              onClick={() => onChange(option.value)}
              onKeyDown={(event) => onKeyDown(event, index)}
              className={cx(
                "cursor-pointer whitespace-nowrap text-sm font-bold transition-colors",
                "focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary disabled:cursor-not-allowed disabled:opacity-45",
                segmented
                  ? cx("h-10 flex-1 rounded-field border-0 px-3", checked ? "bg-card text-ink shadow-sm" : "bg-transparent text-ink-2 hover:text-ink")
                  : cx("h-11 rounded-field border px-4", checked ? "border-primary bg-primary text-white" : "border-line-input bg-card text-ink hover:bg-page"),
              )}
            >
              {option.label}
            </button>
          );
        })}
      </div>
    </FieldShell>
  );
}

const nativeMarkClass = "size-[18px] shrink-0 cursor-pointer accent-primary disabled:cursor-not-allowed";
const choiceRowClass = "flex min-h-11 cursor-pointer items-center gap-2.5 text-body font-bold text-ink has-[:disabled]:cursor-not-allowed has-[:disabled]:opacity-50 md:min-h-8";

/** Native radio buttons (keyboard and screen reader support for free) in the app's style. */
export function RadioGroup({ options, value, onChange, label, required, helper, error, className, name, "aria-label": ariaLabel }: SingleChoiceProps & { name?: string }) {
  const autoName = useId();
  return (
    <FieldShell as="fieldset" label={label} required={required} helper={helper} error={error} className={className}>
      <div role="radiogroup" aria-label={label ? undefined : ariaLabel} className="flex flex-col">
        {options.map((option) => (
          <label key={option.value} className={choiceRowClass}>
            <input
              type="radio"
              name={name ?? autoName}
              value={option.value}
              checked={option.value === value}
              disabled={option.disabled}
              onChange={() => onChange(option.value)}
              className={nativeMarkClass}
            />
            {option.label}
          </label>
        ))}
      </div>
    </FieldShell>
  );
}

type CheckProps = {
  checked: boolean;
  onChange: (checked: boolean) => void;
  label: ReactNode;
  disabled?: boolean;
  className?: string;
};

export function Checkbox({ checked, onChange, label, disabled, className }: CheckProps) {
  return (
    <label className={cx(choiceRowClass, "font-ui", className)}>
      <input type="checkbox" checked={checked} disabled={disabled} onChange={(event) => onChange(event.target.checked)} className={nativeMarkClass} />
      {label}
    </label>
  );
}

/** On / off switch; green when on. */
export function Toggle({ checked, onChange, label, disabled, className }: CheckProps) {
  return (
    <label className={cx(choiceRowClass, "font-ui", className)}>
      <button
        type="button"
        role="switch"
        aria-checked={checked}
        disabled={disabled}
        onClick={() => onChange(!checked)}
        className={cx(
          "relative h-7 w-11 shrink-0 cursor-pointer rounded-full transition-colors disabled:cursor-not-allowed",
          "focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary",
          checked ? "bg-success" : "bg-line-input",
        )}
      >
        <span
          aria-hidden="true"
          className={cx("absolute top-1 left-1 size-5 rounded-full bg-card shadow-sm transition-transform", checked && "translate-x-4")}
        />
      </button>
      {label}
    </label>
  );
}
