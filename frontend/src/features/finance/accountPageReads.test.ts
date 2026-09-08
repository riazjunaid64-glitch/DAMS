import { describe, expect, it, vi } from "vitest";
import { readFinanceAccountsPage } from "./accountPageReads";

const filters = { search: "", status: "all", typeFilter: "", holderFilter: "" };
const ok = (body: unknown) => Promise.resolve(new Response(JSON.stringify(body), { status: 200 }));

describe("Finance Accounts page reads", () => {
  it("loads the page and global summary in a single request on initial load and refresh", async () => {
    const body = {
      items: [{ id: 1, currentBalance: 500 }],
      hasMore: true,
      overview: { totalBalance: 12000, activeAccounts: 205, inactiveAccounts: 3, holderBalances: [] },
    };
    const request = vi.fn<(path: string) => Promise<Response>>(() => ok(body));

    const result = await readFinanceAccountsPage(filters, true, request);

    expect(request).toHaveBeenCalledExactlyOnceWith("/api/finance/accounts/page-with-overview?take=200");
    // The server's global summary is retained even when the account list is filtered or paged.
    expect(result).toEqual(body);
  });

  it("preserves all filters when the combined load is replaced or refreshed", async () => {
    const request = vi.fn<(path: string) => Promise<Response>>(() => ok({ items: [], hasMore: false, overview: { totalBalance: 12000 } }));

    await readFinanceAccountsPage({ search: "Main & Bank", status: "inactive", typeFilter: "2", holderFilter: "Ali Khan" }, true, request);

    expect(request).toHaveBeenCalledTimes(1);
    const url = new URL(request.mock.calls[0][0], "https://test.local");
    expect(url.pathname).toBe("/api/finance/accounts/page-with-overview");
    expect(Object.fromEntries(url.searchParams)).toEqual({
      take: "200", search: "Main & Bank", isActive: "false", type: "2", holder: "Ali Khan",
    });
  });

  it("only requests the filtered page after the overview has loaded", async () => {
    const request = vi.fn<(path: string) => Promise<Response>>(() => ok({ items: [], hasMore: false }));

    await readFinanceAccountsPage({ ...filters, status: "active" }, false, request);

    expect(request).toHaveBeenCalledExactlyOnceWith("/api/finance/accounts?take=200&isActive=true");
  });

  it("propagates failures without issuing duplicate balance requests", async () => {
    const request = vi.fn<(path: string) => Promise<Response>>(() => Promise.resolve(new Response("", { status: 500 })));

    await expect(readFinanceAccountsPage(filters, true, request)).rejects.toThrow("Accounts could not be loaded.");
    expect(request).toHaveBeenCalledTimes(1);
  });
});
