import { useEffect, useId, useRef, useState, type KeyboardEvent } from "react";
import { BottomSheet } from "./BottomSheet.tsx";
import { Button } from "./Button.tsx";
import { cx } from "./cx.ts";
import { FieldShell, type FieldBaseProps } from "./FieldShell.tsx";
import { IconClock } from "./icons.tsx";
import { MenuPanel } from "./MenuPanel.tsx";
import { HALF_HOURS, formatPickerTime, fromClockParts, parseTypedTime, toClockParts, type ClockParts } from "./pickerFormat.ts";
import { controlBoxClass, menuRowClass } from "./styles.ts";
import { useIsPhone } from "./useMediaQuery.ts";
import { usePopover } from "./usePopover.ts";

export type TimePickerProps = FieldBaseProps & {
  /** The time as "HH:mm" (24 hour), or "" when none is picked. */
  value: string;
  onChange: (value: string) => void;
  disabled?: boolean;
  /** Shown while empty. */
  placeholder?: string;
  id?: string;
  className?: string;
  /** Accessible name when there is no visible label. */
  "aria-label"?: string;
  /** Starts with the list open (previews). */
  defaultOpen?: boolean;
};

const HOURS = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];
const MINUTES = [0, 30];

/**
 * The one time field in the app: shows "11:30 AM". On desktop a list in 30-minute steps opens under
 * the field and a time such as "11:15 AM" can be typed; on a phone a bottom sheet has hour, minute
 * (00 / 30) and AM / PM columns with a Done button. The value is kept as "HH:mm".
 */
export function TimePicker({
  value,
  onChange,
  disabled = false,
  placeholder = "Select time",
  label,
  required,
  helper,
  error,
  id,
  className,
  "aria-label": ariaLabel,
  defaultOpen = false,
}: TimePickerProps) {
  const autoId = useId();
  const inputId = id ?? `tp-${autoId}`;
  const listId = `${inputId}-list`;
  const messageId = `${inputId}-msg`;
  const isPhone = useIsPhone();
  const [open, setOpen] = useState(defaultOpen);
  // What is in the box while it is being typed; null shows the formatted value.
  const [typed, setTyped] = useState<string | null>(null);
  const [active, setActive] = useState(() => HALF_HOURS.indexOf(value));
  const closeList = () => setOpen(false);
  const { anchorRef, panelRef, style } = usePopover<HTMLDivElement, HTMLDivElement>({
    open: open && !isPhone,
    onClose: () => {
      closeList();
      commitTyped();
    },
    // Esc backs out: the list closes and half-typed text is dropped.
    onEscape: () => {
      closeList();
      setTyped(null);
      inputRef.current?.focus();
    },
    matchWidth: true,
  });
  const inputRef = useRef<HTMLInputElement>(null);
  const shown = typed ?? formatPickerTime(value);
  const selectedIndex = HALF_HOURS.indexOf(value);

  const commitTyped = () => {
    if (typed === null) return;
    const parsed = parseTypedTime(typed);
    setTyped(null);
    if (parsed !== null) {
      if (parsed !== value) onChange(parsed);
    } else if (typed.trim() === "" && value !== "" && !required) {
      onChange("");
    }
  };

  useEffect(() => {
    if (!open || isPhone) return;
    const index = selectedIndex >= 0 ? selectedIndex : HALF_HOURS.indexOf("09:00");
    document.getElementById(`${listId}-${index}`)?.scrollIntoView({ block: "center" });
    // Waits for the popover to be placed: until it has a max height the list is not yet scrollable.
  }, [open, isPhone, listId, selectedIndex, style.maxHeight]);

  const showRow = (index: number) => {
    setActive(index);
    document.getElementById(`${listId}-${index}`)?.scrollIntoView({ block: "nearest" });
  };

  const openList = () => {
    setActive(selectedIndex >= 0 ? selectedIndex : HALF_HOURS.indexOf("09:00"));
    setOpen(true);
  };

  const pick = (time: string) => {
    setTyped(null);
    closeList();
    inputRef.current?.focus();
    if (time !== value) onChange(time);
  };

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (isPhone) {
      if (event.key === "Enter" || event.key === " " || event.key === "ArrowDown") {
        event.preventDefault();
        setOpen(true);
      }
      return;
    }
    switch (event.key) {
      case "ArrowDown":
      case "ArrowUp":
        event.preventDefault();
        if (!open) openList();
        else showRow(Math.min(HALF_HOURS.length - 1, Math.max(0, active + (event.key === "ArrowDown" ? 1 : -1))));
        return;
      case "Enter":
        event.preventDefault();
        if (open && typed === null && active >= 0) pick(HALF_HOURS[active]!);
        else {
          commitTyped();
          closeList();
        }
        return;
      case "Tab":
        commitTyped();
        closeList();
        return;
    }
  };

  const phoneOpen = isPhone && open && !disabled;

  const box = (
    <div
      ref={anchorRef}
      className={cx(controlBoxClass({ error: !!error, disabled, open }), "cursor-text")}
      onClick={() => {
        if (disabled) return;
        if (isPhone) setOpen(true);
        else {
          inputRef.current?.focus();
          if (!open) openList();
        }
      }}
    >
      <input
        ref={inputRef}
        id={inputId}
        type="text"
        role={isPhone ? undefined : "combobox"}
        aria-expanded={isPhone ? undefined : open}
        aria-controls={open && !isPhone ? listId : undefined}
        aria-activedescendant={open && !isPhone && active >= 0 ? `${listId}-${active}` : undefined}
        aria-haspopup={isPhone ? "dialog" : "listbox"}
        aria-label={label ? undefined : ariaLabel}
        aria-invalid={error ? true : undefined}
        aria-describedby={error || helper ? messageId : undefined}
        aria-required={required || undefined}
        required={required}
        disabled={disabled}
        // A phone opens the sheet instead of the keyboard, so the box is read-only there.
        readOnly={isPhone}
        inputMode={isPhone ? "none" : "text"}
        autoComplete="off"
        placeholder={placeholder}
        value={shown}
        onChange={(event) => {
          setTyped(event.target.value);
          if (!open) setOpen(true);
        }}
        onBlur={() => {
          if (!open) commitTyped();
        }}
        onKeyDown={onKeyDown}
        className={cx("h-full min-w-0 flex-1 border-0 bg-transparent p-0 text-inherit outline-none placeholder:text-ink-faint disabled:cursor-not-allowed", isPhone && "cursor-pointer")}
      />
      <IconClock size={18} className="shrink-0 text-ink-2" />
    </div>
  );

  return (
    <FieldShell label={label} required={required} helper={helper} error={error} htmlFor={inputId} messageId={messageId} className={className}>
      {box}
      {open && !disabled && !isPhone && (
        <MenuPanel ref={panelRef} id={listId} role="listbox" style={style} label={typeof label === "string" ? label : ariaLabel}>
          {HALF_HOURS.map((time, index) => {
            const selected = time === value;
            return (
              <div
                key={time}
                id={`${listId}-${index}`}
                role="option"
                aria-selected={selected}
                onPointerMove={() => setActive(index)}
                // Keep focus in the box: taking it would blur the input and commit half-typed text first.
                onPointerDown={(event) => event.preventDefault()}
                onClick={() => pick(time)}
                className={menuRowClass({ active: index === active, selected })}
              >
                {formatPickerTime(time)}
              </div>
            );
          })}
        </MenuPanel>
      )}
      {isPhone && (
        <TimeSheet
          open={phoneOpen}
          title={typeof label === "string" ? label : ariaLabel ?? "Time"}
          value={value}
          onClose={closeList}
          onDone={(time) => {
            closeList();
            if (time !== value) onChange(time);
          }}
        />
      )}
    </FieldShell>
  );
}

