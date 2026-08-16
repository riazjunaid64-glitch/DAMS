export type CancellationRefundDecision = "None" | "PayNow" | "PayLater";
export type CancellationRefundStatus = "NotRequired" | "Pending" | "Paid";
export type RefundPaymentMethod = "Cash" | "BankTransfer" | "Cheque" | "Online";

export interface CancellationRefund {
  id: number;
  amount: number;
  financeAccountId: number;
  financeAccountName: string;
  paymentMethod: RefundPaymentMethod;
  paymentReference?: string | null;
  paidAt: string;
  notes?: string | null;
  recordedByUserId: number;
  recordedByName: string;
  recordedAt: string;
}

export interface CancellationSettlement {
  id: number;
  customerCashReceivedSnapshot: number;
  refundAmount: number;
  retainedAmount: number;
  refundDecision: CancellationRefundDecision;
  refundStatus: CancellationRefundStatus;
  reason: string;
  notes?: string | null;
  cancelledAt: string;
  cancelledByUserId: number;
  cancelledByName: string;
  refundPayableAccountId?: number | null;
  refundPayableAccountName?: string | null;
  refund?: CancellationRefund | null;
}

// The subset of the booking response this feature reads/writes. The page's own BookingDetail
// interface carries the rest; the two are kept structurally compatible on purpose.
export interface CancellableBooking {
  id: number;
  status: string;
  unitNumber: string;
  concurrencyToken: string;
  payments: { amount: number }[];
  cancellationSettlement?: CancellationSettlement | null;
}

export interface CancelBookingRequest {
  reason: string;
  expectedCustomerCashReceived: number;
  refundAmount: number;
  refundDecision: CancellationRefundDecision;
  idempotencyKey: string;
  concurrencyToken?: string | null;
  refundFinanceAccountId?: number | null;
  refundPaymentMethod?: RefundPaymentMethod | null;
  refundPaymentReference?: string | null;
  refundPaidAt?: string | null;
  refundNotes?: string | null;
}

export interface PayCancellationRefundRequest {
  financeAccountId: number;
  paymentMethod: RefundPaymentMethod;
  paymentReference?: string | null;
  paidAt?: string | null;
  notes?: string | null;
  idempotencyKey: string;
}
