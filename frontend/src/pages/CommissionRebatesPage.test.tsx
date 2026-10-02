// @vitest-environment happy-dom
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { ToastProvider } from "../components/ui/Toast.tsx";
import { PHONE_QUERY } from "../components/ui/useMediaQuery.ts";
import type { User } from "../App.tsx";
import { commissionRebateApi } from "../features/commissionRebates/api.ts";
import type { Commission, CommissionRebateSummary, PagedResult, Partner, Rebate } from "../features/commissionRebates/types.ts";
import CommissionRebatesPage from "./CommissionRebatesPage.tsx";

vi.mock("../features/commissionRebates/api.ts", () => ({
  commissionRebateApi: {
    summary: vi.fn(),
    commissions: vi.fn(),
    rebates: vi.fn(),
    partners: vi.fn(),
    partner: vi.fn(),
    savePartner: vi.fn(),
    partnerStatus: vi.fn(),
  },
}));

const api = vi.mocked(commissionRebateApi);

const summary: CommissionRebateSummary = {
  accruedCommission: 1_274_000, payableCommission: 1_073_000, commissionPaid: 201_000, commissionReversalRequired: 0,
  rebatesGranted: 30_000, rebatesAppliedOrPaid: 0, rebateReversalRequired: 0, activePartners: 5, pendingRecords: 4,
};

const commission = (over: Partial<Commission> = {}) => ({
  id: 1, bookingId: 13, bookingReference: "BK-000013", partnerId: 3, partnerName: "test", ruleNameSnapshot: null,
  basisAmount: 450_000, calculationBasis: "NetSalePriceAfterDiscount", finalAmount: 1_000, paidAmount: 1_000,
  outstandingAmount: 0, recoveryRequiredAmount: 0, status: "Paid", ...over,
}) as unknown as Commission;

const rebate = (over: Partial<Rebate> = {}) => ({
  id: 5, bookingId: 12, bookingReference: "BK-000012", customerName: "Junaid Riaz satt", reason: "Fixed amount of 10,000",
  method: "OutstandingBalanceReduction", finalAmount: 10_000, appliedOrPaidAmount: 0, outstandingAmount: 10_000,
  recoveryRequiredAmount: 0, status: "Pending", ...over,
}) as unknown as Rebate;

const partner = (over: Partial<Partner> = {}) => ({
  id: 4, name: "ali", partnerType: "Dealer", internalCode: "PTR-0004", contactPerson: null, phone: "0312 3397373", email: null,
  address: null, cnic: null, ntn: null, registrationNumber: null, bankName: null, accountTitle: null, accountNumber: null,
  iban: null, notes: null, isActive: true, attributionCount: 0, commissionCount: 1, concurrencyToken: "tok-4", ...over,
}) as unknown as Partner;

const page = <T,>(items: T[], totalCount = items.length): PagedResult<T> => ({ items, hasMore: totalCount > items.length, totalCount });

const admin: User = { userId: "1", role: "Admin", email: "a@b.c" };

function Probe() {
  const location = useLocation();
  return <p data-testid="where">{location.pathname}{location.search}</p>;
}

function show(user: User | null = admin, path = "/finance/commissions-rebates") {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <ToastProvider>
        <Routes>
          <Route path="/finance/commissions-rebates" element={<><CommissionRebatesPage user={user} /><Probe /></>} />
          <Route path="/" element={<p>Home page</p>} />
        </Routes>
      </ToastProvider>
    </MemoryRouter>,
  );
}

function stubMedia(phone: boolean) {
  vi.stubGlobal("matchMedia", (query: string) => ({
    matches: phone && query === PHONE_QUERY,
    media: query,
    addEventListener: () => {},
    removeEventListener: () => {},
  }));
}

const table = () => within(screen.getByRole("table"));
const here = () => screen.getByTestId("where").textContent;
const lastCall = (mock: { mock: { calls: unknown[][] } }) => mock.mock.calls.at(-1)!;

