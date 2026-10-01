import { describe, expect, it } from "vitest";
import { blockedLine, customerBadge, headerActions } from "./customerHeader.ts";

describe("customerBadge", () => {
  it("says how many documents are needed, in orange", () => {
    expect(customerBadge({ status: "Active", documentsNeeded: 2 })).toEqual({ text: "2 documents needed", tone: "orange" });
    expect(customerBadge({ status: "Active", documentsNeeded: 1 }).text).toBe("1 document needed");
  });

  it("says Documents complete, in green, when nothing is needed", () => {
    expect(customerBadge({ status: "Active", documentsNeeded: 0 })).toEqual({ text: "Documents complete", tone: "green" });
  });

  it("Blocked, in red, replaces the document badges", () => {
    expect(customerBadge({ status: "Blocked", documentsNeeded: 2 })).toEqual({ text: "Blocked", tone: "red" });
  });
});

describe("headerActions", () => {
  it("an active customer can be edited, blocked and given a new booking", () => {
    expect(headerActions({ status: "Active" })).toEqual({ edit: true, block: true, unblock: false, newBooking: true, phoneMenu: true });
  });

  it("a blocked customer can only be edited and unblocked, with no ⋯ menu on a phone", () => {
    expect(headerActions({ status: "Blocked" })).toEqual({ edit: true, block: false, unblock: true, newBooking: false, phoneMenu: false });
  });
});

describe("blockedLine", () => {
  it("names the reason, the person and the day", () => {
    expect(blockedLine({ blockedReason: "Cheque bounced twice", blockedByName: "admin", blockedAt: "2026-09-30T10:00:00" }))
      .toBe("Cheque bounced twice · Blocked by admin on Sep 30, 2026");
  });

  it("copes with a block recorded without a name", () => {
    expect(blockedLine({ blockedReason: "Fraud", blockedByName: null, blockedAt: "2026-09-30T10:00:00" })).toBe("Fraud · Blocked on Sep 30, 2026");
  });
});
