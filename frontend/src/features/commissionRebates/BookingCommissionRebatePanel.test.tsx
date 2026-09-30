// @vitest-environment happy-dom
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ToastProvider } from "../../components/ui/Toast.tsx";
import { api } from "../../api/api.ts";
import BookingCommissionRebatePanel from "./BookingCommissionRebatePanel";
import { commissionRebateApi } from "./api";
import type { BookingWorkspace } from "./types";

vi.mock("../proof/proofApi", () => ({ uploadProof: vi.fn(), openProof: vi.fn() }));
vi.mock("../../api/api.ts", () => ({ api: vi.fn() }));
vi.mock("./api", () => ({
  apiError: vi.fn(),
  commissionRebateApi: {
    workspace: vi.fn(), partners: vi.fn(), reversePayout: vi.fn(), reverseDisbursement: vi.fn(),
    updateCommission: vi.fn(), commissionStatus: vi.fn(), payout: vi.fn(),
  },
}));

const evidence = { id: 9, originalFileName: "transfer-slip.pdf", contentType: "application/pdf", fileSize: 10, uploadedByName: null, uploadedAt: "2026-08-10T00:00:00" };

const aliPayout = {
  id: 61, financeAccountId: 1, financeAccountName: "Meezan Bank — 0123", installmentId: null, rebateMethod: null, amount: 100_000, reversedAmount: 0,
  date: "2026-08-10T00:00:00", paymentMethod: "BankTransfer", reference: "TRX-66120", notes: null, recordedByName: "accountant", concurrencyToken: "p", evidence: [evidence],
};
const ali = {
  id: 1, bookingId: 13, partnerId: 7, partnerName: "Ali Estate Agency", partnerType: "Agency", attributionId: null, ruleId: null, isManual: true, manualReason: null,
  allocationPercent: 100, calculationType: "Percentage", percentageRate: 2, fixedAmount: null, calculationBasis: "NetSalePriceAfterDiscount", basisAmount: 12_800_000,
  calculatedAmount: 256_000, adjustmentAmount: 0, adjustmentReason: null, finalAmount: 256_000, paidAmount: 100_000, outstandingAmount: 156_000, recoveryRequiredAmount: 0,
  status: "Pending", concurrencyToken: "c1", payouts: [aliPayout], evidence: [],
};
const khan = {
  ...ali, id: 2, partnerId: 8, partnerName: "Khan Associates", partnerType: "Referral Partner", calculationType: "FixedAmount", percentageRate: null, fixedAmount: 50_000,
  finalAmount: 50_000, paidAmount: 50_000, outstandingAmount: 0, status: "Paid",
  payouts: [{ ...aliPayout, id: 62, amount: 50_000, paymentMethod: "Cash", reference: null, financeAccountName: "Cash in hand — Head office", recordedByName: "admin", evidence: [] }],
};
const rebate = {
  id: 5, bookingId: 13, customerName: "Usman Tariq", calculationType: "FixedAmount", percentageRate: null, fixedAmount: 200_000, calculationBasis: "NetSalePriceAfterDiscount",
  basisAmount: 12_800_000, calculatedAmount: 200_000, adjustmentAmount: 0, adjustmentReason: null, finalAmount: 200_000, appliedOrPaidAmount: 50_000, outstandingAmount: 150_000,
  recoveryRequiredAmount: 0, reason: "Loyal customer", method: "CashOrBankPayment", status: "Pending", concurrencyToken: "r1", evidence: [],
  disbursements: [{ ...aliPayout, id: 81, amount: 50_000, date: "2026-07-28T00:00:00", reference: "TRX-70455", rebateMethod: "CashOrBankPayment", recordedByName: "admin", evidence: [] }],
};

const workspace = (over: Partial<BookingWorkspace> = {}) => ({
  bookingId: 13, bookingReference: "BK-000013", bookingStatus: "PaymentPlanActive", agreedSalePrice: 12_800_000, netSalePrice: 12_800_000, amountCollected: 4_172_000,
  rebateCredits: 0, attributions: [], commissions: [ali, khan], rebates: [rebate], audit: [], hasMoreAudit: false, ...over,
}) as unknown as BookingWorkspace;

function show(ws = workspace()) {
  vi.mocked(commissionRebateApi.workspace).mockResolvedValue(ws);
  const onChanged = vi.fn();
  render(
    <MemoryRouter>
      <ToastProvider>
        <BookingCommissionRebatePanel bookingId={13} installments={[]} installmentsFresh onChanged={onChanged} />
      </ToastProvider>
    </MemoryRouter>,
  );
  return onChanged;
}

