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
  "inline-flex items-center justify-center font-semibold transition-all duration-200 ease-out focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-offset-2 focus-visible:ring-offset-[#0a0a0f] disabled:opacity-40 disabled:cursor-not-allowed disabled:pointer-events-none cursor-pointer";

const variants: Record<NonNullable<ButtonProps["variant"]>, string> = {
  primary:
    "rounded-xl bg-gradient-to-r from-indigo-500 to-indigo-600 text-white shadow-[0_4px_16px_rgba(99,102,241,0.3)] hover:shadow-[0_6px_24px_rgba(99,102,241,0.4)] hover:from-indigo-400 hover:to-indigo-500 active:scale-[0.97] focus-visible:ring-indigo-400",
  outline:
    "rounded-xl border border-white/[0.08] text-[#a1a1b5] bg-white/[0.02] hover:bg-white/[0.06] hover:text-white hover:border-white/[0.15] active:scale-[0.97] focus-visible:ring-white/30",
  ghost:
    "rounded-xl text-[#a1a1b5] hover:text-white hover:bg-white/[0.06] active:scale-[0.97] focus-visible:ring-white/30",
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
