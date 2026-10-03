import { describe, expect, it } from "vitest";
import { statusLabel, statusTone } from "./statusTone.ts";

describe("statusTone", () => {
  it("maps the design's statuses to their colours", () => {
    expect(statusTone("In progress")).toBe("blue");
    expect(statusTone("Ongoing")).toBe("blue");
    expect(statusTone("Won")).toBe("green");
    expect(statusTone("Available")).toBe("green");
    expect(statusTone("Completed")).toBe("green");
    expect(statusTone("Lost")).toBe("red");
    expect(statusTone("Dormant")).toBe("orange");
    expect(statusTone("Booked")).toBe("orange");
    expect(statusTone("Planning")).toBe("orange");
    expect(statusTone("Needs details")).toBe("orange");
    expect(statusTone("Sold")).toBe("grey");
  });
  it("ignores case, spaces and separators", () => {
    expect(statusTone("InProgress")).toBe("blue");
    expect(statusTone("in_progress")).toBe("blue");
    expect(statusTone("NEEDS-DETAILS")).toBe("orange");
  });
  it("falls back to grey for anything unknown", () => {
    expect(statusTone("Something else")).toBe("grey");
  });
});

describe("filer statuses", () => {
  it("colours the filer status tax is withheld under", () => {
    expect(statusTone("Filer")).toBe("green");
    expect(statusTone("NonFiler")).toBe("red");
    expect(statusTone("Unknown")).toBe("orange");
  });
  it("writes NonFiler as 'Non-filer'", () => {
    expect(statusLabel("NonFiler")).toBe("Non-filer");
    expect(statusLabel("Filer")).toBe("Filer");
    expect(statusLabel("Unknown")).toBe("Unknown");
  });
});

describe("booking statuses", () => {
  it("colours every booking, installment, commission, rebate and refund status", () => {
    const expected: Record<string, string> = {
      AwaitingBookingAmount: "orange", PaymentPlanActive: "blue", PossessionGiven: "grey", SaleCompleted: "green", Cancelled: "red",
      Paid: "green", Pending: "orange", "Partly paid": "orange", Overdue: "red",
      "Reversal required": "red", "Partly given": "orange", Given: "green", Applied: "green",
      "Refund to pay": "orange", "Refund paid": "green", "No refund": "grey",
    };
    for (const [status, tone] of Object.entries(expected)) expect([status, statusTone(status)]).toEqual([status, tone]);
  });
  it("writes a payment plan as 'Payment plan'", () => {
    expect(statusLabel("PaymentPlanActive")).toBe("Payment plan");
    expect(statusLabel("AwaitingBookingAmount")).toBe("Awaiting booking amount");
    expect(statusLabel("PossessionGiven")).toBe("Possession given");
    expect(statusLabel("SaleCompleted")).toBe("Sale completed");
  });
});

describe("customer document statuses", () => {
  it("colours Blocked, Needed, Uploaded and Not needed", () => {
    expect(statusTone("Blocked")).toBe("red");
    expect(statusTone("Needed")).toBe("orange");
    expect(statusTone("Uploaded")).toBe("green");
    expect(statusTone("Not needed")).toBe("grey");
  });
});

describe("statusLabel", () => {
  it("turns codes into sentence case", () => {
    expect(statusLabel("InProgress")).toBe("In progress");
    expect(statusLabel("needs_details")).toBe("Needs details");
    expect(statusLabel("Won")).toBe("Won");
  });
});
