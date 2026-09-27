import { matchedOnLabel, statusOfGroup, type LeadStatus } from "./labels.ts";

/** A lead a held enquiry matched, as `GET /api/leads/held-enquiries` returns it. */
export type HeldEnquiryCandidate = {
  leadId: number;
  leadReference: string;
  leadName: string;
  leadStage: string;
  /** The simple status group (New, InProgress, Won, Lost, Dormant). */
  leadStageGroup: string;
  leadOwnerName?: string | null;
  matchedOn: string;
  isOpen: boolean;
};

/** An external enquiry whose details match more than one open lead, waiting for an admin. */
export type HeldEnquiry = {
  id: number;
  receivedAt: string;
  provider?: string | null;
  sourceName?: string | null;
  firstName: string;
  lastName?: string | null;
  phone?: string | null;
  whatsappNumber?: string | null;
  email?: string | null;
  campaignName?: string | null;
  notes?: string | null;
  bookingRequestId?: number | null;
  candidates: HeldEnquiryCandidate[];
};

/** `GET /api/leads/held-enquiries`: the oldest waiting enquiries, and how many are waiting in all. */
export type HeldEnquiryList = { totalWaiting: number; items: HeldEnquiry[] };

/** The banner's title: how many enquiries are waiting for a person to choose their lead. */
export function heldEnquiriesTitle(totalWaiting: number): string {
  return totalWaiting === 1
    ? "1 enquiry needs your decision"
    : `${totalWaiting.toLocaleString("en-PK")} enquiries need your decision`;
}

/** A lead the enquiry matched, as the review lists it. */
export type HeldEnquiryMatch = {
  leadId: number;
  leadName: string;
  leadReference: string;
  status: LeadStatus;
  ownerName: string;
  /** "Same phone", "Same email" or "Same WhatsApp". */
  matched: string;
  /** False once the lead has closed: it must be reopened before it can receive the enquiry. */
  canAdd: boolean;
};

export type HeldEnquiryView = {
  title: string;
  /** Where it came from: the source's name, or the provider when the source is unknown. */
  origin: string;
  contact: string[];
  /** False while a website booking request waits on this enquiry: dismissing would leave it without a lead. */
  canDismiss: boolean;
  matches: HeldEnquiryMatch[];
};

/** How one held enquiry is presented for a decision. Adding goes only to the lead chosen. */
export function describeHeldEnquiry(enquiry: HeldEnquiry): HeldEnquiryView {
  const name = [enquiry.firstName, enquiry.lastName].filter(Boolean).join(" ");
  const whatsapp = enquiry.whatsappNumber && enquiry.whatsappNumber !== enquiry.phone ? enquiry.whatsappNumber : null;

  return {
    title: name || "Unnamed enquiry",
    origin: enquiry.sourceName || enquiry.provider || "External enquiry",
    contact: [enquiry.phone, whatsapp, enquiry.email].filter((line): line is string => !!line),
    canDismiss: !enquiry.bookingRequestId,
    matches: enquiry.candidates.map((candidate) => ({
      leadId: candidate.leadId,
      leadName: candidate.leadName,
      leadReference: candidate.leadReference,
      status: statusOfGroup(candidate.leadStageGroup),
      ownerName: candidate.leadOwnerName || "Unassigned",
      matched: matchedOnLabel(candidate.matchedOn),
      canAdd: candidate.isOpen,
    })),
  };
}