beforeEach(() => {
  vi.resetAllMocks();
  stubMedia(false);
  Element.prototype.scrollIntoView = () => {};
  api.summary.mockResolvedValue(summary);
  api.commissions.mockResolvedValue(page([commission()]));
  api.rebates.mockResolvedValue(page([rebate()]));
  api.partners.mockResolvedValue(page([partner()]));
});

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("Commissions & Rebates: frame, cards and tabs", () => {
  it("has the title and three tabs, opens on Commissions, and has no Overview, Rules or back link", async () => {
    show();
    expect(await screen.findByRole("heading", { name: "Commissions & Rebates" })).toBeTruthy();
    expect(screen.getAllByRole("tab").map((tab) => tab.textContent)).toEqual(["Commissions", "Rebates", "Partners"]);
    expect(screen.getByRole("tab", { name: "Commissions" }).getAttribute("aria-selected")).toBe("true");
    expect(screen.queryByRole("tab", { name: /Overview|Rules/ })).toBeNull();
    expect(screen.queryByText(/Finance dashboard|Finance accounts|Controlled workflow/)).toBeNull();
    expect(screen.queryByRole("button", { name: "Add partner" })).toBeNull();
    await screen.findAllByText("BK-000013");
  });

  it("shows the four cards from the server's figures", async () => {
    show();
    expect(await screen.findByText("Rs 1,073,000")).toBeTruthy();
    expect(screen.getByText("of Rs 1,274,000 agreed")).toBeTruthy();
    expect(screen.getByText("Rs 201,000")).toBeTruthy();
    expect(screen.getByText("Rs 30,000")).toBeTruthy();
    expect(screen.getByText("Rs 0 given so far")).toBeTruthy();
    expect(screen.getByText("Commission owed")).toBeTruthy();
    expect(screen.getByText("Commission paid")).toBeTruthy();
    expect(screen.getAllByText("Rebates").length).toBe(2);
    expect(screen.getByText("Reversal required")).toBeTruthy();
  });

  it("adds the two reversal fields on the last card, and turns it red only above zero", async () => {
    show();
    await screen.findByText("Rs 1,073,000");
    expect(screen.getByText("Reversal required").className).not.toContain("text-danger");
    cleanup();

    api.summary.mockResolvedValue({ ...summary, commissionReversalRequired: 100_000.1, rebateReversalRequired: 25_000.2 });
    show();
    expect(await screen.findByText("Rs 125,000.3")).toBeTruthy();
    expect(screen.getByText("Reversal required").className).toContain("text-danger");
  });

  it("never shows Rs 0 before the figures arrive, then shows dashes and Try again when they fail", async () => {
    let fail: (error: Error) => void = () => {};
    api.summary.mockReturnValueOnce(new Promise((_, reject) => { fail = reject; }));
    show();
    await screen.findByRole("heading", { name: "Commissions & Rebates" });
    expect(screen.queryByText("Rs 1,073,000")).toBeNull();
    expect(within(screen.getByText("Reversal required").parentElement!).queryByText("Rs 0")).toBeNull();
    expect(document.querySelectorAll(".animate-pulse").length).toBeGreaterThan(0);

    fail(new Error("down"));
    expect(await screen.findByText("The totals could not be loaded.")).toBeTruthy();
    expect(screen.getAllByText("—").length).toBe(4);

    fireEvent.click(screen.getByRole("button", { name: "Try again" }));
    expect(await screen.findByText("Rs 1,073,000")).toBeTruthy();
    expect(screen.queryByText("The totals could not be loaded.")).toBeNull();
  });

  it("drops the notes and the red tone when a reload of the cards fails", async () => {
    api.summary.mockResolvedValueOnce({ ...summary, commissionReversalRequired: 5_000 });
    api.partners.mockResolvedValue(page([partner()]));
    api.partnerStatus.mockResolvedValue(partner({ isActive: false }));
    show(admin, "/finance/commissions-rebates?tab=partners");
    expect(await screen.findByText("of Rs 1,274,000 agreed")).toBeTruthy();
    expect(screen.getByText("Reversal required").className).toContain("text-danger");

    api.summary.mockRejectedValue(new Error("down"));
    fireEvent.click((await screen.findAllByRole("button", { name: "More for ali" }))[0]!);
    fireEvent.click(await screen.findByRole("menuitem", { name: "Deactivate" }));
    const dialog = within(await screen.findByRole("dialog"));
    fireEvent.change(dialog.getByLabelText(/Reason/), { target: { value: "Gone" } });
    fireEvent.click(dialog.getByRole("button", { name: "Deactivate" }));

    expect(await screen.findByText("The totals could not be loaded.")).toBeTruthy();
    expect(screen.queryByText("of Rs 1,274,000 agreed")).toBeNull();
    expect(screen.queryByText("Rs 0 given so far")).toBeNull();
    expect(screen.getByText("Reversal required").className).not.toContain("text-danger");
  });

  it("opens the tab named in the address, and writes the tab when it changes", async () => {
    show(admin, "/finance/commissions-rebates?tab=rebates");
    expect((await screen.findAllByText("Junaid Riaz satt")).length).toBeGreaterThan(0);
    expect(screen.getByRole("tab", { name: "Rebates" }).getAttribute("aria-selected")).toBe("true");
    expect(api.commissions).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole("tab", { name: "Partners" }));
    await waitFor(() => expect(here()).toBe("/finance/commissions-rebates?tab=partners"));
    expect(await screen.findByRole("button", { name: "Add partner" })).toBeTruthy();
  });

  it("falls back to Commissions for an unknown tab", async () => {
    show(admin, "/finance/commissions-rebates?tab=rules");
    expect(await screen.findAllByText("BK-000013")).toBeTruthy();
    expect(screen.getByRole("tab", { name: "Commissions" }).getAttribute("aria-selected")).toBe("true");
  });

  it("shows Add partner only on the Partners tab, and Add on a phone", async () => {
    stubMedia(true);
    show();
    await screen.findAllByText("BK-000013");
    expect(screen.queryByRole("button", { name: /^Add/ })).toBeNull();
    fireEvent.click(screen.getByRole("combobox", { name: /Show/ }));
    fireEvent.click(screen.getByRole("option", { name: "Partners" }));
    expect(await screen.findByRole("button", { name: "Add" })).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Add partner" })).toBeNull();
  });

  it("offers the sections as a Show dropdown on a phone", async () => {
    stubMedia(true);
    show();
    await screen.findAllByText("BK-000013");
    expect(screen.queryByRole("tab")).toBeNull();
    expect(screen.getByRole("combobox", { name: /Show/ })).toBeTruthy();
  });

  it("waits for the signed-in user before deciding, and sends a user without Finance home", async () => {
    show(null);
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(screen.queryByText("Home page")).toBeNull();
    expect(api.summary).not.toHaveBeenCalled();
    cleanup();

    show({ userId: "2", role: "SalesEmployee", email: "s@b.c" });
    expect(await screen.findByText("Home page")).toBeTruthy();
    expect(api.summary).not.toHaveBeenCalled();
    expect(api.commissions).not.toHaveBeenCalled();
  });
});

