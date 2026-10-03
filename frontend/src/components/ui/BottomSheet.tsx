import type { ReactNode } from "react";
import { DialogPanel } from "./DialogPanel.tsx";

export type BottomSheetProps = {
  open: boolean;
  onClose: () => void;
  title: ReactNode;
  children: ReactNode;
  /** Shows Reset / Apply buttons, split 50/50. */
  onApply?: () => void;
  onReset?: () => void;
  applyLabel?: ReactNode;
  resetLabel?: ReactNode;
  /** Replaces the generated footer; `null` removes it. */
  footer?: ReactNode;
};

/** Phone panel that slides up from the bottom (filters, quick choices). Backdrop tap closes it. */
export function BottomSheet({ onApply, onReset, applyLabel = "Apply", resetLabel = "Reset", ...rest }: BottomSheetProps) {
  return (
    <DialogPanel
      {...rest}
      layout="sheet"
      splitFooter
      primaryAction={onApply ? { label: applyLabel, onClick: onApply } : undefined}
      secondaryLabel={onReset ? resetLabel : null}
      onSecondary={onReset}
    />
  );
}

export type SheetActionProps = { icon: ReactNode; label: ReactNode; disabled?: boolean; onSelect: () => void };

/** One row of a phone "More actions" sheet: icon and label, the whole row is the tap target. Put the rows in a `<ul>`. */
export function SheetAction({ icon, label, disabled, onSelect }: SheetActionProps) {
  return (
    <li>
      <button
        type="button"
        disabled={disabled}
        onClick={onSelect}
        className="flex h-12 w-full cursor-pointer items-center gap-3 border-0 bg-transparent px-0 text-left text-body font-extrabold text-ink focus-visible:outline-2 focus-visible:outline-primary disabled:cursor-not-allowed disabled:text-ink-faint"
      >
        <span className="flex text-ink-2">{icon}</span>
        {label}
      </button>
    </li>
  );
}
