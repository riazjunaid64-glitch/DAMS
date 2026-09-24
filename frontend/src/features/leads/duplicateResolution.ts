/** The open lead a new manual entry collided with, as the intake API reports it. */
export type DuplicateMatch = { leadId?: number | null; leadReference?: string | null; matchedOn?: string };

export type DuplicateResolution = {
  heading: string;
  explanation: string;
  openLabel: string;
  /** Null when there is no lead to add the enquiry to. */
  addLabel: string | null;
  addOutcome: string;
};

const MATCHED_ON: Record<string, string> = {
  phone: "phone number",
  whatsapp: "WhatsApp number",
  email: "email address",
};

/**
 * What each duplicate-resolution choice on the capture form actually does.
 *
 * DAMS keeps one open lead per person: the API refuses a second one on create and on edit.
 * Resubmitting with `allowDuplicate` therefore adds this enquiry to the existing lead — it never
 * creates a separate one — so no choice here may promise a separate lead.
 */
export function describeDuplicate(match: DuplicateMatch): DuplicateResolution {
  const reference = match.leadReference || "the existing lead";
  const matchedOn = (match.matchedOn && MATCHED_ON[match.matchedOn]) || "contact details";

  return {
    heading: `An open lead (${reference}) already uses this ${matchedOn}.`,
    explanation: "DAMS keeps one open lead per person, so a separate lead cannot be created for this enquiry.",
    openLabel: `Open ${reference}`,
    addLabel: match.leadId ? `Add this enquiry to ${reference}` : null,
    addOutcome:
      "Adding it records this enquiry's source and notes on that lead and fills in only the details it is missing. " +
      "The lead's owner, stage and history do not change.",
  };
}
