import { cx } from "./cx.ts";

/* Class recipes shared by several components, kept apart so component files export only components. */

export const labelClass = "text-label font-bold uppercase tracking-[0.4px] text-ink-2";

/**
 * The box every input-like control draws: 48px, radius 10, grey border; navy border and a soft
 * ring on focus, red on error, grey fill when disabled. `focus-within` so a prefix / suffix box
 * lights up as one control.
 */
export function controlBoxClass({ error, disabled, open }: { error?: boolean; disabled?: boolean; open?: boolean }) {
  return cx(
    "flex h-12 w-full min-w-0 items-center gap-2 rounded-field border bg-card px-3.5 text-body text-ink transition-[border-color,box-shadow]",
    error
      ? "border-danger focus-within:ring-3 focus-within:ring-danger-soft"
      : open
        ? "border-primary ring-3 ring-primary-ring"
        : "border-line-input hover:border-ink-faint focus-within:border-primary focus-within:ring-3 focus-within:ring-primary-ring",
    disabled && "cursor-not-allowed border-line-input bg-disabled text-ink-2 hover:border-line-input",
  );
}

/** The bare input inside a control box. */
export const bareInputClass =
  "h-full min-w-0 flex-1 border-0 bg-transparent p-0 text-inherit outline-none placeholder:text-ink-faint disabled:cursor-not-allowed";

/** One 42px row inside a MenuPanel. */
export function menuRowClass({ active, selected, danger }: { active?: boolean; selected?: boolean; danger?: boolean }) {
  return cx(
    "flex min-h-[42px] w-full cursor-pointer items-center gap-2.5 rounded-lg px-3 text-left text-sm font-bold outline-none transition-colors",
    "disabled:cursor-not-allowed disabled:opacity-45",
    danger ? "text-danger" : selected ? "text-primary" : "text-ink",
    selected ? "bg-selected" : active ? "bg-page" : "hover:bg-page",
  );
}
