import { useState, type ReactNode } from "react";
import { Link, useLocation } from "react-router-dom";
import { Avatar } from "./Avatar.tsx";
import { BottomSheet } from "./BottomSheet.tsx";
import { cx } from "./cx.ts";
import { IconChevronLeft, IconChevronRight, IconGrid, IconLogout } from "./icons.tsx";
import { isNavActive, type NavGroup, type NavItem } from "./navigation.ts";

export type ShellUser = { name: string; role: string };

/* ─── Desktop sidebar ─── */

export type SidebarProps = {
  logo: ReactNode;
  groups: readonly NavGroup[];
  /** Signed-in user card at the bottom. */
  user?: ShellUser;
  onLogout?: () => void;
  /** Shown at the bottom instead of the user card (e.g. Log in / Sign up). */
  footer?: ReactNode;
};

/** 248px white sidebar: logo, grouped menu with the active item in navy, user card with Log out. */
export function Sidebar({ logo, groups, user, onLogout, footer }: SidebarProps) {
  const { pathname } = useLocation();
  return (
    <aside className="fixed inset-y-0 left-0 z-40 hidden w-[248px] flex-col border-r border-line bg-card font-ui md:flex">
      <div className="flex h-16 shrink-0 items-center border-b border-line-soft px-5">{logo}</div>
      <nav aria-label="Main" className="flex-1 overflow-y-auto px-3 py-4">
        {groups.map((group, index) => (
          <div key={group.label ?? index} className={cx(index > 0 && "mt-5")}>
            {group.label && <p className="m-0 mb-1.5 px-3 text-caption font-bold uppercase tracking-[0.6px] text-ink-faint">{group.label}</p>}
            <ul className="m-0 flex list-none flex-col gap-0.5 p-0">
              {group.items.map((item) => {
                const active = isNavActive(pathname, item.to);
                return (
                  <li key={item.to}>
                    <Link
                      to={item.to}
                      aria-current={active ? "page" : undefined}
                      className={cx(
                        "flex h-10 items-center gap-3 rounded-field px-3 text-sm font-bold no-underline transition-colors",
                        "focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-primary",
                        active ? "bg-primary text-white" : "text-ink-2 hover:bg-page hover:text-ink",
                      )}
                    >
                      <span className="flex shrink-0">{item.icon}</span>
                      {item.label}
                    </Link>
                  </li>
                );
              })}
            </ul>
          </div>
        ))}
      </nav>
      <div className="shrink-0 border-t border-line-soft p-3">
        {user ? (
          <div className="flex items-center gap-2.5 rounded-card bg-page p-2.5">
            <Avatar name={user.name} />
            <span className="min-w-0 flex-1">
              <span className="block truncate text-sm font-extrabold text-ink">{user.name}</span>
              <span className="block truncate text-label text-ink-muted">{user.role}</span>
            </span>
            {onLogout && (
              <button
                type="button"
                onClick={onLogout}
                aria-label="Log out"
                title="Log out"
                className="flex size-9 shrink-0 cursor-pointer items-center justify-center rounded-lg text-ink-2 hover:bg-card hover:text-danger focus-visible:outline-2 focus-visible:outline-primary"
              >
                <IconLogout size={17} />
              </button>
            )}
          </div>
        ) : footer}
      </div>
    </aside>
  );
}

/* ─── Desktop top bar ─── */

export type Crumb = { label: string; to?: string };

/** 64px top bar: breadcrumb on the left, bell and other actions on the right. */
export function TopBar({ crumbs, actions }: { crumbs: readonly Crumb[]; actions?: ReactNode }) {
  return (
    <header className="sticky top-0 z-30 hidden h-16 items-center justify-between gap-4 border-b border-line bg-card px-8 font-ui md:flex">
      <nav aria-label="Breadcrumb" className="min-w-0">
        <ol className="m-0 flex list-none items-center gap-1.5 p-0 text-sm">
          {crumbs.map((crumb, index) => {
            const last = index === crumbs.length - 1;
            return (
              <li key={`${crumb.label}-${index}`} className="flex min-w-0 items-center gap-1.5">
                {index > 0 && <IconChevronRight size={14} className="shrink-0 text-ink-faint" />}
                {crumb.to && !last ? (
                  <Link to={crumb.to} className="truncate font-bold text-ink-muted no-underline hover:text-ink">{crumb.label}</Link>
                ) : (
                  <span aria-current={last ? "page" : undefined} className={cx("truncate font-extrabold", last ? "text-ink" : "text-ink-muted")}>{crumb.label}</span>
                )}
              </li>
            );
          })}
        </ol>
      </nav>
      {actions && <div className="flex items-center gap-3">{actions}</div>}
    </header>
  );
}

