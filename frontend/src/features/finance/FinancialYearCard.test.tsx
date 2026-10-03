// @vitest-environment happy-dom
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ToastProvider } from "../../components/ui/Toast.tsx";
import { FinancialYearCard } from "./FinancialYearCard.tsx";
import * as whtApi from "./whtApi.ts";
import type { FinanceSettings } from "./whtTypes.ts";

vi.mock("./whtApi.ts", () => ({ saveSettings: vi.fn() }));
const save = vi.mocked(whtApi.saveSettings);

const settings = (over: Partial<FinanceSettings> = {}): FinanceSettings => ({
  financialYearStartMonth: 7, whtRatesConfirmedAt: null, whtRatesConfirmedByName: null, goLiveDate: "2026-08-15T00:00:00",
  currentFinancialYear: "2026-27", concurrencyToken: "tok", ...over,
});

const show = (value: FinanceSettings, onSaved = vi.fn(), disabled = false) => {
  render(<ToastProvider><FinancialYearCard settings={value} onSaved={onSaved} disabled={disabled} /></ToastProvider>);
  return onSaved;
};

const goLiveField = () => screen.getByRole("button", { name: /Go-live date/ });
const pickDay = (day: string) => {
  fireEvent.click(goLiveField());
  fireEvent.click(screen.getByRole("button", { name: day }));
};
const saveButton = () => screen.getByRole("button", { name: "Save" }) as HTMLButtonElement;

beforeEach(() => {
  vi.resetAllMocks();
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date("2026-10-02T07:00:00Z"));
  Element.prototype.scrollIntoView = () => {};
  save.mockResolvedValue(settings());
});

afterEach(() => {
  cleanup();
  vi.useRealTimers();
});

