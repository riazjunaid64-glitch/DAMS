import { formatWhen, isPastServerTime } from "../../lib/dates.ts";
import { leadStatus, paymentPreferenceLabel } from "./labels.ts";
import type { LeadListItem } from "./types.ts";

/** "2 Bed · Needs details": the apartment type and how they want to pay, whichever is known. */
export function requirementText(lead: LeadListItem): string {
  return [lead.propertyType, paymentPreferenceLabel(lead.paymentPreference)?.label].filter(Boolean).join(" · ");
}

/**
 * When the next follow-up is due, in words. Overdue only while the lead is being worked: a Won,
 * Lost or Dormant lead has nothing left to chase.
 */
export function nextFollowUp(lead: LeadListItem): { text: string; overdue: boolean } | null {
  if (!lead.nextActionAt) return null;
  const overdue = leadStatus(lead) === "InProgress" && isPastServerTime(lead.nextActionAt);
  return { text: `${overdue ? "Overdue · " : ""}${formatWhen(lead.nextActionAt)}`, overdue };
}
