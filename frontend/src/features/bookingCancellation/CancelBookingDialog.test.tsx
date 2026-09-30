// @vitest-environment happy-dom
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ToastProvider } from "../../components/ui/Toast.tsx";
import { pakistanToday } from "../../lib/financePeriods.ts";
import { uploadProof } from "../proof/proofApi";
import { CancelBookingDialog } from "./CancelBookingDialog";
import { bookingCancellationApi } from "./api";

vi.mock("../proof/proofApi", () => ({ uploadProof: vi.fn(), openProof: vi.fn() }));
vi.mock("./api", () => ({ bookingCancellationApi: { current: vi.fn(), cancel: vi.fn() } }));

const accounts = [{ id: 1, name: "Meezan Bank", accountHolderName: "Deen", isActive: true }];

async function show(paid = 4_172_000) {
  vi.mocked(bookingCancellationApi.current).mockResolvedValue({ concurrencyToken: "tok-1", payments: paid > 0 ? [{ amount: paid }] : [] } as never);
  const onClose = vi.fn();
  const onCancelled = vi.fn();
  render(
    <ToastProvider>
      <CancelBookingDialog
        booking={{ id: 13, bookingReference: "BK-000013", unitNumber: "B08" }}
        financeAccounts={accounts}
        accountsError={null}
        onClose={onClose}
        onCancelled={onCancelled}
      />
    </ToastProvider>,
  );
  await screen.findByText("Customer has paid");
  return { onClose, onCancelled };
}

const reason = () => screen.getByLabelText(/Reason/) as HTMLTextAreaElement;
const choose = (name: string) => fireEvent.click(screen.getByRole("radio", { name }));
const submit = () => fireEvent.click(screen.getByRole("button", { name: "Cancel booking" }));
const sent = () => vi.mocked(bookingCancellationApi.cancel).mock.calls[0]![1];

beforeEach(() => {
  vi.mocked(uploadProof).mockReset();
  vi.mocked(bookingCancellationApi.current).mockReset();
  vi.mocked(bookingCancellationApi.cancel).mockReset();
  vi.mocked(bookingCancellationApi.cancel).mockResolvedValue({ cancellationSettlement: { refund: { id: 31 } } } as never);
});
afterEach(cleanup);

describe("Cancel booking", () => {
  it("shows what the customer has paid and the warning about the unit", async () => {
    await show();
    expect(screen.getByText("Rs 4,172,000")).toBeTruthy();
    expect(screen.getByText("Unit B08 becomes Available again. This cannot be undone.")).toBeTruthy();
  });

  it("No refund: the company keeps everything paid, and nothing about a payout is sent", async () => {
    const { onCancelled } = await show();
    fireEvent.change(reason(), { target: { value: "Customer moved abroad" } });
    choose("No refund");
    expect(screen.getByText("Company keeps")).toBeTruthy();
    expect(screen.getAllByText("Rs 4,172,000").length).toBeGreaterThan(1);

    submit();
    await waitFor(() => expect(onCancelled).toHaveBeenCalledTimes(1));
    expect(sent()).toMatchObject({
      reason: "Customer moved abroad", expectedCustomerCashReceived: 4_172_000, refundAmount: 0, refundDecision: "None",
      concurrencyToken: "tok-1", refundFinanceAccountId: null, refundPaidAt: null,
    });
  });

  it("Pay later: Company keeps follows the amount as it is typed, with no payout fields", async () => {
    await show();
    fireEvent.change(reason(), { target: { value: "Customer moved abroad" } });
    choose("Pay later");
    expect(screen.queryByLabelText(/Reference/)).toBeNull();
    fireEvent.change(screen.getByLabelText(/Refund amount/), { target: { value: "1500000" } });
    expect(screen.getByText("Up to Rs 4,172,000")).toBeTruthy();
    expect(screen.getByText("Rs 2,672,000")).toBeTruthy();

    submit();
    await waitFor(() => expect(bookingCancellationApi.cancel).toHaveBeenCalledTimes(1));
    expect(sent()).toMatchObject({ refundAmount: 1_500_000, refundDecision: "PayLater", refundFinanceAccountId: null, refundPaidAt: null });
  });

  it("Pay now: the refund date is today, and the proof goes onto the refund the server created", async () => {
    const { onCancelled } = await show();
    fireEvent.change(reason(), { target: { value: "Customer moved abroad" } });
    choose("Pay now");
    fireEvent.change(screen.getByLabelText(/Refund amount/), { target: { value: "1500000" } });
    const slip = new File(["x"], "slip.pdf", { type: "application/pdf" });
    fireEvent.change(document.querySelector("input[type=file]") as HTMLInputElement, { target: { files: [slip] } });
    vi.mocked(uploadProof).mockResolvedValue();

    submit();
    await waitFor(() => expect(onCancelled).toHaveBeenCalledTimes(1));
    expect(sent()).toMatchObject({
      refundDecision: "PayNow", refundAmount: 1_500_000, refundFinanceAccountId: 1, refundPaymentMethod: "Cash", refundPaidAt: pakistanToday(),
    });
    expect(uploadProof).toHaveBeenCalledWith("CancellationRefund", 31, slip, expect.any(Function), expect.any(AbortSignal));
  });

  it("skips the refund question when the customer has paid nothing", async () => {
    const { onCancelled } = await show(0);
    expect(screen.queryByRole("radio")).toBeNull();
    fireEvent.change(reason(), { target: { value: "Changed their mind" } });
    submit();
    await waitFor(() => expect(onCancelled).toHaveBeenCalledTimes(1));
    expect(sent()).toMatchObject({ refundAmount: 0, refundDecision: "None", expectedCustomerCashReceived: 0 });
  });

  it("does not send until the reason and the refund choice are given", async () => {
    await show();
    submit();
    expect(screen.getByText("Enter the reason for cancelling.")).toBeTruthy();
    expect(screen.getByText("Choose what happens to the money paid.")).toBeTruthy();
    expect(bookingCancellationApi.cancel).not.toHaveBeenCalled();
  });

  it("keeps the popup open with what was typed when the server refuses", async () => {
    vi.mocked(bookingCancellationApi.cancel).mockRejectedValue(new Error("Refund amount cannot exceed the customer's paid amount."));
    const { onClose, onCancelled } = await show();
    fireEvent.change(reason(), { target: { value: "Customer moved abroad" } });
    choose("No refund");
    submit();

    expect(await screen.findByText("Refund amount cannot exceed the customer's paid amount.")).toBeTruthy();
    expect(reason().value).toBe("Customer moved abroad");
    expect(onClose).not.toHaveBeenCalled();
    expect(onCancelled).not.toHaveBeenCalled();
  });

  it("offers to reload the figures when they went stale, and takes the fresh total", async () => {
    vi.mocked(bookingCancellationApi.cancel).mockRejectedValue(new Error("Customer payments changed while you were cancelling this booking. Reload and review the settlement again."));
    await show();
    fireEvent.change(reason(), { target: { value: "Customer moved abroad" } });
    choose("No refund");
    submit();
    await screen.findByText(/payments changed/);

    vi.mocked(bookingCancellationApi.current).mockResolvedValue({ concurrencyToken: "tok-2", payments: [{ amount: 5_000_000 }] } as never);
    fireEvent.click(screen.getByRole("button", { name: "Reload figures" }));
    expect((await screen.findAllByText("Rs 5,000,000")).length).toBeGreaterThan(0);
    expect(screen.queryByText(/payments changed/)).toBeNull();
  });
});