describe("Commissions tab", () => {
  it("lists today's eight columns with the booking as a link and the rule or Manual with its basis", async () => {
    api.commissions.mockResolvedValue(page([
      commission(),
      commission({ id: 2, bookingId: 8, bookingReference: "BK-000008", partnerName: "Junaid Riaz 101", ruleNameSnapshot: "Standard agency rate", calculationBasis: "AgreedSalePrice", basisAmount: 6_200_000, finalAmount: 62_000, paidAmount: 0, outstandingAmount: 62_000, status: "Pending" }),
    ]));
    show();
    await screen.findAllByText("BK-000013");
    const view = table();
    expect(view.getAllByRole("columnheader").map((header) => header.textContent)).toEqual(
      ["Booking", "Partner", "Rule / basis", "Commission", "Paid", "Remaining", "Recovery", "Status"],
    );
    expect(view.getByRole("link", { name: "BK-000013" }).getAttribute("href")).toBe("/confirmed-bookings/13");
    expect(view.getByText("Manual")).toBeTruthy();
    expect(view.getByText("Rs 450,000 · Net sale price")).toBeTruthy();
    expect(view.getByText("Standard agency rate")).toBeTruthy();
    expect(view.getByText("Rs 6,200,000 · Agreed sale price")).toBeTruthy();
    const [, paid, pending] = view.getAllByRole("row");
    expect(within(paid!).getByText("Paid")).toBeTruthy();
    expect(within(pending!).getByText("Pending")).toBeTruthy();
    expect(screen.queryByRole("button", { name: /Pay|Edit|Cancel|Reverse/ })).toBeNull();
  });

  it("filters by status with today's values, and shows Reset only while a filter is set", async () => {
    show();
    await screen.findAllByText("BK-000013");
    expect(screen.queryByRole("button", { name: "Reset" })).toBeNull();
    expect(lastCall(api.commissions).slice(0, 3)).toEqual(["", 0, 20]);

    fireEvent.click(screen.getByRole("combobox", { name: /Status/ }));
    expect(screen.getAllByRole("option").map((option) => option.textContent)).toEqual(
      ["All statuses", "Pending", "Paid", "Cancelled", "Reversal required", "Reversed"],
    );
    fireEvent.click(screen.getByRole("option", { name: "Reversal required" }));
    await waitFor(() => expect(lastCall(api.commissions)[0]).toBe("ReversalRequired"));
    fireEvent.click(await screen.findByRole("button", { name: "Reset" }));
    await waitFor(() => expect(lastCall(api.commissions)[0]).toBe(""));
    expect(screen.queryByRole("button", { name: "Reset" })).toBeNull();
  });

  it("calls the server 20 at a time and shows numbered pages with the total", async () => {
    api.commissions.mockImplementation(async (_status, skip) =>
      page(Array.from({ length: 20 }, (_, index) => commission({ id: skip! + index + 1, bookingReference: `BK-${skip! + index + 1}` })), 45));
    show();
    const summaryLine = (text: string) => screen.findByText((_, element) => element?.tagName === "P" && element.textContent === text);
    expect(await summaryLine("Showing 1–20 of 45 entries")).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: "Page 2" }));
    await waitFor(() => expect(lastCall(api.commissions).slice(1, 3)).toEqual([20, 20]));
    expect(await summaryLine("Showing 21–40 of 45 entries")).toBeTruthy();
  });

  it("explains an empty list, differently with a filter set", async () => {
    api.commissions.mockResolvedValue(page([]));
    show();
    expect(await screen.findByText("No commissions yet. Add them from a booking.")).toBeTruthy();

    fireEvent.click(screen.getByRole("combobox", { name: /Status/ }));
    fireEvent.click(screen.getByRole("option", { name: "Paid" }));
    expect(await screen.findByText("No commissions match the current filter.")).toBeTruthy();
  });

  it("shows grey rows while the first page loads", async () => {
    api.commissions.mockReturnValue(new Promise(() => {}));
    show();
    await screen.findByRole("heading", { name: "Commissions & Rebates" });
    expect(table().queryByText("BK-000013")).toBeNull();
    expect(document.querySelectorAll("tbody .animate-pulse").length).toBeGreaterThan(0);
  });

  it("shows the error with Try again, and loads again when pressed", async () => {
    api.commissions.mockRejectedValueOnce(new Error("boom"));
    show();
    expect(await screen.findByText("Financial records could not be loaded.")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Try again" }));
    expect((await screen.findAllByText("BK-000013")).length).toBeGreaterThan(0);
    expect(screen.queryByText("Financial records could not be loaded.")).toBeNull();
  });

  it("on a phone shows the card with Amount, Paid, Remaining, and Recovery only above zero", async () => {
    stubMedia(true);
    api.commissions.mockResolvedValue(page([
      commission(),
      commission({ id: 2, bookingId: 11, bookingReference: "BK-000011", partnerName: "Junaid Riaz", status: "ReversalRequired", recoveryRequiredAmount: 100_000 }),
    ]));
    show();
    await screen.findAllByText("BK-000011");
    const cards = screen.getAllByRole("article");
    expect(cards.length).toBe(2);
    expect(within(cards[0]!).getByText("Amount")).toBeTruthy();
    expect(within(cards[0]!).getByText("Remaining")).toBeTruthy();
    expect(within(cards[0]!).queryByText(/Recovery/)).toBeNull();
    expect(within(cards[1]!).getByText("Recovery Rs 100,000").className).toContain("text-danger");
    expect(within(cards[1]!).getByText("Reversal required")).toBeTruthy();
    expect(screen.getByText("Showing 2 of 2 entries")).toBeTruthy();
  });

  it("on a phone loads the next 20 with Load more", async () => {
    stubMedia(true);
    api.commissions.mockImplementation(async (_status, skip) =>
      page(Array.from({ length: 20 }, (_, index) => commission({ id: skip! + index + 1, bookingReference: `BK-${skip! + index + 1}` })), 45));
    show();
    fireEvent.click(await screen.findByRole("button", { name: /Load more/ }));
    await waitFor(() => expect(lastCall(api.commissions)[1]).toBe(20));
    expect(await screen.findByText("Showing 40 of 45 entries")).toBeTruthy();
  });
});

