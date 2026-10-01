import { useEffect, useState } from "react";
import { COMPANY_STATS } from "../../lib/companyStats.ts";
import { cx } from "../ui/cx.ts";
import { countUpFrame, splitStatValue, usePrefersReducedMotion } from "./motion.ts";

export type CompanyNumbersLayout = "row" | "panel";
export type CompanyNumbersTone = "onDark" | "onLight";

const toneStyles: Record<CompanyNumbersTone, { value: string; label: string; line: string }> = {
  onDark: { value: "text-gold", label: "text-white/72", line: "border-gold/30" },
  onLight: { value: "text-gold-text", label: "text-ink-muted", line: "border-line" },
};

/**
 * The company's fixed figures. `row` is four columns with dividers (Home, desktop).
 * `panel` is a 2 by 2 grid (Home on a phone; About will use it later). The values
 * count up once, and show their final text immediately when reduced motion is on.
 */
export function CompanyNumbers({
  layout,
  tone = "onDark",
  className,
}: {
  layout: CompanyNumbersLayout;
  tone?: CompanyNumbersTone;
  className?: string;
}) {
  const styles = toneStyles[tone];
  return (
    <ul
      data-layout={layout}
      className={cx(
        "m-0 grid list-none p-0",
        layout === "row" ? "grid-cols-4 border-t" : "grid-cols-2",
        styles.line,
        className,
      )}
    >
      {COMPANY_STATS.map((stat, index) => (
        <li
          key={stat.label}
          className={cx(
            "min-w-0 px-3 py-4 md:px-5 md:py-5",
            layout === "row" && index > 0 && "border-l",
            layout === "panel" && index % 2 === 1 && "border-l",
            layout === "panel" && index < 2 && "border-b",
            styles.line,
          )}
        >
          <p className={cx("m-0 text-2xl leading-none font-extrabold tabular-nums md:text-3xl", styles.value)}>
            <CountUp value={stat.value} />
          </p>
          <p className={cx("m-0 mt-1.5 text-caption leading-snug md:text-small", styles.label)}>{stat.label}</p>
        </li>
      ))}
    </ul>
  );
}

function CountUp({ value }: { value: string }) {
  const reduce = usePrefersReducedMotion();
  const { target, suffix } = splitStatValue(value);
  const start = Number.isFinite(target) ? `0${suffix}` : value;
  const [shown, setShown] = useState(start);

  useEffect(() => {
    if (reduce || !Number.isFinite(target)) return;
    const started = performance.now();
    let frame = 0;
    const tick = (now: number) => {
      const next = countUpFrame(value, now - started);
      setShown(next);
      if (next !== value) frame = requestAnimationFrame(tick);
    };
    frame = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(frame);
  }, [reduce, target, value]);

  return <span>{reduce || !Number.isFinite(target) ? value : shown}</span>;
}
