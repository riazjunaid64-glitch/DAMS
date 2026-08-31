import {
  Children,
  isValidElement,
  useEffect,
  useEffectEvent,
  useId,
  useLayoutEffect,
  useRef,
  useState,
} from "react";
import type {
  ChangeEvent,
  KeyboardEvent,
  OptionHTMLAttributes,
  ReactElement,
  ReactNode,
  SelectHTMLAttributes,
} from "react";
import { createPortal } from "react-dom";

type AppSelectProps = Omit<SelectHTMLAttributes<HTMLSelectElement>, "multiple" | "size">;

type SelectOption = {
  value: string;
  label: ReactNode;
  disabled: boolean;
};

function optionValue(option: ReactElement<OptionHTMLAttributes<HTMLOptionElement>>) {
  if (option.props.value != null) return String(option.props.value);
  return Children.toArray(option.props.children).join("");
}

function readOptions(children: ReactNode): SelectOption[] {
  return Children.toArray(children).flatMap((child) => {
    if (!isValidElement<OptionHTMLAttributes<HTMLOptionElement>>(child) || child.type !== "option") return [];
    return [{ value: optionValue(child), label: child.props.children, disabled: !!child.props.disabled }];
  });
}

function initialValue(value: AppSelectProps["value"], defaultValue: AppSelectProps["defaultValue"]) {
  const candidate = value ?? defaultValue ?? "";
  return Array.isArray(candidate) ? String(candidate[0] ?? "") : String(candidate);
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
  "aria-label": ariaLabel,
  "aria-describedby": ariaDescribedBy,
  ...nativeProps
}: AppSelectProps) {
  const generatedId = useId().replace(/:/g, "");
  const selectId = id ?? `app-select-${generatedId}`;
  const listboxId = `${selectId}-listbox`;
  const selectRef = useRef<HTMLSelectElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const [open, setOpen] = useState(false);
  const [currentValue, setCurrentValue] = useState(() => initialValue(value, defaultValue));
  const [activeIndex, setActiveIndex] = useState(-1);
  const [menuStyle, setMenuStyle] = useState<{
    left?: number;
    right?: number;
    top: number;
    minWidth: number;
    maxWidth: number;
    maxHeight: number;
  }>({ left: 0, top: 0, minWidth: 0, maxWidth: 320, maxHeight: 280 });
  const options = readOptions(children);
  const controlledValue = value == null ? currentValue : String(value);
  const selectedIndex = options.findIndex((option) => option.value === controlledValue);
  const selectedOption = options[selectedIndex] ?? options[0];

  const positionMenu = useEffectEvent(() => {
    const trigger = triggerRef.current;
    if (!trigger) return;
    const rect = trigger.getBoundingClientRect();
    const gap = 4;
    const viewportPadding = 10;
    const preferredHeight = Math.min(280, options.length * 42 + 10);
    const below = window.innerHeight - rect.bottom - viewportPadding;
    const above = rect.top - viewportPadding;
    const openAbove = below < Math.min(preferredHeight, 180) && above > below;
    const alignRight = rect.left + rect.width / 2 > window.innerWidth / 2;
    const maxHeight = Math.max(120, Math.min(preferredHeight, openAbove ? above - gap : below - gap));
    setMenuStyle({
      left: alignRight ? undefined : Math.max(viewportPadding, rect.left),
      right: alignRight ? Math.max(viewportPadding, window.innerWidth - rect.right) : undefined,
      top: openAbove ? Math.max(viewportPadding, rect.top - maxHeight - gap) : rect.bottom + gap,
      minWidth: rect.width,
      maxWidth: alignRight
        ? Math.max(rect.width, rect.right - viewportPadding)
        : Math.max(rect.width, window.innerWidth - rect.left - viewportPadding),
      maxHeight,
    });
  });

  useLayoutEffect(() => {
    if (!open) return;
    positionMenu();
  }, [open, options.length]);

  useEffect(() => {
    if (!open) return;
    const closeOnOutsidePress = (event: PointerEvent) => {
      const target = event.target as Node;
      if (triggerRef.current?.contains(target) || document.getElementById(listboxId)?.contains(target)) return;
      setOpen(false);
    };
    const reposition = () => positionMenu();
    document.addEventListener("pointerdown", closeOnOutsidePress);
    window.addEventListener("resize", reposition);
    window.addEventListener("scroll", reposition, true);
    return () => {
      document.removeEventListener("pointerdown", closeOnOutsidePress);
      window.removeEventListener("resize", reposition);
      window.removeEventListener("scroll", reposition, true);
    };
  }, [open, listboxId]);

  const openMenu = () => {
    const selected = selectedIndex >= 0 && !options[selectedIndex]?.disabled
      ? selectedIndex
      : options.findIndex((option) => !option.disabled);
    setActiveIndex(selected);
    setOpen(true);
  };

  const commitValue = (nextValue: string) => {
    const select = selectRef.current;
    if (!select) return;
    const setter = Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, "value")?.set;
    setter?.call(select, nextValue);
    select.dispatchEvent(new Event("change", { bubbles: true }));
    setCurrentValue(nextValue);
    setOpen(false);
    triggerRef.current?.focus();
  };

  const moveActive = (direction: 1 | -1) => {
    if (!options.length) return;
    let next = activeIndex;
    for (let index = 0; index < options.length; index += 1) {
      next = (next + direction + options.length) % options.length;
      if (!options[next]?.disabled) {
        setActiveIndex(next);
        document.getElementById(`${listboxId}-option-${next}`)?.scrollIntoView({ block: "nearest" });
        return;
      }
    }
  };

  const handleKeyDown = (event: KeyboardEvent<HTMLButtonElement>) => {
    if (disabled) return;
    if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      event.preventDefault();
      if (!open) openMenu();
      else moveActive(event.key === "ArrowDown" ? 1 : -1);
      return;
    }
    if (event.key === "Home" && open) {
      event.preventDefault();
      setActiveIndex(options.findIndex((option) => !option.disabled));
      return;
    }
    if (event.key === "End" && open) {
      event.preventDefault();
      for (let index = options.length - 1; index >= 0; index -= 1) {
        if (!options[index]?.disabled) { setActiveIndex(index); break; }
      }
      return;
    }
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      if (!open) openMenu();
      else if (activeIndex >= 0 && !options[activeIndex]?.disabled) commitValue(options[activeIndex]!.value);
      return;
    }
    if (event.key === "Escape" && open) {
      event.preventDefault();
      setOpen(false);
    }
  };

  const handleNativeChange = (event: ChangeEvent<HTMLSelectElement>) => {
    setCurrentValue(event.target.value);
    onChange?.(event);
  };

  return (
    <span className={`app-select ${open ? "app-select--open" : ""} ${disabled ? "app-select--disabled" : ""} ${className ?? ""}`}>
      <select
        {...nativeProps}
        ref={selectRef}
        id={selectId}
        value={value}
        defaultValue={value == null ? defaultValue : undefined}
        disabled={disabled}
        required={required}
        aria-hidden="true"
        tabIndex={-1}
        className="app-select__native"
        onChange={handleNativeChange}
        onFocus={() => { if (!disabled) triggerRef.current?.focus(); }}
      >
        {children}
      </select>
      <button
        ref={triggerRef}
        type="button"
        className="app-select__trigger"
        disabled={disabled}
        role="combobox"
        aria-label={ariaLabel}
        aria-describedby={ariaDescribedBy}
        aria-controls={listboxId}
        aria-expanded={open}
        aria-haspopup="listbox"
        aria-activedescendant={open && activeIndex >= 0 ? `${listboxId}-option-${activeIndex}` : undefined}
        onClick={() => { if (open) setOpen(false); else openMenu(); }}
        onKeyDown={handleKeyDown}
      >
        <span className={`app-select__value ${controlledValue === "" ? "app-select__value--placeholder" : ""}`}>
          {selectedOption?.label ?? "Select…"}
        </span>
        <svg className="app-select__chevron" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <path d="m6 9 6 6 6-6" />
        </svg>
      </button>
      {open && createPortal(
        <div
          id={listboxId}
          role="listbox"
          aria-label={ariaLabel}
          className="app-select__menu"
          style={{
            left: menuStyle.left,
            right: menuStyle.right,
            top: menuStyle.top,
            minWidth: menuStyle.minWidth,
            maxWidth: menuStyle.maxWidth,
            maxHeight: menuStyle.maxHeight,
          }}
        >
          {options.map((option, index) => {
            const selected = option.value === controlledValue;
            return (
              <button
                key={`${option.value}-${index}`}
                id={`${listboxId}-option-${index}`}
                type="button"
                role="option"
                aria-selected={selected}
                disabled={option.disabled}
                className={`app-select__option ${selected ? "app-select__option--selected" : ""} ${activeIndex === index ? "app-select__option--active" : ""}`}
                onPointerMove={() => { if (!option.disabled) setActiveIndex(index); }}
                onClick={() => commitValue(option.value)}
              >
                <span>{option.label}</span>
                {selected && (
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                    <path d="m5 12 4 4L19 6" />
                  </svg>
                )}
              </button>
            );
          })}
        </div>,
        document.body,
      )}
    </span>
  );
}
