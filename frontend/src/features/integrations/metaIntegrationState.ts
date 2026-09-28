import type { MetaConnection, MetaConnectionStatus, MetaResource } from "../leads/types.ts";
import { formatWhen, parseServerDateTime } from "../../lib/dates.ts";
import type {
  LeadFormAnswerTarget,
  LeadFormMapping,
  LeadFormOption,
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
      return "Needs reconnect";
    case "Disconnected":
      return "Disconnected";
    default:
      return "Error";
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

/** Used only if the server did not say; it is the server's own default. */
const DEFAULT_SIGN_IN_WARNING_DAYS = 7;

export type SignInExpiry = { text: string; tone: "normal" | "warning" | "expired" };

/**
 * When the account's own Meta sign-in runs out. Leads are fetched with Page tokens that outlive
 * it, but discovery stops, so it is worth reconnecting before the date. The warning window is
 * the server's, so the panel turns amber exactly when Admins are alerted.
 */
export function signInExpiry(connection: MetaConnection, now: Date = new Date()): SignInExpiry | null {
  if (connection.status === "Disconnected") return null;
  // The server sends UTC without a "Z"; read as local time it would be five hours off in Pakistan.
  const expiresAt = parseServerDateTime(connection.tokenExpiresAt);
  if (!expiresAt) return null;

  const date = expiresAt.toLocaleDateString(undefined, { day: "numeric", month: "short", year: "numeric" });
  const left = expiresAt.getTime() - now.getTime();
  if (left <= 0) return { text: `Meta sign-in expired ${date}`, tone: "expired" };
  const warningDays = connection.signInWarningDays ?? DEFAULT_SIGN_IN_WARNING_DAYS;
  if (left <= warningDays * DAY_MS) {
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
  if (result.previouslyFailed > 0) {
    parts.push(`${result.previouslyFailed} previously failed – retry from the event list`);
  }
  if (result.failed > 0) parts.push(`${result.failed} failed`);
  const summary = parts.join(" · ");
  if (result.new === 0) return summary;

  const quiet = result.addedWithoutAlert;
  const quietNote = quiet > 0
    ? ` ${quiet} older ${quiet === 1 ? "lead is" : "leads are"} added without a new-lead alert; assign ${quiet === 1 ? "it" : "them"} from the Leads queue.`
    : "";
  return `${summary}. New leads appear within a minute.${quietNote}`;
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
  PropertyType: "Apartment type",
  PurchaseIntent: "Buying for",
  PaymentPreference: "Installment plan",
};

/** The fixed values a choice can be saved as. Apartment type is no longer free text. */
export const answerTargetValues: Record<LeadFormAnswerTarget, { value: string; label: string }[]> = {
  PropertyType: [
    { value: "Studio", label: "Studio" },
    { value: "1 Bed", label: "1 Bed" },
    { value: "2 Bed", label: "2 Bed" },
    { value: "3 Bed", label: "3 Bed" },
  ],
  PaymentPreference: [
    { value: "Installments", label: "Installments" },
    { value: "NeedsDetails", label: "Needs details" },
    { value: "Cash", label: "Cash" },
  ],
  PurchaseIntent: [
    { value: "SelfUse", label: "Personal living" },
    { value: "Investment", label: "Investment" },
  ],
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

/**
 * A new field means different values. Passing the question's options pre-selects the ones whose
 * text clearly matches; without them the values start empty.
 */
export function withQuestionTarget(
  draft: FormMappingDraft,
  questionKey: string,
  target: LeadFormAnswerTarget | "",
  options: LeadFormOption[] = [],
): FormMappingDraft {
  return {
    ...draft,
    questions: { ...draft.questions, [questionKey]: { target, values: target ? suggestOptionValues(target, options) : {} } },
  };
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
    const options = question.options
      .filter((option) => chosen.values[option.key]?.trim())
      .map((option) => ({ optionKey: option.key, optionLabel: option.value ?? null, value: chosen.values[option.key].trim() }));
    // A field with no values is "Don't save": the server refuses an answer that maps nothing.
    if (options.length === 0) return [];
    return [{ questionKey: question.key, target, options }];
  });

  return {
    interestedProjectId: draft.projectId ? Number(draft.projectId) : null,
    answers,
    version: mapping.version ?? null,
  };
}

/** A connection that can still receive or reconnect. A disconnected row is the "not connected" card. */
export function activeConnections(connections: MetaConnection[]): MetaConnection[] {
  return connections.filter((connection) => connection.status === "Connected" || connection.status === "NeedsReauthorization");
}

/** Instagram account linked under a Facebook Page, matched by the Page's external id. */
export function instagramForPage(resources: MetaResource[], page: MetaResource): MetaResource | undefined {
  return resources.find((resource) => resource.resourceType === "instagram_account" && resource.parentExternalId === page.externalId && resource.isActive);
}

export function pageChannelLine(_page: MetaResource, instagram?: MetaResource | null): string {
  const handle = instagram?.name?.trim().replace(/^@/, "");
  return handle ? `Facebook Page · Instagram @${handle}` : "Facebook Page · no Instagram linked";
}

/** "Last lead today, 11:40 AM · 11 this week". Null until a lead has arrived. */
export function pageLeadLine(page: MetaResource, now: Date = new Date()): string | null {
  if (!page.lastLeadAt) return null;
  const when = formatWhen(page.lastLeadAt, now).replace(/^(Today|Tomorrow|Yesterday)/, (word) => word.toLowerCase());
  const count = page.leadsLast7Days ?? 0;
  return `Last lead ${when} · ${count} this week`;
}

export type FormSetupState = "ready" | "missing" | "empty" | "unread";

/** How a lead form's answer row should read. Null question count means the form has not been read yet. */
export function formSetupState(form: MetaResource): FormSetupState {
  if (form.choiceQuestionCount == null) return "unread";
  if (form.choiceQuestionCount === 0) return "empty";
  return form.answersSetUp ? "ready" : "missing";
}

export function formDetailLine(form: MetaResource, pageName?: string | null): string {
  const page = pageName?.trim() || "Facebook Page";
  const state = formSetupState(form);
  if (state === "unread") return `${page} · Refresh forms first`;
  if (state === "empty") return `${page} · no choice questions`;
  const questions = form.choiceQuestionCount ?? 0;
  const leads = form.leadCount ?? 0;
  return `${page} · ${questions} choice question${questions === 1 ? "" : "s"} · ${leads} lead${leads === 1 ? "" : "s"}`;
}

/** The result line under an import: found, added, and the ones already in DAMS. */
export function importOutcomeLine(result: MetaLeadImportResult): string {
  const parts = [
    `${result.found} lead${result.found === 1 ? "" : "s"} found`,
    `${result.new} added`,
    `${result.alreadyInDams} ${result.alreadyInDams === 1 ? "was" : "were"} already in DAMS`,
  ];
  if (result.failed > 0) parts.push(`${result.failed} failed`);
  return parts.join(" · ");
}

/** Floria Heights when the form has no project saved yet. */
export function defaultProjectId(projects: { id: number; name: string }[]): string {
  const match = projects.find((project) => /floria heights/i.test(project.name))
    ?? projects.find((project) => /floria/i.test(project.name));
  return match ? String(match.id) : "";
}

/** Which lead field a Facebook question is obviously about. Empty when it is not obvious. */
export function suggestTarget(question: LeadFormQuestion): LeadFormAnswerTarget | "" {
  const text = `${question.label ?? ""} ${question.key}`.toLowerCase();
  if (/apartment|bedroom|bed\s*room|\bstudio\b|property type/.test(text)) return "PropertyType";
  if (/installment|payment preference|on cash/.test(text)) return "PaymentPreference";
  if (/buying for|purchase intent|buy for/.test(text)) return "PurchaseIntent";
  return "";
}

/** The fixed value an option clearly names, or empty when it does not. */
export function suggestOptionValue(target: LeadFormAnswerTarget, option: LeadFormOption): string {
  const text = `${option.value ?? ""} ${option.key}`.toLowerCase().replace(/_+/g, " ");
  if (target === "PropertyType") {
    if (/\bstudio\b/.test(text)) return "Studio";
    if (/\b3\b|three/.test(text) && /bed/.test(text)) return "3 Bed";
    if (/\b2\b|two/.test(text) && /bed/.test(text)) return "2 Bed";
    if (/\b1\b|one/.test(text) && /bed/.test(text)) return "1 Bed";
    return "";
  }
  if (target === "PaymentPreference") {
    if (/need more|needs detail|more detail/.test(text)) return "NeedsDetails";
    if (/cash/.test(text)) return "Cash";
    if (/\byes\b|installment/.test(text)) return "Installments";
    return "";
  }
  if (/personal|self use|selfuse/.test(text)) return "SelfUse";
  if (/investment/.test(text)) return "Investment";
  return "";
}

function suggestOptionValues(target: LeadFormAnswerTarget, options: LeadFormOption[]): Record<string, string> {
  return Object.fromEntries(
    options.flatMap((option) => {
      const value = suggestOptionValue(target, option);
      return value ? [[option.key, value]] : [];
    }),
  );
}

/**
 * The draft the Set up answers popup opens on: saved answers win, and anything still blank is
 * filled when the question or the option text makes the value obvious.
 */
export function initialFormDraft(mapping: LeadFormMapping, projects: { id: number; name: string }[]): FormMappingDraft {
  const draft = draftFromMapping(mapping);
  const questions = { ...draft.questions };
  for (const question of mappableQuestions(mapping)) {
    if (questions[question.key]) continue;
    const target = suggestTarget(question);
    if (!target) continue;
    questions[question.key] = { target, values: suggestOptionValues(target, question.options) };
  }
  return { projectId: draft.projectId || defaultProjectId(projects), questions };
}
