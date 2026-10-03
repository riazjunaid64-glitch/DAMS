// @vitest-environment happy-dom
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { ToastProvider } from "../components/ui/Toast.tsx";
import { PHONE_QUERY } from "../components/ui/useMediaQuery.ts";
import type { User } from "../App.tsx";
import { openAttachmentAt } from "../api/financeAttachments.ts";
import { capitalApi } from "../features/finance/capitalPartners/api.ts";
import type { AccountOption, Partner, Statement, Transaction } from "../features/finance/capitalPartners/types.ts";
import FinancePartnersPage from "./FinancePartnersPage.tsx";

vi.mock("../features/finance/capitalPartners/api.ts", () => ({
  capitalApi: {
    partners: vi.fn(),
    accounts: vi.fn(),
    saveShares: vi.fn(),
    savePartner: vi.fn(),
    statement: vi.fn(),
    recordTransaction: vi.fn(),
  },
}));
vi.mock("../api/financeAttachments.ts", async (original) => ({
  ...(await original<typeof import("../api/financeAttachments.ts")>()),
  openAttachmentAt: vi.fn(),
}));

const api = vi.mocked(capitalApi);
const openFile = vi.mocked(openAttachmentAt);

const partner = (over: Partial<Partner> = {}): Partner => ({
  id: 1, name: "Riaz Junaid", cnic: null, ntn: null, profitSharePercent: 40, financeAccountId: 11, financeAccountName: "Capital — Riaz Junaid",
  isActive: true, joinedDate: null, exitedDate: null, openingBalance: 0, contributions: 4_000_000, withdrawals: 100_000, profitShare: 416_660,
  lossShare: 0, closingBalance: 4_316_660, concurrencyToken: "tok-1", ...over,
});

const riaz = partner();
const adeel = partner({ id: 2, name: "Adeel Satti", profitSharePercent: 25, financeAccountId: 12, financeAccountName: "Capital — Adeel Satti", contributions: 1_500_000, withdrawals: 200_000, profitShare: 260_412, closingBalance: 1_560_412, concurrencyToken: "tok-2" });
const ayub = partner({ id: 3, name: "Ayub Satti", profitSharePercent: 20, financeAccountId: 13, financeAccountName: "Capital — Ayub Satti", joinedDate: "2026-01-01T00:00:00", concurrencyToken: "tok-3" });
const imtiaz = partner({ id: 4, name: "Imtiaz Raheem", profitSharePercent: 15, financeAccountId: 14, financeAccountName: "Capital — Imtiaz Raheem", concurrencyToken: "tok-4" });
const shabbir = partner({
  id: 5, name: "Shabbir Hussain", profitSharePercent: 5, isActive: false, financeAccountId: null, financeAccountName: null,
  contributions: 0, withdrawals: 0, profitShare: 0, closingBalance: 0, concurrencyToken: "tok-5",
});
const all = [riaz, adeel, ayub, imtiaz, shabbir];

const account = (over: Partial<AccountOption>): AccountOption => ({ id: 7, name: "Meezan Bank", accountHolderName: "Seven Ventures", isActive: true, type: "Bank", ...over });
const cashAccounts = [account({}), account({ id: 8, name: "HBL Current", accountHolderName: "Seven Ventures" }), account({ id: 9, name: "Old bank", isActive: false })];
const allAccounts = [
  ...cashAccounts,
  account({ id: 11, name: "Capital — Riaz Junaid", type: "Capital" }),
  account({ id: 12, name: "Capital — Adeel Satti", type: "Capital" }),
  account({ id: 13, name: "Capital — Ayub Satti", type: "Capital" }),
  account({ id: 14, name: "Capital — Imtiaz Raheem", type: "Capital" }),
  account({ id: 15, name: "Capital — Naveed Akhtar", type: "Capital" }),
  account({ id: 16, name: "Capital — Retired", type: 6, isActive: false }),
];

const movement = (over: Partial<Transaction>): Transaction => ({
  id: 1, type: "Contribution", amount: 3_000_000, date: "2026-07-01T00:00:00", financeAccountId: 7, reference: "CH-1001", note: null,
  profitSharePercentSnapshot: null, attachment: null, ...over,
});
const slip = { fileName: "slip.pdf", contentType: "application/pdf", fileSize: 100, uploadedAt: "2026-07-01T00:00:00" };
const statement = (over: Partial<Statement> = {}): Statement => ({
  partnerId: 1, partnerName: "Riaz Junaid", from: null, to: null, openingBalance: 0, closingBalance: 4_316_660,
  transactions: [
    movement({ attachment: slip }),
    movement({ id: 2, amount: 1_000_000, date: "2026-08-15T00:00:00", financeAccountId: 8, reference: "CH-1018", note: "Second instalment of agreed capital" }),
    movement({ id: 3, type: "Withdrawal", amount: 100_000, date: "2026-09-05T00:00:00", financeAccountId: null, reference: null, note: "Cash in office" }),
    movement({ id: 4, type: "ProfitShare", amount: 416_660, date: "2026-09-30T00:00:00", financeAccountId: null, reference: null, note: "ProfitShare", profitSharePercentSnapshot: 40 }),
  ],
  ...over,
});

const admin: User = { userId: "1", role: "Admin", email: "a@b.c" };
const accountant: User = { userId: "2", role: "Accountant", email: "c@b.c" };

