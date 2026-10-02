import { describe, expect, it } from "vitest";
import { isCurrentRowsRequest, rowsKey, visibleRows, type Loaded } from "./useFinanceRows.ts";

/**
 * The finance home picks its table columns from the active view, and each view's columns read
 * fields only that view's rows have. Rows and columns must never be a render out of step.
 */
describe("finance row/key coupling", () => {
  const revenueRows: Loaded<{ id: number }> = {
    key: rowsKey("revenue", "", "", "", ""),
    rows: [{ id: 1 }, { id: 2 }],
    hasMore: true,
    totalCount: 2,
    loading: false,
    error: null,
  };

  it("withholds rows fetched for a different view", () => {
    const assets = rowsKey("assetPurchase", "", "", "", "");
    const shown = visibleRows(revenueRows, assets);
    expect(shown.rows).toEqual([]);
    expect(shown.key).toBe(assets);
    expect(shown.loading).toBe(true);
    expect(shown.hasMore).toBe(false);
  });

  it("hands over rows that belong to the requested view", () => {
    expect(visibleRows(revenueRows, revenueRows.key)).toBe(revenueRows);
  });

  it("withholds rows when any filter changes, not just the view", () => {
    for (const key of [
      rowsKey("revenue", "7", "", "", ""),
      rowsKey("revenue", "", "2026-01-01", "", ""),
      rowsKey("revenue", "", "", "2026-12-31", ""),
      rowsKey("revenue", "", "", "", "3"),
    ]) {
      expect(visibleRows(revenueRows, key).rows).toEqual([]);
    }
  });

  it("keeps an empty result referentially stable so it cannot churn renders", () => {
    const a = visibleRows(revenueRows, rowsKey("expense", "", "", "", ""));
    const b = visibleRows(revenueRows, rowsKey("overdue", "", "", "", ""));
    expect(a.rows).toBe(b.rows);
  });

  it("cannot confuse two different filter combinations", () => {
    expect(rowsKey("revenue", "1 2", "", "", "")).not.toBe(rowsKey("revenue", "1", "2", "", ""));
    expect(rowsKey("a", "", "", "", "")).not.toBe(rowsKey("", "a", "", "", ""));
  });

  it("rejects both stale and aborted responses", () => {
    const live = new AbortController();
    expect(isCurrentRowsRequest(4, 4, live.signal)).toBe(true);
    expect(isCurrentRowsRequest(3, 4, live.signal)).toBe(false);
    live.abort();
    expect(isCurrentRowsRequest(4, 4, live.signal)).toBe(false);
  });
});
