import type { ReactNode } from "react";
import { Button } from "../ui/Button.tsx";
import { cx } from "../ui/cx.ts";
import { hoverLiftClass } from "./motion.ts";

/**
 * Soft-gold band with one navy action. About uses it; Contact can reuse the same block.
 * The button lifts on desktop hover. The page around it owns the fade-up.
 */
export function CallToActionBand({
  title,
  message,
  action = "Contact us",
  to = "/contact",
}: {
  title: ReactNode;
  message: ReactNode;
  action?: ReactNode;
  to?: string;
}) {
  return (
    <div className="flex flex-col gap-4 rounded-popup bg-gold-soft px-5 py-6 md:flex-row md:items-center md:justify-between md:gap-8 md:px-8 md:py-7">
      <div className="min-w-0">
        <h2 className="m-0 text-section font-extrabold text-ink">{title}</h2>
        <p className="m-0 mt-1.5 max-w-xl text-small text-ink-muted md:text-body">{message}</p>
      </div>
      <div className={cx("w-full shrink-0 md:w-auto", hoverLiftClass)}>
        <Button to={to} size="lg" fullWidth className="md:w-auto">{action}</Button>
      </div>
    </div>
  );
}
