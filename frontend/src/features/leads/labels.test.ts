import { describe, expect, it } from "vitest";
import {
  channelLabel,
  directionLabel,
  followUpTypeLabel,
  leadStatus,
  matchedOnLabel,
  paymentPreferenceLabel,
  priorityTone,
  purchaseIntentLabel,
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
