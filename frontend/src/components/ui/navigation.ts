import type { ReactNode } from "react";

export type NavItem = { to: string; label: string; icon: ReactNode };
export type NavGroup = { label?: string; items: NavItem[] };

/** Whether `to` is the section the user is in: itself or any page below it. "/" matches only itself. */
export function isNavActive(pathname: string, to: string): boolean {
  if (to === "/") return pathname === "/";
  return pathname === to || pathname.startsWith(`${to}/`);
}

/** The menu item that owns `pathname`, preferring the most specific match. */
export function activeNavItem(groups: readonly NavGroup[], pathname: string): NavItem | undefined {
  return groups
    .flatMap((group) => group.items)
    .filter((item) => isNavActive(pathname, item.to))
    .sort((a, b) => b.to.length - a.to.length)[0];
}
