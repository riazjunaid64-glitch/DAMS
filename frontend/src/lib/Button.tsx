import type { ButtonHTMLAttributes } from "react";

type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: "primary" | "outline" | "ghost";
};

const base =
  "inline-flex items-center justify-center rounded-full px-6 py-2.5 text-sm font-semibold transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-amber-300 focus-visible:ring-offset-2 focus-visible:ring-offset-slate-900";

const variants: Record<NonNullable<ButtonProps["variant"]>, string> = {
  primary:
    "bg-amber-400 text-slate-900 shadow-[0_10px_30px_-12px_rgba(251,191,36,0.7)] hover:bg-amber-300",
  outline:
    "border border-slate-300/60 text-slate-100 hover:border-amber-300 hover:text-amber-200",
  ghost: "text-slate-100/80 hover:text-slate-100",
};

export default function Button({
  variant = "primary",
  className,
  ...props
}: ButtonProps) {
  return <button className={`${base} ${variants[variant]} ${className ?? ""}`} {...props} />;
}
