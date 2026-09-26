import type { MetaConnection, MetaConnectionStatus, MetaResource } from "../leads/types.ts";
import type {
  LeadFormAnswerTarget,
  LeadFormMapping,
  LeadFormQuestion,
  MetaEventStatus,
  MetaLeadImportResult,
  SaveLeadFormMapping,
} from "./types.ts";

/**
 * The presentation logic behind the Integrations panel, kept as pure functions.
 *
 * This project has no component-rendering test setup, so anything worth asserting lives
 * here rather than inside the component, where it would be untestable.
 */

export type CallbackResult =
  | { kind: "none" }
  | { kind: "connected" }
  | { kind: "error"; message: string };

/** Fixed vocabulary the API is allowed to send back. Anything else is treated generically. */
const CALLBACK_REASONS: Record<string, string> = {
  denied: "The Meta authorization was cancelled before it finished.",
  invalid_state: "That connection link was already used or has expired. Start again from Connect Meta.",
  expired_state: "That connection link expired. Start again from Connect Meta.",
  exchange_failed: "Meta accepted the sign-in but DAMS could not complete the connection. Try again.",
  not_configured: "The Meta integration is not configured on this server yet.",
};

/**
 * Reads the outcome Meta's callback redirected back with. The URL only ever carries a
 * success flag or one of the fixed reasons above — never a token or a provider message.
 */
export function readCallbackResult(search: string): CallbackResult {
  const params = new URLSearchParams(search);
  const meta = params.get("meta");

  if (meta === "connected") return { kind: "connected" };
  if (meta !== "error") return { kind: "none" };

  const reason = params.get("reason") ?? "";
  return {
    kind: "error",
    message: CALLBACK_REASONS[reason] ?? "The Meta connection could not be completed.",
  };
}

export function connectionStatusLabel(status: MetaConnectionStatus): string {
  switch (status) {
    case "Connected":
      return "Connected";
    case "NeedsReauthorization":
      return "Needs reconnection";
    case "Disconnected":
      return "Disconnected";
    default:
      return "Error";
  }
}

/** Tailwind classes per status. Anything not healthy is visually distinct at a glance. */
export function connectionStatusTone(status: MetaConnectionStatus): string {
  switch (status) {
    case "Connected":
      return "border-emerald-500/30 bg-emerald-500/10 text-emerald-300";
    case "NeedsReauthorization":
      return "border-amber-500/30 bg-amber-500/10 text-amber-200";
    case "Disconnected":
      return "border-[var(--border)] bg-[var(--bg-muted)] text-[var(--text-muted)]";
    default:
      return "border-red-500/30 bg-red-500/10 text-red-300";
  }
}

export function resourceTypeLabel(resourceType: string): string {
  switch (resourceType) {
    case "facebook_page":
      return "Facebook Page";
    case "instagram_account":
      return "Instagram account";
    case "ad_account":
      return "Ad account";
    case "campaign":
      return "Campaign";
    case "ad_set":
      return "Ad set";
    case "ad":
      return "Ad";
    case "lead_form":
      return "Lead form";
    default:
      return resourceType;
  }
}

/**
 * Only a Facebook Page can be toggled. Everything else is discovered for attribution and
 * reporting; enabling an ad or a campaign would imply a control DAMS does not have.
 */
export function isToggleable(resource: MetaResource): boolean {
  return resource.resourceType === "facebook_page";
}

/** Lead forms can be linked to a project and have their answers mapped to lead fields. */
export function isMappableForm(resource: MetaResource): boolean {
  return resource.resourceType === "lead_form";
}

export function formMappingSummary(resource: MetaResource): string {
  if (!resource.hasFormMapping) return "Not linked to a project";
  return resource.formMappingProjectName
    ? `Project: ${resource.formMappingProjectName}`
    : "Answers mapped · no project";
}

export function canSync(connection: MetaConnection): boolean {
  return connection.status === "Connected" || connection.status === "Error";
}

/** True while a freshly connected account has not had its first discovery run yet. */
export function isAwaitingFirstSync(connection: MetaConnection): boolean {
  return connection.status === "Connected" && !connection.lastSyncedAt;
}

export function summarizeCounts(connection: MetaConnection): string {
  const parts: string[] = [];
  if (connection.pageCount > 0) parts.push(plural(connection.pageCount, "Page", "Pages"));
  if (connection.instagramCount > 0) parts.push(plural(connection.instagramCount, "Instagram account", "Instagram accounts"));
  if (connection.adAccountCount > 0) parts.push(plural(connection.adAccountCount, "ad account", "ad accounts"));
  if (connection.leadFormCount > 0) parts.push(plural(connection.leadFormCount, "lead form", "lead forms"));

  return parts.length === 0 ? "Nothing discovered yet" : parts.join(" · ");
}