function show(user: User | null = admin) {
  return render(
    <MemoryRouter initialEntries={["/finance/partners"]}>
      <ToastProvider>
        <Routes>
          <Route path="/finance/partners" element={<FinancePartnersPage user={user} />} />
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
const dialog = () => within(screen.getByRole("dialog"));
const row = (name: string) => within(table().getAllByRole("row").find((candidate) => within(candidate).queryByText(name)) as HTMLElement);
const button = (name: string | RegExp, scope: { getByRole: typeof screen.getByRole } = screen) => scope.getByRole("button", { name }) as HTMLButtonElement;
const field = (name: string | RegExp) => dialog().getByRole("textbox", { name }) as HTMLInputElement;
const type = (input: HTMLElement, value: string) => fireEvent.change(input, { target: { value } });
const pick = (name: RegExp | string, option: string) => {
  fireEvent.click(dialog().getByRole("combobox", { name }));
  fireEvent.click(screen.getByRole("option", { name: option }));
};
const pickDay = (name: RegExp | string, day: string) => {
  fireEvent.click(dialog().getByRole("button", { name }));
  fireEvent.click(screen.getByRole("button", { name: day }));
};
const footerButton = (name: string) => within(screen.getByRole("dialog").querySelector("footer") as HTMLElement).getByRole("button", { name }) as HTMLButtonElement;
const lastCall = (mock: { mock: { calls: unknown[][] } }) => mock.mock.calls.at(-1)!;
const loaded = async () => { await screen.findAllByText("Riaz Junaid"); };

beforeEach(() => {
  vi.resetAllMocks();
  stubMedia(false);
  // 2026-10-02 12:00 in Pakistan.
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date("2026-10-02T07:00:00Z"));
  Element.prototype.scrollIntoView = () => {};
  api.partners.mockResolvedValue(all);
  api.accounts.mockImplementation(async (cashLikeOnly) => (cashLikeOnly ? cashAccounts : allAccounts));
  api.saveShares.mockResolvedValue(undefined);
  api.savePartner.mockResolvedValue(undefined);
  api.recordTransaction.mockResolvedValue(undefined);
  api.statement.mockResolvedValue(statement());
  openFile.mockResolvedValue(undefined);
});

afterEach(() => {
  cleanup();
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe("Capital partners: the list", () => {
  it("has the title and two buttons, and no back link, subtitle, Capital Accounts button or banner", async () => {
    show();
    expect(await screen.findByRole("heading", { name: "Capital partners" })).toBeTruthy();
    expect(button("Edit shares")).toBeTruthy();
    expect(button("Add partner")).toBeTruthy();
    await loaded();
    expect(screen.queryByText(/Financial reports/)).toBeNull();
    expect(screen.queryByText(/frozen distribution shares/)).toBeNull();
    expect(screen.queryByRole("button", { name: "Capital Accounts" })).toBeNull();
    expect(screen.queryByText(/Active profit shares/)).toBeNull();
    expect(screen.queryByText(/Historical ProfitShare/)).toBeNull();
  });

  it("asks for every partner, including inactive ones, and the cash and the capital accounts", async () => {
    show();
    await loaded();
    expect(api.partners).toHaveBeenCalledTimes(1);
    expect(api.accounts.mock.calls.map((call) => call[0]).sort()).toEqual([false, true]);
  });

  it("shows today's columns, without boxes inside the rows or paging", async () => {
    show();
    await loaded();
    expect(table().getAllByRole("columnheader").map((header) => header.textContent)).toEqual([
      "Partner", "Share", "Opening", "Contributions", "Withdrawals", "Profit share", "Loss share", "Closing", "Actions",
    ]);
    expect(table().queryByRole("checkbox")).toBeNull();
    expect(table().queryByRole("spinbutton")).toBeNull();
    expect(screen.queryByRole("navigation", { name: /pagination/i })).toBeNull();
    expect(screen.queryByRole("button", { name: /Load more/ })).toBeNull();
  });

  it("shows the name with the capital account under it, the share and the money", async () => {
    show();
    await loaded();
    const riazRow = row("Riaz Junaid");
    expect(riazRow.getByText("Capital — Riaz Junaid")).toBeTruthy();
    expect(riazRow.getByText("40%")).toBeTruthy();
    expect(riazRow.getByText("Rs 4,000,000")).toBeTruthy();
    expect(riazRow.getByText("Rs 100,000")).toBeTruthy();
    expect(riazRow.getByText("Rs 416,660")).toBeTruthy();
    expect(riazRow.getByText("Rs 4,316,660")).toBeTruthy();
    expect(riazRow.queryByText("Inactive")).toBeNull();
  });

  it("shows up to 4 decimals of a share", async () => {
    api.partners.mockResolvedValue([partner({ profitSharePercent: 14.9999 })]);
    show();
    await loaded();
    expect(row("Riaz Junaid").getByText("14.9999%")).toBeTruthy();
  });

  it("marks an inactive partner with a badge, its saved share, and 'No capital account'", async () => {
    show();
    await loaded();
    const shabbirRow = row("Shabbir Hussain");
    expect(shabbirRow.getByText("Inactive")).toBeTruthy();
    expect(shabbirRow.getByText("5%")).toBeTruthy();
    expect(shabbirRow.getByText("No capital account")).toBeTruthy();
  });

  it("keeps the order the server sends", async () => {
    show();
    await loaded();
    const names = table().getAllByRole("row").slice(1).map((candidate) => candidate.querySelector("td span.font-extrabold")?.textContent);
    expect(names).toEqual(["Riaz Junaid", "Adeel Satti", "Ayub Satti", "Imtiaz Raheem", "Shabbir Hussain"]);
  });

  it("has a Statement button and a ⋯ menu with Add transaction and Edit partner on each row", async () => {
    show();
    await loaded();
    expect(row("Riaz Junaid").getByRole("button", { name: "Statement for Riaz Junaid" })).toBeTruthy();
    fireEvent.click(row("Riaz Junaid").getByRole("button", { name: "More for Riaz Junaid" }));
    expect(screen.getAllByRole("menuitem").map((item) => item.textContent)).toEqual(["Add transaction", "Edit partner"]);
    expect((screen.getByRole("menuitem", { name: "Add transaction" }) as HTMLButtonElement).disabled).toBe(false);
  });

  it("greys out Add transaction for an inactive partner", async () => {
    show();
    await loaded();
    fireEvent.click(row("Shabbir Hussain").getByRole("button", { name: "More for Shabbir Hussain" }));
    expect((screen.getByRole("menuitem", { name: "Add transaction" }) as HTMLButtonElement).disabled).toBe(true);
    expect((screen.getByRole("menuitem", { name: "Edit partner" }) as HTMLButtonElement).disabled).toBe(false);
  });

  it("shows grey placeholders on the first load", () => {
    api.partners.mockReturnValue(new Promise(() => {}));
    show();
    expect(document.querySelectorAll("tbody tr.border-t")).toHaveLength(8);
    expect(screen.queryByText("No capital partners yet")).toBeNull();
  });

  it("keeps the old rows on screen while a refresh is on its way", async () => {
    show();
    await loaded();
    api.partners.mockReturnValue(new Promise(() => {}));
    fireEvent.click(row("Riaz Junaid").getByRole("button", { name: "More for Riaz Junaid" }));
    api.statement.mockResolvedValue(statement());
    fireEvent.click(screen.getByRole("menuitem", { name: "Edit partner" }));
    api.savePartner.mockResolvedValue(undefined);
    fireEvent.click(button("Save partner", dialog()));
    await waitFor(() => expect(api.savePartner).toHaveBeenCalled());
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(screen.getAllByText("Riaz Junaid").length).toBeGreaterThan(0);
    expect(screen.getByRole("progressbar", { name: "Loading" })).toBeTruthy();
  });

  it("shows an empty state with an Add partner button when there are no partners", async () => {
    api.partners.mockResolvedValue([]);
    show();
    expect(await screen.findByText("No capital partners yet")).toBeTruthy();
    expect(button("Edit shares").disabled).toBe(true);
    fireEvent.click(within(screen.getByText("No capital partners yet").parentElement!).getByRole("button", { name: "Add partner" }));
    expect(screen.getByRole("dialog")).toBeTruthy();
  });

  it("shows the server's message with Try again when the partners cannot be loaded", async () => {
    api.partners.mockRejectedValueOnce(new Error("Partners could not be loaded."));
    show();
    expect(await screen.findByText("Partners could not be loaded.")).toBeTruthy();
    expect(screen.queryByRole("table")).toBeNull();
    fireEvent.click(button("Try again"));
    await loaded();
    expect(screen.queryByText("Partners could not be loaded.")).toBeNull();
    expect(api.partners).toHaveBeenCalledTimes(2);
  });
});

describe("Capital partners: who can open it", () => {
  it("waits for the signed-in user before deciding", () => {
    show(null);
    expect(screen.queryByText("Home page")).toBeNull();
    expect(api.partners).not.toHaveBeenCalled();
  });

  it("lets an Accountant in", async () => {
    show(accountant);
    await loaded();
    expect(api.partners).toHaveBeenCalled();
  });

  it("sends a Sales manager home without asking for anything", async () => {
    show({ userId: "3", role: "SalesManager", email: "s@b.c" });
    expect(await screen.findByText("Home page")).toBeTruthy();
    expect(api.partners).not.toHaveBeenCalled();
  });
});

describe("Capital partners: the shares notice", () => {
  it("shows when the saved shares of the active partners do not add up to 100%, and opens Edit shares", async () => {
    api.partners.mockResolvedValue([riaz, adeel, ayub, partner({ ...imtiaz, profitSharePercent: 10 }), shabbir]);
    show();
    const notice = await screen.findByText("Active shares add up to 95%. They must total 100%.");
    fireEvent.click(within(notice.closest("[role=alert]") as HTMLElement).getByRole("button", { name: "Edit shares" }));
    expect(dialog().getByText("Active partners must total exactly 100%")).toBeTruthy();
  });

  it("is not shown when the total is right", async () => {
    show();
    await loaded();
    expect(screen.queryByText(/Active shares add up to/)).toBeNull();
  });

  it("is not shown when there are no partners yet", async () => {
    api.partners.mockResolvedValue([]);
    show();
    await screen.findByText("No capital partners yet");
    expect(screen.queryByText(/Active shares add up to/)).toBeNull();
  });

  it("shows up to 4 decimals", async () => {
    api.partners.mockResolvedValue([partner({ profitSharePercent: 99.5 })]);
    show();
    expect(await screen.findByText("Active shares add up to 99.5%. They must total 100%.")).toBeTruthy();
  });
});

describe("Edit shares", () => {
  const open = async () => {
    show();
    await loaded();
    fireEvent.click(button("Edit shares"));
  };
  const share = (name: string) => dialog().getByRole("textbox", { name: `${name} share` }) as HTMLInputElement;
  const active = (name: string) => dialog().getByRole("switch", { name }) as HTMLButtonElement;

  it("has one row for every partner, with a switch and a share box, and keeps an inactive partner's saved share greyed", async () => {
    await open();
    expect(dialog().getAllByRole("switch")).toHaveLength(5);
    expect(active("Riaz Junaid").getAttribute("aria-checked")).toBe("true");
    expect(share("Riaz Junaid").value).toBe("40");
    expect(active("Shabbir Hussain").getAttribute("aria-checked")).toBe("false");
    expect(share("Shabbir Hussain").value).toBe("5");
    expect(share("Shabbir Hussain").disabled).toBe(true);
    expect(share("Riaz Junaid").disabled).toBe(false);
  });

  it("shows a plain total line and keeps Save on when the total is 100%", async () => {
    await open();
    expect(dialog().getByText("Total of active partners")).toBeTruthy();
    expect(dialog().getByText("100% of 100%")).toBeTruthy();
    expect(dialog().queryByRole("status")).toBeNull();
    expect(button("Save shares", dialog()).disabled).toBe(false);
  });

  it("turns the total red and keeps Save off while it is not 100%", async () => {
    await open();
    type(share("Imtiaz Raheem"), "10");
    const notice = dialog().getByRole("status");
    expect(within(notice).getByText("95% of 100%")).toBeTruthy();
    expect(button("Save shares", dialog()).disabled).toBe(true);
    type(share("Imtiaz Raheem"), "15");
    expect(dialog().queryByRole("status")).toBeNull();
    expect(button("Save shares", dialog()).disabled).toBe(false);
  });

  it("adds only the partners whose switch is on, and lets a switched-on partner count again", async () => {
    await open();
    fireEvent.click(active("Shabbir Hussain"));
    expect(share("Shabbir Hussain").disabled).toBe(false);
    expect(dialog().getByText("105% of 100%")).toBeTruthy();
    expect(button("Save shares", dialog()).disabled).toBe(true);
    fireEvent.click(active("Imtiaz Raheem"));
    expect(share("Imtiaz Raheem").disabled).toBe(true);
    expect(dialog().getByText("90% of 100%")).toBeTruthy();
  });

  it("refuses a single share above 100 even when the total is within tolerance, and does not call the API", async () => {
    api.partners.mockResolvedValue([partner({ profitSharePercent: 100 })]);
    await open();
    expect(button("Save shares", dialog()).disabled).toBe(false);
    type(share("Riaz Junaid"), "100.0001");
    expect(dialog().getByText("0 to 100")).toBeTruthy();
    expect(button("Save shares", dialog()).disabled).toBe(true);
    fireEvent.submit(dialog().getByRole("textbox", { name: "Riaz Junaid share" }).closest("form")!);
    expect(api.saveShares).not.toHaveBeenCalled();
  });

  it("accepts a total within 0.01 of 100", async () => {
    await open();
    type(share("Imtiaz Raheem"), "14.99");
    expect(button("Save shares", dialog()).disabled).toBe(false);
    type(share("Imtiaz Raheem"), "14.9899");
    expect(button("Save shares", dialog()).disabled).toBe(true);
  });

  it("saves every partner with its share, switch and token, then closes, toasts and reloads", async () => {
    await open();
    type(share("Adeel Satti"), "30");
    type(share("Imtiaz Raheem"), "10");
    fireEvent.click(button("Save shares", dialog()));
    await waitFor(() => expect(api.saveShares).toHaveBeenCalledTimes(1));
    expect(lastCall(api.saveShares)[0]).toEqual([
      { id: 1, profitSharePercent: 40, isActive: true, concurrencyToken: "tok-1" },
      { id: 2, profitSharePercent: 30, isActive: true, concurrencyToken: "tok-2" },
      { id: 3, profitSharePercent: 20, isActive: true, concurrencyToken: "tok-3" },
      { id: 4, profitSharePercent: 10, isActive: true, concurrencyToken: "tok-4" },
      { id: 5, profitSharePercent: 5, isActive: false, concurrencyToken: "tok-5" },
    ]);
    expect(await screen.findByText("Shares saved.")).toBeTruthy();
    expect(screen.queryByRole("dialog")).toBeNull();
    await waitFor(() => expect(api.partners).toHaveBeenCalledTimes(2));
  });

  it("shows the server's refusal inside the popup and keeps what was typed", async () => {
    api.saveShares.mockRejectedValue(new Error("Partner data changed. Refresh and try again."));
    await open();
    type(share("Adeel Satti"), "30");
    type(share("Imtiaz Raheem"), "10");
    fireEvent.click(button("Save shares", dialog()));
    expect(await dialog().findByText("Partner data changed. Refresh and try again.")).toBeTruthy();
    expect(share("Adeel Satti").value).toBe("30");
    expect(api.partners).toHaveBeenCalledTimes(1);
  });

  it("blocks closing mid-save and turns Save into a spinner", async () => {
    api.saveShares.mockReturnValue(new Promise(() => {}));
    await open();
    fireEvent.click(button("Save shares", dialog()));
    await waitFor(() => expect(api.saveShares).toHaveBeenCalled());
    expect(button("Cancel", dialog()).disabled).toBe(true);
    expect(button("Close", dialog()).disabled).toBe(true);
    expect(button("Save shares", dialog()).getAttribute("aria-busy")).toBe("true");
  });

  it("throws the edits away on Cancel and on ✕", async () => {
    await open();
    type(share("Adeel Satti"), "30");
    fireEvent.click(button("Cancel", dialog()));
    expect(screen.queryByRole("dialog")).toBeNull();
    fireEvent.click(button("Edit shares"));
    expect(share("Adeel Satti").value).toBe("25");
    type(share("Adeel Satti"), "30");
    fireEvent.click(button("Close", dialog()));
    fireEvent.click(button("Edit shares"));
    expect(share("Adeel Satti").value).toBe("25");
    expect(api.saveShares).not.toHaveBeenCalled();
  });
});

describe("Add partner and Edit partner", () => {
  const openAdd = async () => {
    show();
    await loaded();
    fireEvent.click(button("Add partner"));
  };
  const openEdit = async (name: string) => {
    show();
    await loaded();
    fireEvent.click(row(name).getByRole("button", { name: `More for ${name}` }));
    fireEvent.click(screen.getByRole("menuitem", { name: "Edit partner" }));
  };

  it("has the fields in order, no share field, and Name as the only required one", async () => {
    await openAdd();
    expect(dialog().getByRole("heading", { name: "Add partner" })).toBeTruthy();
    const text = screen.getByRole("dialog").textContent!;
    const order = ["Name", "CNIC", "NTN", "Capital account", "Joined date", "Exited date"].map((label) => text.indexOf(label));
    expect(order).toEqual([...order].sort((a, b) => a - b));
    expect(order.every((index) => index >= 0)).toBe(true);
    for (const label of [/^Name/, /CNIC/, /NTN/]) expect(dialog().getByRole("textbox", { name: label })).toBeTruthy();
    expect(dialog().getByRole("combobox", { name: /Capital account/ })).toBeTruthy();
    expect(dialog().getByRole("button", { name: /Joined date/ })).toBeTruthy();
    expect(dialog().getByRole("button", { name: /Exited date/ })).toBeTruthy();
    expect(dialog().queryByText(/share/i)).toBeNull();
    expect(dialog().getAllByText("(optional)")).toHaveLength(5);
    expect(dialog().queryByText(/Historical|Change existing shares|New partners start inactive/)).toBeNull();
  });

  it("starts empty, with Joined date empty, and keeps Save off until the name is filled", async () => {
    await openAdd();
    expect(field(/^Name/).value).toBe("");
    expect(dialog().getByRole("button", { name: /Joined date/ }).textContent).toContain("Select date");
    expect(button("Save partner", dialog()).disabled).toBe(true);
    type(field(/^Name/), "  ");
    expect(button("Save partner", dialog()).disabled).toBe(true);
    type(field(/^Name/), "Naveed Akhtar");
    expect(button("Save partner", dialog()).disabled).toBe(false);
  });

  it("saves a new partner with share 0, inactive, empty text as null, then toasts and reloads", async () => {
    await openAdd();
    type(field(/^Name/), " Naveed Akhtar ");
    type(field(/CNIC/), "37405-1234567-1");
    fireEvent.click(button("Save partner", dialog()));
    await waitFor(() => expect(api.savePartner).toHaveBeenCalledTimes(1));
    expect(api.savePartner.mock.calls[0]).toEqual([{
      name: "Naveed Akhtar", cnic: "37405-1234567-1", ntn: null, profitSharePercent: 0, financeAccountId: null, isActive: false,
      joinedDate: null, exitedDate: null, concurrencyToken: null,
    }, null]);
    expect(await screen.findByText("Partner saved.")).toBeTruthy();
    expect(screen.queryByRole("dialog")).toBeNull();
    await waitFor(() => expect(api.partners).toHaveBeenCalledTimes(2));
  });

  it("limits the lengths: name 200, CNIC 20, NTN 30", async () => {
    await openAdd();
    expect(field(/^Name/).maxLength).toBe(200);
    expect(field(/CNIC/).maxLength).toBe(20);
    expect(field(/NTN/).maxLength).toBe(30);
  });

  it("offers the active Capital accounts no other partner uses, and lets the account be cleared", async () => {
    await openAdd();
    fireEvent.click(dialog().getByRole("combobox", { name: /Capital account/ }));
    expect(screen.getAllByRole("option").map((option) => option.textContent)).toEqual(["Select account", "Capital — Naveed Akhtar"]);
    fireEvent.click(screen.getByRole("option", { name: "Capital — Naveed Akhtar" }));
    type(field(/^Name/), "Naveed Akhtar");
    fireEvent.click(button("Save partner", dialog()));
    await waitFor(() => expect(api.savePartner).toHaveBeenCalled());
    expect((lastCall(api.savePartner)[0] as { financeAccountId: number }).financeAccountId).toBe(15);
  });

  it("says to add a Capital account first when there is none to pick", async () => {
    api.accounts.mockImplementation(async (cashLikeOnly) => (cashLikeOnly ? cashAccounts : cashAccounts));
    await openAdd();
    expect(dialog().getByText("Add a Capital account in Manage accounts first.")).toBeTruthy();
  });

  it("fills Edit with the partner's values and the name as the subtitle", async () => {
    await openEdit("Ayub Satti");
    expect(dialog().getByRole("heading", { name: /Edit partner/ }).textContent).toContain("Ayub Satti");
    expect(field(/^Name/).value).toBe("Ayub Satti");
    expect(dialog().getByRole("combobox", { name: /Capital account/ }).textContent).toContain("Capital — Ayub Satti");
    expect(dialog().getByRole("button", { name: /Joined date/ }).textContent).toContain("Jan 1, 2026");
  });

  it("keeps the partner's own account in the list, marked Inactive when it is", async () => {
    api.partners.mockResolvedValue([riaz, partner({ ...adeel, financeAccountId: 16, financeAccountName: "Capital — Retired" })]);
    await openEdit("Adeel Satti");
    fireEvent.click(dialog().getByRole("combobox", { name: /Capital account/ }));
    expect(screen.getAllByRole("option").map((option) => option.textContent)).toEqual([
      "Select account", "Capital — Adeel Satti", "Capital — Ayub Satti", "Capital — Imtiaz Raheem", "Capital — Naveed Akhtar", "Capital — Retired (Inactive)",
    ]);
  });

  it("turns Save off while the Exited date is before the Joined date", async () => {
    await openAdd();
    type(field(/^Name/), "Naveed Akhtar");
    pickDay(/Exited date/, "October 6, 2026");
    expect(button("Save partner", dialog()).disabled).toBe(false);
    pickDay(/Joined date/, "October 20, 2026");
    expect(button("Save partner", dialog()).disabled).toBe(true);
  });

  it("sends the saved share, Active setting and token back on Edit", async () => {
    await openEdit("Adeel Satti");
    type(field(/CNIC/), "37405-7654321-3");
    fireEvent.click(button("Save partner", dialog()));
    await waitFor(() => expect(api.savePartner).toHaveBeenCalled());
    expect(api.savePartner.mock.calls[0]).toEqual([{
      name: "Adeel Satti", cnic: "37405-7654321-3", ntn: null, profitSharePercent: 25, financeAccountId: 12, isActive: true,
      joinedDate: null, exitedDate: null, concurrencyToken: "tok-2",
    }, 2]);
  });

  it("keeps an inactive partner inactive and its saved share on Edit", async () => {
    await openEdit("Shabbir Hussain");
    fireEvent.click(button("Save partner", dialog()));
    await waitFor(() => expect(api.savePartner).toHaveBeenCalled());
    expect(api.savePartner.mock.calls[0]![0]).toMatchObject({ profitSharePercent: 5, isActive: false, concurrencyToken: "tok-5" });
  });

  it("sends the dates as plain days", async () => {
    await openEdit("Ayub Satti");
    fireEvent.click(button("Save partner", dialog()));
    await waitFor(() => expect(api.savePartner).toHaveBeenCalled());
    expect(api.savePartner.mock.calls[0]![0]).toMatchObject({ joinedDate: "2026-01-01", exitedDate: null });
  });

  it("will not pick an Exited date before the Joined date, and says so when Joined is moved past it", async () => {
    await openAdd();
    pickDay(/Joined date/, "October 5, 2026");
    fireEvent.click(dialog().getByRole("button", { name: /Exited date/ }));
    expect((screen.getByRole("button", { name: "October 4, 2026" }) as HTMLButtonElement).disabled).toBe(true);
    fireEvent.click(screen.getByRole("button", { name: "October 6, 2026" }));
    expect(dialog().getByRole("button", { name: /Exited date/ }).textContent).toContain("Oct 6, 2026");
    pickDay(/Joined date/, "October 20, 2026");
    expect(dialog().getByText("Exit date cannot be before joined date.")).toBeTruthy();
    type(field(/^Name/), "Naveed Akhtar");
    fireEvent.click(button("Save partner", dialog()));
    expect(api.savePartner).not.toHaveBeenCalled();
  });

  for (const message of [
    "A capital partner with this name already exists.",
    "This capital account is already linked to another partner.",
    "CNIC or NTN is too long.",
    "Active partner profit shares must total 100%. The proposed total is 95.0000%.",
    "This partner already has capital movements recorded, so its capital account cannot be changed. Deactivate this partner and create a new one against the new account instead.",
  ]) {
    it(`shows "${message.slice(0, 40)}…" inside the popup and keeps the popup open with what was typed`, async () => {
      api.savePartner.mockRejectedValue(new Error(message));
      await openAdd();
      type(field(/^Name/), "Naveed Akhtar");
      fireEvent.click(button("Save partner", dialog()));
      expect(await dialog().findByText(message)).toBeTruthy();
      expect(field(/^Name/).value).toBe("Naveed Akhtar");
      expect(api.partners).toHaveBeenCalledTimes(1);
    });
  }

  it("blocks closing while it saves", async () => {
    api.savePartner.mockReturnValue(new Promise(() => {}));
    await openAdd();
    type(field(/^Name/), "Naveed Akhtar");
    fireEvent.click(button("Save partner", dialog()));
    await waitFor(() => expect(api.savePartner).toHaveBeenCalled());
    expect(button("Cancel", dialog()).disabled).toBe(true);
    expect(button("Close", dialog()).disabled).toBe(true);
  });
});

describe("Add transaction", () => {
  const open = async (name = "Riaz Junaid") => {
    show();
    await loaded();
    fireEvent.click(row(name).getByRole("button", { name: `More for ${name}` }));
    fireEvent.click(screen.getByRole("menuitem", { name: "Add transaction" }));
  };
  const amount = () => field(/^Amount/);
  const fill = (value = "1000000") => {
    type(amount(), value);
    pick(/Cash or bank account/, "Meezan Bank · Seven Ventures");
  };
  const submit = () => fireEvent.click(button("Save", dialog()));

  it("opens on Contribution, today in Pakistan, with the partner's name as the subtitle", async () => {
    await open();
    expect(dialog().getByRole("heading", { name: /Add transaction/ }).textContent).toContain("Riaz Junaid");
    expect(dialog().getByRole("combobox", { name: /Type/ }).textContent).toContain("Contribution");
    expect(dialog().getByRole("button", { name: /^Date/ }).textContent).toContain("Oct 2, 2026");
    expect(dialog().getByText("Attachment")).toBeTruthy();
  });

  it("lists the four types", async () => {
    await open();
    fireEvent.click(dialog().getByRole("combobox", { name: /Type/ }));
    expect(screen.getAllByRole("option").map((option) => option.textContent)).toEqual(["Contribution", "Withdrawal", "Profit share", "Loss share"]);
  });

  it("lists the active cash and bank accounts as Name · Holder", async () => {
    await open();
    fireEvent.click(dialog().getByRole("combobox", { name: /Cash or bank account/ }));
    expect(screen.getAllByRole("option").map((option) => option.textContent)).toEqual(["Meezan Bank · Seven Ventures", "HBL Current · Seven Ventures"]);
  });

  it("keeps Save off until the amount and the account are filled", async () => {
    await open();
    expect(button("Save", dialog()).disabled).toBe(true);
    type(amount(), "1000");
    expect(button("Save", dialog()).disabled).toBe(true);
    pick(/Cash or bank account/, "Meezan Bank · Seven Ventures");
    expect(button("Save", dialog()).disabled).toBe(false);
  });

  it("hides the account for Profit share and Loss share, and Save then needs only the amount", async () => {
    await open();
    pick(/Type/, "Profit share");
    expect(dialog().queryByRole("combobox", { name: /Cash or bank account/ })).toBeNull();
    expect(button("Save", dialog()).disabled).toBe(true);
    type(amount(), "1000");
    expect(button("Save", dialog()).disabled).toBe(false);
    pick(/Type/, "Withdrawal");
    expect(dialog().getByRole("combobox", { name: /Cash or bank account/ })).toBeTruthy();
    expect(button("Save", dialog()).disabled).toBe(true);
  });

  it("refuses an amount of zero", async () => {
    await open();
    fill("0");
    submit();
    expect(await dialog().findByText("Enter an amount greater than zero.")).toBeTruthy();
    expect(api.recordTransaction).not.toHaveBeenCalled();
    type(amount(), "5");
    expect(dialog().queryByText("Enter an amount greater than zero.")).toBeNull();
  });

  it("will not pick a day after today", async () => {
    await open();
    fireEvent.click(dialog().getByRole("button", { name: /^Date/ }));
    expect((screen.getByRole("button", { name: "October 3, 2026" }) as HTMLButtonElement).disabled).toBe(true);
    expect((screen.getByRole("button", { name: "October 2, 2026" }) as HTMLButtonElement).disabled).toBe(false);
  });

  it("limits the reference to 200 and the note to 1000", async () => {
    await open();
    expect(field(/Reference/).maxLength).toBe(200);
    expect(field(/Note/).maxLength).toBe(1000);
  });

  it("sends the form with the type, amount, date and account, then closes, toasts and reloads", async () => {
    await open();
    fill();
    type(field(/Reference/), " CH-1042 ");
    submit();
    await waitFor(() => expect(api.recordTransaction).toHaveBeenCalledTimes(1));
    const [id, body, key] = api.recordTransaction.mock.calls[0]! as [number, FormData, string];
    expect(id).toBe(1);
    expect(Object.fromEntries(body.entries())).toEqual({ type: "Contribution", amount: "1000000", date: "2026-10-02", financeAccountId: "7", reference: "CH-1042" });
    expect(key).toMatch(/^capital-movement-/);
    expect(await screen.findByText("Transaction recorded.")).toBeTruthy();
    expect(screen.queryByRole("dialog")).toBeNull();
    await waitFor(() => expect(api.partners).toHaveBeenCalledTimes(2));
  });

  it("sends no account for a Profit share or a Loss share", async () => {
    await open();
    pick(/Type/, "Loss share");
    type(amount(), "60");
    submit();
    await waitFor(() => expect(api.recordTransaction).toHaveBeenCalled());
    const body = api.recordTransaction.mock.calls[0]![1] as FormData;
    expect(body.get("type")).toBe("LossShare");
    expect(body.has("financeAccountId")).toBe(false);
  });

  it("does not send an account chosen before switching to Profit share", async () => {
    await open();
    pick(/Cash or bank account/, "HBL Current · Seven Ventures");
    pick(/Type/, "Profit share");
    type(amount(), "60");
    submit();
    await waitFor(() => expect(api.recordTransaction).toHaveBeenCalled());
    expect((api.recordTransaction.mock.calls[0]![1] as FormData).has("financeAccountId")).toBe(false);
  });

  it("sends a picked attachment", async () => {
    await open();
    fill();
    const file = new File(["x"], "slip.pdf", { type: "application/pdf" });
    fireEvent.change(document.querySelector("input[type=file]") as HTMLInputElement, { target: { files: [file] } });
    expect(await dialog().findByText("slip.pdf")).toBeTruthy();
    submit();
    await waitFor(() => expect(api.recordTransaction).toHaveBeenCalled());
    expect((api.recordTransaction.mock.calls[0]![1] as FormData).get("attachment")).toBe(file);
  });

  it("shows the server's refusal inside the popup, keeps what was typed, and reuses the money key on the retry", async () => {
    api.recordTransaction.mockRejectedValueOnce(new Error("The attachment is too large. The maximum allowed size is 15 MB."));
    await open();
    fill();
    submit();
    expect(await dialog().findByText("The attachment is too large. The maximum allowed size is 15 MB.")).toBeTruthy();
    expect(amount().value).toBe("1,000,000");
    submit();
    await waitFor(() => expect(api.recordTransaction).toHaveBeenCalledTimes(2));
    expect(api.recordTransaction.mock.calls[1]![2]).toBe(api.recordTransaction.mock.calls[0]![2]);
  });

  it("gives a deliberate second entry its own key once the first was saved", async () => {
    show();
    await loaded();
    const record = async () => {
      fireEvent.click(row("Riaz Junaid").getByRole("button", { name: "More for Riaz Junaid" }));
      fireEvent.click(screen.getByRole("menuitem", { name: "Add transaction" }));
      fill();
      submit();
      await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    };
    await record();
    await record();
    expect(api.recordTransaction).toHaveBeenCalledTimes(2);
    expect(api.recordTransaction.mock.calls[1]![2]).not.toBe(api.recordTransaction.mock.calls[0]![2]);
  });

  it("keeps the same key when the popup is closed after a failure and opened again for the same entry", async () => {
    api.recordTransaction.mockRejectedValueOnce(new Error("The connection dropped."));
    show();
    await loaded();
    const start = () => {
      fireEvent.click(row("Riaz Junaid").getByRole("button", { name: "More for Riaz Junaid" }));
      fireEvent.click(screen.getByRole("menuitem", { name: "Add transaction" }));
      fill();
      submit();
    };
    start();
    await dialog().findByText("The connection dropped.");
    fireEvent.click(button("Cancel", dialog()));
    start();
    await waitFor(() => expect(api.recordTransaction).toHaveBeenCalledTimes(2));
    expect(api.recordTransaction.mock.calls[1]![2]).toBe(api.recordTransaction.mock.calls[0]![2]);
  });

  it("blocks closing mid-save", async () => {
    api.recordTransaction.mockReturnValue(new Promise(() => {}));
    await open();
    fill();
    submit();
    await waitFor(() => expect(api.recordTransaction).toHaveBeenCalled());
    expect(button("Cancel", dialog()).disabled).toBe(true);
    expect(button("Close", dialog()).disabled).toBe(true);
  });

  it("says why when the cash accounts could not be loaded, but not when only the capital accounts failed", async () => {
    api.accounts.mockImplementation(async (cashLikeOnly) => { if (cashLikeOnly) throw new Error("nope"); return allAccounts; });
    await open();
    expect(dialog().getByText("The accounts could not be loaded.")).toBeTruthy();
    cleanup();
    api.accounts.mockImplementation(async (cashLikeOnly) => { if (!cashLikeOnly) throw new Error("nope"); return cashAccounts; });
    await open();
    expect(dialog().queryByText("The accounts could not be loaded.")).toBeNull();
  });
});

describe("Statement", () => {
  const open = async (name = "Riaz Junaid") => {
    show();
    await loaded();
    fireEvent.click(row(name).getByRole("button", { name: `Statement for ${name}` }));
  };
  const rows = () => dialog().getAllByRole("row").slice(1).map((candidate) => Array.from(candidate.querySelectorAll("td")).map((cell) => cell.textContent));

  it("opens on All time and loads that, with the name, share and account in the header", async () => {
    await open();
    expect(api.statement).toHaveBeenCalledWith(1, "", "", expect.anything());
    expect(await dialog().findByText("Opening balance", { selector: "p" })).toBeTruthy();
    expect(dialog().getByRole("heading", { name: /Riaz Junaid · Statement/ }).textContent).toContain("40% share · Capital — Riaz Junaid");
    expect(footerButton("Close")).toBeTruthy();
  });

  it("has Close as its only footer button", async () => {
    await open();
    await dialog().findByText("Closing balance", { selector: "p" });
    const footer = screen.getByRole("dialog").querySelector("footer")!;
    expect(within(footer).getAllByRole("button").map((candidate) => candidate.textContent)).toEqual(["Close"]);
  });

  it("shows the opening and closing figures, the closing one green", async () => {
    await open();
    const closing = (await dialog().findByText("Closing balance", { selector: "p" })).nextElementSibling!;
    expect(closing.textContent).toBe("Rs 4,316,660");
    expect(closing.className).toContain("text-success");
    expect(dialog().queryByText(/As of|before the selected period|Every movement recorded/)).toBeNull();
  });

  it("shows the closing figure red when it is below zero", async () => {
    api.statement.mockResolvedValue(statement({ closingBalance: -500, openingBalance: -500, transactions: [] }));
    await open();
    const closing = (await dialog().findByText("Closing balance", { selector: "p" })).nextElementSibling!;
    expect(closing.textContent).toBe("-Rs 500");
    expect(closing.className).toContain("text-danger");
  });

  it("has the columns Date, Particulars, Debit, Credit and Balance", async () => {
    await open();
    await dialog().findAllByText("Contribution", { selector: "span.font-extrabold" });
    expect(dialog().getAllByRole("columnheader").map((header) => header.textContent)).toEqual(["Date", "Particulars", "Debit", "Credit", "Balance"]);
  });

  it("draws the opening row first, then each movement with its running balance, then the closing row", async () => {
    await open();
    await dialog().findAllByText("Contribution", { selector: "span.font-extrabold" });
    const lines = rows();
    expect(lines[0]).toEqual(["", "Opening balance", "", "", "Rs 0"]);
    expect(lines[1]![0]).toBe("Jul 1, 2026");
    expect(lines[1]![2]).toBe("—");
    expect(lines[1]![3]).toBe("Rs 3,000,000");
    expect(lines[1]![4]).toBe("Rs 3,000,000");
    expect(lines[2]![4]).toBe("Rs 4,000,000");
    expect(lines[3]![2]).toBe("Rs 100,000");
    expect(lines[3]![3]).toBe("—");
    expect(lines[3]![4]).toBe("Rs 3,900,000");
    expect(lines[4]![4]).toBe("Rs 4,316,660");
    expect(lines.at(-1)).toEqual(["", "Closing balance", "", "", "Rs 4,316,660"]);
  });

  it("shows the account and reference, the note when it adds something, the share snapshot, and drops a note that repeats the type", async () => {
    await open();
    await dialog().findAllByText("Contribution", { selector: "span.font-extrabold" });
    const lines = within(dialog().getAllByRole("row")[2]!);
    expect(lines.getByText("Meezan Bank · CH-1001")).toBeTruthy();
    expect(dialog().getByText("HBL Current · CH-1018")).toBeTruthy();
    expect(dialog().getByText("Second instalment of agreed capital")).toBeTruthy();
    expect(dialog().getByText("Cash in office")).toBeTruthy();
    expect(dialog().getByText("40% share")).toBeTruthy();
    expect(dialog().queryByText("ProfitShare")).toBeNull();
    expect(dialog().getByText("Profit share", { selector: "span.font-extrabold" })).toBeTruthy();
  });

  it("colours Debit red and Credit green", async () => {
    await open();
    await dialog().findAllByText("Contribution", { selector: "span.font-extrabold" });
    expect(dialog().getByText("Rs 100,000", { selector: "td span" }).className).toContain("text-danger");
    expect(dialog().getAllByText("Rs 3,000,000", { selector: "td span" })[0]!.className).toContain("text-success");
  });

  it("opens and downloads a movement's file", async () => {
    await open();
    await dialog().findAllByText("Contribution", { selector: "span.font-extrabold" });
    expect(dialog().getAllByRole("button", { name: "View" })).toHaveLength(1);
    fireEvent.click(button("View", dialog()));
    await waitFor(() => expect(openFile).toHaveBeenCalledWith("/api/finance/partners/1/transactions/1/attachment", "slip.pdf", false));
    fireEvent.click(button("Download", dialog()));
    await waitFor(() => expect(openFile).toHaveBeenLastCalledWith("/api/finance/partners/1/transactions/1/attachment", "slip.pdf", true));
  });

  it("shows a failure to open a file inside the popup", async () => {
    openFile.mockRejectedValue(new Error("The attachment could not be opened."));
    await open();
    await dialog().findAllByText("Contribution", { selector: "span.font-extrabold" });
    fireEvent.click(button("View", dialog()));
    expect(await dialog().findByText("The attachment could not be opened.")).toBeTruthy();
  });

  it("reads dates out of the server text, not through a time-zone parse", async () => {
    api.statement.mockResolvedValue(statement({ from: "2026-09-30T00:00:00", transactions: [movement({ date: "2026-09-30T00:00:00" })] }));
    await open();
    await dialog().findAllByText("Contribution", { selector: "span.font-extrabold" });
    expect(rows()[0]![0]).toBe("Sep 30, 2026");
    expect(rows()[1]![0]).toBe("Sep 30, 2026");
  });

  it("shows the go-live Opening balance row as money in, without repeating its own label as a note", async () => {
    api.statement.mockResolvedValue(statement({
      closingBalance: 500, transactions: [movement({ id: 9, type: "OpeningBalance", amount: 500, financeAccountId: null, reference: null, note: "Opening balance" })],
    }));
    await open();
    await dialog().findAllByText("Opening balance", { selector: "span.font-extrabold" });
    const lines = rows();
    expect(lines[1]![1]).toBe("Opening balance");
    expect(lines[1]![3]).toBe("Rs 500");
    expect(lines[1]![4]).toBe("Rs 500");
    // The server's note is the same words as the row's label, so the cell holds them once.
  });

  it("says so when there are no movements in the period", async () => {
    api.statement.mockResolvedValue(statement({ transactions: [], closingBalance: 0 }));
    await open();
    expect(await dialog().findByText("No capital movements in this period.")).toBeTruthy();
    expect(dialog().queryByRole("table")).toBeNull();
  });

  it("shows grey blocks while the first answer is on its way", async () => {
    api.statement.mockReturnValue(new Promise(() => {}));
    await open();
    expect(dialog().getByRole("status").getAttribute("aria-busy")).toBe("true");
    expect(button("Apply", dialog()).disabled).toBe(true);
  });

  it("does nothing until Apply, then loads the typed range", async () => {
    await open();
    await dialog().findAllByText("Contribution", { selector: "span.font-extrabold" });
    pickDay(/From/, "October 1, 2026");
    pickDay(/To/, "October 2, 2026");
    expect(api.statement).toHaveBeenCalledTimes(1);
    fireEvent.click(button("Apply", dialog()));
    await waitFor(() => expect(api.statement).toHaveBeenCalledTimes(2));
    expect(api.statement).toHaveBeenLastCalledWith(1, "2026-10-01", "2026-10-02", expect.anything());
  });

  it("will not pick a date after today", async () => {
    await open();
    fireEvent.click(dialog().getByRole("button", { name: /From/ }));
    expect((screen.getByRole("button", { name: "October 3, 2026" }) as HTMLButtonElement).disabled).toBe(true);
  });

  it("accepts a From date alone, or a To date alone", async () => {
    await open();
    await dialog().findAllByText("Contribution", { selector: "span.font-extrabold" });
    pickDay(/From/, "October 1, 2026");
    fireEvent.click(button("Apply", dialog()));
    await waitFor(() => expect(api.statement).toHaveBeenLastCalledWith(1, "2026-10-01", "", expect.anything()));
  });

  it("turns Apply off and shows a notice when From is after To", async () => {
    await open();
    await dialog().findAllByText("Contribution", { selector: "span.font-extrabold" });
    pickDay(/To/, "October 1, 2026");
    pickDay(/From/, "October 2, 2026");
    expect(dialog().getByText("The From date cannot be after the To date.")).toBeTruthy();
    expect(button("Apply", dialog()).disabled).toBe(true);
    expect(api.statement).toHaveBeenCalledTimes(1);
  });

  it("clears both dates and loads at once on All time", async () => {
    await open();
    await dialog().findAllByText("Contribution", { selector: "span.font-extrabold" });
    pickDay(/From/, "October 1, 2026");
    fireEvent.click(button("All time", dialog()));
    await waitFor(() => expect(api.statement).toHaveBeenCalledTimes(2));
    expect(api.statement).toHaveBeenLastCalledWith(1, "", "", expect.anything());
    expect(dialog().getByRole("button", { name: /From/ }).textContent).toContain("Select date");
  });

  it("keeps the old figures, dimmed, while a new period loads", async () => {
    await open();
    await dialog().findAllByText("Contribution", { selector: "span.font-extrabold" });
    api.statement.mockReturnValue(new Promise(() => {}));
    fireEvent.click(button("Apply", dialog()));
    expect(dialog().getAllByText("Rs 4,316,660").length).toBeGreaterThan(0);
    expect(dialog().getByText("Opening balance", { selector: "p" }).closest("[aria-busy=true]")).toBeTruthy();
  });

  it("only lets the newest request write its answer", async () => {
    await open();
    await dialog().findAllByText("Contribution", { selector: "span.font-extrabold" });
    let slow!: (value: Statement) => void;
    api.statement.mockReturnValueOnce(new Promise<Statement>((resolve) => { slow = resolve; }));
    fireEvent.click(button("Apply", dialog()));
    api.statement.mockResolvedValueOnce(statement({ closingBalance: 111, transactions: [] }));
    fireEvent.click(button("All time", dialog()));
    await dialog().findByText("Rs 111");
    await act(async () => { slow(statement({ closingBalance: 999, transactions: [] })); });
    expect(dialog().queryByText("Rs 999")).toBeNull();
    expect(dialog().getAllByText("Rs 111").length).toBeGreaterThan(0);
  });

  it("shows the server's message with Try again, and Try again repeats the range last asked for", async () => {
    await open();
    await dialog().findAllByText("Contribution", { selector: "span.font-extrabold" });
    pickDay(/From/, "October 1, 2026");
    api.statement.mockRejectedValueOnce(new Error("Capital partner not found."));
    fireEvent.click(button("Apply", dialog()));
    expect(await dialog().findByText("Capital partner not found.")).toBeTruthy();
    pickDay(/From/, "October 2, 2026");
    fireEvent.click(button("Try again", dialog()));
    await waitFor(() => expect(api.statement).toHaveBeenLastCalledWith(1, "2026-10-01", "", expect.anything()));
    await waitFor(() => expect(dialog().queryByText("Capital partner not found.")).toBeNull());
  });

  it("shows the error and Try again when the first load fails", async () => {
    api.statement.mockRejectedValueOnce(new Error("The statement could not be loaded."));
    await open();
    expect(await dialog().findByText("The statement could not be loaded.")).toBeTruthy();
    fireEvent.click(button("Try again", dialog()));
    await dialog().findAllByText("Contribution", { selector: "span.font-extrabold" });
  });

  it("cancels the pending answer when the popup is closed", async () => {
    let answer!: (value: Statement) => void;
    api.statement.mockReturnValue(new Promise<Statement>((resolve) => { answer = resolve; }));
    await open();
    fireEvent.click(footerButton("Close"));
    expect(screen.queryByRole("dialog")).toBeNull();
    await act(async () => { answer(statement()); });
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("works for an inactive partner too", async () => {
    api.statement.mockResolvedValue(statement({ partnerId: 5, partnerName: "Shabbir Hussain", transactions: [], closingBalance: 0 }));
    await open("Shabbir Hussain");
    expect(await dialog().findByText("No capital movements in this period.")).toBeTruthy();
    expect(api.statement).toHaveBeenCalledWith(5, "", "", expect.anything());
  });
});

describe("Capital partners on a phone", () => {
  beforeEach(() => stubMedia(true));

  it("has Add partner and a ⋯ with the one item Edit shares", async () => {
    show();
    await loaded();
    expect(button("Add partner")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Edit shares" })).toBeNull();
    fireEvent.click(button("More for capital partners"));
    expect(screen.getAllByRole("menuitem").map((item) => item.textContent)).toEqual(["Edit shares"]);
    fireEvent.click(screen.getByRole("menuitem", { name: "Edit shares" }));
    expect(dialog().getByText("Active partners must total exactly 100%")).toBeTruthy();
  });

  it("draws one card per partner with the share, status, Closing bar and the five figures", async () => {
    show();
    await loaded();
    const card = within(screen.getAllByRole("article")[0]!);
    expect(card.getByText("Riaz Junaid")).toBeTruthy();
    expect(card.getByText("Capital — Riaz Junaid")).toBeTruthy();
    expect(card.getByText("40%")).toBeTruthy();
    expect(card.getByText("Active")).toBeTruthy();
    expect(card.getByText("Closing")).toBeTruthy();
    for (const label of ["Opening", "Contributions", "Withdrawals", "Profit share", "Loss share"]) expect(card.getByText(label)).toBeTruthy();
    expect(card.getAllByText("Rs 4,316,660")).toHaveLength(1);
    expect(card.getByRole("button", { name: "Statement for Riaz Junaid" })).toBeTruthy();
    expect(card.getByRole("button", { name: "More for Riaz Junaid" })).toBeTruthy();
  });

  it("shows an inactive partner's badge and greys its Add transaction", async () => {
    show();
    await loaded();
    const card = within(screen.getAllByRole("article")[4]!);
    expect(card.getByText("Inactive")).toBeTruthy();
    fireEvent.click(card.getByRole("button", { name: "More for Shabbir Hussain" }));
    expect((screen.getByRole("menuitem", { name: "Add transaction" }) as HTMLButtonElement).disabled).toBe(true);
  });

  it("puts the notice's button under its text and opens Edit shares", async () => {
    api.partners.mockResolvedValue([riaz, adeel]);
    show();
    const notice = (await screen.findByText(/Active shares add up to 65%/)).closest("[role=alert]") as HTMLElement;
    expect(notice.className).toContain("flex-col");
    fireEvent.click(within(notice).getByRole("button", { name: "Edit shares" }));
    expect(screen.getByRole("dialog")).toBeTruthy();
  });

  it("shows the statement as blocks, with only the share in the header, and All time and Apply under the dates", async () => {
    show();
    await loaded();
    fireEvent.click(within(screen.getAllByRole("article")[0]!).getByRole("button", { name: "Statement for Riaz Junaid" }));
    await dialog().findByText("Closing balance", { selector: "p" });
    expect(dialog().getByRole("heading", { name: /Riaz Junaid · Statement/ }).textContent).toContain("40% share");
    expect(dialog().getByRole("heading", { name: /Riaz Junaid · Statement/ }).textContent).not.toContain("Capital —");
    expect(dialog().queryByRole("table")).toBeNull();
    expect(dialog().getByText("Opening balance", { selector: "span" })).toBeTruthy();
    expect(dialog().getByText("Meezan Bank · CH-1001", { exact: false }).textContent).toContain("Jul 1, 2026");
    expect(dialog().getAllByText(/Debit/).length).toBeGreaterThan(0);
    expect(dialog().getByRole("button", { name: "All time" })).toBeTruthy();
    expect(dialog().getByRole("button", { name: "Apply" })).toBeTruthy();
  });
});