describe("Financial year card", () => {
  it("shows the year start, the current year, the go-live date with its helper, and Save", () => {
    show(settings());
    expect(screen.getByText("Financial year")).toBeTruthy();
    expect(screen.getByRole("combobox", { name: /Year starts in/ }).textContent).toContain("July");
    expect(screen.getByText("Current year: 2026-27")).toBeTruthy();
    expect(goLiveField().textContent).toContain("Aug 15, 2026");
    expect(screen.getByText("Opening balances are the position at the start of this day.")).toBeTruthy();
    expect(saveButton()).toBeTruthy();
  });

  it("marks the go-live date required, and gives it no Clear button, once a date is saved", () => {
    show(settings());
    expect(document.body.textContent).toContain("Go-live date *");
    expect(screen.queryByRole("button", { name: /Clear/ })).toBeNull();
  });

  it("lets the year be saved with no go-live date when none is saved yet, and sends none", async () => {
    const onSaved = show(settings({ goLiveDate: null }));
    expect(document.body.textContent).toContain("Go-live date");
    expect(document.body.textContent).not.toContain("Go-live date *");
    fireEvent.click(screen.getByRole("combobox", { name: /Year starts in/ }));
    fireEvent.click(screen.getByRole("option", { name: "January" }));
    fireEvent.click(saveButton());
    await waitFor(() => expect(save).toHaveBeenCalledTimes(1));
    expect(save.mock.calls[0]![0]).toEqual({
      financialYearStartMonth: 1, goLiveDate: null, markRatesConfirmed: false, clearRatesConfirmation: false, concurrencyToken: "tok",
    });
    expect(await screen.findByText("Financial year saved.")).toBeTruthy();
    expect(onSaved).toHaveBeenCalledTimes(1);
    expect(screen.queryByText("Change the go-live date?")).toBeNull();
  });

  it("sets the first go-live date without a confirm, and sends it as a plain day", async () => {
    show(settings({ goLiveDate: null }));
    pickDay("October 1, 2026");
    fireEvent.click(saveButton());
    await waitFor(() => expect(save).toHaveBeenCalledTimes(1));
    expect(save.mock.calls[0]![0]).toMatchObject({ goLiveDate: "2026-10-01", financialYearStartMonth: 7 });
    expect(screen.queryByText("Change the go-live date?")).toBeNull();
  });

  it("does not offer a day after today", () => {
    show(settings({ goLiveDate: null }));
    fireEvent.click(goLiveField());
    expect((screen.getByRole("button", { name: "October 3, 2026" }) as HTMLButtonElement).disabled).toBe(true);
    expect((screen.getByRole("button", { name: "October 2, 2026" }) as HTMLButtonElement).disabled).toBe(false);
  });

  it("saves the year without a confirm and sends no date when the go-live date is untouched", async () => {
    show(settings());
    fireEvent.click(screen.getByRole("combobox", { name: /Year starts in/ }));
    fireEvent.click(screen.getByRole("option", { name: "April" }));
    fireEvent.click(saveButton());
    await waitFor(() => expect(save).toHaveBeenCalledTimes(1));
    expect(save.mock.calls[0]![0]).toMatchObject({ financialYearStartMonth: 4, goLiveDate: null });
    expect(screen.queryByText("Change the go-live date?")).toBeNull();
  });

  it("asks before changing a saved go-live date, naming the new and the old date", async () => {
    show(settings());
    pickDay("August 20, 2026");
    fireEvent.click(saveButton());
    expect(await screen.findByText("Change the go-live date?")).toBeTruthy();
    expect(screen.getByText("Reports will start from Aug 20, 2026 instead of Aug 15, 2026. Allowed only when nothing is recorded before the new date.")).toBeTruthy();
    expect(save).not.toHaveBeenCalled();
  });

  it("cancel closes only the question and keeps the typed date", async () => {
    show(settings());
    pickDay("August 20, 2026");
    fireEvent.click(saveButton());
    const confirm = await screen.findByRole("dialog");
    fireEvent.click(within(confirm).getByRole("button", { name: "Cancel" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(goLiveField().textContent).toContain("Aug 20, 2026");
    expect(save).not.toHaveBeenCalled();
  });

  it("sends the new date on Change, then says Financial year saved. and reloads", async () => {
    const onSaved = show(settings());
    pickDay("August 20, 2026");
    fireEvent.click(saveButton());
    fireEvent.click(within(await screen.findByRole("dialog")).getByRole("button", { name: "Change" }));
    await waitFor(() => expect(save).toHaveBeenCalledTimes(1));
    expect(save.mock.calls[0]![0]).toMatchObject({ goLiveDate: "2026-08-20" });
    expect(await screen.findByText("Financial year saved.")).toBeTruthy();
    expect(onSaved).toHaveBeenCalledTimes(1);
  });

  it("shows the server's refusal in a red notice in the card, closes the question and keeps the typed values", async () => {
    const message = "DAMS already holds financial records dated before 20 Aug 2026: 3 expenses, 12 customer receipts. The earliest is dated 25 Jul 2026.";
    save.mockRejectedValue(new Error(message));
    const onSaved = show(settings());
    fireEvent.click(screen.getByRole("combobox", { name: /Year starts in/ }));
    fireEvent.click(screen.getByRole("option", { name: "April" }));
    pickDay("August 20, 2026");
    fireEvent.click(saveButton());
    fireEvent.click(within(await screen.findByRole("dialog")).getByRole("button", { name: "Change" }));
    expect(await screen.findByText(message)).toBeTruthy();
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(goLiveField().textContent).toContain("Aug 20, 2026");
    expect(screen.getByRole("combobox", { name: /Year starts in/ }).textContent).toContain("April");
    expect(onSaved).not.toHaveBeenCalled();
    expect(saveButton().disabled).toBe(false);
  });

  it("shows a refusal on a first date in the card too", async () => {
    save.mockRejectedValue(new Error("Go-live date cannot be in the future."));
    show(settings({ goLiveDate: null }));
    pickDay("October 1, 2026");
    fireEvent.click(saveButton());
    expect(await screen.findByText("Go-live date cannot be in the future.")).toBeTruthy();
  });

  it("closes the question and frees Save once saved, as the card stays on screen while the page reloads", async () => {
    show(settings());
    pickDay("August 20, 2026");
    fireEvent.click(saveButton());
    fireEvent.click(within(await screen.findByRole("dialog")).getByRole("button", { name: "Change" }));
    expect(await screen.findByText("Financial year saved.")).toBeTruthy();
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(saveButton().disabled).toBe(false);
  });

  it("keeps Save off while the page reads the settings again, so a stale version is never sent", () => {
    show(settings(), vi.fn(), true);
    expect(saveButton().disabled).toBe(true);
    fireEvent.click(saveButton());
    expect(save).not.toHaveBeenCalled();
  });

  it("blocks a second click while saving", async () => {
    let finish: () => void = () => {};
    save.mockImplementation(() => new Promise((resolve) => { finish = () => resolve(settings()); }));
    show(settings({ goLiveDate: null }));
    fireEvent.click(saveButton());
    fireEvent.click(saveButton());
    expect(save).toHaveBeenCalledTimes(1);
    expect(saveButton().disabled).toBe(true);
    finish();
  });
});
