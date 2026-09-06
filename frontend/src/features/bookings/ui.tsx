import type { ReactNode } from "react";
import { Icons } from "./tokens.tsx";

/**
 * Presentational pieces shared by the Booking module's pages.
 *
 * Deliberately local to `features/bookings`: the app already has a `TabLayout`, but it renders a
 * segmented pill bar, and the booking screens want the quieter underline bar from the design.
 * Restyling the shared one would have changed six other pages that are happy as they are.
 */

// ── Tabs ─────────────────────────────────────────────────────────────────────────────────────

export interface BookingTab {
  id: string;
  label: string;
}

/**
 * The underline tab bar from the design. Arrow keys move between tabs and only the active tab is
 * in the page's tab order, which is what the WAI-ARIA tabs pattern expects and what the shared
 * segmented control already does — behaviour worth keeping even though the skin differs.
 */
export function BookingTabs({
  tabs, active, onChange,
}: { tabs: BookingTab[]; active: string; onChange: (id: string) => void }) {
  const move = (delta: number) => {
    const index = tabs.findIndex((t) => t.id === active);
    if (index < 0) return;
    const next = tabs[(index + delta + tabs.length) % tabs.length];
    onChange(next.id);
    document.getElementById(`booking-tab-${next.id}`)?.focus();
  };

  return (
    // Scrolls rather than wraps on a narrow screen, so the bar stays one line and the underline
    // keeps meaning what it means.
    <div className="mb-6 overflow-x-auto border-b border-[var(--border)] [scrollbar-width:none] [&::-webkit-scrollbar]:hidden">
      <div role="tablist" aria-label="Booking sections" className="flex min-w-max gap-1">
        {tabs.map((tab) => {
          const isActive = tab.id === active;
          return (
            <button
              key={tab.id}
              id={`booking-tab-${tab.id}`}
              role="tab"
              type="button"
              aria-selected={isActive}
              aria-controls={`booking-panel-${tab.id}`}
              tabIndex={isActive ? 0 : -1}
              onClick={() => onChange(tab.id)}
              onKeyDown={(e) => {
                if (e.key === "ArrowRight") { e.preventDefault(); move(1); }
                if (e.key === "ArrowLeft") { e.preventDefault(); move(-1); }
              }}
              className={`-mb-px cursor-pointer whitespace-nowrap border-b-2 px-4 py-3 text-sm font-semibold transition-colors ${
                isActive
                  ? "border-[var(--accent)] text-[var(--accent)]"
                  : "border-transparent text-[var(--text-muted)] hover:text-[var(--text-primary)]"
              }`}
            >
              {tab.label}
            </button>
          );
        })}
      </div>
    </div>
  );
}

/**
 * Renders a panel from the first time its tab is opened, and keeps it mounted afterwards.
 * <p>
 * Not rendering until first opened is what stops the Commission &amp; Rebate tab issuing its four
 * requests on every booking anyone glances at. Keeping it mounted after that is what stops a tab
 * switch throwing away a half-typed commission, an expanded audit history, or the pages of older
 * audit already fetched — and re-issuing those four requests to get back to where the user was.
 * </p>
 * <p>
 * Hidden with `display:none` rather than unmounted, so the panel keeps its state and its scroll
 * position. `hidden` also removes it from the accessibility tree, so a screen reader is not offered
 * four tabs' worth of content at once.
 * </p>
 */
export function TabPanel({
  id, active, visited, children,
}: { id: string; active: string; visited: readonly string[]; children: ReactNode }) {
  const isActive = id === active;
  // Which tabs have been opened is remembered by whoever changes the tab, not latched here: a panel
  // deriving it from its own props needs either an effect that runs a beat late or a ref written
  // during render, and both are the kind of cleverness that goes wrong quietly.
  if (!visited.includes(id)) return null;
  return (
    <div
      role="tabpanel"
      id={`booking-panel-${id}`}
      aria-labelledby={`booking-tab-${id}`}
      hidden={!isActive}
      className={isActive ? "animate-fade-in" : undefined}
    >
      {children}
    </div>
  );
}

// ── Cards ────────────────────────────────────────────────────────────────────────────────────

/** Accent used by a stat card's icon disc. Keyed by meaning, not by colour, so the palette moves in one place. */
export type StatTone = "gold" | "emerald" | "sky" | "rose";

const toneClass: Record<StatTone, string> = {
  gold: "bg-[var(--accent-glow)] text-[var(--accent)]",
  emerald: "bg-emerald-500/10 text-emerald-400",
  sky: "bg-sky-500/10 text-sky-400",
  rose: "bg-rose-500/10 text-rose-400",
};

export function StatCard({
  icon, label, value, tone = "gold",
}: { icon: ReactNode; label: string; value: ReactNode; tone?: StatTone }) {
  return (
    <div className="flex items-center gap-4 rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] p-4">
      <span className={`flex h-11 w-11 shrink-0 items-center justify-center rounded-full ${toneClass[tone]}`}>
        {icon}
      </span>
      <span className="min-w-0">
        <span className="block text-xs text-[var(--text-muted)]">{label}</span>
        {/* Breaks rather than overflows: an unusually large figure must not push the card wider
            than its grid column. */}
        <span className="mt-0.5 block break-words text-xl font-bold leading-tight text-[var(--text-heading)]">
          {value}
        </span>
      </span>
    </div>
  );
}

export function PanelCard({
  title, description, action, children, className,
}: { title?: string; description?: string; action?: ReactNode; children?: ReactNode; className?: string }) {
  return (
    <div className={`rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] ${className ?? ""}`}>
      {(title || action) && (
        <div className="flex flex-col gap-3 p-5 sm:flex-row sm:items-start sm:justify-between sm:p-6">
          <div className="min-w-0">
            {title && <h2 className="text-lg font-semibold text-[var(--text-heading)]">{title}</h2>}
            {description && <p className="mt-1 text-sm text-[var(--text-muted)]">{description}</p>}
          </div>
          {action && <div className="flex shrink-0 flex-wrap gap-2">{action}</div>}
        </div>
      )}
      {children}
    </div>
  );
}

/** A label/value line in a details card. The hairline sits between rows, never under the last one. */
export function DetailRow({ label, value }: { label: string; value: ReactNode }) {
  return (
    <div className="flex items-baseline justify-between gap-4 border-b border-[var(--border)] py-3 last:border-0 last:pb-0">
      <span className="text-sm text-[var(--text-muted)]">{label}</span>
      <span className="text-right text-sm font-semibold tabular-nums text-[var(--text-heading)]">{value}</span>
    </div>
  );
}

export function EmptyState({ message, hint }: { message: string; hint?: string }) {
  return (
    <div className="px-6 py-14 text-center">
      <Icons.inbox className="mx-auto h-7 w-7 text-[var(--text-muted)]" />
      <p className="mt-3 text-sm text-[var(--text-secondary)]">{message}</p>
      {hint && <p className="mt-1 text-xs text-[var(--text-muted)]">{hint}</p>}
    </div>
  );
}
