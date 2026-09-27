/** Desktop lists show 20 per page; phone lists load 20 at a time. */
export const DEFAULT_PAGE_SIZE = 20;

export type PageItem = number | "gap";

/**
 * The page buttons to draw: always the first and last page, a window around the current one, and
 * "gap" where pages are skipped. Near either end the window widens so the row keeps its length:
 * 1 2 3 4 5 … 22 · 1 … 4 5 6 … 22 · 1 … 18 19 20 21 22.
 */
export function pageItems(page: number, totalPages: number): PageItem[] {
  if (totalPages <= 7) return Array.from({ length: totalPages }, (_, i) => i + 1);
  const current = Math.min(Math.max(page, 1), totalPages);
  if (current <= 4) return [1, 2, 3, 4, 5, "gap", totalPages];
  if (current >= totalPages - 3) return [1, "gap", ...Array.from({ length: 5 }, (_, i) => totalPages - 4 + i)];
  return [1, "gap", current - 1, current, current + 1, "gap", totalPages];
}

/** "1–20" style range of the items shown on a page. */
export function pageRange(page: number, pageSize: number, totalCount: number): [number, number] {
  if (totalCount <= 0) return [0, 0];
  const first = (page - 1) * pageSize + 1;
  return [Math.min(first, totalCount), Math.min(page * pageSize, totalCount)];
}
