import type { StatusTone } from "../../components/ui";

/**
 * The words staff see for lead enum values. Screens pass the value the API sent; nothing else
 * turns a lead enum into text.
 */

export type LeadStatus = "InProgress" | "Won" | "Lost" | "Dormant";

export type ToneLabel = { label: string; tone: StatusTone };

/** "SiteVisit" → "Site visit", for a value this file has no word for yet. */
const words = (value: string) => {
  const text = value.replace(/([a-z])([A-Z])/g, "$1 $2").toLowerCase();
  return text.charAt(0).toUpperCase() + text.slice(1);
};

/** The four statuses a salesperson works with; a New lead is already in progress. */
export function statusOfGroup(group: string): LeadStatus {
  return group === "Won" || group === "Lost" || group === "Dormant" ? group : "InProgress";
}

export const leadStatus = (lead: { stageGroup: string }): LeadStatus => statusOfGroup(lead.stageGroup);

export const statusText = (status: LeadStatus) => (status === "InProgress" ? "In progress" : status);

const PAYMENT_PREFERENCES: Record<string, ToneLabel> = {
  Installments: { label: "Installments", tone: "green" },
  NeedsDetails: { label: "Needs details", tone: "orange" },
  Cash: { label: "Cash", tone: "grey" },
};

/** Null for Unknown: nothing is shown until the customer has said. */
export const paymentPreferenceLabel = (value?: string | null): ToneLabel | null =>
  (value && PAYMENT_PREFERENCES[value]) || null;

const PURCHASE_INTENTS: Record<string, string> = {
  SelfUse: "Personal living",
  Investment: "Investment",
  Rental: "Rental",
  Resale: "Resale",
};

/** Null for Unknown. Always shown in grey. */
export const purchaseIntentLabel = (value?: string | null): string | null =>
  (value && PURCHASE_INTENTS[value]) || null;

const CHANNELS: Record<string, string> = {
  Phone: "Phone call",
  Whatsapp: "WhatsApp",
  Email: "Email",
  OfficeVisit: "Office visit",
  Meeting: "Meeting",
  SiteVisit: "Site visit",
  Sms: "SMS",
  Other: "Other",
};

export const channelLabel = (value: string) => CHANNELS[value] ?? words(value);

export const directionLabel = (value: string) =>
  value === "Inbound" ? "They contacted us" : value === "Outbound" ? "We contacted them" : words(value);

/** "We called" · "They messaged us": who reached whom, and how. */
export function contactVerb(channel: string, direction: string): string {
  const inbound = direction === "Inbound";
  switch (channel) {
    case "Phone": return inbound ? "They called" : "We called";
    case "Whatsapp":
    case "Sms": return inbound ? "They messaged us" : "We messaged";
    case "Email": return inbound ? "They emailed us" : "We emailed";
    case "OfficeVisit": return inbound ? "They came to the office" : "We met at the office";
    case "Meeting":
    case "SiteVisit": return "We met";
    default: return directionLabel(direction);
  }
}

/**
 * What an attempt to reach the customer asks: whether they answered a call we made, or replied to
 * a WhatsApp or email we sent. Null when the contact could not have gone unanswered.
 */
export function reachQuestion(channel: string, direction: string): "answer" | "reply" | null {
  if (direction !== "Outbound") return null;
  if (channel === "Phone") return "answer";
  return channel === "Whatsapp" || channel === "Email" ? "reply" : null;
}

/** "No answer" / "No reply" when an attempt did not reach them; "Answered" for a call that did. */
export function reachBadge(contact: { channel: string; direction: string; connected: boolean }): ToneLabel | null {
  const question = reachQuestion(contact.channel, contact.direction);
  if (!contact.connected) return { label: question === "reply" ? "No reply" : "No answer", tone: "red" };
  return question === "answer" ? { label: "Answered", tone: "green" } : null;
}

/** "Call — sent price list"; "Call — no answer" when it did not reach them. */
export function lastContactText(contact: { channel: string; direction: string; connected: boolean; summary: string }): string {
  const how = contact.channel === "Phone" ? "Call" : channelLabel(contact.channel);
  const what = contact.connected ? contact.summary : reachQuestion(contact.channel, contact.direction) === "reply" ? "no reply" : "no answer";
  return `${how} — ${what}`;
}

const FOLLOW_UP_TYPES: Record<string, string> = {
  Call: "Call",
  FollowUp: "Follow-up",
  Whatsapp: "WhatsApp",
  Email: "Email",
  Meeting: "Meeting",
  SiteVisit: "Site visit",
  DocumentCollection: "Documents",
  ManagerReview: "Manager review",
  Other: "Other",
};

export const followUpTypeLabel = (value: string) => FOLLOW_UP_TYPES[value] ?? words(value);

/** Only High and Urgent earn a badge; Low and Medium are the normal case. */
export const priorityTone = (value: string): StatusTone | null =>
  value === "Urgent" ? "red" : value === "High" ? "orange" : null;

const VISIT_OUTCOMES: Record<string, ToneLabel> = {
  VeryInterested: { label: "Very interested", tone: "green" },
  Interested: { label: "Interested", tone: "green" },
  Undecided: { label: "Undecided", tone: "grey" },
  NotInterested: { label: "Not interested", tone: "red" },
  WantsAnotherOption: { label: "Wants another option", tone: "orange" },
  ReadyToBook: { label: "Ready to book", tone: "green" },
};

export const visitOutcomeLabel = (value: string): ToneLabel =>
  VISIT_OUTCOMES[value] ?? { label: words(value), tone: "grey" };

/** A salesperson only needs to know whether a unit can still be offered. */
export function unitStatus(value: string): string {
  switch (value) {
    case "Available":
      return "Available";
    case "PendingReview":
    case "Booked":
    case "Reserved":
    case "OnPaymentPlan":
      return "Booked";
    case "Sold":
      return "Sold";
    default:
      return words(value);
  }
}

const MATCHED_ON: Record<string, string> = {
  phone: "Same phone",
  email: "Same email",
  whatsapp: "Same WhatsApp",
};

export const matchedOnLabel = (matchedOn?: string | null) =>
  (matchedOn && MATCHED_ON[matchedOn]) || "Same details";
