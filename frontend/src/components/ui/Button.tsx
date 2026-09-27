import type { ButtonHTMLAttributes, ReactNode, Ref } from "react";
import { cx } from "./cx.ts";

export type ButtonVariant = "primary" | "outline" | "danger" | "success" | "link" | "ghost";
export type ButtonSize = "sm" | "md" | "lg";

export type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  ref?: Ref<HTMLButtonElement>;
  variant?: ButtonVariant;
  /** sm 34px · md 40px · lg 48px (phone). */
  size?: ButtonSize;
  /** Shown before the text. */
  icon?: ReactNode;
  /** A 44×44 square holding only `icon`; give it an aria-label. */
  iconOnly?: boolean;
  fullWidth?: boolean;
  /** Shows a spinner and blocks clicks until the work finishes. */
  loading?: boolean;
};

const variantClass: Record<ButtonVariant, string> = {
  primary: "bg-primary text-white hover:bg-primary-hover",
  outline: "border border-line-input bg-card text-primary hover:bg-page",
  danger: "border border-danger-line bg-card text-danger hover:bg-danger-soft",
  success: "bg-success text-white hover:bg-success-hover",
  link: "h-auto px-0 text-gold-text underline-offset-4 hover:underline",
  ghost: "text-ink-2 hover:bg-selected",
};

const sizeClass: Record<ButtonSize, string> = {
  // Phones get at least a 44px tap target; the design's 34 / 40 heights apply from md up.
  sm: "h-11 gap-1.5 px-3.5 text-[13px] md:h-[34px]",
  md: "h-11 gap-2 px-4 text-sm md:h-10",
  lg: "h-12 gap-2 rounded-btn-phone px-5 text-[15px]",
};

/* Filled buttons turn grey; boxed outline ones keep their white box and fade their text. */
const disabledClass: Record<ButtonVariant, string> = {
  primary: "disabled:bg-disabled disabled:text-ink-faint",
  success: "disabled:bg-disabled disabled:text-ink-faint",
  outline: "disabled:bg-card disabled:border-line disabled:text-ink-faint",
  danger: "disabled:bg-card disabled:border-line disabled:text-ink-faint",
  ghost: "disabled:bg-transparent disabled:text-ink-faint",
  link: "disabled:text-ink-faint disabled:no-underline",
};

export function Button({
  ref,
  variant = "primary",
  size = "md",
  icon,
  iconOnly = false,
  fullWidth = false,
  loading = false,
  disabled,
  type = "button",
  className,
  children,
  ...rest
}: ButtonProps) {
  return (
    <button
      ref={ref}
      type={type}
      disabled={disabled || loading}
      aria-busy={loading || undefined}
      className={cx(
        "inline-flex shrink-0 cursor-pointer items-center justify-center whitespace-nowrap rounded-field font-ui font-bold transition-colors",
        "focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary",
        variantClass[variant],
        iconOnly ? "size-11 p-0" : variant !== "link" && sizeClass[size],
        variant === "link" && "min-h-11 gap-1.5 text-sm md:min-h-0",
        "disabled:cursor-not-allowed",
        disabledClass[variant],
        fullWidth && "w-full",
        className,
      )}
      {...rest}
    >
      {loading ? <Spinner /> : icon}
      {!iconOnly && children}
    </button>
  );
}

export function Spinner({ className }: { className?: string }) {
  return (
    <span
      aria-hidden="true"
      className={cx("inline-block size-4 animate-spin rounded-full border-2 border-current border-r-transparent", className)}
    />
  );
}
