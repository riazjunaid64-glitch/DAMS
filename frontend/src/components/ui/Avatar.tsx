import { cx } from "./cx.ts";

/** "Ahmed Khan" → "AK"; "sara" → "S". */
function initials(name: string): string {
  const parts = name.trim().split(/[\s._@-]+/).filter(Boolean);
  if (parts.length === 0) return "?";
  return (parts.length === 1 ? parts[0]!.slice(0, 1) : parts[0]!.slice(0, 1) + parts[parts.length - 1]!.slice(0, 1)).toUpperCase();
}

/** Initials on gold-soft. */
export function Avatar({ name, size = 36, className }: { name: string; size?: number; className?: string }) {
  return (
    <span
      aria-hidden="true"
      style={{ width: size, height: size, fontSize: Math.round(size * 0.38) }}
      className={cx("inline-flex shrink-0 items-center justify-center rounded-full border border-gold-line bg-gold-soft font-ui font-extrabold text-gold-text", className)}
    >
      {initials(name)}
    </span>
  );
}
