import { beforeEach, describe, expect, it, vi } from "vitest";

const { apiMock } = vi.hoisted(() => ({ apiMock: vi.fn() }));

vi.mock("../../api/api.ts", () => ({ api: apiMock }));

import { calculateWht } from "./whtApi.ts";

describe("WHT API", () => {
  beforeEach(() => apiMock.mockReset());

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
