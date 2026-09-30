import type { ProofFile } from "../proof/proofApi";
import type { CancellationSettlement } from "../bookingCancellation/types.ts";

/** What `GET /api/Booking/{id}` sends, as far as the booking page reads it. */
export interface BookingDetail {
  id: number;
  bookingReference: string;
  customerName: string;
  customerPhone: string;
  customerCnic?: string | null;
  projectName: string;
  unitNumber: string;
  unitType: string;
  floorName: string;
  unitSize: number;
  status: string;
  listPrice: number;
  agreedSalePrice: number;
  discountAmount: number;
  discountPercent: number;
  discountReason?: string | null;
  bookingDate: string;
  bookingAmountRequired: number;
  bookingAmountReceived: number;
  bookingAmountRemaining: number;
  bookingAmountDueDate?: string | null;
  // Net non-cash rebate credits on this booking. Served by the booking endpoint itself, so it is
  // known whenever the page has a booking at all, and it is the same figure the payment and
  // schedule services measure their own limits against.
  rebateCredits: number;
  totalInstallmentAmount: number;
  installmentPlanStartDate?: string | null;
  // The figures below are worked out by the server so no screen has to.
  collected: number;
  outstanding: number;
  installmentsPaid: number;
  installmentsTotal: number;
  hasInstallmentSchedule: boolean;
  nextInstallment?: NextInstallment | null;
  convertedFromLead?: { leadId: number; leadReference: string } | null;
  customerId: number;
  payments: BookingPayment[];
  concurrencyToken: string;
  cancellationSettlement?: CancellationSettlement | null;
}

export interface NextInstallment {
  id: number;
  number: number;
  isPossession: boolean;
  /** What is still to collect on it. */
  amount: number;
  dueDate: string;
  isOverdue: boolean;
}

export interface ScheduleItem {
  id: number;
  sequenceNumber: number;
  type: string;
  dueDate: string;
  amount: number;
  status: string;
  amountPaid: number;
  remainingBalance: number;
  isOverdue: boolean;
  paidAt?: string | null;
  notes?: string | null;
}

export interface BookingPayment {
  id: number;
  bookingId: number;
  installmentId?: number | null;
  /** Which installment it went to — 0 is the possession row; null for the booking amount. */
  installmentSequence?: number | null;
  installmentType?: string | null;
  financeAccountId?: number | null;
  /** The account the money was received in. */
  accountName?: string | null;
  recordedByName?: string | null;
  type: string;
  amount: number;
  paymentMethod: string;
  paymentReference?: string | null;
  receiptNumber?: string | null;
  notes?: string | null;
  paidAt: string;
  proof?: ProofFile | null;
}

export interface InstallmentSchedule {
  bookingId: number;
  bookingReference: string;
  bookingStatus: string;
  agreedSalePrice: number;
  discountAmount: number;
  discountPercent: number;
  bookingAmountReceived: number;
  possessionAmount: number;
  installmentPool: number;
  frequency?: string | null;
  numberOfInstallments?: number | null;
  /** The due date of installment 1. */
  installmentStartDate?: string | null;
  possessionDueDate?: string | null;
  generatedAt?: string | null;
  hasSchedule: boolean;
  canGenerate: boolean;
  canRegenerate: boolean;
  scheduleTotal: number;
  schedulePaid: number;
  scheduleRemaining: number;
  // What the customer owes that the unpaid installments do not hold — normally zero. The payment
  // service refuses a receipt while it is above zero, so the plan has to be changed first.
  unscheduledBalance?: number;
  items: ScheduleItem[];
}

export interface FinanceAccountOption {
  id: number;
  name: string;
  accountHolderName: string;
  isActive: boolean;
}
