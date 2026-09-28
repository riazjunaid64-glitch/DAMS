import { LeadConflictError } from "./leadApi.ts";
import { paymentPreferenceLabel, purchaseIntentLabel } from "./labels.ts";
import type { Lead } from "./types.ts";

export type EditForm = Record<string, string>;

export type EditConflict = { label: string; theirs: string };

/** The fields the Edit details popup changes, with the names its labels use. */
export const EDIT_LABELS: Record<string, string> = {
  firstName: "First name",
  lastName: "Last name",
  phone: "Phone",
  whatsappNumber: "WhatsApp",
  city: "City",
  email: "Email",
  propertyType: "Apartment type",
  paymentPreference: "5-year installment plan",
  purchaseIntent: "Buying for",
  notes: "Notes",
};

/** A lead's editable details as the form holds them: text, with "" for nothing. */
export function editForm(lead: Lead): EditForm {
  return {
    firstName: lead.firstName,
    lastName: lead.lastName ?? "",
    phone: lead.phone ?? "",
    whatsappNumber: lead.whatsappNumber ?? "",
    city: lead.city ?? "",
    email: lead.email ?? "",
    propertyType: lead.propertyType ?? "",
    paymentPreference: lead.paymentPreference,
    purchaseIntent: lead.purchaseIntent,
    notes: lead.notes ?? "",
  };
}

/**
 * Only the fields the form changed from `base`, as the API takes them. The API leaves every
 * field it is not sent alone, so an edit can never overwrite anything the person did not touch.
 */
export function editChanges(base: Lead, form: EditForm): Record<string, string | null> {
  const was = editForm(base);
  const changes: Record<string, string | null> = {};
  for (const field of Object.keys(was)) {
    const value = form[field] ?? "";
    if (value.trim() === was[field].trim()) continue;
    changes[field] = field === "firstName" || field === "paymentPreference" || field === "purchaseIntent" ? value.trim() : value.trim() || null;
  }
  return changes;
}

// Fields a repeat enquiry appends to (LeadService.Append joins with a newline) rather than
// replaces, with their UpdateLeadDto length limits.
const APPENDED: Record<string, number> = { notes: 2000 };

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
  const was = editForm(base);
  const now = editForm(latest);
  const form: EditForm = { ...mine };
  const conflicts: EditConflict[] = [];

  // Every field the edit form has, so a field added to the form later is merged too.
  for (const field of Object.keys(was)) {
    if (mine[field] === was[field]) {
      form[field] = now[field];
      continue;
    }
    if (now[field] === was[field] || now[field] === mine[field]) continue;

    const rebased = field in APPENDED ? reapplyAppended(was[field], now[field], mine[field], APPENDED[field]) : null;
    if (rebased !== null) form[field] = rebased;
    else conflicts.push({ label: EDIT_LABELS[field] ?? field, theirs: shown(field, now[field]) });
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

// The form holds enum values for the requirement choices; the person needs the words.
function shown(field: string, value: string) {
  if (field === "paymentPreference") return paymentPreferenceLabel(value)?.label ?? "";
  if (field === "purchaseIntent") return purchaseIntentLabel(value) ?? "";
  return value;
}

/** What the person sees after the merge, before deciding whether to save again. */
export function describeEditConflict(conflicts: EditConflict[]): string {
  if (conflicts.length === 0)
    return "This lead changed while you were editing. The newer details are now in the form. None of them clash with your changes, so check the form and save again.";
  return "This lead changed while you were editing. These fields were also changed and still show your values. Saving again replaces the newer values with yours.";
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
