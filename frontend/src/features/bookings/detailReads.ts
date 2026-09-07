/**
 * The three reads the Booking Detail screen is built from, settled independently of one another.
 *
 * They used to be started together and then processed in one `try` that threw the moment the
 * booking response was not OK. The schedule and payment responses had already arrived by then and
 * were simply dropped — their freshness flags left saying nothing was wrong — so the screen went on
 * presenting the PREVIOUS generation's collected total, outstanding balance and installment rows as
 * current, and offered Record Payment against them. One failed request is not a reason to discard
 * the answers that did arrive, and it is not a reason to keep calling the old ones current.
 *
 * Kept out of the component so each failure shape — HTTP failure, a rejected request, a body that
 * is not JSON — can be tested on the thing that decides, rather than through a rendered page.
 */

/** One read's outcome. `value` is present or the read is not usable; `error` says which. */
export interface ReadResult<T> {
  value: T | null;
  error: string | null;
}

// Worded to fit both the first visit and a failed refresh: it is the same sentence in the missing-
// reads banner and on the page that has no booking to show at all.
export const bookingReadFailed = "The booking's own figures could not be loaded.";
export const scheduleReadFailed = "The installment schedule could not be loaded.";
export const paymentsReadFailed = "The payment history could not be loaded.";

/**
 * Turns one settled response into a result. A rejected request arrives here as null, and a body
 * that will not parse is treated the same as no body: the previous value stays on screen as
 * history, but nothing is told that it is current.
 */
export async function settleRead<T>(response: Response | null, failure: string): Promise<ReadResult<T>> {
  if (!response?.ok) return { value: null, error: failure };
  try {
    return { value: await response.json() as T, error: null };
  } catch {
    return { value: null, error: failure };
  }
}

/**
 * Issues exactly the three requests this screen owns and settles each on its own. No read can abort
 * another, and none of them throws: the caller applies three outcomes.
 */
export async function readBookingDetail<TBooking, TSchedule, TPayments>(
  bookingId: number | string,
  request: (path: string) => Promise<Response>,
): Promise<{
  booking: ReadResult<TBooking>;
  schedule: ReadResult<TSchedule>;
  payments: ReadResult<TPayments>;
}> {
  const [bookRes, schedRes, payRes] = await Promise.all([
    request(`/api/Booking/${bookingId}`).catch(() => null),
    request(`/api/Booking/${bookingId}/installments`).catch(() => null),
    request(`/api/Booking/${bookingId}/payments`).catch(() => null),
  ]);
  return {
    booking: await settleRead<TBooking>(bookRes, bookingReadFailed),
    schedule: await settleRead<TSchedule>(schedRes, scheduleReadFailed),
    payments: await settleRead<TPayments>(payRes, paymentsReadFailed),
  };
}

/**
 * What the page tells the operator is missing. The rebate credits ride on the booking response, so
 * when that read failed they are already accounted for — listing them again reads as two separate
 * faults when there is one.
 */
export function missingReadMessages(state: {
  bookingError: string | null;
  creditsMissing: boolean;
  accountsError: string | null;
  paymentsError: string | null;
  scheduleError: string | null;
}): string[] {
  return [
    state.bookingError,
    state.bookingError === null && state.creditsMissing
      ? "The booking's rebate credits are missing from this response."
      : null,
    state.accountsError,
    state.paymentsError,
    state.scheduleError,
  ].filter((m): m is string => m !== null);
}
