// @vitest-environment happy-dom
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router-dom";
import { ToastProvider } from "../components/ui/Toast.tsx";
import type { User } from "../App.tsx";
import * as whtApi from "../features/finance/whtApi.ts";
import { listRevenueCategories } from "../features/finance/revenueCategoryApi.ts";
import type { FinanceSettings } from "../features/finance/whtTypes.ts";
import FinanceSettingsPage from "./FinanceSettingsPage.tsx";

vi.mock("../features/finance/whtApi.ts", () => ({ getSettings: vi.fn(), listCategories: vi.fn(), saveSettings: vi.fn() }));
vi.mock("../features/finance/revenueCategoryApi.ts", () => ({ listRevenueCategories: vi.fn() }));

const wht = vi.mocked(whtApi);
const admin: User = { userId: "1", role: "Admin", email: "a@b.c" };

const settings = (over: Partial<FinanceSettings> = {}): FinanceSettings => ({
  financialYearStartMonth: 7, whtRatesConfirmedAt: null, whtRatesConfirmedByName: null, goLiveDate: "2026-08-15T00:00:00",
  currentFinancialYear: "2026-27", concurrencyToken: "tok", ...over,
});

const show = () => render(<MemoryRouter><ToastProvider><FinanceSettingsPage user={admin} /></ToastProvider></MemoryRouter>);

beforeEach(() => {
  vi.resetAllMocks();
  wht.getSettings.mockResolvedValue(settings());
  wht.listCategories.mockResolvedValue([]);
  wht.saveSettings.mockResolvedValue(settings());
  vi.mocked(listRevenueCategories).mockResolvedValue([]);
});

afterEach(cleanup);

describe("Finance settings", () => {
  it("has no Opening balances or WHT payable tab any more", async () => {
    show();
    await screen.findAllByRole("tab");
    expect(screen.queryByRole("tab", { name: /Opening balances/ })).toBeNull();
    expect(screen.queryByRole("tab", { name: /WHT payable/ })).toBeNull();
    expect(screen.getAllByRole("tab").map((tab) => tab.textContent)).toEqual([
      "Expense categories & WHT rates0", "Revenue categories0", "Vendors", "Financial year",
    ]);
  });

  it("shows the go-live date on the Financial year tab", async () => {
    show();
    fireEvent.click(await screen.findByRole("tab", { name: /Financial year/ }));
    expect(await screen.findByText("Current year: 2026-27")).toBeTruthy();
    expect(screen.getByRole("button", { name: /Go-live date/ }).textContent).toContain("Aug 15, 2026");
  });

  it("keeps the Rate confirmation button working, without sending a go-live date", async () => {
    show();
    fireEvent.click(await screen.findByRole("tab", { name: /Financial year/ }));
    fireEvent.click(await screen.findByRole("button", { name: "Mark rates as confirmed" }));
    await waitFor(() => expect(wht.saveSettings).toHaveBeenCalledTimes(1));
    expect(wht.saveSettings.mock.calls[0]![0]).toEqual({
      financialYearStartMonth: 7, goLiveDate: null, markRatesConfirmed: true, clearRatesConfirmation: false, concurrencyToken: "tok",
    });
    expect(await screen.findByText("Finance settings saved.")).toBeTruthy();
  });
});
