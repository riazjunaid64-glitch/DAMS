import { Children, isValidElement, useRef, useState } from "react";
import type { ChangeEvent, OptionHTMLAttributes, ReactElement, ReactNode, SelectHTMLAttributes } from "react";
import { Dropdown, type Option } from "../components/ui";

/*
 * The select API existing screens were written against — <option> children, a change event — drawn
 * by the shared Dropdown. New code uses Dropdown directly with an options array.
 */

type AppSelectProps = Omit<SelectHTMLAttributes<HTMLSelectElement>, "multiple" | "size">;

function optionValue(option: ReactElement<OptionHTMLAttributes<HTMLOptionElement>>) {
  if (option.props.value != null) return String(option.props.value);
  return Children.toArray(option.props.children).join("");
}

function readOptions(children: ReactNode): Option[] {
  return Children.toArray(children).flatMap((child) => {
    if (!isValidElement<OptionHTMLAttributes<HTMLOptionElement>>(child) || child.type !== "option") return [];
    return [{ value: optionValue(child), label: child.props.children, disabled: !!child.props.disabled }];
  });
}

function initialValue(value: AppSelectProps["value"], defaultValue: AppSelectProps["defaultValue"]) {
  const candidate = value ?? defaultValue ?? "";
  return Array.isArray(candidate) ? String(candidate[0] ?? "") : String(candidate);
}

/** The subset of a select change event callers read: `target.value` (and `name`). */
function changeEvent(value: string, name: string | undefined) {
  const target = { value, name: name ?? "" } as HTMLSelectElement;
  return { target, currentTarget: target } as ChangeEvent<HTMLSelectElement>;
}

export default function AppSelect({
  children,
  className,
  value,
  defaultValue,
  onChange,
  disabled,
  required,
  id,
  name,
  "aria-label": ariaLabel,
}: AppSelectProps) {
  const [ownValue, setOwnValue] = useState(() => initialValue(value, defaultValue));
  const current = value == null ? ownValue : String(value);
  const wrapperRef = useRef<HTMLSpanElement>(null);

  return (
    // The caller's classes were written for a <select> box; the wrapper keeps their width and
    // margins and drops the box itself (see .app-select in index.css), since Dropdown draws its own.
    <span ref={wrapperRef} className={`app-select ${className ?? ""}`}>
      <Dropdown
        id={id}
        name={name}
        aria-label={ariaLabel}
        options={readOptions(children)}
        value={current}
        disabled={disabled}
        onChange={(next) => {
          setOwnValue(next);
          onChange?.(changeEvent(next, name));
        }}
      />
      {/* Keeps the browser's required-field check for forms that relied on the native select. */}
      {required && (
        <input
          tabIndex={-1}
          aria-hidden="true"
          required
          disabled={disabled}
          value={current}
          onChange={() => {}}
          onFocus={() => wrapperRef.current?.querySelector("button")?.focus()}
          className="sr-only"
        />
      )}
    </span>
  );
}
