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
