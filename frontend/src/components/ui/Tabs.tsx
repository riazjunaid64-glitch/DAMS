import { useRef, type KeyboardEvent, type ReactNode } from "react";
import { cx } from "./cx.ts";
import { Dropdown } from "./Dropdown.tsx";
import { useIsPhone } from "./useMediaQuery.ts";

export type TabItem = {
  id: string;
  label: string;
  /** Grey count after the label ("Units 60"). */
  count?: ReactNode;
  icon?: ReactNode;
  disabled?: boolean;
};

export type TabsProps = {
  items: readonly TabItem[];
  value: string;
  onChange: (id: string) => void;
  "aria-label"?: string;
  /** On phone, this many tabs or more become a dropdown. */
  phoneDropdownFrom?: number;
  /** The grey word inside that phone dropdown ("Show", or "Section" on Finance settings). */
  phoneDropdownLabel?: string;
  className?: string;
};

/**
 * Segmented tabs: grey track, white active pill. On phone they stretch full width, and with four
 * or more tabs they turn into a dropdown ("Show" by default) so nothing scrolls sideways.
 */
export function Tabs({ items, value, onChange, "aria-label": ariaLabel = "Sections", phoneDropdownFrom = 4, phoneDropdownLabel = "Show", className }: TabsProps) {
  const isPhone = useIsPhone();
  const refs = useRef<(HTMLButtonElement | null)[]>([]);

  if (isPhone && items.length >= phoneDropdownFrom) {
    return (
      <Dropdown
        size="filter"
        label={phoneDropdownLabel}
        aria-label={ariaLabel}
        value={value}
        onChange={onChange}
        options={items.map((item) => ({ value: item.id, label: item.label, disabled: item.disabled }))}
        className={cx("w-full [&>button]:h-12 [&>button]:text-body", className)}
      />
    );
  }

  const activeIndex = items.findIndex((item) => item.id === value);
  const tabStop = activeIndex >= 0 ? activeIndex : items.findIndex((item) => !item.disabled);

  const onKeyDown = (event: KeyboardEvent, index: number) => {
    const step = event.key === "ArrowRight" ? 1 : event.key === "ArrowLeft" ? -1 : 0;
    if (!step) return;
    event.preventDefault();
    for (let i = 1; i <= items.length; i += 1) {
      const next = (index + step * i + items.length) % items.length;
      if (!items[next]?.disabled) {
        refs.current[next]?.focus();
        onChange(items[next]!.id);
        return;
      }
    }
  };

  return (
    <div role="tablist" aria-label={ariaLabel} className={cx("flex max-w-full gap-1 overflow-x-auto rounded-field bg-track p-1 font-ui md:inline-flex", className)}>
      {items.map((item, index) => {
        const active = item.id === value;
        return (
          <button
            key={item.id}
            ref={(el) => { refs.current[index] = el; }}
            type="button"
            role="tab"
            id={`tab-${item.id}`}
            aria-selected={active}
            tabIndex={index === tabStop ? 0 : -1}
            disabled={item.disabled}
            onClick={() => onChange(item.id)}
            onKeyDown={(event) => onKeyDown(event, index)}
            className={cx(
              "flex h-11 flex-1 cursor-pointer items-center justify-center gap-1.5 whitespace-nowrap rounded-lg px-2 text-sm font-bold transition-colors md:h-9 md:flex-none md:px-4",
              "focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-primary disabled:cursor-not-allowed disabled:opacity-45",
              active ? "bg-card text-ink shadow-sm" : "text-ink-2 hover:text-ink",
            )}
          >
            {item.icon}
            {item.label}
            {item.count != null && <span className="font-extrabold text-ink-faint">{item.count}</span>}
          </button>
        );
      })}
    </div>
  );
}
