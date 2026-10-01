import { useId, useState, type KeyboardEvent } from "react";
import { karachiDateInput } from "../../lib/dates.ts";
import { BottomSheet } from "./BottomSheet.tsx";
import { Button } from "./Button.tsx";
import { Calendar } from "./Calendar.tsx";
import { cx } from "./cx.ts";
import { FieldShell, type FieldBaseProps } from "./FieldShell.tsx";
import { IconCalendar } from "./icons.tsx";
import { formatPickerDate } from "./pickerFormat.ts";
import { Portal } from "./Portal.tsx";
import { controlBoxClass } from "./styles.ts";
import { useIsPhone } from "./useMediaQuery.ts";
import { usePopover } from "./usePopover.ts";

export type DatePickerProps = FieldBaseProps & {
  /** The date as "YYYY-MM-DD", or "" when none is picked. */
  value: string;
  onChange: (value: string) => void;
  /** Earliest and latest pickable day, "YYYY-MM-DD", both inclusive. Other days are faint and cannot be picked. */
  min?: string;
  max?: string;
  disabled?: boolean;
  /** Read-only in the disabled colour and never opens — for a date the server fixes, e.g. a refund date of today. */
  locked?: boolean;
  /** Shown while empty. */
  placeholder?: string;
  id?: string;
  className?: string;
  /** Accessible name when there is no visible label. */
  "aria-label"?: string;
  /** `form`: 48px with the label above. `filter`: 40px compact for filter bars, label drawn inside in grey. */
  size?: "form" | "filter";
  /** Starts with the calendar open (previews). */
  defaultOpen?: boolean;
};

/**
 * The one date field in the app: shows "Sep 29, 2026", opens a calendar card under it (a bottom
 * sheet with a Done button on a phone) and keeps the value as "YYYY-MM-DD", exactly what the API
 * takes. "Today" is today in Pakistan. Clear appears on optional fields only.
 */
export function DatePicker({
  value,
  onChange,
  min,
  max,
  disabled = false,
  locked = false,
  placeholder = "Select date",
  label,
  required,
  helper,
  error,
  id,
  className,
  "aria-label": ariaLabel,
  size = "form",
  defaultOpen = false,
}: DatePickerProps) {
  const autoId = useId();
  const triggerId = id ?? `dp-${autoId}`;
  const messageId = `${triggerId}-msg`;
  const labelId = `${triggerId}-label`;
  const valueId = `${triggerId}-value`;
  const [open, setOpen] = useState(defaultOpen);
  const isPhone = useIsPhone();
  const inert = disabled || locked;
  const isFilter = size === "filter";
  const close = () => setOpen(false);
  const { anchorRef, panelRef, style } = usePopover<HTMLButtonElement, HTMLDivElement>({
    open: open && !isPhone,
    onClose: close,
    onEscape: () => {
      close();
      anchorRef.current?.focus();
    },
  });

  const text = formatPickerDate(value);
  const pick = (next: string) => {
    onChange(next);
    if (!isPhone) {
      close();
      anchorRef.current?.focus();
    }
  };
  const clear = () => {
    onChange("");
    if (!isPhone) {
      close();
      anchorRef.current?.focus();
    }
  };

  const onKeyDown = (event: KeyboardEvent<HTMLButtonElement>) => {
    if (event.key === "ArrowDown" && !open && !inert) {
      event.preventDefault();
      setOpen(true);
    }
  };

  const calendar = (
    <Calendar value={value} min={min} max={max} today={karachiDateInput(0)} clearable={!required} onPick={pick} onClear={clear} autoFocus={!isPhone} />
  );

  const trigger = (
    <button
      ref={anchorRef}
      id={triggerId}
      type="button"
      disabled={inert}
      aria-haspopup="dialog"
      aria-expanded={open}
      aria-labelledby={label ? `${labelId} ${valueId}` : undefined}
      aria-label={label ? undefined : ariaLabel}
      aria-invalid={error ? true : undefined}
      aria-describedby={error || helper ? messageId : undefined}
      aria-required={required || undefined}
      onClick={() => setOpen((current) => !current)}
      onKeyDown={onKeyDown}
      className={cx(
        "text-left outline-none disabled:cursor-not-allowed",
        !inert && "cursor-pointer",
        isFilter
          ? cx(
              "flex h-10 w-full min-w-0 items-center gap-2 rounded-field border bg-card px-3 text-small transition-colors",
              "focus-visible:border-primary focus-visible:ring-3 focus-visible:ring-primary-ring",
              open ? "border-primary" : "border-line-input hover:border-ink-faint",
              inert && "bg-disabled hover:border-line-input",
            )
          : cx(controlBoxClass({ error: !!error, disabled: inert, open }), "focus-visible:border-primary focus-visible:ring-3 focus-visible:ring-primary-ring"),
      )}
    >
      {isFilter && label && <span id={labelId} className="shrink-0 font-bold text-ink-muted">{label}</span>}
      <span id={valueId} className={cx("min-w-0 flex-1 truncate", isFilter ? "font-extrabold" : "font-medium", text ? (inert && !isFilter ? "text-ink-2" : "text-ink") : "text-ink-faint")}>
        {text || placeholder}
      </span>
      <IconCalendar size={18} className="shrink-0 text-ink-2" />
    </button>
  );

  const picker = (
    <>
      {trigger}
      {required && !inert && (
        <input
          aria-hidden="true"
          tabIndex={-1}
          required
          value={value}
          onChange={() => {}}
          onFocus={() => anchorRef.current?.focus()}
          className="pointer-events-none absolute inset-x-0 bottom-0 h-px opacity-0"
        />
      )}
      {open && !inert && !isPhone && (
        <Portal>
          <div
            ref={panelRef}
            role="dialog"
            aria-label={typeof label === "string" ? label : ariaLabel ?? "Choose a date"}
            style={style}
            className="animate-pop-in z-[200] overflow-y-auto overscroll-contain rounded-menu border border-line bg-card p-3 font-ui shadow-menu"
          >
            {calendar}
          </div>
        </Portal>
      )}
      {isPhone && (
        <BottomSheet
          open={open && !inert}
          onClose={close}
          title={typeof label === "string" ? label : ariaLabel ?? "Choose a date"}
          footer={<Button size="lg" fullWidth onClick={close}>Done</Button>}
        >
          {calendar}
        </BottomSheet>
      )}
    </>
  );

  if (isFilter) return <div className={cx("relative min-w-0 font-ui", className)}>{picker}</div>;

  return (
    <FieldShell
      label={label && <span id={labelId}>{label}</span>}
      required={required}
      helper={helper}
      error={error}
      htmlFor={triggerId}
      messageId={messageId}
      className={className}
    >
      <div className="relative">{picker}</div>
    </FieldShell>
  );
}
