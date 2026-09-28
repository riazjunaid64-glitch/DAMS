import { describe, expect, it } from "vitest";
import {
  completionDateError,
  fieldForServerMessage,
  photosUploadedMessage,
  projectDraftChanged,
  projectDraftReady,
  unitDraftChanged,
  unitDraftReady,
  unitSavedMessage,
  type ProjectDraft,
  type UnitDraft,
} from "./editRules.ts";

const unit: UnitDraft = {
  unitNumber: "810",
  unitType: "2 Bed",
  floorNumber: "8",
  size: "1240",
  price: "14255985",
};

const saved = { unitNumber: "810", unitType: "2 Bed", floorNumber: 8, size: 1240, price: 14255985 };

const project: ProjectDraft = {
  name: "Floria Heights",
  location: "Lahore",
  category: "Residential",
  start: "2026-01-01",
  completion: "2028-06-01",
  about: "A tower",
  status: "Ongoing",
};

describe("unit edit rules", () => {
  it("stays disabled until something changed and every field is valid", () => {
    expect(unitDraftReady(unit)).toBe(true);
    expect(unitDraftChanged(unit, saved)).toBe(false);
    expect(unitDraftReady({ ...unit, price: "0" })).toBe(false);
    expect(unitDraftReady({ ...unit, size: "" })).toBe(false);
    expect(unitDraftReady({ ...unit, unitNumber: "  " })).toBe(false);
    expect(unitDraftChanged({ ...unit, price: "15000000" }, saved)).toBe(true);
    expect(unitDraftChanged({ ...unit, unitNumber: " 811 " }, saved)).toBe(true);
  });

  it("ignores a status the form is not allowed to edit", () => {
    expect(unitDraftChanged(unit, saved)).toBe(false);
  });

  it("puts a duplicate unit number under the field and names the toast", () => {
    expect(fieldForServerMessage('Unit number "810" already exists in this project.')).toBe("unitNumber");
    expect(unitSavedMessage("810", false)).toBe("Unit 810 updated");
    expect(unitSavedMessage("810", true)).toBe("Unit 810 added");
  });
});

describe("project edit rules", () => {
  it("requires a name and a location, and a completion date that is not before the start", () => {
    expect(projectDraftReady(project)).toBe(true);
    expect(projectDraftReady({ ...project, name: " " })).toBe(false);
    expect(completionDateError("2026-05-01", "2026-04-01")).toBe("Completion date can't be before the start date.");
    expect(projectDraftReady({ ...project, completion: "2025-01-01" })).toBe(false);
    expect(completionDateError("", "2025-01-01")).toBeUndefined();
  });

  it("treats an unchanged edit as not worth saving, including a project status change", () => {
    expect(projectDraftChanged(project, project)).toBe(false);
    expect(projectDraftChanged({ ...project, name: " Floria Heights " }, project)).toBe(false);
    expect(projectDraftChanged({ ...project, status: "Completed" }, project)).toBe(true);
  });

  it("puts a duplicate project name under the field", () => {
    expect(fieldForServerMessage("Project name already exists.")).toBe("projectName");
    expect(fieldForServerMessage("Database error.")).toBeNull();
  });
});

describe("photo upload toast", () => {
  it("counts the photos", () => {
    expect(photosUploadedMessage(1)).toBe("1 photo uploaded");
    expect(photosUploadedMessage(3)).toBe("3 photos uploaded");
  });
});
