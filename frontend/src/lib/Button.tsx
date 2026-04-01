import type { ButtonHTMLAttributes } from "react";

type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: "primary" | "outline" | "ghost" | "danger";
  size?: "sm" | "md" | "lg";
};

const sizeClasses: Record<NonNullable<ButtonProps["size"]>, string> = {
  sm: "px-4 py-2 text-xs gap-1.5",
  md: "px-6 py-2.5 text-sm gap-2",
  lg: "px-8 py-3.5 text-sm gap-2.5",
};

const base =
  "inline-flex items-center justify-center font-semibold transition-all duration-200 ease-out focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-offset-2 focus-visible:ring-offset-[var(--bg-primary)] disabled:opacity-40 disabled:cursor-not-allowed disabled:pointer-events-none cursor-pointer";

const variants: Record<NonNullable<ButtonProps["variant"]>, string> = {
  primary:
    "rounded-xl bg-gradient-to-r from-[var(--btn-primary-start)] to-[var(--btn-primary-end)] text-[var(--btn-primary-text)] shadow-[var(--btn-primary-shadow)] hover:shadow-lg [background-size:200%] hover:[background-position:right_center] transition-all duration-300 active:scale-[0.97] focus-visible:ring-[var(--accent)]",
  outline:
    "rounded-xl border border-[var(--btn-outline-border)] text-[var(--btn-outline-text)] bg-transparent hover:bg-[var(--btn-outline-hover-bg)] hover:text-[var(--btn-outline-hover-text)] hover:border-[var(--border-hover)] active:scale-[0.97] focus-visible:ring-[var(--accent-glow)]",
  ghost:
    "rounded-xl text-[var(--btn-ghost-text)] hover:text-[var(--btn-ghost-hover-text)] hover:bg-[var(--btn-ghost-hover-bg)] active:scale-[0.97] focus-visible:ring-[var(--accent-glow)]",
  danger:
    "rounded-xl bg-gradient-to-r from-rose-500 to-rose-600 text-white shadow-[0_4px_16px_rgba(251,113,133,0.2)] hover:shadow-[0_6px_24px_rgba(251,113,133,0.3)] hover:from-rose-400 hover:to-rose-500 active:scale-[0.97] focus-visible:ring-rose-400",
};

export default function Button({
  variant = "primary",
  size = "md",
  className,
  ...props
}: ButtonProps) {
  return (
    <button
      className={`${base} ${sizeClasses[size]} ${variants[variant]} ${className ?? ""}`}
      {...props}
    />
  );
}
