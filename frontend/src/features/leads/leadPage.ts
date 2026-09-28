import { daysFromToday, formatDateInput, formatWhen, fromKarachiInputs, isPastServerTime, karachiDateInput, parseServerDateTime } from "../../lib/dates.ts";
import { leadStatus, paymentPreferenceLabel, purchaseIntentLabel, statusText } from "./labels.ts";
import type { ExternalFieldAnswer, ExternalSubmission, FollowUp, Lead, SiteVisit, StaffMember, TimelineItem, UnitLookup } from "./types.ts";

/** "Needs details · Personal living": how they want to pay and who they are buying for. */
export function requirementDetail(lead: Pick<Lead, "paymentPreference" | "purchaseIntent">): string {
  return [paymentPreferenceLabel(lead.paymentPreference)?.label, purchaseIntentLabel(lead.purchaseIntent)].filter(Boolean).join(" · ");
}

/** "In progress · Sana Malik": where the lead stands, for the top of a popup. */
export const statusAndOwner = (lead: Lead) =>
  [statusText(leadStatus(lead)), lead.assignedEmployeeName ?? "Unassigned"].join(" · ");

/** A lead that is Won, Lost or Dormant has nothing left to work on. */
export const isClosed = (lead: Pick<Lead, "stageGroup">) => leadStatus(lead) !== "InProgress";

/** When a follow-up is due, in words: red "Overdue · …" once it has passed or was missed. */
export function followUpDue(item: FollowUp, now: Date = new Date()): { text: string; overdue: boolean } {
  const overdue = item.status === "Missed" || (item.status === "Pending" && isPastServerTime(item.dueAt, now));
  return { text: `${overdue ? "Overdue · " : ""}${formatWhen(item.dueAt, now)}`, overdue };
}

const time = (value?: string | null) => parseServerDateTime(value)?.getTime() ?? 0;

/** To do (the next one first) and done (the latest first). Cancelled follow-ups are not shown. */
export function followUpGroups(items: FollowUp[]) {
  return {
    todo: items.filter((item) => item.status === "Pending" || item.status === "Missed").sort((a, b) => time(a.dueAt) - time(b.dueAt)),
    done: items.filter((item) => item.status === "Completed").sort((a, b) => time(b.completedAt ?? b.dueAt) - time(a.completedAt ?? a.dueAt)),
  };
}

/** Upcoming (the next one first) and past (the latest first). Cancelled visits are not shown. */
export function visitGroups(items: SiteVisit[]) {
  return {
    upcoming: items.filter((item) => item.status === "Scheduled" || item.status === "Rescheduled").sort((a, b) => time(a.scheduledAt) - time(b.scheduledAt)),
    past: items.filter((item) => item.status === "Completed" || item.status === "Missed").sort((a, b) => time(b.scheduledAt) - time(a.scheduledAt)),
  };
}

/** Timeline entries under their Karachi day, keeping the newest-first order. */
export function timelineDays(items: TimelineItem[], dayOf: (value: string) => string) {
  const days: { day: string; items: TimelineItem[] }[] = [];
  for (const item of items) {
    const day = dayOf(item.occurredAt);
    const last = days[days.length - 1];
    if (last?.day === day) last.items.push(item);
    else days.push({ day, items: [item] });
  }
  return days;
}

/** The dot beside a timeline entry: gold for follow-ups, grey for what DAMS did by itself, navy for people's work. */
export function timelineDot(item: TimelineItem): "gold" | "grey" | "navy" {
  if (item.type.startsWith("FollowUp")) return "gold";
  return item.isSystemGenerated ? "grey" : "navy";
}

/** "Facebook" / "Instagram"; never a guess when Meta did not say. */
export function platformLabel(item: Pick<ExternalSubmission, "platform" | "provider">): string {
  if (item.platform === "instagram") return "Instagram";
  if (item.platform === "facebook") return "Facebook";
  return item.provider === "meta" ? "Meta" : item.provider;
}

/** The newest form submission behind the lead, if it came from one. */
export function latestSubmission(items: ExternalSubmission[]): ExternalSubmission | null {
  return items.reduce<ExternalSubmission | null>(
    (latest, item) => (!latest || time(item.receivedAt) > time(latest.receivedAt) ? item : latest), null);
}

/** Form answers DAMS has no field for yet, as "question → answer", so nothing the customer said is hidden. */
export function unmappedAnswers(submission: ExternalSubmission | null): { label: string; value: string }[] {
  return (submission?.fieldData ?? [])
    .filter((answer: ExternalFieldAnswer) => !answer.isMapped)
    .map((answer) => ({ label: answer.label || answer.name, value: answer.valueLabel || answer.value || "" }));
}

/** "Ground floor" · "1st floor" · "5th floor". */
export function floorLabel(floor: number): string {
  if (floor === 0) return "Ground floor";
  const ones = floor % 10;
  const tens = Math.floor(floor / 10) % 10;
  const suffix = tens === 1 ? "th" : ones === 1 ? "st" : ones === 2 ? "nd" : ones === 3 ? "rd" : "th";
  return `${floor}${suffix} floor`;
}

