import { beforeEach, describe, expect, it, vi } from "vitest";

const { apiMock } = vi.hoisted(() => ({ apiMock: vi.fn() }));

vi.mock("../../api/api.ts", () => ({ api: apiMock }));

import { calculateWht, listVendors } from "./whtApi.ts";

describe("WHT API", () => {
  beforeEach(() => apiMock.mockReset());

  it("asks for one page of vendors with the search and the filer status, and leaves out what is not set", async () => {
    const page = { items: [], hasMore: false, totalCount: 0 };
    apiMock.mockResolvedValue({ ok: true, status: 200, json: vi.fn().mockResolvedValue(page) });
    const controller = new AbortController();

    await expect(listVendors({ search: "steel", filerStatus: "NonFiler", skip: 20, take: 20 }, controller.signal)).resolves.toEqual(page);
    expect(apiMock).toHaveBeenLastCalledWith("/api/finance/vendors?skip=20&take=20&search=steel&filerStatus=NonFiler", { signal: controller.signal });

    await listVendors({ search: "", filerStatus: "", skip: 0, take: 20 });
    expect(apiMock).toHaveBeenLastCalledWith("/api/finance/vendors?skip=0&take=20", { signal: undefined });
  });

  it("passes the caller's abort signal to the preview request", async () => {
    const result = { isWhtApplicable: true, rate: 5, whtAmount: 50, netPaid: 950 };
    apiMock.mockResolvedValue({
      ok: true,
      status: 200,
      json: vi.fn().mockResolvedValue(result),
    });
    const controller = new AbortController();
    const body = {
      categoryId: 3,
      vendorId: 7,
      grossAmount: 1_000,
      date: "2026-08-24",
      excludeExpenseId: null,
      excludeAssetPurchaseId: null,
    };

    await expect(calculateWht(body, controller.signal)).resolves.toEqual(result);
    expect(apiMock).toHaveBeenCalledWith(
      "/api/finance/wht/calculate",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify(body),
        signal: controller.signal,
      }),
    );
  });
});
