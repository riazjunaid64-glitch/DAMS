import type { FilterDef, FilterValues } from "./FilterBar.tsx";

/** One id for the From/To pair, shared by a date range and the Period that owns it. */
export const rangeId = (fromKey: string, toKey: string) => `${fromKey}\0${toKey}`;

/** A date range counts once, even when a Period select owns the same dates. */
export function activeFilterCount(filters: readonly FilterDef[], values: FilterValues): number {
  const seen = new Set<string>();
  let count = 0;
  for (const filter of filters) {
    if (filter.type === "dateRange" || filter.type === "period") {
      const id = rangeId(filter.fromKey, filter.toKey);
      if (seen.has(id)) continue;
      seen.add(id);
      if (values[filter.fromKey] || values[filter.toKey]) count += 1;
    } else if (values[filter.key]) {
      count += 1;
    }
  }
  return count;
}
