import { parseServerDateTime } from "../staff/staffAccessState.ts";
import type { LeadAction } from "./LeadActionDialog.tsx";
import type { Lead } from "./types.ts";

// Formats an instant as the browser-local wall-clock value a datetime-local input expects.
// Seconds are dropped, so the value never lands later than the instant it came from.
// Server strings carry no zone marker and are read as UTC, as the API stores them.
export const toLocalInput = (date: string | Date) => {
  const value = typeof date === "string" ? parseServerDateTime(date) : date;
  if (!value) return "";
  const local = new Date(value.getTime() - value.getTimezoneOffset() * 60000);
  return local.toISOString().slice(0, 16);
};

// Scheduled work (follow-ups, site visits) must be in the future; logged activity must not be.
export const oneHourFromNow = () => new Date(Date.now() + 60 * 60 * 1000);

export function initialForm(action: LeadAction, lead: Lead): Record<string, string | boolean> {
  switch (action.type) {
    case "edit": return {
      firstName: lead.firstName, lastName: lead.lastName ?? "", phone: lead.phone ?? "", whatsappNumber: lead.whatsappNumber ?? "", email: lead.email ?? "", address: lead.address ?? "", city: lead.city ?? "", preferredContactMethod: lead.preferredContactMethod, preferredContactTime: lead.preferredContactTime ?? "", sourceDetails: lead.sourceDetails ?? "", campaignName: lead.campaignName ?? "", campaignReference: lead.campaignReference ?? "", adReference: lead.adReference ?? "", interestedProjectId: lead.interestedProjectId?.toString() ?? "", interestedUnitId: lead.interestedUnitId?.toString() ?? "", propertyType: lead.propertyType ?? "", preferredLocation: lead.preferredLocation ?? "", budgetMin: lead.budgetMin?.toString() ?? "", budgetMax: lead.budgetMax?.toString() ?? "", purchaseIntent: lead.purchaseIntent, notes: lead.notes ?? "",
    };
    case "assign": return { employeeId: lead.assignedEmployeeId?.toString() ?? "", teamId: lead.assignedTeamId?.toString() ?? "", reason: "" };
    case "stage": return { stage: "", notes: "" };
    case "qualification": return { qualification: lead.qualification, notes: "" };
    case "communication": return { channel: "Phone", direction: "Outbound", occurredAt: toLocalInput(new Date()), connected: true, summary: "", customerResponse: "", nextAction: "", nextActionAt: "" };
    case "followUp": return { followUpType: "FollowUp", priority: "Medium", assignedEmployeeId: lead.assignedEmployeeId?.toString() ?? "", dueAt: toLocalInput(oneHourFromNow()), title: "", notes: "" };
    case "completeFollowUp": return { outcome: "", nextFollowUpAt: "", nextFollowUpTitle: "" };
    case "rescheduleFollowUp": return { dueAt: toLocalInput(action.item.dueAt), reason: "" };
    case "cancelFollowUp": return { reason: "" };
    case "siteVisit": return { projectId: lead.interestedProjectId?.toString() ?? "", unitId: lead.interestedUnitId?.toString() ?? "", assignedEmployeeId: lead.assignedEmployeeId?.toString() ?? "", scheduledAt: toLocalInput(oneHourFromNow()), meetingLocation: "", customerAttendees: lead.fullName, internalAttendees: "", notes: "" };
    case "completeVisit": return { outcome: "Interested", outcomeNotes: "", customerFeedback: "", nextAction: "" };
    case "rescheduleVisit": return { scheduledAt: toLocalInput(action.item.scheduledAt), meetingLocation: action.item.meetingLocation, reason: "" };
    case "closeVisit": return { reason: "" };
    case "comment": return { body: "", managerReview: false, decisionRecord: false, mentionedUserIds: "" };
    case "document": return { category: "Quotation", description: "" };
    case "close": return { closureType: "lost", closureReasonId: "", notes: "", reactivateOn: "" };
    case "reopen": return { stage: "Contacted", reason: "" };
    case "convert": return { projectId: lead.interestedProjectId?.toString() ?? "", unitId: lead.interestedUnitId?.toString() ?? "", customerSearch: "", customerId: "", cnic: "", fatherName: "", agreedSalePrice: "", discountPercent: "", discountReason: "", bookingAmountRequired: "", bookingAmountDueDate: "", notes: "", confirmed: false };
  }
}
