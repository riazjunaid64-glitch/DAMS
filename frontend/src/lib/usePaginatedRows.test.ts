import { describe, expect, it } from "vitest";
import { rowsKey, visibleRows, type Loaded } from "./usePaginatedRows";

/**
 * The finance dashboard picks its table columns from the active view, and each view's columns read
 * fields only that view's rows have. So rows and columns must never be a render out of step: a
 * revenue row handed to the fixed-asset columns has no whtRate, and the cell that formats it took
 * the whole screen down. These tests pin the rule that prevents it.
 */
describe("usePaginatedRows row/key coupling", () => {
  const revenueRows: Loaded<{ id: number }> = {
    key: rowsKey("revenue", "", "", "", ""),
    rows: [{ id: 1 }, { id: 2 }],
    hasMore: true,
    loading: false,
    error: null,
  };

  it("withholds rows fetched for a different view", () => {
    const assets = rowsKey("assetPurchase", "", "", "", "");
    const shown = visibleRows(revenueRows, assets);

    expect(shown.rows).toEqual([]);
    expect(shown.key).toBe(assets);
    // Reported as loading, because the rows for what is being rendered genuinely have not arrived.
    expect(shown.loading).toBe(true);
    // hasMore belonged to the old view; offering it would page the wrong dataset.
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
    // A separator-joined key would collide here; a serialised one cannot.
    expect(rowsKey("revenue", "1 2", "", "", "")).not.toBe(rowsKey("revenue", "1", "2", "", ""));
    expect(rowsKey("a", "", "", "", "")).not.toBe(rowsKey("", "a", "", "", ""));
  });
});
