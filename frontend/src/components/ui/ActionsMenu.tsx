import { useId, useRef, useState, type KeyboardEvent, type ReactNode } from "react";
import { Button } from "./Button.tsx";
import { cx } from "./cx.ts";
import { IconChevronDown, IconMore } from "./icons.tsx";
import { MenuPanel } from "./MenuPanel.tsx";
import { menuRowClass } from "./styles.ts";
import { usePopover } from "./usePopover.ts";

export type ActionItem = {
  label: ReactNode;
  onSelect: () => void;
  icon?: ReactNode;
  /** Red, and placed after a divider at the end of the menu. */
  danger?: boolean;
  disabled?: boolean;
};

export type ActionsMenuProps = {
  items: readonly ActionItem[];
  /** `button`: navy "Actions ▾"; `dots`: round ⋯ button. */
  trigger?: "button" | "dots";
  /** Text of the `button` trigger. */
  label?: ReactNode;
  /** Accessible name of the ⋯ trigger. */
  "aria-label"?: string;
  /** Starts with the menu open (previews). */
  defaultOpen?: boolean;
  className?: string;
};

/**
 * A menu of actions. Danger items are moved to the end behind a divider. Closes on outside press,
 * Esc or picking an item; arrow keys, Home / End move between items.
 */
export function ActionsMenu({ items, trigger = "button", label = "Actions", "aria-label": ariaLabel = "More actions", defaultOpen = false, className }: ActionsMenuProps) {
  const menuId = useId();
  const [open, setOpen] = useState(defaultOpen);
  const itemRefs = useRef<(HTMLButtonElement | null)[]>([]);
  const { anchorRef, panelRef, style } = usePopover<HTMLButtonElement, HTMLDivElement>({
    open,
    onClose: () => setOpen(false),
    onEscape: () => closeMenu(),
  });
  const ordered = [...items.filter((item) => !item.danger), ...items.filter((item) => item.danger)];
  const firstDanger = ordered.findIndex((item) => item.danger);

  const focusItem = (from: number, step: 1 | -1) => {
    for (let i = 1; i <= ordered.length; i += 1) {
      const next = (from + step * i + ordered.length) % ordered.length;
      if (!ordered[next]?.disabled) { itemRefs.current[next]?.focus(); return; }
    }
  };

  const openMenu = (focusLast = false) => {
    setOpen(true);
    requestAnimationFrame(() => focusItem(focusLast ? 0 : -1, focusLast ? -1 : 1));
  };

  const closeMenu = () => {
    setOpen(false);
    anchorRef.current?.focus();
  };

  const onMenuKeyDown = (event: KeyboardEvent) => {
    const index = itemRefs.current.findIndex((el) => el === document.activeElement);
    if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      event.preventDefault();
      focusItem(index, event.key === "ArrowDown" ? 1 : -1);
    } else if (event.key === "Home" || event.key === "End") {
      event.preventDefault();
      focusItem(event.key === "Home" ? -1 : ordered.length, event.key === "Home" ? 1 : -1);
    } else if (event.key === "Tab") {
      setOpen(false);
    }
  };

  const triggerProps = {
    ref: anchorRef,
    "aria-haspopup": "menu" as const,
    "aria-expanded": open,
    "aria-controls": open ? menuId : undefined,
    onClick: () => (open ? setOpen(false) : openMenu()),
    onKeyDown: (event: KeyboardEvent) => {
      if (event.key === "ArrowDown" || event.key === "ArrowUp") {
        event.preventDefault();
        openMenu(event.key === "ArrowUp");
      }
    },
  };

  return (
    <div className={cx("inline-flex font-ui", className)}>
      {trigger === "button" ? (
        <Button {...triggerProps}>
          {label}
          <IconChevronDown size={16} className={cx("transition-transform", open && "rotate-180")} />
        </Button>
      ) : (
        <button
          {...triggerProps}
          type="button"
          aria-label={ariaLabel}
          className="flex size-11 cursor-pointer items-center justify-center rounded-full border border-line-input bg-card text-ink hover:bg-page focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary md:size-9"
        >
          <IconMore size={18} />
        </button>
      )}
      {open && (
        <MenuPanel ref={panelRef} id={menuId} role="menu" style={style} className="min-w-[200px]">
          <div onKeyDown={onMenuKeyDown}>
            {ordered.map((item, index) => (
              <div key={index}>
                {index === firstDanger && index > 0 && <hr aria-hidden="true" className="mx-1.5 my-1.5 border-0 border-t border-line-soft" />}
                <button
                  ref={(el) => { itemRefs.current[index] = el; }}
                  type="button"
                  role="menuitem"
                  tabIndex={-1}
                  disabled={item.disabled}
                  onClick={() => { closeMenu(); item.onSelect(); }}
                  className={cx(menuRowClass({ danger: item.danger }), "focus-visible:bg-page")}
                >
                  {item.icon && <span className="flex shrink-0">{item.icon}</span>}
                  {item.label}
                </button>
              </div>
            ))}
          </div>
        </MenuPanel>
      )}
    </div>
  );
}
