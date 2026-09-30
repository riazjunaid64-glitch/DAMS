import { createContext, useContext, useEffect } from "react";
import type { Crumb } from "../components/ui";

/**
 * Lets a page add its own steps to the top bar's breadcrumb ("Bookings / BK-000013 / Receipt RCP-000231").
 * The layout owns the trail and puts the page's steps after the section from the menu.
 */
export const TrailContext = createContext<(steps: readonly Crumb[]) => void>(() => undefined);

/** Shows these steps after the current menu section while the page is open. `key` changes only when the steps do. */
export function usePageTrail(steps: readonly Crumb[]) {
  const setTrail = useContext(TrailContext);
  const key = JSON.stringify(steps);
  useEffect(() => {
    setTrail(JSON.parse(key) as Crumb[]);
    return () => setTrail([]);
  }, [key, setTrail]);
}