describe("Rebates tab", () => {
  const open = () => show(admin, "/finance/commissions-rebates?tab=rebates");

  it("lists today's eight columns, with the way a rebate is given in plain words", async () => {
    open();
    await screen.findAllByText("BK-000012");
    const view = table();
    expect(view.getAllByRole("columnheader").map((header) => header.textContent)).toEqual(
      ["Booking", "Customer", "Reason / method", "Rebate", "Given", "Remaining", "Recovery", "Status"],
    );
    expect(view.getByRole("link", { name: "BK-000012" }).getAttribute("href")).toBe("/confirmed-bookings/12");
    expect(view.getByText("Fixed amount of 10,000")).toBeTruthy();
    expect(view.getByText("Reduce the outstanding balance")).toBeTruthy();
    expect(view.getByText("Pending")).toBeTruthy();
  });

  it("offers Applied in its filter, and sends the chosen status", async () => {
    open();
    await screen.findAllByText("BK-000012");
    fireEvent.click(screen.getByRole("combobox", { name: /Status/ }));
    expect(screen.getAllByRole("option").map((option) => option.textContent)).toEqual(
      ["All statuses", "Pending", "Applied", "Paid", "Cancelled", "Reversal required", "Reversed"],
    );
    fireEvent.click(screen.getByRole("option", { name: "Applied" }));
    await waitFor(() => expect(lastCall(api.rebates)[0]).toBe("Applied"));
  });

  it("has its own empty texts and error", async () => {
    api.rebates.mockResolvedValueOnce(page([]));
    open();
    expect(await screen.findByText("No rebates yet. Add them from a booking.")).toBeTruthy();
    cleanup();

    api.rebates.mockRejectedValueOnce(new Error("boom"));
    open();
    expect(await screen.findByText("Financial records could not be loaded.")).toBeTruthy();
  });

  it("on a phone shows the way line and a red Recovery above zero", async () => {
    stubMedia(true);
    api.rebates.mockResolvedValue(page([rebate({ recoveryRequiredAmount: 5_000, status: "ReversalRequired" })]));
    open();
    await screen.findAllByText("BK-000012");
    const card = screen.getByRole("article");
    expect(within(card).getByText("Reduce the outstanding balance")).toBeTruthy();
    expect(within(card).getByText("Rebate")).toBeTruthy();
    expect(within(card).getByText("Given")).toBeTruthy();
    expect(within(card).getByText("Recovery Rs 5,000").className).toContain("text-danger");
  });
});

