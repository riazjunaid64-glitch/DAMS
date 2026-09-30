// @vitest-environment happy-dom
import { cleanup, render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ToastProvider } from "../../components/ui/Toast.tsx";
import { BookingSummary } from "./BookingSummary";
import type { BookingDetail } from "./detailTypes";

vi.mock("../proof/proofApi", () => ({ uploadProof: vi.fn(), openProof: vi.fn() }));

const cancelled = (settlement: Record<string, unknown> | null) => ({
  id: 13, bookingReference: "BK-000013", customerName: "Usman Tariq", customerPhone: "03334412987", customerId: 5,
  projectName: "Floria Heights", unitNumber: "B08", unitType: "2 Bed", floorName: "8th floor", unitSize: 1180,
  status: "Cancelled", agreedSalePrice: 12_800_000, discountAmount: 0, discountPercent: 0, bookingAmountRequired: 500_000,
  outstanding: 8_628_000, payments: [], cancellationSettlement: settlement,
}) as unknown as BookingDetail;

const settlement = {
  cancelledAt: "2026-09-29T07:00:00", reason: "Customer moved abroad", customerCashReceivedSnapshot: 4_172_000,
  refundAmount: 1_500_000, retainedAmount: 2_672_000, refundStatus: "Pending",
};

function show(booking: BookingDetail) {
  render(
    <MemoryRouter>
      <ToastProvider>
        <BookingSummary booking={booking} leadLink={false} onSetTerms={vi.fn()} onEditTerms={vi.fn()} onProofChanged={vi.fn()} />
      </ToastProvider>
    </MemoryRouter>,
  );
}

afterEach(cleanup);

describe("Summary of a cancelled booking", () => {
  it("shows the cancellation, with a Refund to pay badge, and a Price card without Outstanding", () => {
    show(cancelled(settlement));
    expect(screen.getByText("Cancellation")).toBeTruthy();
    expect(screen.getByText("Sep 29, 2026")).toBeTruthy();
    expect(screen.getByText("Customer moved abroad")).toBeTruthy();
    expect(screen.getByText("Rs 4,172,000")).toBeTruthy();
    expect(screen.getByText("Rs 1,500,000")).toBeTruthy();
    expect(screen.getByText("Rs 2,672,000")).toBeTruthy();
    expect(screen.getByText("Refund to pay")).toBeTruthy();
    expect(screen.getByText("Price")).toBeTruthy();
    expect(screen.getByText("Net sale price")).toBeTruthy();
    expect(screen.queryByText("Outstanding")).toBeNull();
    expect(screen.queryByText("Refund paid on")).toBeNull();
  });

  it("once paid: Refund paid, with the date, method, reference and the proof link", () => {
    show(cancelled({
      ...settlement, refundStatus: "Paid",
      refund: { id: 31, paidAt: "2026-09-29T00:00:00", paymentMethod: "BankTransfer", paymentReference: "TRX-90021", proof: { id: 4, fileName: "refund-slip.pdf", fileSize: 9 } },
    }));
    expect(screen.getByText("Refund paid")).toBeTruthy();
    expect(screen.getByText("Bank transfer")).toBeTruthy();
    expect(screen.getByText("TRX-90021")).toBeTruthy();
    expect(screen.getByRole("button", { name: "refund-slip.pdf" })).toBeTruthy();
  });

  it("no refund: a grey No refund badge and no payment details", () => {
    show(cancelled({ ...settlement, refundAmount: 0, retainedAmount: 4_172_000, refundStatus: "NotRequired" }));
    expect(screen.getByText("No refund")).toBeTruthy();
    expect(screen.queryByText("Refund paid on")).toBeNull();
  });

  it("a booking cancelled before details were kept says so instead of showing empty figures", () => {
    show(cancelled(null));
    expect(screen.getByText(/cancelled before cancellation details were recorded/)).toBeTruthy();
  });
});
