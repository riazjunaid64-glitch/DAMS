import { statusOfGroup, statusText } from "./labels.ts";

/** The open lead a new manual entry collided with, as the intake API reports it. */
export type DuplicateMatch = {
  leadId?: number | null;
  leadReference?: string | null;
  leadName?: string | null;
  /** The simple status group (New, InProgress, Won, Lost, Dormant). */
  leadStageGroup?: string | null;
  leadOwnerName?: string | null;
  matchedOn?: string;
};

export type DuplicateChoice = {
  leadId: number;
  /** "Hamza Iqbal · LD-000437 · In progress · Sana Malik". */
  detail: string;
};

export type DuplicateWarning = { title: string; choices: DuplicateChoice[] };

const TITLES: Record<string, string> = {
  phone: "This number already has a lead",
  whatsapp: "This number already has a lead",
  email: "This email already has a lead",
};

type LeadMatch = DuplicateMatch & { leadId: number };

const namesALead = (match: DuplicateMatch): match is LeadMatch => !!match.leadId;

const choiceOf = (match: LeadMatch): DuplicateChoice => ({
  leadId: match.leadId,
  detail: [
    match.leadName,
    match.leadReference,
    match.leadStageGroup && statusText(statusOfGroup(match.leadStageGroup)),
    match.leadOwnerName || "Unassigned",
  ].filter(Boolean).join(" · "),
});

/**
 * The warning on the capture form when the details belong to an open lead.
 *
 * DAMS keeps one open lead per person: the API refuses a second one on create and on edit.
 * Resubmitting with `allowDuplicate` therefore adds this enquiry to the existing lead — it never
 * creates a separate one.
 */
export function describeDuplicate(match: DuplicateMatch): DuplicateWarning {
  return {
    title: (match.matchedOn && TITLES[match.matchedOn]) || "These details already have a lead",
    choices: namesALead(match) ? [choiceOf(match)] : [],
  };
}

/**
 * The details matched more than one open lead — the phone one person's, the email another's.
 * The API changes neither until the person picks one, so every lead is listed and adding goes
 * only to the lead that was picked.
 */
export function describeConflict(matches: DuplicateMatch[]): DuplicateWarning {
  const choices = matches.filter(namesALead).map(choiceOf);
  return { title: `These details match ${choices.length} leads`, choices };
}