function plural(count: number, singular: string, plural: string): string {
  return `${count} ${count === 1 ? singular : plural}`;
}

/**
 * Explains why lead delivery is or is not happening. A connection with pages but none
 * enabled looks healthy while quietly ingesting nothing, which is the confusing case worth
 * calling out explicitly.
 */
export function deliverySummary(connection: MetaConnection): string {
  if (connection.status === "Disconnected") return "Not receiving leads.";
  if (connection.status === "NeedsReauthorization") return "Paused until this account is reconnected.";
  if (connection.pageCount === 0) return "No Pages discovered yet.";
  if (connection.enabledResourceCount === 0) return "No Pages enabled, so no leads are being received.";

  return `Receiving leads from ${plural(connection.enabledResourceCount, "Page", "Pages")}.`;
}

const DAY_MS = 24 * 60 * 60 * 1000;

/** Days before expiry the sign-in is called out; the server alerts Admins on the same default. */
export const SIGN_IN_WARNING_DAYS = 7;

export type SignInExpiry = { text: string; tone: "normal" | "warning" | "expired" };

/**
 * When the account's own Meta sign-in runs out. Leads are fetched with Page tokens that outlive
 * it, but discovery stops, so it is worth reconnecting before the date.
 */
export function signInExpiry(connection: MetaConnection, now: Date = new Date()): SignInExpiry | null {
  if (connection.status === "Disconnected" || !connection.tokenExpiresAt) return null;
  const expiresAt = new Date(connection.tokenExpiresAt);
  if (Number.isNaN(expiresAt.getTime())) return null;

  const date = expiresAt.toLocaleDateString(undefined, { day: "numeric", month: "short", year: "numeric" });
  const left = expiresAt.getTime() - now.getTime();
  if (left <= 0) return { text: `Meta sign-in expired ${date}`, tone: "expired" };
  if (left <= SIGN_IN_WARNING_DAYS * DAY_MS) {
    const days = Math.ceil(left / DAY_MS);
    return { text: `Meta sign-in expires ${date} (${days} day${days === 1 ? "" : "s"} left)`, tone: "warning" };
  }
  return { text: `Meta sign-in expires ${date}`, tone: "normal" };
}

/** Whether Meta is still delivering anything at all — the first thing to check when leads stop. */
export function lastLeadSummary(connection: MetaConnection, format: (value: string) => string): string {
  return connection.lastLeadReceivedAt
    ? `Last lead received ${format(connection.lastLeadReceivedAt)}`
    : "No leads received yet";
}

/**
 * A refused sign-in on a connection that still delivers leads: a reason to reconnect, not an
 * outage. A connection already waiting to be reconnected says so on its own.
 */
export function syncRejectionWarning(connection: MetaConnection): string | null {
  if (!connection.syncRejectedAt || connection.status !== "Connected") return null;
  return "Meta refused this account's sign-in, so its Pages and lead forms are no longer refreshed. " +
    "Leads still arrive through the Page tokens. Reconnect the account with Connect Meta to restore syncing.";
}

/** Meta keeps a lead readable through its form for this many days. */
export const MAX_IMPORT_DAYS = 90;

