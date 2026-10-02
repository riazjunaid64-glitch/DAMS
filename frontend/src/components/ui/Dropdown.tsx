import { useEffect, useId, useRef, useState, type KeyboardEvent, type ReactNode } from "react";
import { cx } from "./cx.ts";
import { FieldShell, type FieldBaseProps } from "./FieldShell.tsx";
import { IconCheck, IconChevronDown, IconSearch } from "./icons.tsx";
import { MenuPanel } from "./MenuPanel.tsx";
import { controlBoxClass, menuRowClass } from "./styles.ts";
import type { Option } from "./types.ts";
import { usePopover } from "./usePopover.ts";

export type DropdownOption = Option;

export type DropdownProps = FieldBaseProps & {
  options: readonly DropdownOption[];
  value: string;
  onChange: (value: string) => void;
  /** Shown when no option matches `value`. */
  placeholder?: ReactNode;
  /** Shown before the value, e.g. a filter icon. */
  icon?: ReactNode;
  disabled?: boolean;
  /**
   * `form`: 48px, full width, label above. `filter`: 40px compact for filter bars, with the label
   * drawn inside in grey before the value ("Status All").
   */
  size?: "form" | "filter";
  id?: string;
  /** Adds a hidden input so the value is part of a submitted form. */
  name?: string;
  className?: string;
  /** Accessible name when there is no visible label. */
  "aria-label"?: string;
  /** Starts with the list open (previews). */
  defaultOpen?: boolean;
  /** A search box in the list. Options whose label contains the typed text stay visible. */
  searchable?: boolean;
};

/** The text a person can search or type-ahead against. */
function optionText(option: DropdownOption): string {
  return typeof option.label === "string" ? option.label : option.value;
}

/**
 * The one select in the app. A custom list (not the browser's native look) that works the same
 * everywhere, including inside popups and the phone bottom sheet: its list is portalled so it is
 * never cut off. Arrow keys / Home / End move, Enter or Space picks, Esc or an outside press closes,
 * and typing a letter jumps to the next option starting with it.
 */
