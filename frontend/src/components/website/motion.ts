import { useMediaQuery } from "../ui/useMediaQuery.ts";

/** Matches the OS "reduce motion" setting. Website motion checks this and stays still. */
export const REDUCED_MOTION_QUERY = "(prefers-reduced-motion: reduce)";

/** How long the company numbers take to count from 0 to their final text. */
export const COUNT_UP_MS = 1000;

/**
 * Desktop-only hover lift. The stylesheet applies it on a fine pointer at the desktop
 * width, and drops it when reduced motion is on.
 */
export const hoverLiftClass = "web-lift";

/** Desktop-only photo zoom (about 4%) while a parent `.group` is hovered. */
export const hoverZoomClass = "web-photo";

export function usePrefersReducedMotion(): boolean {
  return useMediaQuery(REDUCED_MOTION_QUERY);
}

/** Splits "10K+" into 10 and "K+" so a count-up can finish on the exact text. */
export function splitStatValue(value: string): { target: number; suffix: string } {
  const match = /^(\d+)(.*)$/.exec(value.trim());
  if (!match) return { target: Number.NaN, suffix: value };
  return { target: Number(match[1]), suffix: match[2] };
}

/**
 * The text shown `elapsedMs` into a one-shot count-up. At the end (and for a value that
 * is not a leading number) this is the original string, not a rebuilt approximation.
 */
export function countUpFrame(value: string, elapsedMs: number, durationMs = COUNT_UP_MS): string {
  const { target, suffix } = splitStatValue(value);
  if (!Number.isFinite(target)) return value;
  if (elapsedMs >= durationMs) return value;
  const t = Math.max(0, elapsedMs) / durationMs;
  const eased = 1 - (1 - t) ** 3;
  const shown = Math.round(eased * target);
  return shown === target ? value : `${shown}${suffix}`;
}
