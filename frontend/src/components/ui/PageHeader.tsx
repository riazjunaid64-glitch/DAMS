import type { ReactNode } from "react";
import { Link } from "react-router-dom";
import { cx } from "./cx.ts";
import { IconChevronLeft } from "./icons.tsx";
import { StatusBadge } from "./StatusBadge.tsx";

export type PageHeaderProps = {
  title: ReactNode;
  /** Status shown as a badge beside the title. */
  status?: string;
  /** One line under the title. */
  subtitle?: ReactNode;
  /** Back link above the title (desktop; phones use the top bar's back). */
  back?: { to: string; label: string };
  /** Buttons on the right; they wrap under the title on phone. */
  actions?: ReactNode;
  className?: string;
};

export function PageHeader({ title, status, subtitle, back, actions, className }: PageHeaderProps) {
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
        </div>
        {subtitle && <p className="m-0 mt-1 text-small text-ink-muted md:text-body">{subtitle}</p>}
      </div>
      {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
    </header>
  );
}
