import type { ReactNode } from "react";

type Tone = "accent" | "emerald" | "rose" | "amber" | "orange" | "indigo" | "muted";

interface StatCardProps {
  label: string;
  value: ReactNode;
  icon?: ReactNode;
  tone?: Tone;
  active?: boolean;
  onClick?: () => void;
}

const TONE: Record<Tone, { text: string; bg: string }> = {
  accent: { text: "text-[var(--accent)]", bg: "bg-[var(--accent-glow)]" },
  emerald: { text: "text-emerald-500", bg: "bg-emerald-500/10" },
  rose: { text: "text-rose-500", bg: "bg-rose-500/10" },
  amber: { text: "text-amber-500", bg: "bg-amber-500/10" },
  orange: { text: "text-orange-500", bg: "bg-orange-500/10" },
  indigo: { text: "text-indigo-500", bg: "bg-indigo-500/10" },
  muted: { text: "text-[var(--text-muted)]", bg: "bg-[var(--surface-glass)]" },
};

export default function StatCard({ label, value, icon, tone = "accent", active, onClick }: StatCardProps) {
  const t = TONE[tone];
  const Comp = onClick ? "button" : "div";

  return (
    <Comp
      type={onClick ? "button" : undefined}
      onClick={onClick}
      className={`stat-card ${onClick ? "stat-card--interactive" : ""} ${active ? "stat-card--active" : ""}`}
    >
      {icon && <span className={`stat-card__icon ${t.bg} ${t.text}`}>{icon}</span>}
      <span className="stat-card__body">
        <span className="stat-card__label">{label}</span>
        <span className={`stat-card__value ${t.text}`}>{value}</span>
      </span>
    </Comp>
  );
}
