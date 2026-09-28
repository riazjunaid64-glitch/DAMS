import { describe, expect, it } from "vitest";
import {
  channelLabel,
  contactVerb,
  directionLabel,
  followUpTypeLabel,
  lastContactText,
  leadStatus,
  matchedOnLabel,
  paymentPreferenceLabel,
  priorityTone,
  purchaseIntentLabel,
  reachBadge,
  reachQuestion,
  statusOfGroup,
  statusText,
  unitStatus,
  visitOutcomeLabel,
} from "./labels.ts";

describe("lead status", () => {
  it("folds New into In progress and keeps the closed groups", () => {
    expect(statusOfGroup("New")).toBe("InProgress");
    expect(statusOfGroup("InProgress")).toBe("InProgress");
    expect(statusOfGroup("Won")).toBe("Won");
    expect(statusOfGroup("Lost")).toBe("Lost");
    expect(statusOfGroup("Dormant")).toBe("Dormant");
    expect(leadStatus({ stageGroup: "New" })).toBe("InProgress");
  });

  it("words a status for plain text", () => {
    expect(statusText("InProgress")).toBe("In progress");
    expect(statusText("Dormant")).toBe("Dormant");
  });
});

describe("lead details", () => {
  it("shows a payment preference only once the customer has given one", () => {
    expect(paymentPreferenceLabel("Installments")).toEqual({ label: "Installments", tone: "green" });
    expect(paymentPreferenceLabel("NeedsDetails")).toEqual({ label: "Needs details", tone: "orange" });
    expect(paymentPreferenceLabel("Cash")).toEqual({ label: "Cash", tone: "grey" });
    expect(paymentPreferenceLabel("Unknown")).toBeNull();
    expect(paymentPreferenceLabel(null)).toBeNull();
  });

  it("names the purchase intent in the salesperson's words", () => {
    expect(purchaseIntentLabel("SelfUse")).toBe("Personal living");
    expect(purchaseIntentLabel("Resale")).toBe("Resale");
    expect(purchaseIntentLabel("Unknown")).toBeNull();
  });

  it("names who reached out, and how", () => {
    expect(channelLabel("Phone")).toBe("Phone call");
    expect(channelLabel("Whatsapp")).toBe("WhatsApp");
    expect(channelLabel("Sms")).toBe("SMS");
    expect(channelLabel("CarrierPigeon")).toBe("Carrier pigeon");
    expect(directionLabel("Outbound")).toBe("We contacted them");
    expect(directionLabel("Inbound")).toBe("They contacted us");
  });

  it("labels follow-ups and flags only the pressing ones", () => {
    expect(followUpTypeLabel("FollowUp")).toBe("Follow-up");
    expect(followUpTypeLabel("DocumentCollection")).toBe("Documents");
    expect(followUpTypeLabel("ManagerReview")).toBe("Manager review");
    expect(priorityTone("Low")).toBeNull();
    expect(priorityTone("Medium")).toBeNull();
    expect(priorityTone("High")).toBe("orange");
    expect(priorityTone("Urgent")).toBe("red");
  });

  it("colours a site visit outcome", () => {
    expect(visitOutcomeLabel("ReadyToBook")).toEqual({ label: "Ready to book", tone: "green" });
    expect(visitOutcomeLabel("NotInterested")).toEqual({ label: "Not interested", tone: "red" });
    expect(visitOutcomeLabel("WantsAnotherOption")).toEqual({ label: "Wants another option", tone: "orange" });
    expect(visitOutcomeLabel("Undecided")).toEqual({ label: "Undecided", tone: "grey" });
  });

  it("reduces unit statuses to what can still be offered", () => {
    expect(unitStatus("Available")).toBe("Available");
    for (const status of ["PendingReview", "Booked", "Reserved", "OnPaymentPlan"]) {
      expect(unitStatus(status)).toBe("Booked");
    }
    expect(unitStatus("Sold")).toBe("Sold");
  });

  it("says what a held enquiry matched on", () => {
    expect(matchedOnLabel("phone")).toBe("Same phone");
    expect(matchedOnLabel("email")).toBe("Same email");
    expect(matchedOnLabel("whatsapp")).toBe("Same WhatsApp");
    expect(matchedOnLabel("name")).toBe("Same details");
    expect(matchedOnLabel(undefined)).toBe("Same details");
  });
});

describe("communication wording", () => {
  it("says who reached whom", () => {
    expect(contactVerb("Phone", "Outbound")).toBe("We called");
    expect(contactVerb("Phone", "Inbound")).toBe("They called");
    expect(contactVerb("Whatsapp", "Outbound")).toBe("We messaged");
    expect(contactVerb("Email", "Inbound")).toBe("They emailed us");
    expect(contactVerb("Other", "Inbound")).toBe("They contacted us");
  });

  it("asks whether they answered only for calls and messages we sent", () => {
    expect(reachQuestion("Phone", "Outbound")).toBe("answer");
    expect(reachQuestion("Whatsapp", "Outbound")).toBe("reply");
    expect(reachQuestion("Email", "Outbound")).toBe("reply");
    expect(reachQuestion("Phone", "Inbound")).toBeNull();
    expect(reachQuestion("OfficeVisit", "Outbound")).toBeNull();
    expect(reachQuestion("Other", "Outbound")).toBeNull();
  });

  it("badges a missed attempt red and an answered call green", () => {
    expect(reachBadge({ channel: "Phone", direction: "Outbound", connected: false })).toEqual({ label: "No answer", tone: "red" });
    expect(reachBadge({ channel: "Whatsapp", direction: "Outbound", connected: false })).toEqual({ label: "No reply", tone: "red" });
    expect(reachBadge({ channel: "Phone", direction: "Outbound", connected: true })).toEqual({ label: "Answered", tone: "green" });
    expect(reachBadge({ channel: "Whatsapp", direction: "Outbound", connected: true })).toBeNull();
    expect(reachBadge({ channel: "Phone", direction: "Inbound", connected: true })).toBeNull();
  });

  it("sums up the last contact", () => {
    expect(lastContactText({ channel: "Phone", direction: "Outbound", connected: true, summary: "sent price list" })).toBe("Call — sent price list");
    expect(lastContactText({ channel: "Phone", direction: "Outbound", connected: false, summary: "Tried" })).toBe("Call — no answer");
    expect(lastContactText({ channel: "Email", direction: "Outbound", connected: false, summary: "Sent" })).toBe("Email — no reply");
    expect(lastContactText({ channel: "Whatsapp", direction: "Inbound", connected: true, summary: "Asked for plans" })).toBe("WhatsApp — Asked for plans");
  });
});
