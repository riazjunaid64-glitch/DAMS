import type { ReactNode } from "react";
import { Link } from "react-router-dom";
import { cx } from "./cx.ts";
import { IconChevronLeft } from "./icons.tsx";
import { StatusBadge } from "./StatusBadge.tsx";
import type { StatusTone } from "./statusTone.ts";

export type PageHeaderProps = {
  title: ReactNode;
  /** Status shown as a badge beside the title. */
  status?: string;
  /** A badge with its own words and tone, for a state that is not a status name ("2 documents needed"). */
  badge?: { text: ReactNode; tone: StatusTone };
  /** One line under the title. */
  subtitle?: ReactNode;
  /** A row of small facts under the title (phone, ID number, customer since). */
  details?: ReactNode;
  /** Back link above the title (desktop; phones use the top bar's back). */
  back?: { to: string; label: string };
  /** Buttons on the right; they wrap under the title on phone. */
  actions?: ReactNode;
  className?: string;
};

export function PageHeader({ title, status, badge, subtitle, details, back, actions, className }: PageHeaderProps) {
  return (
    <header className={cx("flex flex-col gap-4 font-ui md:flex-row md:items-end md:justify-between", className)}>
      <div className="min-w-0">
        {back && (
          <Link to={back.to} className="mb-2 hidden items-center gap-1 text-small font-bold text-ink-muted no-underline hover:text-ink md:inline-flex">
            <IconChevronLeft size={16} />
            {back.label}
          </Link>
        )}
        <div className="flex flex-wrap items-center gap-2.5">
          <h1 className="m-0 text-[22px] leading-tight font-extrabold text-ink md:text-page-title">{title}</h1>
          {status && <StatusBadge status={status} />}
          {badge && <StatusBadge status="" tone={badge.tone}>{badge.text}</StatusBadge>}
        </div>
        {details && <div className="mt-2 flex flex-col items-start gap-1.5 text-body md:flex-row md:flex-wrap md:items-center md:gap-x-5">{details}</div>}
        {subtitle && <p className="m-0 mt-1 text-small text-ink-muted md:text-body">{subtitle}</p>}
      </div>
      {actions && <div className="flex flex-wrap items-center gap-2 max-md:shrink-0">{actions}</div>}
    </header>
  );
}
