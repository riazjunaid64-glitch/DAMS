/** Save rules for the project and unit forms (KAN-56). Pure, so the screens and the tests share them. */

export type UnitDraft = {
  unitNumber: string;
  unitType: string;
  floorNumber: string;
  size: string;
  price: string;
};

export type UnitBaseline = {
  unitNumber: string;
  unitType: string;
  floorNumber: number;
  size: number;
  price: number;
};

export type ProjectDraft = {
  name: string;
  location: string;
  category: string;
  start: string;
  completion: string;
  about: string;
  status: string;
};

/** A chosen floor, including ground ("0") and basements. Blank is not ground: Number("") is 0. */
function selectedFloor(value: string): number | null {
  const trimmed = value.trim();
  if (!/^-?\d+$/.test(trimmed)) return null;
  return Number(trimmed);
}

export function unitDraftReady(draft: UnitDraft): boolean {
  return draft.unitNumber.trim().length > 0
    && draft.unitType.length > 0
    && selectedFloor(draft.floorNumber) !== null
    && Number(draft.size) > 0
    && Number(draft.price) > 0;
}

/** True when a field the form is allowed to change differs from the unit on screen. Status is not one of them. */
export function unitDraftChanged(draft: UnitDraft, unit: UnitBaseline): boolean {
  const floor = selectedFloor(draft.floorNumber);
  return draft.unitNumber.trim() !== unit.unitNumber.trim()
    || draft.unitType !== unit.unitType
    || floor === null
    || floor !== unit.floorNumber
    || Number(draft.size) !== unit.size
    || Number(draft.price) !== unit.price;
}

export function completionDateError(start: string, completion: string): string | undefined {
  if (start && completion && completion < start) return "Completion date can't be before the start date.";
  return undefined;
}

export function projectDraftReady(draft: Pick<ProjectDraft, "name" | "location" | "start" | "completion">): boolean {
  return draft.name.trim().length > 0
    && draft.location.trim().length > 0
    && !completionDateError(draft.start, draft.completion);
}

export function projectDraftChanged(draft: ProjectDraft, baseline: ProjectDraft): boolean {
  const same = (left: string, right: string) => left.trim() === right.trim();
  return !same(draft.name, baseline.name)
    || !same(draft.location, baseline.location)
    || draft.category !== baseline.category
    || draft.start !== baseline.start
    || draft.completion !== baseline.completion
    || !same(draft.about, baseline.about)
    || draft.status !== baseline.status;
}

/** Which field should show the server's message. Anything else is a toast only. */
export function fieldForServerMessage(message: string): "unitNumber" | "projectName" | null {
  if (message.includes("already exists in this project")) return "unitNumber";
  if (message.includes("Project name already exists")) return "projectName";
  return null;
}

export function unitSavedMessage(unitNumber: string, created: boolean): string {
  return created ? `Unit ${unitNumber} added` : `Unit ${unitNumber} updated`;
}

export function photosUploadedMessage(count: number): string {
  return count === 1 ? "1 photo uploaded" : `${count} photos uploaded`;
}

/**
 * Bulk upload keeps the files that succeeded and skips the ones that failed.
 * The toast must count what was stored, and say so when some of the selection did not go up.
 */
export function photoUploadNotice(selected: number, uploaded: number): { success?: string; error?: string } {
  if (uploaded <= 0) {
    return { error: selected === 1 ? "The photo could not be uploaded." : "The photos could not be uploaded." };
  }
  const failed = Math.max(0, selected - uploaded);
  return {
    success: photosUploadedMessage(uploaded),
    ...(failed > 0
      ? { error: failed === 1 ? "1 photo could not be uploaded." : `${failed} photos could not be uploaded.` }
      : {}),
  };
}