const columnButton = "flex h-11 w-full cursor-pointer items-center justify-center rounded-lg text-body font-bold outline-none focus-visible:ring-3 focus-visible:ring-primary-ring";

/** Phone time sheet: hour, minute (00 / 30) and AM / PM, applied with Done. Mounted only while open, so it starts from the current value each time. */
function TimeSheet({ open, ...rest }: TimeSheetProps) {
  return open ? <TimeSheetBody {...rest} /> : null;
}

type TimeSheetProps = { open: boolean; title: string; value: string; onClose: () => void; onDone: (time: string) => void };

function TimeSheetBody({ title, value, onClose, onDone }: Omit<TimeSheetProps, "open">) {
  // A time already set keeps its own minute (e.g. 11:15) until another is chosen.
  const [draft, setDraft] = useState<ClockParts>(() => toClockParts(value) ?? { hour12: 9, minute: 0, pm: false });
  // Brings the chosen hour to the middle of its column, as the design shows it.
  const hourList = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const list = hourList.current;
    const chosen = list?.querySelector<HTMLElement>('[aria-selected="true"]');
    if (list && chosen) list.scrollTop = chosen.offsetTop - list.clientHeight / 2 + chosen.clientHeight / 2;
  }, []);
  const pickedColumn = (selected: boolean) => (selected ? "bg-selected text-ink" : "text-ink-faint hover:bg-page");

  return (
    <BottomSheet open onClose={onClose} title={title} footer={<Button size="lg" fullWidth onClick={() => onDone(fromClockParts(draft))}>Done</Button>}>
      <div className="flex items-start justify-center gap-4">
        <div ref={hourList} role="listbox" aria-label="Hour" className="relative flex max-h-64 w-16 flex-col gap-1 overflow-y-auto">
          {HOURS.map((hour) => (
            <button key={hour} type="button" role="option" aria-selected={draft.hour12 === hour} onClick={() => setDraft({ ...draft, hour12: hour })} className={cx(columnButton, pickedColumn(draft.hour12 === hour))}>
              {hour}
            </button>
          ))}
        </div>
        <div role="listbox" aria-label="Minute" className="flex w-16 flex-col gap-1">
          {MINUTES.map((minute) => (
            <button key={minute} type="button" role="option" aria-selected={draft.minute === minute} onClick={() => setDraft({ ...draft, minute })} className={cx(columnButton, pickedColumn(draft.minute === minute))}>
              {String(minute).padStart(2, "0")}
            </button>
          ))}
        </div>
        <div role="listbox" aria-label="AM or PM" className="flex w-16 flex-col gap-1">
          {(["AM", "PM"] as const).map((half) => {
            const selected = draft.pm === (half === "PM");
            return (
              <button key={half} type="button" role="option" aria-selected={selected} onClick={() => setDraft({ ...draft, pm: half === "PM" })} className={cx(columnButton, "border", selected ? "border-primary bg-primary text-white" : "border-line-input bg-card text-ink hover:bg-page")}>
                {half}
              </button>
            );
          })}
        </div>
      </div>
    </BottomSheet>
  );
}
