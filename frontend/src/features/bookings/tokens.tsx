import type { ReactNode } from "react";

/**
 * The Booking module's icon set and the one shared table-header style.
 *
 * Separate from `ui.tsx` because these are values, not components, and React Fast Refresh only
 * works on a module whose exports are all components.
 */

type IconProps = { className?: string };

const stroke = {
  fill: "none",
  stroke: "currentColor",
  strokeWidth: 1.8,
  strokeLinecap: "round" as const,
  strokeLinejoin: "round" as const,
};

// Sized by the caller, so one set serves the 16px meta row and the 22px stat-card discs.
function svg(children: ReactNode, className?: string) {
  return (
    <svg viewBox="0 0 24 24" className={className ?? "h-5 w-5"} aria-hidden="true" {...stroke}>
      {children}
    </svg>
  );
}

export const Icons = {
  user: ({ className }: IconProps) =>
    svg(<><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2" /><circle cx="12" cy="7" r="4" /></>, className),
  phone: ({ className }: IconProps) =>
    svg(<path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72c.13.96.36 1.9.7 2.81a2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45c.9.34 1.85.57 2.81.7A2 2 0 0 1 22 16.92Z" />, className),
  pin: ({ className }: IconProps) =>
    svg(<><path d="M20 10c0 6-8 12-8 12s-8-6-8-12a8 8 0 0 1 16 0Z" /><circle cx="12" cy="10" r="3" /></>, className),
  unit: ({ className }: IconProps) =>
    svg(<><rect x="4" y="2" width="16" height="20" rx="1.5" /><path d="M9 7h.01M15 7h.01M9 12h.01M15 12h.01M9 17h.01M15 17h.01" /></>, className),
  home: ({ className }: IconProps) =>
    svg(<><path d="m3 9 9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z" /><polyline points="9 22 9 12 15 12 15 22" /></>, className),
  wallet: ({ className }: IconProps) =>
    svg(<><path d="M21 12V7H5a2 2 0 0 1 0-4h14v4" /><path d="M3 5v14a2 2 0 0 0 2 2h16v-5" /><path d="M18 12a2 2 0 0 0 0 4h4v-4Z" /></>, className),
  coins: ({ className }: IconProps) =>
    svg(<><ellipse cx="12" cy="6" rx="8" ry="3" /><path d="M4 6v6c0 1.66 3.58 3 8 3s8-1.34 8-3V6" /><path d="M4 12v6c0 1.66 3.58 3 8 3s8-1.34 8-3v-6" /></>, className),
  calendar: ({ className }: IconProps) =>
    svg(<><rect x="3" y="4" width="18" height="18" rx="2" /><path d="M16 2v4M8 2v4M3 10h18" /></>, className),
  doc: ({ className }: IconProps) =>
    svg(<><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" /><polyline points="14 2 14 8 20 8" /></>, className),
  pulse: ({ className }: IconProps) =>
    svg(<path d="M22 12h-4l-3 9L9 3l-3 9H2" />, className),
  percent: ({ className }: IconProps) =>
    svg(<><line x1="19" y1="5" x2="5" y2="19" /><circle cx="6.5" cy="6.5" r="2.5" /><circle cx="17.5" cy="17.5" r="2.5" /></>, className),
  bank: ({ className }: IconProps) =>
    svg(<><rect x="2" y="5" width="20" height="14" rx="2" /><path d="M2 10h20" /></>, className),
  printer: ({ className }: IconProps) =>
    svg(<><polyline points="6 9 6 2 18 2 18 9" /><path d="M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2" /><rect x="6" y="14" width="12" height="8" /></>, className),
  inbox: ({ className }: IconProps) =>
    svg(<><polyline points="22 12 16 12 14 15 10 15 8 12 2 12" /><path d="M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11Z" /></>, className),
  back: ({ className }: IconProps) =>
    svg(<><line x1="19" y1="12" x2="5" y2="12" /><polyline points="12 19 5 12 12 5" /></>, className),
};

/** Column header styling shared by every table in the module. */
export const th = "px-5 py-3.5 text-[10px] font-bold uppercase tracking-[0.12em] text-[var(--text-muted)]";