describe("Partners tab", () => {
  const open = (path = "/finance/commissions-rebates?tab=partners") => show(admin, path);

  it("lists the directory with contact, tax id and how many commissions each is used in", async () => {
    api.partners.mockResolvedValue(page([
      partner(),
      partner({ id: 5, name: "Khan Estate Agency", internalCode: "PTR-0005", partnerType: "Agency", contactPerson: "Imran Khan", phone: "0333 5512098", cnic: "61101-4455123-7", commissionCount: 2 }),
      partner({ id: 6, name: "Junaid Riaz", internalCode: "PTR-0002", phone: null, commissionCount: 0 }),
    ]));
    open();
    await screen.findAllByText("Khan Estate Agency");
    const view = table();
    expect(view.getAllByRole("columnheader").map((header) => header.textContent)).toEqual(
      ["Partner", "Type", "Contact", "CNIC / NTN", "Used in", "Status", "Actions"],
    );
    expect(view.getByText("PTR-0004")).toBeTruthy();
    expect(view.getByText("0312 3397373")).toBeTruthy();
    expect(view.getByText("Imran Khan")).toBeTruthy();
    expect(view.getByText("61101-4455123-7")).toBeTruthy();
    expect(view.getByText("1 commission")).toBeTruthy();
    expect(view.getByText("2 commissions")).toBeTruthy();
    expect(view.getByText("0 commissions")).toBeTruthy();
    expect(view.queryByText(/attribution/i)).toBeNull();
    expect(view.getAllByText("Active").length).toBe(3);
  });

  it("opens on Active partners, with Reset only once the search or status moves off that", async () => {
    open();
    await screen.findAllByText("ali");
    expect(lastCall(api.partners).slice(0, 3)).toEqual(["", true, 0]);
    expect(screen.queryByRole("button", { name: "Reset" })).toBeNull();

    fireEvent.click(screen.getByRole("combobox", { name: /Status/ }));
    expect(screen.getAllByRole("option").map((option) => option.textContent)).toEqual(["All", "Active", "Inactive"]);
    fireEvent.click(screen.getByRole("option", { name: "All" }));
    await waitFor(() => expect(lastCall(api.partners)[1]).toBeUndefined());
    expect(await screen.findByRole("button", { name: "Reset" })).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: "Reset" }));
    await waitFor(() => expect(lastCall(api.partners)[1]).toBe(true));
    expect(screen.queryByRole("button", { name: "Reset" })).toBeNull();
  });

  it("searches by name, code, contact or tax id, and goes back to the first page", async () => {
    open();
    await screen.findAllByText("ali");
    const [box] = screen.getAllByRole("searchbox", { name: "Partner, code, contact or tax ID" });
    fireEvent.change(box!, { target: { value: "Imran" } });
    await waitFor(() => expect(lastCall(api.partners).slice(0, 3)).toEqual(["Imran", true, 0]), { timeout: 2000 });
    expect(await screen.findByRole("button", { name: "Reset" })).toBeTruthy();
  });

  it("runs the search 250 ms after typing stops", async () => {
    open();
    await screen.findAllByText("ali");
    const calls = api.partners.mock.calls.length;
    vi.useFakeTimers({ toFake: ["setTimeout", "clearTimeout"] });
    try {
      fireEvent.change(screen.getAllByRole("searchbox", { name: "Partner, code, contact or tax ID" })[0]!, { target: { value: "Imran" } });
      await act(async () => { await vi.advanceTimersByTimeAsync(240); });
      expect(api.partners.mock.calls.length).toBe(calls);
      await act(async () => { await vi.advanceTimersByTimeAsync(20); });
      expect(lastCall(api.partners)[0]).toBe("Imran");
    } finally {
      vi.useRealTimers();
    }
  });

  it("explains an empty list and an error", async () => {
    api.partners.mockResolvedValueOnce(page([]));
    open();
    expect(await screen.findByText("No partners match the current filters.")).toBeTruthy();
    cleanup();

    api.partners.mockRejectedValueOnce(new Error("boom"));
    open();
    expect(await screen.findByText("Partners could not be loaded.")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Try again" }));
    expect(await screen.findAllByText("ali")).toBeTruthy();
  });

  it("on a phone shows the partner as a card with the pencil and ⋯ beside it", async () => {
    stubMedia(true);
    open();
    await screen.findAllByText("ali");
    const card = screen.getByRole("article");
    expect(within(card).getByText("Dealer · PTR-0004 · 1 commission")).toBeTruthy();
    expect(within(card).getByText("0312 3397373")).toBeTruthy();
    expect(within(card).getByText("CNIC / NTN —")).toBeTruthy();
    expect(within(card).getByRole("button", { name: "Edit ali" })).toBeTruthy();

    fireEvent.click(within(card).getByRole("button", { name: "More for ali" }));
    expect(await screen.findByText("ali · PTR-0004")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Deactivate partner" })).toBeTruthy();
  });
});

