import type { ButtonHTMLAttributes, ReactNode, Ref } from "react";
import { Link } from "react-router-dom";
import { cx } from "./cx.ts";

export type ButtonVariant = "primary" | "outline" | "danger" | "success" | "link" | "ghost" | "gold" | "light";
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
  /** Renders a link with the same look, for the website pages. */
  to?: string;
  /** External address with the same look. In-app routes stay on `to`. */
  href?: string;
  target?: string;
  rel?: string;
};

const variantClass: Record<ButtonVariant, string> = {
  primary: "bg-primary text-white hover:bg-primary-hover",
  outline: "border border-line-input bg-card text-primary hover:bg-page",
  danger: "border border-danger-line bg-card text-danger hover:bg-danger-soft",
  success: "bg-success text-white hover:bg-success-hover",
  link: "h-auto px-0 text-gold-text underline-offset-4 hover:underline",
  ghost: "text-ink-2 hover:bg-selected",
  /* Gold fill and a white outline, for a navy background. */
  gold: "bg-gold text-ink",
  light: "border border-white bg-transparent text-white",
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
  gold: "disabled:bg-disabled disabled:text-ink-faint",
  light: "disabled:border-white/40 disabled:bg-transparent disabled:text-white/40",
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
  to,
  href,
  target,
  rel,
  className,
  children,
  ...rest
}: ButtonProps) {
  const classes = cx(
    "inline-flex shrink-0 cursor-pointer items-center justify-center whitespace-nowrap rounded-field font-ui font-bold transition-colors",
    "focus-visible:outline-2 focus-visible:outline-offset-2",
    variant === "gold" || variant === "light" ? "focus-visible:outline-white" : "focus-visible:outline-primary",
    variantClass[variant],
    iconOnly ? "size-11 p-0" : variant !== "link" && sizeClass[size],
    variant === "link" && "min-h-11 gap-1.5 text-sm md:min-h-0",
    "disabled:cursor-not-allowed",
    disabledClass[variant],
    fullWidth && "w-full",
    (to || href) && "no-underline",
    className,
  );
  const content = (
    <>
      {loading ? <Spinner /> : icon}
      {!iconOnly && children}
    </>
  );
  if (to) {
    return <Link to={to} className={classes}>{content}</Link>;
  }
  if (href) {
    return <a href={href} target={target} rel={rel} className={classes}>{content}</a>;
  }
  return (
    <button
      ref={ref}
      type={type}
      disabled={disabled || loading}
      aria-busy={loading || undefined}
      className={classes}
      {...rest}
    >
      {content}
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
