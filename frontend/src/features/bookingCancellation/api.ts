import { api } from "../../api/api";
import type { CancelBookingRequest, CancellableBooking, PayCancellationRefundRequest } from "./types";

const root = "/api/Booking";

async function apiError(response: Response, fallback: string) {
  const payload = (await response.json().catch(() => null)) as { message?: string } | null;
  return new Error(payload?.message ?? fallback);
}

async function json<T>(path: string, options?: RequestInit): Promise<T> {
  const response = await api(`${root}${path}`, options);
  if (!response.ok) throw await apiError(response, "The cancellation request could not be completed.");
  return (await response.json()) as T;
}

export const bookingCancellationApi = {
  // A fresh read right before the dialog opens, so the concurrency token and the payment total
  // reflect the current state, not whatever was loaded when the page first rendered.
  current: (bookingId: number) => json<CancellableBooking>(`/${bookingId}`),
  cancel: (bookingId: number, body: CancelBookingRequest) =>
    json<CancellableBooking>(`/${bookingId}/cancel`, { method: "POST", body: JSON.stringify(body) }),
  payRefund: (bookingId: number, body: PayCancellationRefundRequest) =>
    json<CancellableBooking>(`/${bookingId}/cancellation-settlement/refund`, { method: "POST", body: JSON.stringify(body) }),
};
