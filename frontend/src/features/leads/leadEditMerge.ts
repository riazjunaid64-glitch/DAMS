import { initialForm } from "./leadActionDefaults.ts";
import type { Lead } from "./types.ts";

type EditForm = Record<string, string | boolean>;

export type EditConflict = { label: string; theirs: string };

const LABELS: Record<string, string> = {
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
  notes: "Notes",
};

// A unit only means something within its project, so the two are merged as one choice:
// taking a newer project while keeping this form's unit could pair a unit with the wrong project.
const GROUPS: string[][] = [
  ["interestedProjectId", "interestedUnitId"],
  ...Object.keys(LABELS).filter((field) => !field.startsWith("interested")).map((field) => [field]),
];

/**
 * Merges an edit form whose save was refused because the lead changed after the form opened.
 * `base` is the lead the form was opened from and `latest` the lead as it is now.
 *
 * A field left untouched in the form takes the newer value, so saving again cannot revert
 * someone else's change or an external enquiry's enrichment. A field changed only in the form
 * keeps the form's value. A field changed on both sides to different values keeps the form's
 * value and is reported, so replacing the other change is a decision made with it in view.
 */
export function mergeLeadEdit(base: Lead, latest: Lead, mine: EditForm): { form: EditForm; conflicts: EditConflict[] } {
  const was = initialForm({ type: "edit" }, base);
  const now = initialForm({ type: "edit" }, latest);
  const form: EditForm = { ...mine };
  const conflicts: EditConflict[] = [];

  for (const group of GROUPS) {
    const differs = (a: EditForm, b: EditForm) => group.some((field) => a[field] !== b[field]);
    if (!differs(mine, was)) {
      for (const field of group) form[field] = now[field];
    } else if (differs(now, was) && differs(now, mine)) {
      for (const field of group) conflicts.push({ label: LABELS[field], theirs: shown(field, latest, now) });
    }
  }

  return { form, conflicts };
}

// The form holds ids for the project and unit; the person needs the names.
function shown(field: string, latest: Lead, now: EditForm) {
  if (field === "interestedProjectId") return latest.interestedProjectName ?? "";
  if (field === "interestedUnitId") return latest.interestedUnitNumber ?? "";
  return String(now[field]);
}

/** What the person sees after the merge, before deciding whether to save again. */
export function describeEditConflict(conflicts: EditConflict[]): string {
  const intro = "Someone else updated this lead while you were editing. The newer details have been loaded into the form";
  if (conflicts.length === 0)
    return `${intro} and none of them clash with your changes. Review the form and save again.`;

  const fields = conflicts
    .map(({ label, theirs }) => `${label} (now ${theirs ? `"${theirs.length > 60 ? `${theirs.slice(0, 60)}…` : theirs}"` : "empty"})`)
    .join(", ");
  return `${intro}, but these fields were also changed and still show your values: ${fields}. ` +
    "Saving again replaces the newer values with yours.";
}
