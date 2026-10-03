// @vitest-environment happy-dom
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ToastProvider } from "../../components/ui/Toast.tsx";
import { pakistanToday } from "../../lib/financePeriods.ts";
import { uploadProof } from "../proof/proofApi";
import { PayRefundDialog } from "./PayRefundDialog";
import { bookingCancellationApi } from "./api";

vi.mock("../proof/proofApi", () => ({ uploadProof: vi.fn(), openProof: vi.fn() }));
vi.mock("./api", () => ({ bookingCancellationApi: { payRefund: vi.fn() } }));

const slip = new File(["x"], "refund-slip.pdf", { type: "application/pdf" });
const accounts = [{ id: 1, name: "Meezan Bank", accountHolderName: "Deen", isActive: true }];

function show() {
  const onClose = vi.fn();
  const onPaid = vi.fn();
  render(
    <ToastProvider>
      <PayRefundDialog
        booking={{ id: 13, bookingReference: "BK-000013", customerName: "Usman Tariq" }}
        refundAmount={1_500_000}
        financeAccounts={accounts}
        accountsError={null}
        onClose={onClose}
        onPaid={onPaid}
      />
    </ToastProvider>,
  );
  return { onClose, onPaid };
}

const payButton = () => screen.getByRole("button", { name: "Pay refund" });

beforeEach(() => {
  vi.mocked(uploadProof).mockReset();
  vi.mocked(bookingCancellationApi.payRefund).mockReset();
  vi.mocked(bookingCancellationApi.payRefund).mockResolvedValue({ cancellationSettlement: { refund: { id: 31 } } } as never);
});
afterEach(cleanup);

describe("Pay refund", () => {
  it("shows the whole refund, locked, and pays exactly that with today's date", async () => {
    const { onClose, onPaid } = show();
    expect((screen.getByLabelText("Refund amount") as HTMLInputElement).value).toBe("1,500,000");
    expect((screen.getByLabelText("Refund amount") as HTMLInputElement).disabled).toBe(true);

    fireEvent.click(payButton());
    await waitFor(() => expect(onPaid).toHaveBeenCalledTimes(1));
    expect(bookingCancellationApi.payRefund).toHaveBeenCalledWith(13, {
      financeAccountId: 1, paymentMethod: "Cash", paymentReference: null, paidAt: pakistanToday(), notes: null, idempotencyKey: expect.any(String),
    });
    expect(onClose).toHaveBeenCalled();
  });

  it("puts the proof onto the refund the server created", async () => {
    const { onPaid } = show();
    fireEvent.change(document.querySelector("input[type=file]") as HTMLInputElement, { target: { files: [slip] } });
    vi.mocked(uploadProof).mockResolvedValue();
    fireEvent.click(payButton());
    await waitFor(() => expect(uploadProof).toHaveBeenCalledWith("CancellationRefund", 31, slip, expect.any(Function), expect.any(AbortSignal)));
    await waitFor(() => expect(onPaid).toHaveBeenCalledTimes(1));
  });

  it("closes with a warning when the proof fails, and the refund is paid only once", async () => {
    const { onClose, onPaid } = show();
    fireEvent.change(document.querySelector("input[type=file]") as HTMLInputElement, { target: { files: [slip] } });
    vi.mocked(uploadProof).mockRejectedValue(new Error("Network down"));
    fireEvent.click(payButton());

    expect(await screen.findByText("Refund paid.")).toBeTruthy();
    expect(await screen.findByText("The refund was saved, but its proof did not upload. Attach it from the refund.")).toBeTruthy();
    await waitFor(() => expect(onPaid).toHaveBeenCalledTimes(1));
    expect(onClose).toHaveBeenCalled();
    expect(bookingCancellationApi.payRefund).toHaveBeenCalledTimes(1);
  });

  it("keeps the popup open with what was typed and shows the server's message when the payment is refused", async () => {
    vi.mocked(bookingCancellationApi.payRefund).mockRejectedValue(new Error("Refund date cannot be before the go-live date (Sep 1, 2026)."));
    const { onClose, onPaid } = show();
    fireEvent.change(screen.getByLabelText("Notes"), { target: { value: "Paid at the branch" } });
    fireEvent.click(payButton());

    expect(await screen.findByText(/go-live date/)).toBeTruthy();
    expect((screen.getByLabelText("Notes") as HTMLTextAreaElement).value).toBe("Paid at the branch");
    expect(onClose).not.toHaveBeenCalled();
    expect(onPaid).not.toHaveBeenCalled();
  });

  it("asks for the account before sending anything", () => {
    const onClose = vi.fn();
    render(
      <ToastProvider>
        <PayRefundDialog booking={{ id: 13, bookingReference: "BK-000013", customerName: "Usman Tariq" }} refundAmount={1_500_000}
          financeAccounts={[...accounts, { id: 2, name: "HBL", accountHolderName: "Deen", isActive: true }]} accountsError={null} onClose={onClose} onPaid={vi.fn()} />
      </ToastProvider>,
    );
    fireEvent.click(payButton());
    expect(screen.getByText("Choose the account the refund is paid from.")).toBeTruthy();
    expect(bookingCancellationApi.payRefund).not.toHaveBeenCalled();
  });

  it("drops the missing-reference message once the method is changed to Cash", () => {
    show();
    fireEvent.click(screen.getByRole("combobox", { name: /Payment method/ }));
    fireEvent.click(screen.getByRole("option", { name: "Bank transfer" }));
    fireEvent.click(payButton());
    expect(screen.getByText("Enter the cheque or transfer number.")).toBeTruthy();

    fireEvent.click(screen.getByRole("combobox", { name: /Payment method/ }));
    fireEvent.click(screen.getByRole("option", { name: "Cash" }));
    expect(screen.queryByText("Enter the cheque or transfer number.")).toBeNull();
  });
});
