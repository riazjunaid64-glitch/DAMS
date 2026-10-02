import { useId, type ReactNode } from "react";
import { Button, type ButtonVariant } from "./Button.tsx";
import { cx } from "./cx.ts";
import { IconClose } from "./icons.tsx";
import { Overlay } from "./Overlay.tsx";

export type DialogAction = {
  label: ReactNode;
  onClick?: () => void;
  variant?: ButtonVariant;
  disabled?: boolean;
  loading?: boolean;
  /** Makes the button submit the form with this id (the body can hold a <form id=…>). */
  form?: string;
};

export type DialogLayout = "centered" | "sheet" | "fullscreen";

type DialogPanelProps = {
  open: boolean;
  onClose: () => void;
  title: ReactNode;
  children: ReactNode;
  layout: DialogLayout;
  /** Max width of a centred panel, in px. */
  width?: number;
  /** Stacked full-width footer buttons, split 50/50 (phone). */
  splitFooter: boolean;
  primaryAction?: DialogAction;
  /** Label of the outline button left of the primary one; it runs `onSecondary` (or closes). */
  secondaryLabel?: ReactNode;
  onSecondary?: () => void;
  /** Replaces the generated footer. `null` removes it. */
  footer?: ReactNode;
  /** Sits on the left of Cancel and the primary button. On a phone it stacks under them. */
  footerLeading?: ReactNode;
  /** While true, Esc and backdrop taps are ignored (e.g. mid-save). */
  busy?: boolean;
};

const layoutClass: Record<DialogLayout, string> = {
  centered: "animate-pop-in max-h-[calc(100dvh-32px)] w-full rounded-popup shadow-popup",
  sheet: "animate-sheet-up max-h-[88dvh] w-full rounded-t-popup pb-[env(safe-area-inset-bottom)] shadow-popup",
  fullscreen: "h-dvh w-full",
};

/**
 * The one popup frame: header (title + ×), scrolling body, footer (Cancel + primary). Laid out as
 * a centred popup, a phone bottom sheet, or a full-screen phone form. Modal, ConfirmDialog and
 * BottomSheet are this frame with different defaults.
 */
export function DialogPanel({
  open,
  onClose,
  title,
  children,
  layout,
  width = 520,
  splitFooter,
  primaryAction,
  secondaryLabel = "Cancel",
  onSecondary,
  footer,
  footerLeading,
  busy = false,
}: DialogPanelProps) {
  const titleId = useId();
  const close = busy ? () => {} : onClose;
  const buttonSize = splitFooter ? "lg" : "md";
  const secondaryButton = secondaryLabel && (
    <Button variant="outline" size={buttonSize} onClick={onSecondary ?? onClose} disabled={busy}>
      {secondaryLabel}
    </Button>
  );
  const primaryButton = primaryAction && (
    <Button
      variant={primaryAction.variant ?? "primary"}
      size={buttonSize}
      onClick={primaryAction.onClick}
      disabled={primaryAction.disabled}
      loading={primaryAction.loading}
      type={primaryAction.form ? "submit" : "button"}
      form={primaryAction.form}
    >
      {primaryAction.label}
    </Button>
  );
  const generatedFooter = (primaryAction || secondaryLabel || footerLeading) && (
    footerLeading ? (
      <div className={cx("gap-3", splitFooter ? "flex flex-col" : "flex items-center justify-between")}>
        {splitFooter ? (
          <>
            {primaryButton}
            {secondaryButton}
            <div className="[&_button]:w-full">{footerLeading}</div>
          </>
        ) : (
          <>
            <div>{footerLeading}</div>
            <div className="flex gap-3">
              {secondaryButton}
              {primaryButton}
            </div>
          </>
        )}
      </div>
    ) : (
      <div className={cx("gap-3", splitFooter ? (primaryAction && secondaryLabel ? "grid grid-cols-2" : "grid") : "flex justify-end")}>
        {secondaryButton}
        {primaryButton}
      </div>
    )
  );
  const footerContent = footer === undefined ? generatedFooter : footer;

  return (
    <Overlay open={open} onClose={close} closeOnBackdrop={!busy} placement={layout === "sheet" ? "bottom" : layout === "fullscreen" ? "fullscreen" : "center"}>
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        style={layout === "centered" ? { maxWidth: width } : undefined}
        className={cx("relative z-10 flex flex-col overflow-hidden bg-card font-ui text-ink", layoutClass[layout])}
      >
        {layout === "sheet" && <span aria-hidden="true" className="mx-auto mt-2.5 h-1.5 w-10 shrink-0 rounded-full bg-line-input" />}
        <header className={cx("flex shrink-0 items-center justify-between gap-3 px-5", layout === "sheet" ? "py-3" : "border-b border-line-soft py-4")}>
          <h2 id={titleId} className="m-0 min-w-0 text-section font-extrabold text-ink">{title}</h2>
          <button
            type="button"
            aria-label="Close"
            onClick={close}
            disabled={busy}
            className="-mr-2 flex size-11 shrink-0 cursor-pointer items-center justify-center rounded-lg text-ink-2 hover:bg-page disabled:cursor-not-allowed disabled:opacity-40 focus-visible:outline-2 focus-visible:outline-primary"
          >
            <IconClose size={20} />
          </button>
        </header>
        <div className="min-h-0 flex-1 overflow-y-auto overscroll-contain px-5 py-4 text-body text-ink-2">{children}</div>
        {footerContent && <footer className="shrink-0 border-t border-line-soft px-5 py-3.5">{footerContent}</footer>}
      </div>
    </Overlay>
  );
}
