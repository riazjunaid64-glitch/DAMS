// @vitest-environment happy-dom
import { act, cleanup, renderHook, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { pageAfterEmptyDelete, usePagedList, type PagedListPage } from "./usePagedList.ts";

function phone(matches: boolean) {
  vi.stubGlobal("matchMedia", (query: string) => ({ matches, media: query, addEventListener: () => {}, removeEventListener: () => {} }));
}

beforeEach(() => phone(false));
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((done) => { resolve = done; });
  return { promise, resolve };
}

describe("pageAfterEmptyDelete", () => {
  it("steps back one page when a delete empties the last page", () => {
    expect(pageAfterEmptyDelete(3, 0)).toBe(2);
    expect(pageAfterEmptyDelete(1, 0)).toBe(1);
    expect(pageAfterEmptyDelete(3, 4)).toBe(3);
  });
});

describe("usePagedList", () => {
  it("replaces rows on desktop, resets to page 1 when the filter changes, and ignores a late answer", async () => {
    const first = deferred<PagedListPage<{ id: string }>>();
    let key = "open";
    const fetchPage = vi.fn((query: { page: number }) => {
      if (key === "open") return first.promise;
      return Promise.resolve({ items: [{ id: query.page === 1 ? "fresh" : "other" }], totalCount: 40, hasMore: true });
    });
    const { result, rerender } = renderHook(
      ({ queryKey }) => usePagedList({ queryKey, fetchPage, pageSize: 20 }),
      { initialProps: { queryKey: "open" } },
    );
    await waitFor(() => expect(fetchPage).toHaveBeenCalled());
    key = "closed";
    rerender({ queryKey: "closed" });
    await waitFor(() => expect(result.current.rows).toEqual([{ id: "fresh" }]));
    expect(result.current.page).toBe(1);
    await act(async () => { first.resolve({ items: [{ id: "stale" }], totalCount: 1, hasMore: false }); });
    expect(result.current.rows).toEqual([{ id: "fresh" }]);

    fetchPage.mockImplementation((query: { page: number }) => Promise.resolve({
      items: [{ id: query.page === 2 ? "page-2" : "fresh" }],
      totalCount: 21,
      hasMore: query.page === 1,
    }));
    act(() => result.current.setPage(2));
    await waitFor(() => expect(result.current.rows).toEqual([{ id: "page-2" }]));
    expect(result.current.pagination.page).toBe(2);
    expect(result.current.pagination.totalCount).toBe(21);
  });

  it("appends on a phone and steps back when a delete empties the last page", async () => {
    phone(true);
    const all = [{ id: 1 }, { id: 2 }];
    let source = all;
    const fetchPage = vi.fn(({ skip, take }: { skip: number; take: number }) => {
      const items = source.slice(skip, skip + take);
      return Promise.resolve({ items, totalCount: source.length, hasMore: skip + items.length < source.length });
    });
    const { result } = renderHook(() => usePagedList({ queryKey: "all", fetchPage, pageSize: 1 }));
    await waitFor(() => expect(result.current.rows).toEqual([{ id: 1 }]));
    act(() => result.current.loadMore());
    await waitFor(() => expect(result.current.rows).toEqual([{ id: 1 }, { id: 2 }]));
    expect(result.current.loadMoreBar.shown).toBe(2);
    expect(result.current.loadMoreBar.total).toBe(2);

    source = [{ id: 1 }];
    act(() => result.current.afterDelete());
    await waitFor(() => expect(result.current.page).toBe(1));
    await waitFor(() => expect(result.current.rows).toEqual([{ id: 1 }]));
  });
});