describe("Add and edit a partner", () => {
  const open = (path = "/finance/commissions-rebates?tab=partners") => show(admin, path);
  const saveButton = () => screen.getByRole("button", { name: "Save" }) as HTMLButtonElement;

  it("keeps Save off until the name is filled, makes the code on Add, and sends empty fields as null", async () => {
    api.savePartner.mockResolvedValue(partner());
    open();
    fireEvent.click(await screen.findByRole("button", { name: "Add partner" }));
    const dialog = within(await screen.findByRole("dialog"));
    expect(dialog.getByText("Add partner")).toBeTruthy();
    expect(dialog.getAllByRole("heading", { level: 3 }).map((heading) => heading.textContent)).toEqual(["Partner", "Tax", "Bank", "Notes"]);
    expect((dialog.getByPlaceholderText("Made automatically") as HTMLInputElement).value).toBe("");
    expect(saveButton().disabled).toBe(true);

    fireEvent.change(dialog.getByLabelText(/^Name/), { target: { value: "  New Agency  " } });
    expect(saveButton().disabled).toBe(false);
    fireEvent.click(saveButton());

    await waitFor(() => expect(api.savePartner).toHaveBeenCalledTimes(1));
    expect(api.savePartner.mock.calls[0]![0]).toEqual({
      name: "New Agency", partnerType: "Broker", internalCode: null, contactPerson: null, phone: null, email: null, address: null,
      cnic: null, ntn: null, registrationNumber: null, bankName: null, accountTitle: null, accountNumber: null, iban: null, notes: null,
    });
    expect(api.savePartner.mock.calls[0]![1]).toBeUndefined();
    expect(await screen.findByText("Partner saved.")).toBeTruthy();
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    // The list and the cards are read again.
    await waitFor(() => expect(api.partners.mock.calls.length).toBeGreaterThan(1));
    await waitFor(() => expect(api.summary.mock.calls.length).toBeGreaterThan(1));
  });

  it("has all fifteen fields, with the limits the server enforces", async () => {
    open();
    fireEvent.click(await screen.findByRole("button", { name: "Add partner" }));
    const dialog = within(await screen.findByRole("dialog"));
    const limits: [RegExp, number][] = [
      [/^Name/, 200], [/Partner code/, 80], [/Contact person/, 200], [/^Phone/, 50], [/^Email/, 200], [/Address/, 500],
      [/^CNIC/, 50], [/NTN \/ tax number/, 80], [/Registration number/, 100], [/Bank name/, 150], [/Account title/, 150],
      [/Account number/, 100], [/^IBAN/, 100], [/^Notes/, 2000],
    ];
    for (const [label, max] of limits) expect(dialog.getByLabelText(label).getAttribute("maxlength")).toBe(String(max));
    expect(dialog.getByRole("combobox", { name: /Type/ })).toBeTruthy();
    expect(dialog.getAllByText("(optional)").length).toBe(13);
  });

  it("keeps the popup open with what was typed and the server's message when the save is refused", async () => {
    api.savePartner.mockRejectedValue(new Error("A partner with this CNIC already exists."));
    open();
    fireEvent.click(await screen.findByRole("button", { name: "Add partner" }));
    const dialog = within(await screen.findByRole("dialog"));
    fireEvent.change(dialog.getByLabelText(/^Name/), { target: { value: "Dup" } });
    fireEvent.change(dialog.getByLabelText(/^CNIC/), { target: { value: "61101-4455123-7" } });
    fireEvent.click(saveButton());

    expect(await dialog.findByText("A partner with this CNIC already exists.")).toBeTruthy();
    expect((dialog.getByLabelText(/^Name/) as HTMLInputElement).value).toBe("Dup");
    expect((dialog.getByLabelText(/^CNIC/) as HTMLInputElement).value).toBe("61101-4455123-7");
    expect(saveButton().disabled).toBe(false);
    expect(screen.queryByText("Partner saved.")).toBeNull();
  });

  it("opens Edit from the pencil with the partner filled in, the code required, and the concurrency token sent", async () => {
    api.partners.mockResolvedValue(page([partner({ id: 7, name: "Junaid Riaz", internalCode: "101", commissionCount: 3, contactPerson: "Junaid Riaz", address: "Faisal Hills, Taxila", bankName: "Meezan Bank" })]));
    api.savePartner.mockResolvedValue(partner());
    open();
    fireEvent.click((await screen.findAllByRole("button", { name: "Edit Junaid Riaz" }))[0]!);
    const dialog = within(await screen.findByRole("dialog"));
    expect(dialog.getByText("Edit partner")).toBeTruthy();
    expect(dialog.getByText("Junaid Riaz · 3 commissions")).toBeTruthy();
    expect((dialog.getByLabelText(/^Name/) as HTMLInputElement).value).toBe("Junaid Riaz");
    expect((dialog.getByLabelText(/Partner code/) as HTMLInputElement).value).toBe("101");
    expect((dialog.getByLabelText(/Address/) as HTMLTextAreaElement).value).toBe("Faisal Hills, Taxila");
    expect((dialog.getByLabelText(/Bank name/) as HTMLInputElement).value).toBe("Meezan Bank");

    fireEvent.change(dialog.getByLabelText(/Partner code/), { target: { value: " " } });
    expect(saveButton().disabled).toBe(true);
    fireEvent.change(dialog.getByLabelText(/Partner code/), { target: { value: "101-A" } });
    fireEvent.click(saveButton());

    await waitFor(() => expect(api.savePartner).toHaveBeenCalledTimes(1));
    const [body, id] = api.savePartner.mock.calls[0]!;
    expect(id).toBe(7);
    expect(body).toMatchObject({ name: "Junaid Riaz", internalCode: "101-A", concurrencyToken: "tok-4", bankName: "Meezan Bank", iban: null });
  });

  it("opens exactly the partner named by ?partner=, wherever it is in the directory, and drops the parameter", async () => {
    api.partner.mockResolvedValue(partner({ id: 9, name: "Far Away", internalCode: "PTR-0009" }));
    open("/finance/commissions-rebates?tab=partners&partner=9");
    const dialog = within(await screen.findByRole("dialog"));
    expect((dialog.getByLabelText(/^Name/) as HTMLInputElement).value).toBe("Far Away");
    expect(api.partner.mock.calls[0]![0]).toBe(9);
    await waitFor(() => expect(here()).toBe("/finance/commissions-rebates?tab=partners"));
  });

  it("says so, and drops the parameter, when ?partner= matches nobody", async () => {
    api.partner.mockResolvedValue(null);
    open("/finance/commissions-rebates?tab=partners&partner=999");
    expect((await screen.findByRole("alert")).textContent).toContain("That partner could not be found.");
    await waitFor(() => expect(here()).toBe("/finance/commissions-rebates?tab=partners"));
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("keeps ?partner= and offers Try again when the lookup fails", async () => {
    api.partner.mockRejectedValueOnce(new Error("down")).mockResolvedValueOnce(partner({ id: 9, name: "Far Away" }));
    open("/finance/commissions-rebates?tab=partners&partner=9");
    expect(await screen.findByText("That partner could not be opened.")).toBeTruthy();
    expect(here()).toBe("/finance/commissions-rebates?tab=partners&partner=9");
    expect(screen.queryByRole("dialog")).toBeNull();

    fireEvent.click(screen.getAllByRole("button", { name: "Try again" })[0]!);
    const dialog = within(await screen.findByRole("dialog"));
    expect((dialog.getByLabelText(/^Name/) as HTMLInputElement).value).toBe("Far Away");
    await waitFor(() => expect(here()).toBe("/finance/commissions-rebates?tab=partners"));
    expect(screen.queryByText("That partner could not be opened.")).toBeNull();
  });

  it("will not save an email address that is not one, and keeps what was typed", async () => {
    open();
    fireEvent.click(await screen.findByRole("button", { name: "Add partner" }));
    const dialog = within(await screen.findByRole("dialog"));
    fireEvent.change(dialog.getByLabelText(/^Name/), { target: { value: "Mail Test" } });
    fireEvent.change(dialog.getByLabelText(/^Email/), { target: { value: "not-an-email" } });
    fireEvent.click(saveButton());
    expect(await dialog.findByText("Enter a valid email address.")).toBeTruthy();
    expect(api.savePartner).not.toHaveBeenCalled();

    fireEvent.change(dialog.getByLabelText(/^Email/), { target: { value: "finance@example.com" } });
    expect(dialog.queryByText("Enter a valid email address.")).toBeNull();
    api.savePartner.mockResolvedValue(partner());
    fireEvent.click(saveButton());
    await waitFor(() => expect(api.savePartner).toHaveBeenCalledTimes(1));
    expect(api.savePartner.mock.calls[0]![0]).toMatchObject({ email: "finance@example.com" });
  });
});

describe("Deactivate and reactivate a partner", () => {
  const open = () => show(admin, "/finance/commissions-rebates?tab=partners");
  const menu = async (name: string) => {
    const view = table();
    fireEvent.click(await view.findByRole("button", { name: `More for ${name}` }));
  };

  it("asks for a reason in the shared popup, keeps the button off until one is typed, and sends it with the token", async () => {
    api.partnerStatus.mockResolvedValue(partner({ isActive: false }));
    open();
    await screen.findAllByText("ali");
    await menu("ali");
    fireEvent.click(await screen.findByRole("menuitem", { name: "Deactivate" }));

    const dialog = within(await screen.findByRole("dialog"));
    expect(dialog.getByText("Deactivate ali?")).toBeTruthy();
    expect(dialog.getByText("They can't get new commissions, and their pending commissions can't be paid or changed until they are reactivated.")).toBeTruthy();
    const confirm = () => dialog.getByRole("button", { name: "Deactivate" }) as HTMLButtonElement;
    expect(confirm().disabled).toBe(true);
    // No length limit is added for this reason.
    expect(dialog.getByLabelText(/Reason/).getAttribute("maxlength")).toBeNull();
    fireEvent.change(dialog.getByLabelText(/Reason/), { target: { value: "   " } });
    expect(confirm().disabled).toBe(true);
    expect(api.partnerStatus).not.toHaveBeenCalled();

    fireEvent.change(dialog.getByLabelText(/Reason/), { target: { value: "Left the business" } });
    expect(confirm().disabled).toBe(false);
    fireEvent.click(confirm());
    await waitFor(() => expect(api.partnerStatus).toHaveBeenCalledWith(4, { isActive: false, reason: "Left the business", concurrencyToken: "tok-4" }));
    expect(await screen.findByText("ali deactivated.")).toBeTruthy();
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    await waitFor(() => expect(api.summary.mock.calls.length).toBeGreaterThan(1));
  });

  it("steps back a page when deactivating the only partner on the last page empties it", async () => {
    let deactivated = false;
    api.partners.mockImplementation(async (_search, _active, skip) => {
      if (skip === 0) return page(Array.from({ length: 20 }, (_, index) => partner({ id: 100 + index, name: `P${index}`, internalCode: `PTR-${index}` })), deactivated ? 20 : 21);
      return deactivated ? page([], 20) : page([partner({ id: 4, name: "Last One" })], 21);
    });
    api.partnerStatus.mockImplementation(async () => { deactivated = true; return partner({ isActive: false }); });
    open();
    await screen.findAllByText("P0");
    fireEvent.click(screen.getByRole("button", { name: "Page 2" }));
    await screen.findAllByText("Last One");
    await menu("Last One");
    fireEvent.click(await screen.findByRole("menuitem", { name: "Deactivate" }));
    const dialog = within(await screen.findByRole("dialog"));
    fireEvent.change(dialog.getByLabelText(/Reason/), { target: { value: "Gone" } });
    fireEvent.click(dialog.getByRole("button", { name: "Deactivate" }));

    expect((await screen.findAllByText("P0")).length).toBeGreaterThan(0);
    expect(lastCall(api.partners)[2]).toBe(0);
    expect(screen.queryByText("Last One")).toBeNull();
  });

  it("shows a refusal inside the Deactivate popup and keeps what was typed", async () => {
    api.partnerStatus.mockRejectedValue(new Error("This financial record changed. Refresh and try again."));
    open();
    await screen.findAllByText("ali");
    await menu("ali");
    fireEvent.click(await screen.findByRole("menuitem", { name: "Deactivate" }));
    const dialog = within(await screen.findByRole("dialog"));
    fireEvent.change(dialog.getByLabelText(/Reason/), { target: { value: "Duplicate" } });
    fireEvent.click(dialog.getByRole("button", { name: "Deactivate" }));
    expect(await dialog.findByText("This financial record changed. Refresh and try again.")).toBeTruthy();
    expect((dialog.getByLabelText(/Reason/) as HTMLTextAreaElement).value).toBe("Duplicate");
  });

  it("asks before reactivating, then sends today's fixed reason", async () => {
    api.partners.mockResolvedValue(page([partner({ id: 5, name: "Khan Estate Agency", isActive: false, concurrencyToken: "tok-5" })]));
    api.partnerStatus.mockResolvedValue(partner({ id: 5, isActive: true }));
    open();
    await screen.findAllByText("Khan Estate Agency");
    await menu("Khan Estate Agency");
    fireEvent.click(await screen.findByRole("menuitem", { name: "Reactivate" }));

    const dialog = within(await screen.findByRole("dialog"));
    expect(dialog.getByText("Reactivate Khan Estate Agency?")).toBeTruthy();
    expect(dialog.getByText("They can get new commissions and be paid again.")).toBeTruthy();
    expect(api.partnerStatus).not.toHaveBeenCalled();
    fireEvent.click(dialog.getByRole("button", { name: "Reactivate" }));
    await waitFor(() => expect(api.partnerStatus).toHaveBeenCalledWith(5, { isActive: true, reason: "Reactivated for new business", concurrencyToken: "tok-5" }));
    expect(await screen.findByText("Khan Estate Agency reactivated.")).toBeTruthy();
  });

  it("shows a refused Reactivate as a toast", async () => {
    api.partners.mockResolvedValue(page([partner({ isActive: false })]));
    api.partnerStatus.mockRejectedValue(new Error("This financial record changed. Refresh and try again."));
    open();
    await screen.findAllByText("ali");
    await menu("ali");
    fireEvent.click(await screen.findByRole("menuitem", { name: "Reactivate" }));
    fireEvent.click(within(await screen.findByRole("dialog")).getByRole("button", { name: "Reactivate" }));
    expect((await screen.findByRole("alert")).textContent).toContain("This financial record changed. Refresh and try again.");
  });

  it("offers only Deactivate for an active partner and only Reactivate for an inactive one", async () => {
    api.partners.mockResolvedValue(page([partner(), partner({ id: 5, name: "Khan", isActive: false })]));
    open();
    await screen.findAllByText("Khan");
    await menu("ali");
    expect(screen.getAllByRole("menuitem").map((item) => item.textContent)).toEqual(["Deactivate"]);
    fireEvent.keyDown(document.activeElement ?? document.body, { key: "Escape" });
    await menu("Khan");
    expect(screen.getAllByRole("menuitem").map((item) => item.textContent)).toEqual(["Reactivate"]);
  });

  it("on a phone opens the ⋯ sheet and asks for the reason in a popup", async () => {
    stubMedia(true);
    api.partnerStatus.mockResolvedValue(partner({ isActive: false }));
    open();
    await screen.findAllByText("ali");
    fireEvent.click(within(screen.getByRole("article")).getByRole("button", { name: "More for ali" }));
    fireEvent.click(await screen.findByRole("button", { name: "Deactivate partner" }));
    expect(await screen.findByText("Deactivate ali?")).toBeTruthy();
  });
});
