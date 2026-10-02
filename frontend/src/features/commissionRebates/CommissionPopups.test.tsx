// @vitest-environment happy-dom
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ToastProvider } from "../../components/ui/Toast.tsx";
import { pakistanToday } from "../../lib/financePeriods.ts";
import { uploadProof } from "../proof/proofApi";
import { ApplyRebateDialog } from "./ApplyRebateDialog";
import { CommissionDialog } from "./CommissionDialog";
import { PayCommissionDialog } from "./PayCommissionDialog";
import { RebateDialog } from "./RebateDialog";
import { commissionRebateApi } from "./api";
import type { BookingWorkspace, Commission, Partner, Rebate } from "./types";

vi.mock("../proof/proofApi", () => ({ uploadProof: vi.fn(), openProof: vi.fn() }));
vi.mock("./api", () => ({
  apiError: vi.fn(),
  commissionRebateApi: { payout: vi.fn(), disburseRebate: vi.fn(), savePartner: vi.fn(), createCommission: vi.fn(), createRebate: vi.fn(), updateRebate: vi.fn(), previewCommission: vi.fn(), updateCommission: vi.fn() },
}));

const accounts = [{ id: 1, name: "Meezan Bank", accountHolderName: "Deen", isActive: true }];
const figures = { bookingReference: "BK-000013", agreedSalePrice: 12_800_000, netSalePrice: 12_800_000, amountCollected: 4_172_000, rebateCredits: 0 };
const commission = { id: 1, partnerId: 7, partnerName: "Ali Estate Agency", outstandingAmount: 156_000, concurrencyToken: "c1", payouts: [] } as unknown as Commission;
const rebate = (method: string, extra = {}) => ({ id: 5, outstandingAmount: 150_000, method, concurrencyToken: "r1", disbursements: [], ...extra }) as unknown as Rebate;

const wrap = (ui: React.ReactNode) => render(<MemoryRouter><ToastProvider>{ui}</ToastProvider></MemoryRouter>);
const primary = (name: string) => screen.getByRole("button", { name });
const workspaceOf = (over: Partial<BookingWorkspace>) => ({ commissions: [], rebates: [], ...over }) as unknown as BookingWorkspace;

beforeEach(() => vi.mocked(uploadProof).mockResolvedValue());
afterEach(() => { cleanup(); vi.clearAllMocks(); });

