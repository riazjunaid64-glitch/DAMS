// @vitest-environment happy-dom
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ToastProvider } from "../../components/ui/Toast.tsx";
import { uploadProof } from "../proof/proofApi";
import BookingCancellationPanel from "./BookingCancellationPanel";
import { bookingCancellationApi } from "./api";
import type { CancellationSettlement } from "./types";

vi.mock("../proof/proofApi", () => ({ uploadProof: vi.fn(), openProof: vi.fn() }));
vi.mock("./api", () => ({ bookingCancellationApi: { payRefund: vi.fn() } }));

const settlement = {
  id: 1, customerCashReceivedSnapshot: 500_000, refundAmount: 500_000, retainedAmount: 0, refundDecision: "PayLater", refundStatus: "Pending",
  reason: "Customer request", cancelledAt: "2026-09-29T07:00:00", cancelledByUserId: 1, cancelledByName: "Admin",
} as CancellationSettlement;
const slip = new File(["x"], "refund-slip.pdf", { type: "application/pdf" });

function show(onChanged = vi.fn()) {
  render(<ToastProvider><BookingCancellationPanel bookingId={13} status="Cancelled" settlement={settlement} financeAccounts={[{ id: 1, name: "Cash", accountHolderName: "Head office" }]} onChanged={onChanged} /></ToastProvider>);
  return onChanged;
}

async function payWithProof() {
  fireEvent.click(screen.getByRole("button", { name: "Pay Refund" }));
  const dialog = await screen.findByRole("dialog");
  fireEvent.change(dialog.querySelector("input[type=file]") as HTMLInputElement, { target: { files: [slip] } });
  fireEvent.click(within(dialog).getByRole("button", { name: "Confirm Payment" }));
  return dialog;
}

beforeEach(() => {
  vi.mocked(uploadProof).mockReset();
  vi.mocked(bookingCancellationApi.payRefund).mockReset();
  vi.mocked(bookingCancellationApi.payRefund).mockResolvedValue({ cancellationSettlement: { refund: { id: 31 } } } as never);
});
afterEach(cleanup);

describe("proof on a pending refund", () => {
  it("goes onto the refund the server created", async () => {
    vi.mocked(uploadProof).mockResolvedValue();
    const onChanged = show();
    await payWithProof();
    await waitFor(() => expect(uploadProof).toHaveBeenCalledWith("CancellationRefund", 31, slip, expect.any(Function), expect.any(AbortSignal)));
    await waitFor(() => expect(onChanged).toHaveBeenCalledTimes(1));
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("closes with a warning when the upload fails, refreshing the page and paying only once", async () => {
    vi.mocked(uploadProof).mockRejectedValue(new Error("Network down"));
    const onChanged = show();
    await payWithProof();

    expect(await screen.findByText("Refund recorded, but the proof did not upload. Attach it from the refund.")).toBeTruthy();
    await waitFor(() => expect(onChanged).toHaveBeenCalledTimes(1));
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(bookingCancellationApi.payRefund).toHaveBeenCalledTimes(1);
  });
});

describe("proof on a paid refund", () => {
  it("shows the stored file name, or Attach proof when the upload never went through", () => {
    const paid = (proof: unknown) => ({ ...settlement, refundStatus: "Paid", refund: { id: 31, financeAccountName: "Cash", paymentMethod: "Cash", paidAt: "2026-09-29T07:00:00", proof } }) as never;
    const view = render(<ToastProvider><BookingCancellationPanel bookingId={13} status="Cancelled" settlement={paid({ id: 4, fileName: "refund-slip.pdf", fileSize: 9 })} financeAccounts={[]} onChanged={vi.fn()} /></ToastProvider>);
    expect(screen.getByRole("button", { name: "refund-slip.pdf" })).toBeTruthy();
    view.rerender(<ToastProvider><BookingCancellationPanel bookingId={13} status="Cancelled" settlement={paid(null)} financeAccounts={[]} onChanged={vi.fn()} /></ToastProvider>);
    expect(screen.getByRole("button", { name: "Attach proof" })).toBeTruthy();
  });
});