export function Dropdown({
  options,
  value,
  onChange,
  placeholder,
  icon,
  disabled = false,
  size = "form",
  label,
  required,
  helper,
  error,
  id,
  name,
  className,
  "aria-label": ariaLabel,
  defaultOpen = false,
  searchable = false,
}: DropdownProps) {
  const autoId = useId();
  const triggerId = id ?? `dd-${autoId}`;
  const listId = `${triggerId}-list`;
  const labelId = `${triggerId}-label`;
  const valueId = `${triggerId}-value`;
  const messageId = `${triggerId}-msg`;
  const [open, setOpen] = useState(defaultOpen);
  const [active, setActive] = useState(() => (defaultOpen ? options.findIndex((option) => option.value === value) : -1));
  const [search, setSearch] = useState("");
  const searchRef = useRef<HTMLInputElement>(null);
  const query = search.trim().toLowerCase();
  const shown = searchable && query !== ""
    ? options.filter((option) => optionText(option).toLowerCase().includes(query))
    : options;
  const closeList = () => {
    setOpen(false);
    setSearch("");
  };
  const { anchorRef, panelRef, style } = usePopover<HTMLButtonElement, HTMLDivElement>({
    open,
    onClose: closeList,
    matchWidth: true,
  });
  const typed = useRef({ text: "", at: 0 });

  const selected = options.find((option) => option.value === value);
  const selectedIndex = shown.findIndex((option) => option.value === value);
  const isFilter = size === "filter";

  useEffect(() => {
    if (open && searchable) searchRef.current?.focus();
  }, [open, searchable]);

  const enabledIndex = (from: number, step: 1 | -1) => {
    if (shown.length === 0) return -1;
    for (let i = 0, next = from; i < shown.length; i += 1) {
      next = (next + step + shown.length) % shown.length;
      if (!shown[next]?.disabled) return next;
    }
    return -1;
  };

  const show = (index: number) => {
    setActive(index);
    document.getElementById(`${listId}-${index}`)?.scrollIntoView({ block: "nearest" });
  };

  const openList = () => {
    setActive(selectedIndex >= 0 && !selected?.disabled ? selectedIndex : enabledIndex(-1, 1));
    setOpen(true);
  };

  const pick = (option: DropdownOption) => {
    if (option.disabled) return;
    closeList();
    anchorRef.current?.focus();
    if (option.value !== value) onChange(option.value);
  };

  const onKeyDown = (event: KeyboardEvent<HTMLElement>) => {
    switch (event.key) {
      case "ArrowDown":
      case "ArrowUp":
        event.preventDefault();
        if (!open) openList();
        else show(enabledIndex(active, event.key === "ArrowDown" ? 1 : -1));
        return;
      case "Home":
      case "End":
        if (!open) return;
        event.preventDefault();
        show(event.key === "Home" ? enabledIndex(-1, 1) : enabledIndex(shown.length, -1));
        return;
      case "Enter":
      case " ":
        event.preventDefault();
        if (!open) openList();
        else if (shown[active]) pick(shown[active]);
        return;
      case "Tab":
        if (open) closeList();
        return;
    }
    if (searchable || event.key.length !== 1 || !/\S/.test(event.key)) return;
    const now = Date.now();
    typed.current = { text: (now - typed.current.at < 700 ? typed.current.text : "") + event.key.toLowerCase(), at: now };
    const pool = open ? shown : options;
    const start = open ? active : options.findIndex((option) => option.value === value);
    for (let i = 1; i <= pool.length; i += 1) {
      const index = (start + i + pool.length) % pool.length;
      const option = pool[index]!;
      if (!option.disabled && optionText(option).toLowerCase().startsWith(typed.current.text)) {
        if (open) show(index);
        else if (option.value !== value) onChange(option.value);
        break;
      }
    }
  };

  const onSearchKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === "ArrowDown" || event.key === "ArrowUp" || event.key === "Home" || event.key === "End" || event.key === "Enter") {
      onKeyDown(event);
      return;
    }
    if (event.key === "Tab" && open) closeList();
  };

  const trigger = (
    <button
      ref={anchorRef}
      id={triggerId}
      type="button"
      role="combobox"
      disabled={disabled}
      aria-haspopup="listbox"
      aria-expanded={open}
      aria-controls={open ? listId : undefined}
      aria-activedescendant={open && active >= 0 ? `${listId}-${active}` : undefined}
      aria-labelledby={label ? `${labelId} ${valueId}` : undefined}
      aria-label={label ? undefined : ariaLabel}
      aria-invalid={error ? true : undefined}
      aria-describedby={error || helper ? messageId : undefined}
      aria-required={required || undefined}
      onClick={() => (open ? closeList() : openList())}
      onKeyDown={onKeyDown}
      className={cx(
        "cursor-pointer text-left outline-none disabled:cursor-not-allowed",
        isFilter
          ? cx(
              "flex h-10 w-full min-w-0 items-center gap-2 rounded-field border bg-card px-3 text-small transition-colors",
              "focus-visible:border-primary focus-visible:ring-3 focus-visible:ring-primary-ring",
              open ? "border-primary" : "border-line-input hover:border-ink-faint",
              disabled && "bg-disabled",
            )
          : cx(controlBoxClass({ error: !!error, disabled, open }), "focus-visible:border-primary focus-visible:ring-3 focus-visible:ring-primary-ring"),
      )}
    >
      {icon && <span className="shrink-0 text-ink-2">{icon}</span>}
      {isFilter && label && <span id={labelId} className="shrink-0 font-bold text-ink-muted">{label}</span>}
      <span
        id={valueId}
        className={cx(
          "min-w-0 flex-1 truncate",
          isFilter ? "font-extrabold" : "font-bold",
          // An empty value in a form reads as a placeholder even when an option stands for it.
          selected && (isFilter || selected.value !== "") ? "text-ink" : "text-ink-faint",
        )}
      >
        {selected?.label ?? placeholder ?? (isFilter ? "All" : "Select an option")}
      </span>
      <IconChevronDown size={16} className={cx("shrink-0 text-ink-2 transition-transform", open && "rotate-180")} />
    </button>
  );

  const list = open && (
    <MenuPanel ref={panelRef} id={listId} role="listbox" style={style} labelledBy={label ? labelId : undefined} label={label ? undefined : ariaLabel}>
      {searchable && (
        <div className="sticky top-0 z-10 bg-card px-1.5 pb-1.5 pt-0.5">
          <div className="flex h-10 items-center gap-2 rounded-field border border-line-input bg-card px-3">
            <IconSearch size={16} className="shrink-0 text-ink-2" />
            <input
              ref={searchRef}
              type="search"
              value={search}
              onChange={(event) => { setSearch(event.target.value); setActive(0); }}
              onKeyDown={onSearchKeyDown}
              placeholder="Search"
              aria-label="Search options"
              autoComplete="off"
              className="h-full min-w-0 flex-1 border-0 bg-transparent p-0 text-sm font-bold text-ink outline-none placeholder:font-bold placeholder:text-ink-faint [&::-webkit-search-cancel-button]:hidden"
            />
          </div>
        </div>
      )}
      {shown.length === 0 && <p className="px-3 py-2.5 text-small text-ink-muted">{query ? "No matches" : "No options"}</p>}
      {shown.map((option, index) => {
        const isSelected = index === selectedIndex;
        return (
          <div
            key={`${option.value}-${index}`}
            id={`${listId}-${index}`}
            role="option"
            aria-selected={isSelected}
            aria-disabled={option.disabled || undefined}
            onPointerMove={() => !option.disabled && setActive(index)}
            onClick={() => pick(option)}
            className={cx(menuRowClass({ active: index === active, selected: isSelected }), option.disabled && "cursor-not-allowed opacity-45")}
          >
            <span className="min-w-0 flex-1 truncate">{option.label}</span>
            {isSelected && <IconCheck size={16} className="shrink-0" />}
          </div>
        );
      })}
    </MenuPanel>
  );

  const hidden = name && <input type="hidden" name={name} value={value} />;

  if (isFilter) {
    return (
      <div className={cx("relative min-w-0 font-ui", className)}>
        {trigger}
        {list}
        {hidden}
      </div>
    );
  }

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
      {trigger}
      {list}
      {hidden}
    </FieldShell>
  );
}
