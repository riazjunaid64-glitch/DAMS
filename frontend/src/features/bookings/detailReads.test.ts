import { describe, expect, it } from "vitest";
import {
  bookingReadFailed,
  missingReadMessages,
  paymentsReadFailed,
  readBookingDetail,
  scheduleReadFailed,
} from "./detailReads";

/**
 * Booking Detail is a screen people collect money from, so the question these tests exist to pin is
 * not "did the reads work" but "when one of them does NOT work, does the page still claim the rest
 * is current?".
 *
 * The defect: the three reads were started together and processed in one block that threw the
 * moment the booking response was not OK. A refresh that fetched a fresh Rs 12,000 payment history
 * and a fresh schedule, but failed on the booking, discarded both — left their error flags null,
 * left Rs 10,000 on screen as Amount Collected, and left Record Payment enabled against installment
 * ids read before the failure.
 */

const ok = (body: unknown) => new Response(JSON.stringify(body), { status: 200 });
const failed = () => new Response("", { status: 500 });
const notJson = () => new Response("<html>Gateway timeout</html>", { status: 200 });

/** Answers each of the three paths from a map, recording what was actually requested. */
const responder = (answers: Record<string, () => Promise<Response> | Response>) => {
  const calls: string[] = [];
  const request = (path: string) => {
    calls.push(path);
    const answer = answers[path];
    if (!answer) throw new Error(`unexpected request: ${path}`);
    return Promise.resolve(answer()).then((r) => r);
  };
  return { calls, request };
};

const paths = {
  booking: "/api/Booking/7",
  schedule: "/api/Booking/7/installments",
  payments: "/api/Booking/7/payments",
};

const schedule = { hasSchedule: true, items: [{ id: 4, remainingBalance: 5_000 }] };
const payments = [{ id: 1, amount: 12_000 }];
const booking = { id: 7, agreedSalePrice: 1_000_000, rebateCredits: 0 };

describe("Booking Detail reads settle independently", () => {
  it("reads exactly the three endpoints this screen owns", async () => {
    const { calls, request } = responder({
      [paths.booking]: () => ok(booking),
      [paths.schedule]: () => ok(schedule),
      [paths.payments]: () => ok(payments),
    });

    await readBookingDetail(7, request);

    expect(calls.sort()).toEqual([paths.booking, paths.schedule, paths.payments].sort());
    expect(calls).toHaveLength(3);
  });

  it("keeps the fresh payment history and schedule when the booking read fails", async () => {
    const { request } = responder({
      [paths.booking]: () => failed(),
      [paths.schedule]: () => ok(schedule),
      [paths.payments]: () => ok(payments),
    });

    const reads = await readBookingDetail(7, request);

    // The whole point: these arrived, so they are applied. Dropping them is what put a stale
    // Rs 10,000 total on screen while the server had already answered with Rs 12,000.
    expect(reads.payments.value).toEqual(payments);
    expect(reads.payments.error).toBeNull();
    expect(reads.schedule.value).toEqual(schedule);
    expect(reads.schedule.error).toBeNull();
    // And the booking is explicitly marked not current, rather than left silently trusted.
    expect(reads.booking.value).toBeNull();
    expect(reads.booking.error).toBe(bookingReadFailed);
  });

  it("treats a rejected booking request the same as a failed one", async () => {
    const { request } = responder({
      [paths.booking]: () => Promise.reject(new Error("network down")),
      [paths.schedule]: () => ok(schedule),
      [paths.payments]: () => ok(payments),
    });

    const reads = await readBookingDetail(7, request);

    expect(reads.booking.error).toBe(bookingReadFailed);
    expect(reads.payments.value).toEqual(payments);
    expect(reads.schedule.value).toEqual(schedule);
  });

  it("treats a booking body that is not JSON as no booking at all", async () => {
    const { request } = responder({
      [paths.booking]: () => notJson(),
      [paths.schedule]: () => ok(schedule),
      [paths.payments]: () => ok(payments),
    });

    const reads = await readBookingDetail(7, request);

    // A 200 carrying a proxy's HTML error page must not be reported as a successful read.
    expect(reads.booking.value).toBeNull();
    expect(reads.booking.error).toBe(bookingReadFailed);
    expect(reads.payments.value).toEqual(payments);
  });

  it("marks only the schedule when only the schedule fails", async () => {
    const { request } = responder({
      [paths.booking]: () => ok(booking),
      [paths.schedule]: () => failed(),
      [paths.payments]: () => ok(payments),
    });

    const reads = await readBookingDetail(7, request);

    expect(reads.schedule.error).toBe(scheduleReadFailed);
    expect(reads.booking.value).toEqual(booking);
    expect(reads.booking.error).toBeNull();
    expect(reads.payments.value).toEqual(payments);
  });

  it("marks only the payments when only the payments fail", async () => {
    const { request } = responder({
      [paths.booking]: () => ok(booking),
      [paths.schedule]: () => ok(schedule),
      [paths.payments]: () => Promise.reject(new Error("network down")),
    });

    const reads = await readBookingDetail(7, request);

    expect(reads.payments.error).toBe(paymentsReadFailed);
    expect(reads.booking.value).toEqual(booking);
    expect(reads.schedule.value).toEqual(schedule);
  });

  it("reports every read as fresh when all three arrive", async () => {
    const { request } = responder({
      [paths.booking]: () => ok(booking),
      [paths.schedule]: () => ok(schedule),
      [paths.payments]: () => ok(payments),
    });

    const reads = await readBookingDetail(7, request);

    expect([reads.booking.error, reads.schedule.error, reads.payments.error]).toEqual([null, null, null]);
    expect(reads.booking.value).toEqual(booking);
  });
});

describe("what the page says is missing", () => {
  const clean = {
    bookingError: null,
    creditsMissing: false,
    accountsError: null,
    paymentsError: null,
    scheduleError: null,
  };

  it("says nothing when every read arrived", () => {
    expect(missingReadMessages(clean)).toEqual([]);
  });

  it("reports the credits once, as part of the booking failure that carried them", () => {
    const messages = missingReadMessages({ ...clean, bookingError: bookingReadFailed, creditsMissing: true });

    // One fault, stated once — not "the booking failed" followed by "and its credits are missing".
    expect(messages).toEqual([bookingReadFailed]);
  });

  it("reports missing credits on their own when the booking itself arrived", () => {
    const messages = missingReadMessages({ ...clean, creditsMissing: true });

    expect(messages).toHaveLength(1);
    expect(messages[0]).toContain("rebate credits");
  });

  it("lists every failed read together, because they share one Retry", () => {
    const messages = missingReadMessages({
      bookingError: bookingReadFailed,
      creditsMissing: true,
      accountsError: "The list of finance accounts could not be loaded, so payments cannot be recorded.",
      paymentsError: paymentsReadFailed,
      scheduleError: scheduleReadFailed,
    });

    expect(messages).toHaveLength(4);
    expect(messages).toContain(scheduleReadFailed);
    expect(messages).toContain(paymentsReadFailed);
  });
});