/* ─── Phone top bar ─── */

export type PhoneTopBarProps = {
  /** Main pages: logo + app name. */
  logo?: ReactNode;
  /** Inner pages: "‹ Back label" instead of the logo. */
  back?: { to: string; label: string };
  actions?: ReactNode;
};

export function PhoneTopBar({ logo, back, actions }: PhoneTopBarProps) {
  return (
    <header className="sticky top-0 z-30 flex h-14 items-center justify-between gap-3 border-b border-line bg-card px-4 font-ui md:hidden">
      {back ? (
        <Link to={back.to} className="-ml-2 flex h-11 min-w-0 items-center gap-0.5 rounded-lg px-2 text-body font-extrabold text-ink no-underline focus-visible:outline-2 focus-visible:outline-primary">
          <IconChevronLeft size={20} />
          <span className="truncate">{back.label}</span>
        </Link>
      ) : (
        <div className="flex min-w-0 items-center">{logo}</div>
      )}
      {actions && <div className="flex shrink-0 items-center gap-2">{actions}</div>}
    </header>
  );
}

/* ─── Phone bottom nav ─── */

const MAX_BOTTOM_ITEMS = 5;

/**
 * Fixed phone navigation. Up to five items show directly; a longer menu shows the first four and
 * a "More" button that opens the rest in a bottom sheet.
 */
export function BottomNav({ items }: { items: readonly NavItem[] }) {
  const { pathname } = useLocation();
  const [moreOpen, setMoreOpen] = useState(false);
  if (items.length === 0) return null;
  const overflow = items.length > MAX_BOTTOM_ITEMS;
  const shown = overflow ? items.slice(0, MAX_BOTTOM_ITEMS - 1) : items;
  const moreActive = overflow && items.slice(MAX_BOTTOM_ITEMS - 1).some((item) => isNavActive(pathname, item.to));

  const itemClass = (active: boolean) => cx(
    "flex h-full min-w-0 flex-1 cursor-pointer flex-col items-center justify-center gap-1 border-0 bg-transparent text-caption font-bold no-underline",
    "focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-primary",
    active ? "text-primary" : "text-ink-faint",
  );

  return (
    <>
      <nav aria-label="Main" className="fixed inset-x-0 bottom-0 z-40 border-t border-line bg-card pb-[env(safe-area-inset-bottom)] font-ui md:hidden">
        <ul className="m-0 flex h-16 list-none p-0">
          {shown.map((item) => {
            const active = isNavActive(pathname, item.to);
            return (
              <li key={item.to} className="flex flex-1">
                <Link to={item.to} aria-current={active ? "page" : undefined} className={itemClass(active)}>
                  <span className="flex">{item.icon}</span>
                  <span className="max-w-full truncate px-1">{item.label}</span>
                </Link>
              </li>
            );
          })}
          {overflow && (
            <li className="flex flex-1">
              <button type="button" onClick={() => setMoreOpen(true)} className={itemClass(moreActive)}>
                <IconGrid size={19} />
                More
              </button>
            </li>
          )}
        </ul>
      </nav>
      {overflow && (
        <BottomSheet open={moreOpen} onClose={() => setMoreOpen(false)} title="Menu" footer={null}>
          <ul className="m-0 flex list-none flex-col gap-1 p-0">
            {items.map((item) => {
              const active = isNavActive(pathname, item.to);
              return (
                <li key={item.to}>
                  <Link
                    to={item.to}
                    onClick={() => setMoreOpen(false)}
                    aria-current={active ? "page" : undefined}
                    className={cx("flex h-12 items-center gap-3 rounded-field px-3 text-body font-bold no-underline", active ? "bg-primary text-white" : "text-ink hover:bg-page")}
                  >
                    {item.icon}
                    {item.label}
                  </Link>
                </li>
              );
            })}
          </ul>
        </BottomSheet>
      )}
    </>
  );
}
