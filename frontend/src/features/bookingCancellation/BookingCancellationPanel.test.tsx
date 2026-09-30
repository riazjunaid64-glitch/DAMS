// @vitest-environment happy-dom
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
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
  render(<BookingCancellationPanel bookingId={13} status="Cancelled" settlement={settlement} financeAccounts={[{ id: 1, name: "Cash", accountHolderName: "Head office" }]} onChanged={onChanged} />);
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

  it("stays open for a retry when the upload fails, and never pays the refund twice", async () => {
    vi.mocked(uploadProof).mockRejectedValueOnce(new Error("Network down")).mockResolvedValueOnce();
    const onChanged = show();
    const dialog = await payWithProof();

    fireEvent.click(await within(dialog).findByRole("button", { name: "Retry upload" }));
    await waitFor(() => expect(uploadProof).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(onChanged).toHaveBeenCalledTimes(1));
    expect(bookingCancellationApi.payRefund).toHaveBeenCalledTimes(1);
  });

  it("refreshes the page when the popup is closed after the refund was recorded without its proof", async () => {
    vi.mocked(uploadProof).mockRejectedValue(new Error("Network down"));
    const onChanged = show();
    const dialog = await payWithProof();
    await within(dialog).findByRole("button", { name: "Retry upload" });

    fireEvent.click(within(dialog).getByRole("button", { name: "Cancel" }));
    await waitFor(() => expect(onChanged).toHaveBeenCalledTimes(1));
  });
});
