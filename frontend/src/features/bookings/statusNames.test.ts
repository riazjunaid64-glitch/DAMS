import { describe, expect, it } from "vitest";
import { statusLabel, statusTone } from "../../components/ui/statusTone.ts";
import { BOOKING_STATUS_OPTIONS, commissionStatus, installmentStatus, rebateStatus, refundStatus, termsNotSet } from "./statusNames.ts";

const badge = (status: string) => [statusLabel(status), statusTone(status)];

describe("booking status names", () => {
  it("lists the five statuses and Cancelled for the filter", () => {
    expect(BOOKING_STATUS_OPTIONS.map((o) => o.label)).toEqual(["Awaiting booking amount", "Payment plan", "Possession given", "Sale completed", "Cancelled"]);
  });
  it("treats terms not set as an awaiting booking with a booking amount of 0", () => {
    expect(termsNotSet({ status: "AwaitingBookingAmount", bookingAmountRequired: 0 })).toBe(true);
    expect(termsNotSet({ status: "AwaitingBookingAmount", bookingAmountRequired: 500000 })).toBe(false);
    expect(termsNotSet({ status: "PaymentPlanActive", bookingAmountRequired: 0 })).toBe(false);
  });
});

describe("installment names", () => {
  it("matches the table", () => {
    expect(badge(installmentStatus("Paid"))).toEqual(["Paid", "green"]);
    expect(badge(installmentStatus("Pending"))).toEqual(["Pending", "orange"]);
    expect(badge(installmentStatus("PartiallyPaid"))).toEqual(["Partly paid", "orange"]);
    expect(badge(installmentStatus("Overdue"))).toEqual(["Overdue", "red"]);
  });
});

describe("commission names", () => {
  it("matches the table", () => {
    expect(badge(commissionStatus("Pending", 0))).toEqual(["Pending", "orange"]);
    expect(badge(commissionStatus("Pending", 5000))).toEqual(["Partly paid", "orange"]);
    expect(badge(commissionStatus("Paid", 5000))).toEqual(["Paid", "green"]);
    expect(badge(commissionStatus("Cancelled", 0))).toEqual(["Cancelled", "red"]);
    expect(badge(commissionStatus("ReversalRequired", 5000))).toEqual(["Reversal required", "red"]);
  });
});

describe("rebate names", () => {
  it("matches the table", () => {
    expect(badge(rebateStatus("Pending", 0))).toEqual(["Pending", "orange"]);
    expect(badge(rebateStatus("Pending", 100))).toEqual(["Partly given", "orange"]);
    expect(badge(rebateStatus("Paid", 100))).toEqual(["Given", "green"]);
    expect(badge(rebateStatus("Applied", 100))).toEqual(["Applied", "green"]);
    expect(badge(rebateStatus("Cancelled", 0))).toEqual(["Cancelled", "red"]);
  });
});

describe("refund names", () => {
  it("matches the table", () => {
    expect(badge(refundStatus("Pending"))).toEqual(["Refund to pay", "orange"]);
    expect(badge(refundStatus("Paid"))).toEqual(["Refund paid", "green"]);
    expect(badge(refundStatus("NotRequired"))).toEqual(["No refund", "grey"]);
  });
});
