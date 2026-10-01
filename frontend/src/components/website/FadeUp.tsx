import type { CSSProperties, ReactNode } from "react";
import { cx } from "../ui/cx.ts";
import { usePrefersReducedMotion } from "./motion.ts";

/**
 * Fades its children up once, on load. `order` staggers siblings (headline, then buttons,
 * then numbers; project cards in sequence). Nothing plays when reduced motion is on.
 */
export function FadeUp({ order = 0, className, children }: { order?: number; className?: string; children: ReactNode }) {
  const reduce = usePrefersReducedMotion();
  const style: CSSProperties | undefined = !reduce && order > 0 ? { animationDelay: `${order * 90}ms` } : undefined;
  return (
    <div className={cx(className, !reduce && "web-fade-up")} style={style}>
      {children}
    </div>
  );
}
