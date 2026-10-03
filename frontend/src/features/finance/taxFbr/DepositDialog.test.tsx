// @vitest-environment happy-dom
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ToastProvider } from "../../../components/ui/Toast.tsx";
import type { CashAccount } from "../loans/types.ts";
import { saveDeposit } from "../whtApi.ts";
import type { WhtDeposit } from "../whtTypes.ts";
import { DepositDialog } from "./DepositDialog.tsx";

vi.mock("../whtApi.ts", () => ({ saveDeposit: vi.fn() }));

const save = vi.mocked(saveDeposit);

const accounts: CashAccount[] = [
  { id: 3, name: "Meezan Bank", accountHolderName: "Seven Ventures", isActive: true },
  { id: 4, name: "HBL Current", accountHolderName: "Seven Ventures", isActive: true },
  { id: 5, name: "Old Bank", accountHolderName: "Seven Ventures", isActive: false },
];

const saved: WhtDeposit = {
  id: 7, financeAccountId: 3, financeAccountName: "Meezan Bank", amount: 412_000, depositDate: "2026-09-15T00:00:00",
  challanNumber: "CPR-0923-118", periodFrom: "2026-08-01T00:00:00", periodTo: "2026-08-10T00:00:00", notes: null,
  createdAt: "2026-09-15T08:00:00", concurrencyToken: "token-7",
};

const onClose = vi.fn();
const onSaved = vi.fn();

function show(props: { deposit?: WhtDeposit | null; owed?: number; accountsError?: string | null; list?: CashAccount[] } = {}) {
  return render(
    <ToastProvider>
      <DepositDialog
        deposit={props.deposit ?? null}
        owed={props.owed ?? 184_350}
        accounts={props.list ?? accounts}
        accountsError={props.accountsError ?? null}
        onClose={onClose}
        onSaved={onSaved}
      />
    </ToastProvider>,
  );
}

const dialog = () => within(screen.getByRole("dialog"));
const saveButton = () => within(screen.getByRole("dialog").querySelector("footer") as HTMLElement).getByRole("button", { name: "Save" }) as HTMLButtonElement;
const amount = () => dialog().getByLabelText(/^Amount/) as HTMLInputElement;
const chooseAccount = (name: string) => {
  fireEvent.click(dialog().getByRole("combobox", { name: /Paid from account/ }));
  fireEvent.click(screen.getByRole("option", { name }));
};
const pickDay = (field: RegExp, day: string) => {
  fireEvent.click(dialog().getByRole("button", { name: field }));
  fireEvent.click(screen.getByRole("button", { name: day }));
};

beforeEach(() => {
  vi.resetAllMocks();
  vi.stubGlobal("matchMedia", (query: string) => ({ matches: false, media: query, addEventListener: () => {}, removeEventListener: () => {} }));
  // 2026-10-01 12:00 in Pakistan.
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date("2026-10-01T07:00:00Z"));
  Element.prototype.scrollIntoView = () => {};
  save.mockResolvedValue(saved);
});