const block = (name: string) => screen.getByRole("heading", { name: new RegExp(name) }).closest("article") as HTMLElement;

beforeEach(() => {
  vi.mocked(commissionRebateApi.partners).mockResolvedValue({ items: [], hasMore: false });
  vi.mocked(api).mockResolvedValue({ ok: true, json: async () => [{ id: 1, name: "Meezan Bank", accountHolderName: "Deen", isActive: true }] } as Response);
});
afterEach(() => { cleanup(); vi.clearAllMocks(); });

describe("Commission & rebate tab", () => {
  it("shows each commission and the rebate as a block with its figures, buttons and payments", async () => {
    show();
    await screen.findByText("Ali Estate Agency");

    const alis = block("Ali Estate Agency");
    expect(within(alis).getByText("Partly paid")).toBeTruthy();
    expect(within(alis).getByText("Agency · 2% of net sale price")).toBeTruthy();
    expect(within(alis).getByText("Rs 256,000")).toBeTruthy();
    expect(within(alis).getByText("Rs 156,000")).toBeTruthy();
    expect(within(alis).getByText("Aug 10, 2026 · Bank transfer · TRX-66120")).toBeTruthy();
    expect(within(alis).getByText("Meezan Bank — 0123 · by accountant")).toBeTruthy();
    expect(within(alis).getByRole("button", { name: "transfer-slip.pdf" })).toBeTruthy();
    expect(within(alis).getByRole("button", { name: "Pay" })).toBeTruthy();
    expect(within(alis).getByRole("button", { name: "Edit" })).toBeTruthy();

    // Paid in full: nothing left to pay, but it can still be edited; a payment without proof offers Attach proof.
    const khans = block("Khan Associates");
    // The status badge and the "Paid" figure both read Paid.
    expect(within(khans).getAllByText("Paid")).toHaveLength(2);
    expect(within(khans).queryByRole("button", { name: "Pay" })).toBeNull();
    expect(within(khans).getByRole("button", { name: "Edit" })).toBeTruthy();
    expect(within(khans).getByRole("button", { name: "Attach proof" })).toBeTruthy();

    const rebates = block("Rebate · Fixed amount");
    expect(within(rebates).getByText("Partly given")).toBeTruthy();
    expect(within(rebates).getByText("Paid by cash or bank · Loyal customer")).toBeTruthy();
    expect(within(rebates).getByRole("button", { name: "Apply" })).toBeTruthy();

    // The old tab's extras are gone.
    expect(screen.queryByText(/audit/i)).toBeNull();
    expect(screen.queryByText("Amount collected")).toBeNull();
  });

  it("offers Add rebate only while the booking has none", async () => {
    show();
    await screen.findByText("Ali Estate Agency");
    expect(screen.getByRole("button", { name: "Add commission" })).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Add rebate" })).toBeNull();
    cleanup();

    show(workspace({ commissions: [], rebates: [] }));
    expect(await screen.findByText("No commission on this booking.")).toBeTruthy();
    expect(screen.getByText("No rebate on this booking.")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Add rebate" })).toBeTruthy();
  });

  it("reversing a payment asks for a reason in a confirm box, then reverses the whole payment", async () => {
    vi.mocked(commissionRebateApi.reversePayout).mockResolvedValue(workspace({ commissions: [{ ...ali, payouts: [{ ...aliPayout, reversedAmount: 100_000 }], paidAmount: 0, outstandingAmount: 256_000 } as never] }));
    show();
    await screen.findByText("Ali Estate Agency");
    fireEvent.click(within(block("Ali Estate Agency")).getByRole("button", { name: "Reverse" }));

    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText("Reverse this payment?")).toBeTruthy();
    expect(within(dialog).getByText("Rs 100,000 paid to Ali Estate Agency on Aug 10, 2026 will be reversed. Rs 256,000 will then be remaining.")).toBeTruthy();

    fireEvent.click(within(dialog).getByRole("button", { name: "Reverse payment" }));
    expect(within(dialog).getByText("Enter the reason.")).toBeTruthy();
    expect(commissionRebateApi.reversePayout).not.toHaveBeenCalled();

    fireEvent.change(within(dialog).getByLabelText(/Reason/), { target: { value: "Paid to the wrong account" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Reverse payment" }));
    await waitFor(() => expect(commissionRebateApi.reversePayout).toHaveBeenCalledWith(13, 1, 61, { amount: 100_000, reason: "Paid to the wrong account", idempotencyKey: expect.any(String) }));
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(await within(block("Ali Estate Agency")).findByText("Reversed")).toBeTruthy();
  });

  it("keeps the confirm box open with the server's message when the reversal is refused", async () => {
    vi.mocked(commissionRebateApi.reversePayout).mockRejectedValue(new Error("This payment has already been reversed."));
    show();
    await screen.findByText("Ali Estate Agency");
    fireEvent.click(within(block("Ali Estate Agency")).getByRole("button", { name: "Reverse" }));
    const dialog = await screen.findByRole("dialog");
    fireEvent.change(within(dialog).getByLabelText(/Reason/), { target: { value: "Mistake" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Reverse payment" }));

    expect(await within(dialog).findByText("This payment has already been reversed.")).toBeTruthy();
    expect((within(dialog).getByLabelText(/Reason/) as HTMLTextAreaElement).value).toBe("Mistake");
  });

  it("edits a commission that already has a payment, without offering to cancel it", async () => {
    show();
    await screen.findByText("Ali Estate Agency");
    fireEvent.click(within(block("Ali Estate Agency")).getByRole("button", { name: "Edit" }));

    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText("Ali Estate Agency · Rs 100,000 already paid")).toBeTruthy();
    expect(within(dialog).queryByText("+ New partner")).toBeNull();
    expect(within(dialog).queryByRole("button", { name: "Cancel this commission" })).toBeNull();

    // A total below what is paid is refused before anything is sent.
    fireEvent.change(within(dialog).getByLabelText(/Percentage/), { target: { value: "0.5" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Save changes" }));
    expect(within(dialog).getByText("Can't be less than Rs 100,000 already paid.")).toBeTruthy();
    expect(commissionRebateApi.updateCommission).not.toHaveBeenCalled();
  });

  it("offers Cancel this commission only when nothing is paid, and asks for a reason", async () => {
    vi.mocked(commissionRebateApi.commissionStatus).mockResolvedValue(workspace({ commissions: [] }));
    show(workspace({ commissions: [{ ...ali, paidAmount: 0, outstandingAmount: 256_000, payouts: [] } as never], rebates: [] }));
    await screen.findByText("Ali Estate Agency");
    fireEvent.click(within(block("Ali Estate Agency")).getByRole("button", { name: "Edit" }));
    fireEvent.click(await screen.findByRole("button", { name: "Cancel this commission" }));

    const confirm = (await screen.findAllByRole("dialog")).at(-1)!;
    fireEvent.change(within(confirm).getByLabelText(/Reason/), { target: { value: "Partner withdrew" } });
    fireEvent.click(within(confirm).getByRole("button", { name: "Cancel commission" }));
    await waitFor(() => expect(commissionRebateApi.commissionStatus).toHaveBeenCalledWith(13, 1, { targetStatus: "Cancelled", reason: "Partner withdrew", concurrencyToken: "c1" }));
  });

  it("tells the booking page when a rebate payment is reversed, since it moves what the customer owes", async () => {
    vi.mocked(commissionRebateApi.reverseDisbursement).mockResolvedValue(workspace());
    const onChanged = show();
    await screen.findByText("Ali Estate Agency");
    fireEvent.click(within(block("Rebate · Fixed amount")).getByRole("button", { name: "Reverse" }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText(/given to the customer on Jul 28, 2026 will be reversed\. Rs 200,000 will then be remaining\./)).toBeTruthy();
    fireEvent.change(within(dialog).getByLabelText(/Reason/), { target: { value: "Wrong customer" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Reverse payment" }));
    await waitFor(() => expect(onChanged).toHaveBeenCalledTimes(1));
  });

  it("keeps everything readable but offers no action on a cancelled booking", async () => {
    show(workspace({ bookingStatus: "Cancelled" }));
    await screen.findByText("Ali Estate Agency");
    for (const name of ["Add commission", "Add rebate", "Pay", "Edit", "Apply", "Reverse", "Attach proof"]) {
      expect(screen.queryByRole("button", { name })).toBeNull();
    }
    // A stored proof still opens.
    expect(screen.getByRole("button", { name: "transfer-slip.pdf" })).toBeTruthy();
  });
});
