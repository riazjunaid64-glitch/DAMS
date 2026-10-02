import type {
  AnchorHTMLAttributes,
  ButtonHTMLAttributes,
  FocusEventHandler,
  KeyboardEventHandler,
  MouseEventHandler,
  ReactNode,
  Ref,
} from "react";
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
  /**
   * Shows a spinner and blocks the click. On a link, `disabled` and `loading`
   * also block navigation.
   */
  loading?: boolean;
  /** Renders a link with the same look, for the website pages. */
  to?: string;
  /** External address with the same look. In-app routes stay on `to`. */
  href?: string;
  target?: string;
  rel?: string;
} & {
  [key: `data-${string}`]: string | number | boolean | undefined;
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

/* Filled buttons turn grey; boxed outline ones keep their white box and fade their text.
   `disabled:` covers a real button. `aria-disabled:` covers a link, which cannot use the attribute. */
const disabledClass: Record<ButtonVariant, string> = {
  primary: "disabled:bg-disabled disabled:text-ink-faint aria-disabled:bg-disabled aria-disabled:text-ink-faint aria-disabled:hover:bg-disabled",
  success: "disabled:bg-disabled disabled:text-ink-faint aria-disabled:bg-disabled aria-disabled:text-ink-faint aria-disabled:hover:bg-disabled",
  outline: "disabled:bg-card disabled:border-line disabled:text-ink-faint aria-disabled:bg-card aria-disabled:border-line aria-disabled:text-ink-faint aria-disabled:hover:bg-card",
  danger: "disabled:bg-card disabled:border-line disabled:text-ink-faint aria-disabled:bg-card aria-disabled:border-line aria-disabled:text-ink-faint aria-disabled:hover:bg-card",
  ghost: "disabled:bg-transparent disabled:text-ink-faint aria-disabled:bg-transparent aria-disabled:text-ink-faint aria-disabled:hover:bg-transparent",
  link: "disabled:text-ink-faint disabled:no-underline aria-disabled:text-ink-faint aria-disabled:no-underline aria-disabled:hover:no-underline",
  gold: "disabled:bg-disabled disabled:text-ink-faint aria-disabled:bg-disabled aria-disabled:text-ink-faint aria-disabled:hover:bg-disabled",
  light: "disabled:border-white/40 disabled:bg-transparent disabled:text-white/40 aria-disabled:border-white/40 aria-disabled:bg-transparent aria-disabled:text-white/40 aria-disabled:hover:bg-transparent",
};

/* These belong on a <button> and are not forwarded to a link. */
const BUTTON_ONLY = ["form", "formAction", "formEncType", "formMethod", "formNoValidate", "formTarget", "value"] as const;

function linkDomProps(rest: object): Omit<AnchorHTMLAttributes<HTMLAnchorElement>, "href"> {
  const next: Record<string, unknown> = { ...rest };
  for (const key of BUTTON_ONLY) delete next[key];
  return next as Omit<AnchorHTMLAttributes<HTMLAnchorElement>, "href">;
}

function blockEvent(event: { preventDefault(): void; stopPropagation(): void }) {
  event.preventDefault();
  event.stopPropagation();
}

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
  onClick,
  onAuxClick,
  onMouseDown,
  onKeyDown,
  onFocus,
  tabIndex,
  ...rest
}: ButtonProps) {
  const inactive = Boolean(disabled || loading);
  const classes = cx(
    "inline-flex shrink-0 cursor-pointer items-center justify-center whitespace-nowrap rounded-field font-ui font-bold transition-colors",
    "focus-visible:outline-2 focus-visible:outline-offset-2",
    variant === "gold" || variant === "light" ? "focus-visible:outline-white" : "focus-visible:outline-primary",
    variantClass[variant],
    iconOnly ? "size-11 p-0" : variant !== "link" && sizeClass[size],
    variant === "link" && "min-h-11 gap-1.5 text-sm md:min-h-0",
    "disabled:cursor-not-allowed aria-disabled:cursor-not-allowed",
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
  if (to || href) {
    const anchorDom = linkDomProps(rest);
    const anchorRef = ref as Ref<HTMLAnchorElement>;
    const anchorClick = onClick as MouseEventHandler<HTMLAnchorElement> | undefined;
    const anchorAuxClick = onAuxClick as MouseEventHandler<HTMLAnchorElement> | undefined;
    const anchorMouseDown = onMouseDown as MouseEventHandler<HTMLAnchorElement> | undefined;
    const anchorKeyDown = onKeyDown as KeyboardEventHandler<HTMLAnchorElement> | undefined;
    const anchorFocus = onFocus as FocusEventHandler<HTMLAnchorElement> | undefined;
    /* A link cannot be `disabled`. Drop the address so it cannot be opened, mark it, and keep it out of the tab order. */
    if (inactive) {
      return (
        <a
          {...anchorDom}
          ref={anchorRef}
          role="link"
          aria-disabled="true"
          aria-busy={loading || undefined}
          tabIndex={-1}
          className={classes}
          onMouseDown={(event) => {
            event.preventDefault();
          }}
          onClick={blockEvent}
          onAuxClick={blockEvent}
          onKeyDown={(event) => {
            if (event.key === "Enter" || event.key === " ") blockEvent(event);
          }}
          onFocus={(event) => {
            event.currentTarget.blur();
          }}
        >
          {content}
        </a>
      );
    }
    if (to) {
      return (
        <Link
          {...anchorDom}
          ref={anchorRef}
          to={to}
          target={target}
          rel={rel}
          tabIndex={tabIndex}
          className={classes}
          onClick={anchorClick}
          onAuxClick={anchorAuxClick}
          onMouseDown={anchorMouseDown}
          onKeyDown={anchorKeyDown}
          onFocus={anchorFocus}
        >
          {content}
        </Link>
      );
    }
    return (
      <a
        {...anchorDom}
        ref={anchorRef}
        href={href}
        target={target}
        rel={rel}
        tabIndex={tabIndex}
        className={classes}
        onClick={anchorClick}
        onAuxClick={anchorAuxClick}
        onMouseDown={anchorMouseDown}
        onKeyDown={anchorKeyDown}
        onFocus={anchorFocus}
      >
        {content}
      </a>
    );
  }
  return (
    <button
      {...rest}
      ref={ref}
      type={type}
      disabled={inactive}
      aria-busy={loading || undefined}
      tabIndex={tabIndex}
      className={classes}
      onClick={onClick}
      onAuxClick={onAuxClick}
      onMouseDown={onMouseDown}
      onKeyDown={onKeyDown}
      onFocus={onFocus}
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