describe("Pay commission", () => {
  const show = (run = vi.fn(async (operation: () => Promise<BookingWorkspace>) => operation())) => {
    const onClose = vi.fn();
    const onProofUploaded = vi.fn(async () => undefined);
    wrap(<PayCommissionDialog bookingId={13} commission={commission} financeAccounts={accounts} accountsError={null} run={run} onProofUploaded={onProofUploaded} onClose={onClose} />);
    return { run, onClose, onProofUploaded };
  };

  it("starts on what remains and pays that with today's date", async () => {
    vi.mocked(commissionRebateApi.payout).mockResolvedValue(workspaceOf({ commissions: [{ ...commission, payouts: [{ id: 61 }] } as never] }));
    const { onClose } = show();
    expect((screen.getByLabelText(/Amount/) as HTMLInputElement).value).toBe("156,000");
    fireEvent.change(screen.getByLabelText(/Reference/), { target: { value: "TRX-91002" } });
    fireEvent.click(primary("Pay commission"));

    await waitFor(() => expect(onClose).toHaveBeenCalled());
    expect(commissionRebateApi.payout).toHaveBeenCalledWith(13, 1, {
      financeAccountId: 1, amount: 156_000, paymentDate: pakistanToday(), paymentMethod: "BankTransfer", paymentReference: "TRX-91002", notes: null,
      idempotencyKey: expect.any(String), commissionConcurrencyToken: "c1",
    });
  });

  it("attaches the optional proof to the payment that was just made", async () => {
    vi.mocked(commissionRebateApi.payout).mockResolvedValue(workspaceOf({ commissions: [{ ...commission, payouts: [{ id: 61 }] } as never] }));
    const { onClose, onProofUploaded } = show();
    fireEvent.change(screen.getByLabelText(/Reference/), { target: { value: "TRX-1" } });
    const slip = new File(["x"], "slip.pdf", { type: "application/pdf" });
    fireEvent.change(document.querySelector("input[type=file]") as HTMLInputElement, { target: { files: [slip] } });
    fireEvent.click(primary("Pay commission"));
    await waitFor(() => expect(onClose).toHaveBeenCalled());
    expect(uploadProof).toHaveBeenCalledWith("CommissionPayout", 61, slip, expect.any(Function), expect.any(AbortSignal));
    // The row was built from the answer to the save, which was before the file went up: read it again.
    expect(onProofUploaded).toHaveBeenCalledTimes(1);
  });

  it("does not re-read the workspace when there is no proof", async () => {
    vi.mocked(commissionRebateApi.payout).mockResolvedValue(workspaceOf({ commissions: [{ ...commission, payouts: [{ id: 61 }] } as never] }));
    const { onClose, onProofUploaded } = show();
    fireEvent.change(screen.getByLabelText(/Reference/), { target: { value: "TRX-1" } });
    fireEvent.click(primary("Pay commission"));
    await waitFor(() => expect(onClose).toHaveBeenCalled());
    expect(onProofUploaded).not.toHaveBeenCalled();
  });

  it("shows the server's bank-details message with a link to the partner page, and stays open", async () => {
    vi.mocked(commissionRebateApi.payout).mockRejectedValue(new Error("Bank-transfer payouts require the partner's bank name, account title, and account number or IBAN."));
    const { onClose } = show();
    fireEvent.change(screen.getByLabelText(/Reference/), { target: { value: "TRX-1" } });
    fireEvent.click(primary("Pay commission"));

    expect(await screen.findByText(/require the partner's bank name/)).toBeTruthy();
    expect(screen.getByRole("link", { name: "Open the partner page" }).getAttribute("href")).toBe(`/finance/commissions-rebates?tab=partners&partner=${commission.partnerId}`);
    expect((screen.getByLabelText(/Reference/) as HTMLInputElement).value).toBe("TRX-1");
    expect(onClose).not.toHaveBeenCalled();
  });

  it("will not send more than what remains", () => {
    show();
    fireEvent.change(screen.getByLabelText(/Amount/), { target: { value: "156001" } });
    fireEvent.click(primary("Pay commission"));
    expect(screen.getByText("Can't be more than Rs 156,000")).toBeTruthy();
    expect(commissionRebateApi.payout).not.toHaveBeenCalled();
  });
});

describe("Add commission with a new partner", () => {
  it("opens New partner on top, and selects the partner it saves", async () => {
    const created = { id: 99, name: "Noor Brokers", partnerType: "Broker" } as unknown as Partner;
    vi.mocked(commissionRebateApi.savePartner).mockResolvedValue(created);
    const onPartnerCreated = vi.fn();
    wrap(
      <CommissionDialog
        bookingId={13} workspace={figures} existing={null} partners={[{ id: 7, name: "Ali Estate Agency" } as Partner, { id: 8, name: "Khan Associates" } as Partner]}
        takenPartnerIds={new Set([8])} run={vi.fn()} onPartnerCreated={onPartnerCreated} onClose={vi.fn()}
      />,
    );
    // Partners that already hold a commission on this booking are not offered.
    fireEvent.click(screen.getByRole("combobox", { name: /Partner/ }));
    expect(screen.getByRole("option", { name: "Ali Estate Agency" })).toBeTruthy();
    expect(screen.queryByRole("option", { name: "Khan Associates" })).toBeNull();
    fireEvent.keyDown(document.activeElement ?? document.body, { key: "Escape" });

    fireEvent.click(screen.getByRole("button", { name: "+ New partner" }));
    const partnerDialog = (await screen.findAllByRole("dialog")).at(-1)!;
    expect(within(partnerDialog).getByText("New partner")).toBeTruthy();
    fireEvent.change(within(partnerDialog).getByLabelText(/Name/), { target: { value: "Noor Brokers" } });
    fireEvent.click(within(partnerDialog).getByRole("button", { name: "Save partner" }));

    await waitFor(() => expect(onPartnerCreated).toHaveBeenCalledWith(created));
    expect(commissionRebateApi.savePartner).toHaveBeenCalledWith({ name: "Noor Brokers", partnerType: "Agency", phone: null, email: null });
  });

  it("works the commission out live as the percentage is typed", () => {
    wrap(<CommissionDialog bookingId={13} workspace={figures} existing={null} partners={[]} takenPartnerIds={new Set()} run={vi.fn()} onPartnerCreated={vi.fn()} onClose={vi.fn()} />);
    fireEvent.change(screen.getByLabelText(/Percentage/), { target: { value: "2" } });
    expect(screen.getByText("Rs 256,000")).toBeTruthy();
    fireEvent.click(screen.getByRole("radio", { name: "Fixed amount" }));
    fireEvent.change(screen.getByLabelText(/Amount/), { target: { value: "50000" } });
    expect(screen.getByText("Rs 50,000")).toBeTruthy();
  });
});

describe("Edit commission", () => {
  const editing = (over: Record<string, unknown>) => ({
    id: 1, partnerId: 7, partnerName: "Ali Estate Agency", status: "Pending", isManual: true, calculationType: "Percentage",
    calculationBasis: "NetSalePriceAfterDiscount", percentageRate: 2, fixedAmount: null, manualReason: "Agreed on the phone",
    finalAmount: 256_000, paidAmount: 0, outstandingAmount: 256_000, basisAmount: 12_800_000, concurrencyToken: "c1", payouts: [], ...over,
  }) as unknown as Commission;
  const open = (existing: Commission) => wrap(
    <CommissionDialog bookingId={13} workspace={figures} existing={existing} partners={[{ id: 7, name: "Ali Estate Agency" } as Partner]}
      takenPartnerIds={new Set([7])} run={vi.fn()} onPartnerCreated={vi.fn()} onClose={vi.fn()} />,
  );

  it("lets the partner change until the first payout, then locks it", () => {
    open(editing({}));
    expect((screen.getByRole("combobox", { name: /Partner/ }) as HTMLButtonElement).disabled).toBe(false);
    cleanup();
    open(editing({ paidAmount: 100_000, payouts: [{ id: 61 }] }));
    expect((screen.getByRole("combobox", { name: /Partner/ }) as HTMLButtonElement).disabled).toBe(true);
  });

  const ruleDrivenChoices = () => wrap(
    <CommissionDialog bookingId={13} workspace={figures} existing={editing({ isManual: false, ruleId: 3, ruleNameSnapshot: "Standard agency rate" })}
      partners={[{ id: 7, name: "Ali Estate Agency" } as Partner, { id: 8, name: "Noor Brokers" } as Partner]}
      takenPartnerIds={new Set([7])} run={vi.fn(async (operation: () => Promise<BookingWorkspace>) => operation())} onPartnerCreated={vi.fn()} onClose={vi.fn()} />,
  );
  const pickNoor = () => {
    fireEvent.click(screen.getByRole("combobox", { name: /Partner/ }));
    fireEvent.click(screen.getByRole("option", { name: "Noor Brokers" }));
  };
  const save = () => screen.getByRole("button", { name: "Save changes" }) as HTMLButtonElement;

  it("has nothing to save on a rule-driven commission until the partner changes", () => {
    ruleDrivenChoices();
    expect(save().disabled).toBe(true);
    expect(commissionRebateApi.previewCommission).not.toHaveBeenCalled();
  });

  it("shows what the new partner's rule gives before allowing Save, and saves for that partner with no old rule", async () => {
    vi.mocked(commissionRebateApi.previewCommission).mockResolvedValue({ partnerId: 8, ruleName: "Noor flat fee", amount: 200_000 });
    vi.mocked(commissionRebateApi.updateCommission).mockResolvedValue(workspaceOf({}));
    ruleDrivenChoices();
    pickNoor();
    expect(save().disabled).toBe(true);
    expect(await screen.findByText("Rs 200,000")).toBeTruthy();
    expect(screen.getByText(/Calculated from “Noor flat fee” for Noor Brokers/)).toBeTruthy();
    expect(commissionRebateApi.previewCommission).toHaveBeenCalledWith(13, 8, 1);
    await waitFor(() => expect(save().disabled).toBe(false));

    fireEvent.click(save());
    await waitFor(() => expect(commissionRebateApi.updateCommission).toHaveBeenCalled());
    expect(commissionRebateApi.updateCommission).toHaveBeenCalledWith(13, 1, expect.objectContaining({ partnerId: 8, ruleId: null, attributionId: null, isManual: false }));
  });

  it("blocks Save and says why when the new partner has no rule", async () => {
    vi.mocked(commissionRebateApi.previewCommission).mockRejectedValue(new Error("No commission rule applies. Create a rule or use a documented manual commission."));
    ruleDrivenChoices();
    pickNoor();
    expect(await screen.findByText(/No commission rule applies/)).toBeTruthy();
    expect(save().disabled).toBe(true);
    expect(commissionRebateApi.updateCommission).not.toHaveBeenCalled();
  });

  it("locks the partner and the Save button after a payout, saying nothing can change", () => {
    wrap(
      <CommissionDialog bookingId={13} workspace={figures} existing={editing({ isManual: false, ruleId: 3, paidAmount: 100_000, payouts: [{ id: 61 }] })}
        partners={[{ id: 7, name: "Ali Estate Agency" } as Partner]} takenPartnerIds={new Set([7])} run={vi.fn()} onPartnerCreated={vi.fn()} onClose={vi.fn()} />,
    );
    expect(save().disabled).toBe(true);
    expect(screen.getByText(/There is nothing to change here/)).toBeTruthy();
  });

  it("offers Notes on a manual commission but not on a rule-driven one, which would discard them", () => {
    open(editing({}));
    expect(screen.getByLabelText("Notes")).toBeTruthy();
    cleanup();
    open(editing({ isManual: false, ruleNameSnapshot: "Standard agency rate" }));
    expect(screen.queryByLabelText("Notes")).toBeNull();
  });
});

describe("Apply rebate", () => {
  const show = (r: Rebate, installments = [{ id: 15, sequenceNumber: 5, type: "Regular", remainingBalance: 918_000, status: "Pending" }]) => {
    const run = vi.fn(async (operation: () => Promise<BookingWorkspace>) => operation());
    const onClose = vi.fn();
    const onProofUploaded = vi.fn(async () => undefined);
    wrap(<ApplyRebateDialog bookingId={13} rebate={r} workspace={figures} installments={installments} financeAccounts={accounts} accountsError={null} run={run} onProofUploaded={onProofUploaded} onClose={onClose} />);
    return { onClose, onProofUploaded };
  };

  it("pays by cash or bank with an account, a reference and optional proof", async () => {
    vi.mocked(commissionRebateApi.disburseRebate).mockResolvedValue(workspaceOf({ rebates: [rebate("CashOrBankPayment", { disbursements: [{ id: 81 }] })] }));
    const { onClose } = show(rebate("CashOrBankPayment"));
    expect(screen.getByLabelText(/Pay from account/)).toBeTruthy();
    fireEvent.change(screen.getByLabelText(/Reference/), { target: { value: "TRX-91540" } });
    fireEvent.click(primary("Apply rebate"));
    await waitFor(() => expect(onClose).toHaveBeenCalled());
    expect(commissionRebateApi.disburseRebate).toHaveBeenCalledWith(13, 5, expect.objectContaining({
      method: "CashOrBankPayment", amount: 150_000, appliedAt: pakistanToday(), financeAccountId: 1, installmentId: null, paymentMethod: "BankTransfer", reference: "TRX-91540",
      rebateConcurrencyToken: "r1",
    }));
  });

  it("re-reads the workspace once the proof of a cash payment has uploaded", async () => {
    vi.mocked(commissionRebateApi.disburseRebate).mockResolvedValue(workspaceOf({ rebates: [rebate("CashOrBankPayment", { disbursements: [{ id: 81 }] })] }));
    const { onClose, onProofUploaded } = show(rebate("CashOrBankPayment"));
    fireEvent.change(screen.getByLabelText(/Reference/), { target: { value: "TRX-91540" } });
    const slip = new File(["x"], "slip.pdf", { type: "application/pdf" });
    fireEvent.change(document.querySelector("input[type=file]") as HTMLInputElement, { target: { files: [slip] } });
    fireEvent.click(primary("Apply rebate"));
    await waitFor(() => expect(onClose).toHaveBeenCalled());
    expect(uploadProof).toHaveBeenCalledWith("RebateDisbursement", 81, slip, expect.any(Function), expect.any(AbortSignal));
    expect(onProofUploaded).toHaveBeenCalledTimes(1);
  });

  it("taking it off installments asks for the installment, and no account", () => {
    show(rebate("InstallmentAdjustment"));
    expect(screen.queryByLabelText(/Pay from account/)).toBeNull();
    expect(screen.queryByLabelText(/Reference/)).toBeNull();
    fireEvent.click(primary("Apply rebate"));
    expect(screen.getByText("Choose the installment.")).toBeTruthy();
    expect(commissionRebateApi.disburseRebate).not.toHaveBeenCalled();
  });

  it("reducing the outstanding balance asks for an amount and a date only, and sends no installment or account", async () => {
    vi.mocked(commissionRebateApi.disburseRebate).mockResolvedValue(workspaceOf({}));
    const { onClose } = show(rebate("OutstandingBalanceReduction"));
    expect(screen.queryByLabelText(/Installment/)).toBeNull();
    expect(screen.queryByLabelText(/Pay from account/)).toBeNull();
    fireEvent.click(primary("Apply rebate"));
    await waitFor(() => expect(onClose).toHaveBeenCalled());
    expect(commissionRebateApi.disburseRebate).toHaveBeenCalledWith(13, 5, expect.objectContaining({
      method: "OutstandingBalanceReduction", financeAccountId: null, installmentId: null, paymentMethod: null, reference: null,
    }));
  });
});

describe("Add and edit rebate", () => {
  it("shows the net price after the rebate and saves the way the customer gets it", async () => {
    vi.mocked(commissionRebateApi.createRebate).mockResolvedValue(workspaceOf({}));
    const run = vi.fn(async (operation: () => Promise<BookingWorkspace>) => operation());
    const onClose = vi.fn();
    wrap(<RebateDialog bookingId={13} workspace={figures} existing={null} run={run} onClose={onClose} />);
    fireEvent.change(screen.getByLabelText(/Amount/), { target: { value: "200000" } });
    expect(screen.getByText("Rs 12,600,000")).toBeTruthy();
    fireEvent.click(primary("Save rebate"));
    await waitFor(() => expect(onClose).toHaveBeenCalled());
    expect(commissionRebateApi.createRebate).toHaveBeenCalledWith(13, expect.objectContaining({ fixedAmount: 200_000, method: "CashOrBankPayment" }));
  });

  it("locks how the customer gets it once any has been given, and hides Cancel", () => {
    const given = rebate("CashOrBankPayment", { calculationType: "FixedAmount", fixedAmount: 200_000, calculationBasis: "NetSalePriceAfterDiscount", reason: "Loyal", status: "Pending", appliedOrPaidAmount: 50_000, adjustmentAmount: 0, disbursements: [{ id: 81 }] });
    wrap(<RebateDialog bookingId={13} workspace={figures} existing={given} run={vi.fn()} onClose={vi.fn()} />);
    expect(screen.getByText("Rs 50,000 already given")).toBeTruthy();
    expect(screen.getByRole("combobox", { name: /How the customer gets it/ }).hasAttribute("disabled")).toBe(true);
    expect(screen.queryByRole("button", { name: "Cancel this rebate" })).toBeNull();
  });

  it("offers Cancel this rebate while nothing is given", () => {
    const fresh = rebate("CashOrBankPayment", { calculationType: "FixedAmount", fixedAmount: 200_000, calculationBasis: "NetSalePriceAfterDiscount", reason: "Loyal", status: "Pending", appliedOrPaidAmount: 0, adjustmentAmount: 0 });
    wrap(<RebateDialog bookingId={13} workspace={figures} existing={fresh} run={vi.fn()} onClose={vi.fn()} />);
    expect(screen.getByRole("button", { name: "Cancel this rebate" })).toBeTruthy();
  });
});