/** Units that can still be booked, the lead's apartment type first: "Unit 504 — 2 Bed · 5th floor", by the project's floor name. */
export function unitChoices(units: UnitLookup[], propertyType?: string | null) {
  const wanted = propertyType?.trim().toLowerCase();
  const rank = (unit: UnitLookup) => (wanted && unit.type.trim().toLowerCase() === wanted ? 0 : 1);
  return units
    .filter((unit) => unit.status === "Available")
    .sort((a, b) => rank(a) - rank(b) || a.number.localeCompare(b.number, undefined, { numeric: true }))
    .map((unit) => ({ value: String(unit.id), label: `Unit ${unit.number} — ${[unit.type, unit.floorName || floorLabel(unit.floor)].filter(Boolean).join(" · ")}` }));
}

/** Tomorrow / In 3 days / Next week: quick days for a follow-up. */
export const QUICK_DAYS = [
  { value: "1", label: "Tomorrow" },
  { value: "3", label: "In 3 days" },
  { value: "7", label: "Next week" },
] as const;

/** Which quick day a date input value is, or "" when it is none of them. */
export function quickDayOf(dateInput: string, now: Date = new Date()): string {
  const days = String(daysFromToday(dateInput, now));
  return QUICK_DAYS.some((choice) => choice.value === days) ? days : "";
}

/**
 * Tomorrow, the coming Saturday and the coming Sunday, as date input values: the days a site
 * visit usually happens. A day already offered is not offered twice.
 */
export function visitDayChoices(now: Date = new Date()): { value: string; label: string }[] {
  const today = new Date(`${karachiDateInput(0, now)}T00:00:00Z`).getUTCDay();
  const ahead = (weekday: number) => ((weekday - today + 7) % 7) || 7;
  const choices = [
    { value: karachiDateInput(1, now), label: "Tomorrow" },
    { value: karachiDateInput(ahead(6), now), label: "" },
    { value: karachiDateInput(ahead(0), now), label: "" },
  ];
  return choices
    .filter((choice, index) => choices.findIndex((other) => other.value === choice.value) === index)
    .map((choice) => ({ value: choice.value, label: choice.label || formatDateInput(choice.value) }));
}

/** Site visit reminders: the default, or a set time before the visit. */
export const REMINDERS = [
  { value: "", label: "Default reminder" },
  { value: "1", label: "1 hour before" },
  { value: "3", label: "3 hours before" },
  { value: "24", label: "1 day before" },
] as const;

/** When to remind about a visit, or null for the default reminder. */
export function reminderAt(scheduledAt: string, hoursBefore: string): string | null {
  if (!hoursBefore) return null;
  return new Date(Date.parse(scheduledAt) - Number(hoursBefore) * 3_600_000).toISOString();
}

/** A Karachi date and time as the API takes them, or null until both are filled in. */
export const whenValue = (value: { date: string; time: string }) => fromKarachiInputs(value.date, value.time);

/**
 * What reopening sends. The server reopens a lead as contacted; a lead nobody ever reached goes
 * back as New instead, which it refuses to skip. Both show as In progress.
 */
export const reopenBody = (lead: Pick<Lead, "lastContactAt">) => (lead.lastContactAt ? {} : { stage: "New" });

/**
 * Who a follow-up or site visit can be given to: anyone who can own leads for an Admin or Sales
 * manager, only themselves for a Sales employee (the API enforces the same). The lead's owner is
 * named as such and chosen first.
 */
export function workerChoices(staff: StaffMember[], lead: Pick<Lead, "assignedEmployeeId">, me: StaffMember | null, canManage: boolean) {
  const people = staff.filter((member) => member.canOwnLeads && (canManage || member.employeeId === me?.employeeId));
  const options = people.map((member) => ({
    value: String(member.employeeId),
    label: member.employeeId === lead.assignedEmployeeId ? `${member.fullName} (lead owner)` : member.fullName,
  }));
  const preferred = canManage ? lead.assignedEmployeeId ?? me?.employeeId : me?.employeeId;
  const chosen = options.find((option) => option.value === String(preferred)) ?? options[0];
  return { options, chosen: chosen?.value ?? "" };
}

/** Assign to: the current owner, then "Me", then everyone else who can own leads. No "nobody". */
export function assignChoices(staff: StaffMember[], lead: Pick<Lead, "assignedEmployeeId">, me: StaffMember | null) {
  const people = staff.filter((member) => member.canOwnLeads);
  const current = people.filter((member) => member.employeeId === lead.assignedEmployeeId);
  const mine = people.filter((member) => member.employeeId === me?.employeeId && member.employeeId !== lead.assignedEmployeeId);
  const others = people.filter((member) => member.employeeId !== lead.assignedEmployeeId && member.employeeId !== me?.employeeId);
  return [
    ...current.map((member) => ({ value: String(member.employeeId), label: `${member.fullName} (current)` })),
    ...mine.map((member) => ({ value: String(member.employeeId), label: `Me (${member.fullName})` })),
    ...others.map((member) => ({ value: String(member.employeeId), label: member.fullName })),
  ];
}
