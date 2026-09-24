/** A lead a held enquiry matched, as `GET /api/leads/held-enquiries` returns it. */
export type HeldEnquiryCandidate = {
  leadId: number;
  leadReference: string;
  leadName: string;
  leadStage: string;
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

export type HeldEnquiryChoice = {
  leadId: number;
  title: string;
  detail: string;
  addLabel: string;
  /** Why this lead cannot receive the enquiry right now, or null when it can. */
  blockedReason: string | null;
};

export type HeldEnquiryView = {
  title: string;
  contact: string[];
  origin: string;
  /** Set when a website booking request cannot be approved until this is decided. */
  waitingNote: string | null;
  canDismiss: boolean;
  choices: HeldEnquiryChoice[];
};

const MATCHED_ON: Record<string, string> = {
  phone: "phone number",
  whatsapp: "WhatsApp number",
  email: "email address",
};

/**
 * How one held enquiry is presented for a decision. Adding goes only to the lead chosen, and a
 * closed lead cannot receive it until it is reopened. An enquiry a website booking request is
 * waiting on can never be dismissed: the request would be left with no lead at all.
 */
export function describeHeldEnquiry(enquiry: HeldEnquiry): HeldEnquiryView {
  const name = [enquiry.firstName, enquiry.lastName].filter(Boolean).join(" ");

  return {
    title: name || "Unnamed enquiry",
    contact: [
      enquiry.phone && `Phone ${enquiry.phone}`,
      enquiry.whatsappNumber && `WhatsApp ${enquiry.whatsappNumber}`,
      enquiry.email && `Email ${enquiry.email}`,
    ].filter((line): line is string => !!line),
    origin: [enquiry.sourceName || enquiry.provider || "External enquiry", enquiry.campaignName].filter(Boolean).join(" · "),
    waitingNote: enquiry.bookingRequestId
      ? `Website booking request #${enquiry.bookingRequestId} cannot be approved until you choose its lead.`
      : null,
    canDismiss: !enquiry.bookingRequestId,
    choices: enquiry.candidates.map((candidate) => ({
      leadId: candidate.leadId,
      title: `${candidate.leadReference} · ${candidate.leadName}`,
      detail: `Matches this enquiry's ${MATCHED_ON[candidate.matchedOn] ?? "contact details"}` +
        (candidate.leadOwnerName ? ` · owned by ${candidate.leadOwnerName}` : " · unassigned"),
      addLabel: `Add to ${candidate.leadReference}`,
      blockedReason: candidate.isOpen ? null : "This lead is closed. Reopen it first to add the enquiry to it.",
    })),
  };
}
