import { useMemo, useSyncExternalStore } from "react";

/** The phone layout applies below this width (Tailwind's `md` breakpoint). */
export const PHONE_QUERY = "(max-width: 767.98px)";

function subscribeTo(query: string) {
  return (onChange: () => void) => {
    const list = window.matchMedia(query);
    list.addEventListener("change", onChange);
    return () => list.removeEventListener("change", onChange);
  };
}

export function useMediaQuery(query: string): boolean {
  const subscribe = useMemo(() => subscribeTo(query), [query]);
  return useSyncExternalStore(
    subscribe,
    () => window.matchMedia(query).matches,
    () => false,
  );
}

/**
 * True on phone widths. Layout differences stay in CSS (`md:`); this is for components whose
 * behaviour changes on a phone — tabs that become a dropdown, a popup that goes full screen.
 */
export function useIsPhone(): boolean {
  return useMediaQuery(PHONE_QUERY);
}