/** A local calendar date as YYYY-MM-DD, `daysBack` days before `now`. */
export function importDate(daysBack: number, now: Date = new Date()): string {
  const date = new Date(now.getFullYear(), now.getMonth(), now.getDate() - daysBack);
  const pad = (value: number) => String(value).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/** The earliest date an import may start from; the server holds the same 90-day line. */
export function earliestImportDate(now: Date = new Date()): string {
  return importDate(MAX_IMPORT_DAYS - 1, now);
}

/** What an import did, in one line an admin can act on. */
export function importSummary(result: MetaLeadImportResult): string {
  const parts = [
    `${result.found} found`,
    `${result.new} new`,
    `${result.alreadyInDams} already in DAMS`,
  ];
  if (result.failed > 0) parts.push(`${result.failed} failed`);
  const summary = parts.join(" · ");
  return result.new > 0 ? `${summary}. New leads appear within a minute.` : summary;
}

/** The event list shows the latest events, or every event in one status. */
export type MetaEventFilter = MetaEventStatus | "All";

/** How many events the list asks for; the server caps any request at 200. */
export function eventListLimit(filter: MetaEventFilter): number {
  return filter === "All" ? 25 : 200;
}

const eventStatusWords: Record<MetaEventStatus, string> = {
  Pending: "pending",
  Processing: "processing",
  Processed: "processed",
  Retry: "retrying",
  Failed: "failed",
  Ignored: "ignored",
};

/** An empty filtered list means "none in this status", not "nothing ever arrived". */
export function emptyEventsMessage(filter: MetaEventFilter): string {
  return filter === "All"
    ? "No webhook events recorded for this connection."
    : `No ${eventStatusWords[filter]} events for this connection.`;
}

/** Says so when the list is cut off at its limit, so a full page is not read as the whole set. */
export function eventListLimitNote(filter: MetaEventFilter, shown: number): string | null {
  const limit = eventListLimit(filter);
  if (shown < limit) return null;
  return filter === "All"
    ? `Showing the ${limit} most recent events.`
    : `Showing the ${limit} most recent ${eventStatusWords[filter]} events; there may be more.`;
}

/**
 * Lets only the newest of several overlapping loads apply its result. Switching the filter
 * quickly starts a new request before the old one answers, and the older answer must not
 * overwrite the newer one when it arrives last.
 */
export function createLatestRequestGuard() {
  let latest = 0;
  return {
    begin(): () => boolean {
      const id = ++latest;
      return () => id === latest;
    },
  };
}

export const answerTargetLabels: Record<LeadFormAnswerTarget, string> = {
  PropertyType: "Property type",
  PurchaseIntent: "Purchase intent",
  PaymentPreference: "Payment preference",
};

/** The values a choice field accepts. Property type is free text, so it has none. */
export const answerTargetValues: Record<Exclude<LeadFormAnswerTarget, "PropertyType">, string[]> = {
  PurchaseIntent: ["SelfUse", "Investment", "Rental", "Resale"],
  PaymentPreference: ["Installments", "NeedsDetails", "Cash"],
};

export type QuestionMappingDraft = { target: LeadFormAnswerTarget | ""; values: Record<string, string> };

/** The mapping being edited: a project, and per question key the field it fills and each option's value. */
export type FormMappingDraft = { projectId: string; questions: Record<string, QuestionMappingDraft> };

export const questionText = (question: LeadFormQuestion) => question.label || question.key;

/** Only multiple-choice questions can be mapped; a typed answer has no options to give values to. */
export function mappableQuestions(mapping: LeadFormMapping): LeadFormQuestion[] {
  return mapping.questions.filter((question) => question.options.length > 0);
}

export function draftFromMapping(mapping: LeadFormMapping): FormMappingDraft {
  const questions: Record<string, QuestionMappingDraft> = {};
  for (const answer of mapping.answers) {
    questions[answer.questionKey] = {
      target: answer.target,
      values: Object.fromEntries(answer.options.map((option) => [option.optionKey, option.value])),
    };
  }
  return { projectId: mapping.interestedProjectId ? String(mapping.interestedProjectId) : "", questions };
}

/** A new field means different values, so choosing one starts that question's values afresh. */
export function withQuestionTarget(
  draft: FormMappingDraft,
  questionKey: string,
  target: LeadFormAnswerTarget | "",
): FormMappingDraft {
  return { ...draft, questions: { ...draft.questions, [questionKey]: { target, values: {} } } };
}

export function withOptionValue(
  draft: FormMappingDraft,
  questionKey: string,
  optionKey: string,
  value: string,
): FormMappingDraft {
  const question = draft.questions[questionKey] ?? { target: "", values: {} };
  return {
    ...draft,
    questions: { ...draft.questions, [questionKey]: { ...question, values: { ...question.values, [optionKey]: value } } },
  };
}

/**
 * What to save. A question with no field chosen is left out, and so is an option with no value:
 * its answers then stay unmapped rather than being given a value nobody picked.
 */
export function buildFormMappingRequest(mapping: LeadFormMapping, draft: FormMappingDraft): SaveLeadFormMapping {
  const answers = mappableQuestions(mapping).flatMap((question) => {
    const chosen = draft.questions[question.key];
    if (!chosen?.target) return [];
    const target = chosen.target;
    return [{
      questionKey: question.key,
      target,
      options: question.options
        .filter((option) => chosen.values[option.key]?.trim())
        .map((option) => ({ optionKey: option.key, optionLabel: option.value ?? null, value: chosen.values[option.key].trim() })),
    }];
  });

  return {
    interestedProjectId: draft.projectId ? Number(draft.projectId) : null,
    answers,
    version: mapping.version ?? null,
  };
}
