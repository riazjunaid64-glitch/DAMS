import { api } from "../../api/api.ts";
import { moneyRequest } from "../../lib/idempotency.ts";
import type { BookingDetail, BookingPayment } from "./detailTypes.ts";

/** Sends one request and returns its JSON, or throws with the server's own message when it is refused. */
async function send<T>(path: string, init: RequestInit, fallback: string): Promise<T> {
  const response = await api(path, init);
  const data = await response.json().catch(() => null) as (T & { message?: string }) | null;
  if (!response.ok) throw new Error(data?.message ?? fallback);
  return data as T;
}

export interface TermsBody {
  agreedSalePrice: number;
  discountPercent: number;
  /** Not sent = keep what is there; "" = clear it. */
  discountReason: string;
  bookingAmountRequired: number;
  bookingAmountDueDate: string | null;
}

export interface PaymentBody {
  amount: number;
  paymentMethod: string;
  financeAccountId: number;
  paymentReference: string | null;
  notes: string | null;
  paidAt: string;
}

/**
 * The booking's status-changing actions. Each one that moves money or state carries an
 * Idempotency-Key, created by the caller for one intent and kept across retries, so a request that
 * committed and lost its answer is recognised instead of running twice.
 */
export const bookingApi = {
  saveTerms: (bookingId: number, body: TermsBody, key: string) =>
    send<BookingDetail>(`/api/Booking/${bookingId}/financials`, moneyRequest(key, { method: "PUT", body: JSON.stringify(body) }), "The terms could not be saved."),

  recordBookingAmount: (bookingId: number, body: PaymentBody, key: string) =>
    send<BookingDetail>(`/api/Booking/${bookingId}/booking-amount-payment`, moneyRequest(key, { method: "POST", body: JSON.stringify(body) }), "The payment could not be recorded."),

  recordInstallment: (bookingId: number, installmentId: number, body: PaymentBody, key: string) =>
    send<unknown>(`/api/Booking/${bookingId}/installments/${installmentId}/payment`, moneyRequest(key, { method: "POST", body: JSON.stringify(body) }), "The payment could not be recorded."),

  givePossession: (bookingId: number, possessionDate: string, key: string) =>
    send<BookingDetail>(`/api/Booking/${bookingId}/possession`, moneyRequest(key, { method: "POST", body: JSON.stringify({ possessionDate }) }), "Possession could not be given."),

  completeSale: (bookingId: number, key: string) =>
    send<BookingDetail>(`/api/Booking/${bookingId}/complete`, moneyRequest(key, { method: "POST", body: JSON.stringify({}) }), "The sale could not be completed."),

  payments: (bookingId: number) =>
    send<BookingPayment[]>(`/api/Booking/${bookingId}/payments`, {}, "The payments could not be loaded."),
};
