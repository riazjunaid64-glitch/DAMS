import { DEFAULT_PAGE_SIZE } from "../../components/ui/pageItems.ts";

/** Twenty rows a page on desktop; a phone shows twenty more with each Load more. */
export const PAGE_SIZE = DEFAULT_PAGE_SIZE;

/**
 * The page a list cut in the browser is really on. Such a list comes back whole, so after a delete
 * empties the last page the stored page number is past the end; this steps it back to the new last page.
 */
export function clampPage(page: number, total: number, pageSize = PAGE_SIZE): number {
  const last = Math.max(1, Math.ceil(total / pageSize));
  return Math.min(Math.max(1, page), last);
}

/** The rows of one desktop page. */
export function pageRows<T>(rows: readonly T[], page: number, pageSize = PAGE_SIZE): T[] {
  const current = clampPage(page, rows.length, pageSize);
  return rows.slice((current - 1) * pageSize, current * pageSize);
}

/** "1 payment", "18 payments", "1,250 payments". */
export const countOf = (count: number, one: string, many: string) => `${count.toLocaleString("en-PK")} ${count === 1 ? one : many}`;

export const paymentsText = (count: number) => countOf(count, "payment", "payments");
