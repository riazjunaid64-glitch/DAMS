import type { AnchorHTMLAttributes } from "react";

type NavLinkProps = AnchorHTMLAttributes<HTMLAnchorElement>;

export default function NavLink({ className, ...props }: NavLinkProps) {
  return (
    <a
      className={`text-sm font-medium text-slate-200/80 transition hover:text-amber-200 ${className ?? ""}`}
      {...props}
    />
  );
}
