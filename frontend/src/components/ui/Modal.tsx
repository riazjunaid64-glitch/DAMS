import type { ReactNode } from "react";
import { DialogPanel, type DialogAction } from "./DialogPanel.tsx";
import { useIsPhone } from "./useMediaQuery.ts";

const widths = { sm: 400, md: 520, lg: 620 } as const;

export type ModalProps = {
  open: boolean;
  onClose: () => void;
  title: ReactNode;
  children: ReactNode;
  /** Desktop width: sm 400 · md 520 · lg 620. */
  size?: keyof typeof widths;
  /**
   * How it shows on a phone: `popup` — a small centred popup for short forms (Assign);
   * `fullscreen` — a full-screen form with Cancel / Save at the bottom (Edit lead, Add unit).
   */
  phoneLayout?: "popup" | "fullscreen";
  /** The primary footer button (Save). Omit for a footer with only Cancel. */
  primaryAction?: DialogAction;
  /** The outline button's label; `null` hides it. */
  cancelLabel?: ReactNode;
  /** Replaces the generated footer; `null` removes it. */
  footer?: ReactNode;
  /** Sits on the left of Cancel and the primary button. On a phone it stacks under them. */
  footerLeading?: ReactNode;
  /** Blocks closing while work is in flight. */
  busy?: boolean;
};

/** Desktop centred popup; on phone a small popup or a full-screen form. Esc and backdrop close it. */
export function Modal({ size = "md", phoneLayout = "popup", cancelLabel = "Cancel", ...rest }: ModalProps) {
  const isPhone = useIsPhone();
  return (
    <DialogPanel
      {...rest}
      layout={isPhone && phoneLayout === "fullscreen" ? "fullscreen" : "centered"}
      width={widths[size]}
      splitFooter={isPhone}
      secondaryLabel={cancelLabel}
    />
  );
}

export type ConfirmDialogProps = {
  open: boolean;
  onClose: () => void;
  onConfirm: () => void;
  title?: ReactNode;
  message: ReactNode;
  /** Optional content under the message — a field the confirmation needs, such as a required reason. */
  children?: ReactNode;
  confirmLabel?: ReactNode;
  /** Red confirm button for destructive actions. */
  danger?: boolean;
  loading?: boolean;
  /** Greys the confirm button, e.g. while a required field in `children` is empty. */
  confirmDisabled?: boolean;
};

/** Small "Are you sure?" popup, optionally holding a field under the message. */
export function ConfirmDialog({ open, onClose, onConfirm, title = "Are you sure?", message, children, confirmLabel = "Confirm", danger = false, loading = false, confirmDisabled = false }: ConfirmDialogProps) {
  return (
    <Modal
      open={open}
      onClose={onClose}
      title={title}
      size="sm"
      busy={loading}
      primaryAction={{ label: confirmLabel, onClick: onConfirm, variant: danger ? "danger" : "primary", loading, disabled: confirmDisabled }}
    >
      <p className="m-0">{message}</p>
      {children && <div className="mt-3">{children}</div>}
    </Modal>
  );
}
