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
      `Adding it uses the details in this form as they are now: their source and notes are recorded on ${reference}, ` +
      "and only details it is missing are filled in. The lead's owner, stage and history do not change. " +
      "If these details no longer match it when you add, nothing is saved.",
  };
}

export type ConflictChoice = { leadId: number; detail: string; openLabel: string; addLabel: string };

export type ConflictResolution = {
  heading: string;
  explanation: string;
  addOutcome: string;
  choices: ConflictChoice[];
};

/**
 * The details matched more than one open lead — the phone one person's, the email another's.
 * The API changes neither until the person picks one, so every lead is shown side by side and
 * adding goes only to the lead that was picked.
 */
export function describeConflict(matches: DuplicateMatch[]): ConflictResolution {
  const choices = matches
    .filter((match): match is DuplicateMatch & { leadId: number } => !!match.leadId)
    .map((match) => {
      const single = describeDuplicate(match);
      return {
        leadId: match.leadId,
        detail: `${match.leadReference || "A lead"} matches this ${(match.matchedOn && MATCHED_ON[match.matchedOn]) || "contact details"}.`,
        openLabel: single.openLabel,
        addLabel: single.addLabel ?? `Add this enquiry to ${match.leadReference || "this lead"}`,
      };
    });

  return {
    heading: `These details match ${choices.length} different open leads.`,
    explanation:
      "They may belong to different people, so nothing has been changed. Open the leads to check, then choose the one this enquiry belongs to.",
    addOutcome:
      "Adding it uses the details in this form as they are now and changes only the lead you choose. " +
      "If the details no longer match that lead when you add, nothing is saved.",
    choices,
  };
}
