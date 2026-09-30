// @vitest-environment happy-dom
import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ToastProvider } from "../../components/ui/Toast.tsx";
import type { BookingDetail, BookingPayment } from "./detailTypes";
import { PaymentsTab } from "./PaymentsTab";

vi.mock("../proof/proofApi", () => ({ uploadProof: vi.fn(), openProof: vi.fn() }));

const payment = (id: number, proof: unknown) => ({
  id, receiptNumber: `RCP-00023${id}`, amount: 500_000, paymentMethod: "Cash", paidAt: "2026-09-01T00:00:00", type: "BookingAmount", proof,
}) as unknown as BookingPayment;

function show(status: string) {
  render(
    <ToastProvider>
      <PaymentsTab
        booking={{ status, collected: 1_000_000, rebateCredits: 0, outstanding: 0, payments: [] } as unknown as BookingDetail}
        payments={[payment(1, { id: 4, fileName: "slip.pdf", fileSize: 9 }), payment(2, null)]}
        paymentsError={null}
        onOpenReceipt={vi.fn()}
        onChanged={vi.fn()}
        onRetry={vi.fn()}
      />
    </ToastProvider>,
  );
}

afterEach(cleanup);

describe("Payments tab", () => {
  it("lets proof be attached on a live booking", () => {
    show("PaymentPlanActive");
    expect(screen.getByRole("button", { name: "Attach proof" })).toBeTruthy();
  });

  it("on a cancelled booking keeps stored proofs viewable but offers no way to attach one", () => {
    show("Cancelled");
    expect(screen.getByRole("button", { name: "slip.pdf" })).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Attach proof" })).toBeNull();
  });
});