afterEach(() => {
  cleanup();
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe("Record deposit", () => {
  it("opens on what is owed, today, and says how much is owed under the amount", () => {
    show();
    expect(dialog().getByText("Record deposit to FBR")).toBeTruthy();
    expect(amount().value).toBe("184,350");
    expect(dialog().getByText("Owed now: Rs 184,350")).toBeTruthy();
    expect(dialog().getByRole("button", { name: /Deposit date/ }).textContent).toContain("Oct 1, 2026");
    expect(dialog().queryByText(/This reduces the account balance/)).toBeNull();
  });

  it("leaves the amount empty and says nothing about what is owed when nothing is", () => {
    show({ owed: 0 });
    expect(amount().value).toBe("");
    expect(dialog().queryByText(/Owed now/)).toBeNull();
  });

  it("keeps Save off until an account is chosen, then turns it on", () => {
    show();
    expect(saveButton().disabled).toBe(true);
    chooseAccount("Meezan Bank · Seven Ventures");
    expect(saveButton().disabled).toBe(false);
  });

  it("keeps Save off for an empty or zero amount", () => {
    show();
    chooseAccount("Meezan Bank · Seven Ventures");
    fireEvent.change(amount(), { target: { value: "" } });
    expect(saveButton().disabled).toBe(true);
    fireEvent.change(amount(), { target: { value: "0" } });
    expect(saveButton().disabled).toBe(true);
    fireEvent.change(amount(), { target: { value: "500.5" } });
    expect(saveButton().disabled).toBe(false);
  });

  it("offers active cash and bank accounts only, and the placeholder says so", () => {
    show();
    expect(dialog().getByRole("combobox", { name: /Paid from account/ }).textContent).toContain("Select cash or bank account");
    fireEvent.click(dialog().getByRole("combobox", { name: /Paid from account/ }));
    expect(screen.getAllByRole("option").map((option) => option.textContent)).toEqual(["Meezan Bank · Seven Ventures", "HBL Current · Seven Ventures"]);
  });

  it("never lets the period end before its start", () => {
    show({ deposit: { ...saved, periodFrom: "2026-08-10T00:00:00", periodTo: "2026-08-20T00:00:00" } });
    fireEvent.click(dialog().getByRole("button", { name: /Period covered to/ }));
    expect((screen.getByRole("button", { name: "August 9, 2026" }) as HTMLButtonElement).disabled).toBe(true);
    expect((screen.getByRole("button", { name: "August 10, 2026" }) as HTMLButtonElement).disabled).toBe(false);
  });

  it("does not let a future deposit date be picked", () => {
    show();
    fireEvent.click(dialog().getByRole("button", { name: /Deposit date/ }));
    expect((screen.getByRole("button", { name: "October 2, 2026" }) as HTMLButtonElement).disabled).toBe(true);
  });

  it("sends the body with blanks as null and the key, then closes with a toast", async () => {
    show();
    chooseAccount("HBL Current · Seven Ventures");
    fireEvent.click(saveButton());
    await waitFor(() => expect(onClose).toHaveBeenCalled());
    expect(save).toHaveBeenCalledTimes(1);
    const [id, body, key] = save.mock.calls[0]!;
    expect(id).toBeNull();
    expect(body).toEqual({
      financeAccountId: 4, amount: 184_350, depositDate: "2026-10-01", challanNumber: null, periodFrom: null, periodTo: null, notes: null, concurrencyToken: null,
    });
    expect(key).toMatch(/^wht-deposit-/);
    expect(onSaved).toHaveBeenCalledTimes(1);
    expect(await screen.findByText("Deposit recorded.")).toBeTruthy();
  });

  it("shows the server's refusal inside the popup, keeps what was typed and keeps the popup open", async () => {
    save.mockRejectedValueOnce(new Error("Deposit exceeds the withholding tax available to a deposit dated Oct 1, 2026. At most Rs 100 can be deposited on that date."));
    show();
    chooseAccount("Meezan Bank · Seven Ventures");
    fireEvent.change(dialog().getByLabelText(/Challan \/ CPR number/), { target: { value: "CPR-1001-204" } });
    fireEvent.click(saveButton());
    expect((await screen.findByRole("alert")).textContent).toContain("At most Rs 100 can be deposited on that date.");
    expect(onClose).not.toHaveBeenCalled();
    expect(onSaved).not.toHaveBeenCalled();
    expect(amount().value).toBe("184,350");
    expect((dialog().getByLabelText(/Challan \/ CPR number/) as HTMLInputElement).value).toBe("CPR-1001-204");
    expect(saveButton().disabled).toBe(false);
  });

  it("sends the same key when Save is pressed again, even with a corrected amount", async () => {
    save.mockRejectedValueOnce(new Error("The connection dropped."));
    show();
    chooseAccount("Meezan Bank · Seven Ventures");
    fireEvent.click(saveButton());
    await screen.findByRole("alert");
    fireEvent.change(amount(), { target: { value: "100" } });
    fireEvent.click(saveButton());
    await waitFor(() => expect(onClose).toHaveBeenCalled());
    expect(save).toHaveBeenCalledTimes(2);
    expect(save.mock.calls[1]![2]).toBe(save.mock.calls[0]![2]);
  });

  it("makes a new key each time the popup is opened", async () => {
    const first = show();
    chooseAccount("Meezan Bank · Seven Ventures");
    fireEvent.click(saveButton());
    await waitFor(() => expect(onClose).toHaveBeenCalled());
    first.unmount();
    show();
    chooseAccount("Meezan Bank · Seven Ventures");
    fireEvent.click(saveButton());
    await waitFor(() => expect(save).toHaveBeenCalledTimes(2));
    expect(save.mock.calls[1]![2]).not.toBe(save.mock.calls[0]![2]);
  });

  it("shows why the accounts are missing", () => {
    show({ list: [], accountsError: "The cash and bank accounts could not be loaded." });
    expect(dialog().getByText("The cash and bank accounts could not be loaded.")).toBeTruthy();
  });
});

describe("Edit deposit", () => {
  it("names the challan under the title and opens on what was saved", () => {
    show({ deposit: saved });
    expect(dialog().getByText("Edit deposit to FBR")).toBeTruthy();
    expect(dialog().getAllByText("CPR-0923-118").length).toBeGreaterThan(0);
    expect(amount().value).toBe("412,000");
    expect(dialog().getByRole("combobox", { name: /Paid from account/ }).textContent).toContain("Meezan Bank · Seven Ventures");
    expect(dialog().getByRole("button", { name: /Deposit date/ }).textContent).toContain("Sep 15, 2026");
  });

  it("does not say what is owed now", () => {
    show({ deposit: saved, owed: 184_350 });
    expect(dialog().queryByText(/Owed now/)).toBeNull();
  });

  it("keeps the account the deposit was paid from even when it has since been deactivated", () => {
    show({ deposit: { ...saved, financeAccountId: 5, financeAccountName: "Old Bank" } });
    expect(dialog().getByRole("combobox", { name: /Paid from account/ }).textContent).toContain("Old Bank · Seven Ventures (Inactive)");
  });

  it("sends the record version and saves over the same deposit", async () => {
    show({ deposit: saved });
    fireEvent.change(amount(), { target: { value: "400000" } });
    fireEvent.click(saveButton());
    await waitFor(() => expect(onClose).toHaveBeenCalled());
    const [id, body] = save.mock.calls[0]!;
    expect(id).toBe(7);
    expect(body).toMatchObject({ amount: 400_000, challanNumber: "CPR-0923-118", periodFrom: "2026-08-01", periodTo: "2026-08-10", concurrencyToken: "token-7" });
    expect(await screen.findByText("Deposit saved.")).toBeTruthy();
  });

  it("turns Save off and says why when the start of the period is moved past its end", () => {
    show({ deposit: saved });
    pickDay(/Period covered from/, "August 20, 2026");
    expect(dialog().getByText("The period end cannot be before its start.")).toBeTruthy();
    expect(saveButton().disabled).toBe(true);
  });
});
