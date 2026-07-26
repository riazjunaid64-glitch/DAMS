import type { ReactNode } from "react";
import { Link } from "react-router-dom";
import type { User } from "../../App.tsx";
import Button from "../../lib/Button.tsx";
import { stageLabel } from "./types.ts";

const CRM_ROLES = ["Admin", "Manager", "Employee"];

export function CrmAccess({ user, children }: { user: User | null; children: ReactNode }) {
  if (!user) {
    return (
      <StatePanel
        title="Sign in required"
        message="Sign in with an internal staff account to open the Lead CRM."
      />
    );
  }
  if (!CRM_ROLES.includes(user.role)) {
    return (
      <StatePanel
        title="Internal workspace"
        message="Customer accounts cannot access lead assignments, internal activity, or sales records."
      />
    );
  }
  return <>{children}</>;
}

export function CrmHeader({
  title,
  subtitle,
  role,
  actions,
}: {
  title: string;
  subtitle: string;
  role: string;
  actions?: ReactNode;
}) {
  return (
    <div className="border-b border-[var(--border)] bg-[var(--bg-card)]">
      <div className="mx-auto flex w-full max-w-[1500px] flex-col gap-4 px-4 py-6 sm:px-6 lg:flex-row lg:items-end lg:justify-between lg:px-8">
        <div>
          <div className="mb-2 flex flex-wrap items-center gap-2 text-xs font-semibold uppercase tracking-[0.16em] text-[var(--accent)]">
            <Link to="/crm" className="hover:underline">Lead CRM</Link>
            <span className="text-[var(--text-muted)]">/</span>
            <span className="text-[var(--text-muted)]">{role === "Manager" ? "Sales Manager" : role === "Employee" ? "Sales Employee" : role}</span>
          </div>
          <h1 className="text-2xl font-bold text-[var(--text-heading)] sm:text-3xl">{title}</h1>
          <p className="mt-1 max-w-3xl text-sm text-[var(--text-muted)]">{subtitle}</p>
        </div>
        {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
      </div>
    </div>
  );
}

export function CrmTabs({
  items,
  active,
  onChange,
}: {
  items: { id: string; label: string; count?: number }[];
  active: string;
  onChange: (id: string) => void;
}) {
  return (
    <div className="flex gap-1 overflow-x-auto rounded-xl border border-[var(--border)] bg-[var(--bg-card)] p-1" role="tablist">
      {items.map((item) => (
        <button
          key={item.id}
          type="button"
          role="tab"
          aria-selected={active === item.id}
          onClick={() => onChange(item.id)}
          className={`whitespace-nowrap rounded-lg px-3 py-2 text-sm font-medium transition ${
            active === item.id
              ? "bg-[var(--accent)] text-white shadow-sm"
              : "text-[var(--text-muted)] hover:bg-[var(--surface-glass-hover)] hover:text-[var(--text-primary)]"
          }`}
        >
          {item.label}
          {item.count != null && <span className="ml-1.5 opacity-75">{item.count}</span>}
        </button>
      ))}
    </div>
  );
}

export function StageBadge({ stage }: { stage: string }) {
  const tone =
    stage === "Won" ? "border-emerald-500/25 bg-emerald-500/10 text-emerald-400" :
    stage === "Lost" ? "border-rose-500/25 bg-rose-500/10 text-rose-400" :
    stage === "Dormant" ? "border-slate-500/25 bg-slate-500/10 text-slate-400" :
    stage.includes("SiteVisit") ? "border-violet-500/25 bg-violet-500/10 text-violet-400" :
    stage === "Negotiation" || stage === "BookingPending" ? "border-amber-500/25 bg-amber-500/10 text-amber-400" :
    "border-indigo-500/25 bg-indigo-500/10 text-indigo-400";
  return <span className={`inline-flex rounded-full border px-2.5 py-1 text-[11px] font-semibold ${tone}`}>{stageLabel(stage)}</span>;
}

export function QualificationBadge({ value }: { value: string }) {
  const tone =
    value === "Hot" ? "bg-rose-500/10 text-rose-400" :
    value === "Warm" ? "bg-amber-500/10 text-amber-400" :
    value === "Cold" ? "bg-sky-500/10 text-sky-400" :
    "bg-slate-500/10 text-slate-400";
  return <span className={`rounded-md px-2 py-1 text-[11px] font-semibold ${tone}`}>{value}</span>;
}

export function MetricCard({
  label,
  value,
  detail,
  onClick,
}: {
  label: string;
  value: string | number;
  detail?: string;
  onClick?: () => void;
}) {
  const content = (
    <>
      <p className="text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)]">{label}</p>
      <p className="mt-2 text-2xl font-bold text-[var(--text-heading)]">{value}</p>
      {detail && <p className="mt-1 text-xs text-[var(--text-muted)]">{detail}</p>}
    </>
  );
  return onClick ? (
    <button type="button" onClick={onClick} className="rounded-2xl border border-[var(--border)] bg-[var(--bg-card)] p-4 text-left transition hover:border-[var(--accent)]/40 hover:bg-[var(--surface-glass-hover)]">
      {content}
    </button>
  ) : (
    <div className="rounded-2xl border border-[var(--border)] bg-[var(--bg-card)] p-4">{content}</div>
  );
}

export function ErrorBanner({ message, onRetry }: { message: string; onRetry?: () => void }) {
  return (
    <div role="alert" className="flex flex-col gap-3 rounded-xl border border-rose-500/25 bg-rose-500/[0.07] px-4 py-3 text-sm text-rose-300 sm:flex-row sm:items-center sm:justify-between">
      <span>{message}</span>
      {onRetry && <Button size="sm" variant="outline" onClick={onRetry}>Try again</Button>}
    </div>
  );
}

export function StatePanel({ title, message, action }: { title: string; message: string; action?: ReactNode }) {
  return (
    <div className="mx-auto flex min-h-[55vh] max-w-xl flex-col items-center justify-center px-6 text-center">
      <div className="mb-4 flex h-14 w-14 items-center justify-center rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] text-xl text-[var(--accent)]">◆</div>
      <h2 className="text-xl font-semibold text-[var(--text-heading)]">{title}</h2>
      <p className="mt-2 text-sm leading-6 text-[var(--text-muted)]">{message}</p>
      {action && <div className="mt-5">{action}</div>}
    </div>
  );
}

export function CrmModal({
  open,
  title,
  subtitle,
  onClose,
  children,
  footer,
  wide = false,
}: {
  open: boolean;
  title: string;
  subtitle?: string;
  onClose: () => void;
  children: ReactNode;
  footer?: ReactNode;
  wide?: boolean;
}) {
  if (!open) return null;
  return (
    <div className="fixed inset-0 z-[80] flex items-center justify-center p-3 sm:p-5">
      <button type="button" aria-label="Close dialog" className="absolute inset-0 bg-black/65 backdrop-blur-sm" onClick={onClose} />
      <div role="dialog" aria-modal="true" aria-label={title} className={`relative z-10 flex max-h-[94vh] w-full flex-col overflow-hidden rounded-2xl border border-[var(--border)] bg-[var(--modal-bg)] shadow-2xl ${wide ? "max-w-4xl" : "max-w-xl"}`}>
        <div className="flex items-start justify-between border-b border-[var(--border)] px-5 py-4">
          <div>
            <h2 className="text-lg font-semibold text-[var(--text-heading)]">{title}</h2>
            {subtitle && <p className="mt-1 text-sm text-[var(--text-muted)]">{subtitle}</p>}
          </div>
          <button type="button" onClick={onClose} className="rounded-lg px-2 py-1 text-xl text-[var(--text-muted)] hover:bg-[var(--surface-glass-hover)]" aria-label="Close">×</button>
        </div>
        <div className="overflow-y-auto px-5 py-5">{children}</div>
        {footer && <div className="border-t border-[var(--border)] bg-[var(--surface-glass)] px-5 py-4">{footer}</div>}
      </div>
    </div>
  );
}

export const inputClass = "w-full rounded-xl border border-[var(--border)] bg-[var(--input-bg)] px-3.5 py-2.5 text-sm text-[var(--text-primary)] outline-none transition placeholder:text-[var(--text-muted)] focus:border-[var(--accent)] focus:ring-2 focus:ring-[var(--accent-glow)] disabled:cursor-not-allowed disabled:opacity-60";

export function Label({ children, required }: { children: ReactNode; required?: boolean }) {
  return <label className="mb-1.5 block text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)]">{children}{required && <span className="ml-1 text-rose-400">*</span>}</label>;
}
