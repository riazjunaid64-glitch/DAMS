import { initialForm } from "./leadActionDefaults.ts";
import { LeadConflictError } from "./leadApi.ts";
import type { Lead } from "./types.ts";

export type EditForm = Record<string, string | boolean>;

export type EditConflict = { label: string; theirs: string };

export const EDIT_LABELS: Record<string, string> = {
  firstName: "First name",
  lastName: "Last name",
  phone: "Phone",
  whatsappNumber: "WhatsApp",
  email: "Email",
  address: "Address",
  city: "City",
  preferredContactMethod: "Preferred contact",
  preferredContactTime: "Preferred contact time",
  sourceDetails: "Source details",
  campaignName: "Campaign",
  campaignReference: "Campaign reference",
  adReference: "Ad reference",
  interestedProjectId: "Interested project",
  interestedUnitId: "Interested unit",
  propertyType: "Property type",
  preferredLocation: "Preferred location",
  budgetMin: "Minimum budget",
  budgetMax: "Maximum budget",
  purchaseIntent: "Purchase intent",
  paymentPreference: "Payment preference",
  notes: "Notes",
};

// A unit only means something within its project, so the two are merged as one choice:
// taking a newer project while keeping this form's unit could pair a unit with the wrong project.
const PAIRED = ["interestedProjectId", "interestedUnitId"];

// Fields a repeat enquiry appends to (LeadService.Append joins with a newline) rather than
// replaces, with their UpdateLeadDto length limits.
const APPENDED: Record<string, number> = { notes: 2000, sourceDetails: 500 };

/**
 * Merges an edit form whose save was refused because the lead changed after the form opened.
 * `base` is the lead the form was opened from and `latest` the lead as it is now.
 *
 * A field left untouched in the form takes the newer value, so saving again cannot revert
 * someone else's change or an external enquiry's enrichment. A field changed only in the form
 * keeps the form's value. Text an enquiry appended to notes or source details is appended to
 * the form's edited text too. Anything else changed on both sides keeps the form's value and
 * is reported, so replacing the other change is a decision made with it in view.
 */
export function mergeLeadEdit(base: Lead, latest: Lead, mine: EditForm) {
  const was = initialForm({ type: "edit" }, base);
  const now = initialForm({ type: "edit" }, latest);
  const form: EditForm = { ...mine };
  const conflicts: EditConflict[] = [];

  // Every field the edit form has, so a field added to the form later is merged too.
  const groups = [PAIRED, ...Object.keys(was).filter((field) => !PAIRED.includes(field)).map((field) => [field])];
  for (const group of groups) {
    const differs = (a: EditForm, b: EditForm) => group.some((field) => a[field] !== b[field]);
    if (!differs(mine, was)) {
      for (const field of group) form[field] = now[field];
      continue;
    }
    if (!differs(now, was) || !differs(now, mine)) continue;

    const [field] = group;
    const rebased = group.length === 1 && field in APPENDED
      ? reapplyAppended(String(was[field]), String(now[field]), String(mine[field]), APPENDED[field])
      : null;
    if (rebased !== null) form[field] = rebased;
    else for (const f of group) conflicts.push({ label: EDIT_LABELS[f] ?? f, theirs: shown(f, latest, now) });
  }

  return { form, conflicts, detailsChanged: Object.keys(was).some((field) => was[field] !== now[field]) };
}

// The text an enquiry added after `was`, put after the form's own edit. Null when the newer
// value is not simply `was` plus an addition (someone rewrote it, or the server trimmed the
// oldest text to fit), or when the result would no longer fit the field.
function reapplyAppended(was: string, now: string, mine: string, max: number): string | null {
  const addition = was === "" ? now : now.startsWith(`${was}\n`) ? now.slice(was.length + 1) : null;
  if (addition === null) return null;
  const result = mine.trim() ? `${mine}\n${addition}` : addition;
  return result.length <= max ? result : null;
}

// The form holds ids for the project and unit; the person needs the names.
function shown(field: string, latest: Lead, now: EditForm) {
  if (field === "interestedProjectId") return latest.interestedProjectName ?? "";
  if (field === "interestedUnitId") return latest.interestedUnitNumber ?? "";
  return String(now[field]);
}

/** What the person sees after the merge, before deciding whether to save again. */
export function describeEditConflict(conflicts: EditConflict[]): string {
  const intro = "This lead changed while you were editing, and the newer details have been loaded into the form";
  if (conflicts.length === 0)
    return `${intro}. None of them clash with your changes. Review the form and save again.`;
  return `${intro}. The fields below were also changed and still show your values. Saving again replaces the newer values with yours.`;
}

export type EditSaveOutcome =
  | { saved: true }
  | { saved: false; base: Lead; form: EditForm; conflicts: EditConflict[] };

/**
 * Saves an edit against the version it was opened on. A refusal only means the lead's version
 * moved — which every call, comment, follow-up, visit or alert also does — so the lead is
 * reloaded: when none of the details on the form changed, the same form is saved once more
 * against the newer version; otherwise the merge is handed back for the person to review.
 */
export async function saveLeadEdit(
  base: Lead,
  form: EditForm,
  put: (concurrencyToken: string) => Promise<unknown>,
  reload: () => Promise<Lead>,
): Promise<EditSaveOutcome> {
  if (await savedOrConflict(() => put(base.concurrencyToken))) return { saved: true };

  let latest = await reload();
  let merged = mergeLeadEdit(base, latest, form);
  if (!merged.detailsChanged) {
    if (await savedOrConflict(() => put(latest.concurrencyToken))) return { saved: true };
    latest = await reload();
    merged = mergeLeadEdit(base, latest, form);
  }

  return { saved: false, base: latest, form: merged.form, conflicts: merged.conflicts };
}

async function savedOrConflict(save: () => Promise<unknown>) {
  try {
    await save();
    return true;
  } catch (caught) {
    if (caught instanceof LeadConflictError) return false;
    throw caught;
  }
}
