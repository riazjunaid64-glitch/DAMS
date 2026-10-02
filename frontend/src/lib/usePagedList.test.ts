// @vitest-environment happy-dom
import { act, cleanup, renderHook, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { isCurrentRowsRequest, pageAfterEmptyDelete, usePagedList, type PagedListPage } from "./usePagedList.ts";

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
  it("keeps the current rows while a filter or page reloads, and ignores a late answer", async () => {
    const first = deferred<PagedListPage<{ id: string }>>();
    const next = deferred<PagedListPage<{ id: string }>>();
    const late = deferred<PagedListPage<{ id: string }>>();
    let key = "open";
    const fetchPage = vi.fn(() => {
      if (key === "open") return first.promise;
      if (key === "closed") return next.promise;
      return late.promise;
    });
    const { result, rerender } = renderHook(
      ({ queryKey }) => usePagedList({ queryKey, fetchPage, pageSize: 20 }),
      { initialProps: { queryKey: "open" } },
    );
    await act(async () => { first.resolve({ items: [{ id: "open-row" }], totalCount: 1, hasMore: false }); });
    await waitFor(() => expect(result.current.rows).toEqual([{ id: "open-row" }]));

    key = "closed";
    rerender({ queryKey: "closed" });
    expect(result.current.rows).toEqual([{ id: "open-row" }]);
    expect(result.current.rowsKey).toBe("open");
    expect(result.current.loading).toBe(true);
    expect(result.current.page).toBe(1);

    key = "later";
    rerender({ queryKey: "later" });
    expect(result.current.rows).toEqual([{ id: "open-row" }]);
    expect(result.current.loading).toBe(true);
    await act(async () => { next.resolve({ items: [{ id: "stale" }], totalCount: 1, hasMore: false }); });
    expect(result.current.rows).toEqual([{ id: "open-row" }]);
    await act(async () => { late.resolve({ items: [{ id: "fresh" }], totalCount: 40, hasMore: true }); });
    await waitFor(() => expect(result.current.rows).toEqual([{ id: "fresh" }]));
    expect(result.current.rowsKey).toBe("later");

    const page = deferred<PagedListPage<{ id: string }>>();
    fetchPage.mockImplementation(() => page.promise);
    act(() => result.current.setPage(2));
    expect(result.current.rows).toEqual([{ id: "fresh" }]);
    expect(result.current.loading).toBe(true);
    await act(async () => { page.resolve({ items: [{ id: "page-2" }], totalCount: 21, hasMore: false }); });
    await waitFor(() => expect(result.current.rows).toEqual([{ id: "page-2" }]));
    expect(result.current.pagination.page).toBe(2);
    expect(result.current.pagination.totalCount).toBe(21);

    const again = deferred<PagedListPage<{ id: string }>>();
    fetchPage.mockImplementation(() => again.promise);
    act(() => result.current.reload());
    expect(result.current.rows).toEqual([{ id: "page-2" }]);
    expect(result.current.loading).toBe(true);
    await act(async () => { again.resolve({ items: [{ id: "reloaded" }], totalCount: 21, hasMore: false }); });
    await waitFor(() => expect(result.current.rows).toEqual([{ id: "reloaded" }]));
  });

  it("keeps the current rows and their query when a filter reload fails", async () => {
    const first = deferred<PagedListPage<{ id: string }>>();
    const fetchPage = vi.fn(() => first.promise);
    const { result, rerender } = renderHook(
      ({ queryKey }) => usePagedList({ queryKey, fetchPage, pageSize: 20 }),
      { initialProps: { queryKey: "open" } },
    );
    await act(async () => { first.resolve({ items: [{ id: "open-row" }], totalCount: 1, hasMore: false }); });
    await waitFor(() => expect(result.current.rows).toEqual([{ id: "open-row" }]));

    fetchPage.mockImplementation(() => Promise.reject(new Error("The list could not be read.")));
    rerender({ queryKey: "closed" });
    expect(result.current.rows).toEqual([{ id: "open-row" }]);
    expect(result.current.loading).toBe(true);
    await waitFor(() => expect(result.current.error).toBe("The list could not be read."));
    expect(result.current.rows).toEqual([{ id: "open-row" }]);
    expect(result.current.rowsKey).toBe("open");
    expect(result.current.loading).toBe(false);
  });

  it("keeps page 1 pagination when page 2 fails", async () => {
    const first = deferred<PagedListPage<{ id: string }>>();
    const fetchPage = vi.fn(() => first.promise);
    const { result } = renderHook(() => usePagedList({ queryKey: "open", fetchPage, pageSize: 20 }));
    await act(async () => { first.resolve({ items: [{ id: "page-1" }], totalCount: 40, hasMore: true }); });
    await waitFor(() => expect(result.current.rows).toEqual([{ id: "page-1" }]));
    expect(result.current.pagination.page).toBe(1);

    fetchPage.mockImplementation(() => Promise.reject(new Error("The list could not be read.")));
    act(() => result.current.setPage(2));
    expect(result.current.rows).toEqual([{ id: "page-1" }]);
    expect(result.current.pagination.page).toBe(1);
    await waitFor(() => expect(result.current.error).toBe("The list could not be read."));
    expect(result.current.rows).toEqual([{ id: "page-1" }]);
    expect(result.current.page).toBe(1);
    expect(result.current.pagination.page).toBe(1);
    expect(result.current.loading).toBe(false);
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

  it("refetches every loaded phone page on reload", async () => {
    phone(true);
    const all = Array.from({ length: 40 }, (_, index) => ({ id: index + 1 }));
    const fetchPage = vi.fn(({ skip, take }: { skip: number; take: number }) => {
      const items = all.slice(skip, skip + take);
      return Promise.resolve({ items, totalCount: all.length, hasMore: skip + items.length < all.length });
    });
    const { result } = renderHook(() => usePagedList({ queryKey: "all", fetchPage, pageSize: 20 }));
    await waitFor(() => expect(result.current.rows).toEqual(all.slice(0, 20)));
    act(() => result.current.loadMore());
    await waitFor(() => expect(result.current.rows).toEqual(all));

    act(() => result.current.reload());
    expect(result.current.rows).toEqual(all);
    await waitFor(() => expect(result.current.loading).toBe(false));
    expect(result.current.rows).toEqual(all);
    const last = fetchPage.mock.calls.at(-1)?.[0] as { skip: number; take: number };
    expect(last.skip).toBe(0);
    expect(last.take).toBe(40);
  });

  it("appends the retried page when a phone load more fails and Try again succeeds", async () => {
    phone(true);
    const page1 = Array.from({ length: 20 }, (_, index) => ({ id: index + 1 }));
    const page2 = Array.from({ length: 20 }, (_, index) => ({ id: index + 21 }));
    let fail = false;
    const fetchPage = vi.fn(({ skip, take }: { skip: number; take: number }) => {
      if (fail) return Promise.reject(new Error("The list could not be read."));
      const items = (skip === 0 ? page1 : page2).slice(0, take);
      return Promise.resolve({ items, totalCount: 40, hasMore: skip === 0 });
    });
    const { result } = renderHook(() => usePagedList({ queryKey: "all", fetchPage, pageSize: 20 }));
    await waitFor(() => expect(result.current.rows).toEqual(page1));

    fail = true;
    act(() => result.current.loadMore());
    await waitFor(() => expect(result.current.error).toBe("The list could not be read."));
    expect(result.current.rows).toEqual(page1);

    fail = false;
    act(() => result.current.reload());
    expect(result.current.rows).toEqual(page1);
    await waitFor(() => expect(result.current.rows).toEqual([...page1, ...page2]));
    expect(result.current.error).toBeNull();
    const last = fetchPage.mock.calls.at(-1)?.[0] as { skip: number; take: number };
    expect(last.skip).toBe(20);
    expect(last.take).toBe(20);
  });

  it("shows the server's message when a load fails", async () => {
    const fetchPage = vi.fn(() => Promise.reject(new Error("The list could not be read.")));
    const { result } = renderHook(() => usePagedList({ queryKey: "all", fetchPage, pageSize: 20 }));
    await waitFor(() => expect(result.current.error).toBe("The list could not be read."));
    expect(result.current.rows).toEqual([]);
  });
});

describe("isCurrentRowsRequest", () => {
  it("accepts only the newest request that has not been aborted", () => {
    const live = new AbortController();
    expect(isCurrentRowsRequest(4, 4, live.signal)).toBe(true);
    expect(isCurrentRowsRequest(3, 4, live.signal)).toBe(false);
    live.abort();
    expect(isCurrentRowsRequest(4, 4, live.signal)).toBe(false);
  });
});