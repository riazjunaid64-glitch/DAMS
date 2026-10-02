import type { FilterDef, FilterValues } from "./FilterBar.tsx";

/** One id for the From/To pair, shared by a date range and the Period that owns it. */
export const rangeId = (fromKey: string, toKey: string) => `${fromKey}\0${toKey}`;

/**
 * A date range counts once, even when a Period select owns the same dates. A filter counts when
 * its value differs from its default ("" unless the page says otherwise), so a page that opens
 * on a financial year or on today has nothing "set" until the user changes it.
 */
export function activeFilterCount(filters: readonly FilterDef[], values: FilterValues, defaults: FilterValues = {}): number {
  const seen = new Set<string>();
  let count = 0;
  for (const filter of filters) {
    if (filter.when && !filter.when(values)) continue;
    if (filter.type === "dateRange" || filter.type === "period") {
      const id = rangeId(filter.fromKey, filter.toKey);
      if (seen.has(id)) continue;
      seen.add(id);
      if ((values[filter.fromKey] ?? "") !== (defaults[filter.fromKey] ?? "") || (values[filter.toKey] ?? "") !== (defaults[filter.toKey] ?? "")) count += 1;
    } else if ((values[filter.key] ?? "") !== (defaults[filter.key] ?? "")) {
      count += 1;
    }
  }
  return count;
}
