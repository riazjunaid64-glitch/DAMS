import { describe, expect, it } from "vitest";
import { describeConflict, describeDuplicate } from "./duplicateResolution.ts";

const hamza = { leadId: 437, leadReference: "LD-000437", leadName: "Hamza Iqbal", leadStageGroup: "New", leadOwnerName: "Sana Malik" };

describe("describeDuplicate", () => {
  it("names the lead the number belongs to: name, reference, status and owner", () => {
    expect(describeDuplicate({ ...hamza, matchedOn: "phone" })).toEqual({
      title: "This number already has a lead",
      choices: [{ leadId: 437, detail: "Hamza Iqbal · LD-000437 · In progress · Sana Malik" }],
    });
  });

  it.each([
    ["phone", "This number already has a lead"],
    ["whatsapp", "This number already has a lead"],
    ["email", "This email already has a lead"],
    [undefined, "These details already have a lead"],
    ["something-new", "These details already have a lead"],
  ])("titles a %s match as %s", (matchedOn, title) => {
    expect(describeDuplicate({ ...hamza, matchedOn }).title).toBe(title);
  });

  it("says Unassigned for a lead nobody owns, and shows a status only when one was sent", () => {
    expect(describeDuplicate({ ...hamza, leadOwnerName: null, leadStageGroup: "Dormant" }).choices[0]!.detail)
      .toBe("Hamza Iqbal · LD-000437 · Dormant · Unassigned");
    expect(describeDuplicate({ ...hamza, leadStageGroup: null }).choices[0]!.detail)
      .toBe("Hamza Iqbal · LD-000437 · Sana Malik");
  });

  it("offers nothing to open or add to when the API named no lead", () => {
    expect(describeDuplicate({ leadId: null, matchedOn: "email" }).choices).toEqual([]);
  });
});

describe("describeConflict", () => {
  it("lists every matched lead the same way", () => {
    const conflict = describeConflict([
      { ...hamza, matchedOn: "phone" },
      { leadId: 2, leadReference: "LD-000002", leadName: "Adeel Raza", leadStageGroup: "InProgress", leadOwnerName: null, matchedOn: "email" },
    ]);

    expect(conflict).toEqual({
      title: "These details match 2 leads",
      choices: [
        { leadId: 437, detail: "Hamza Iqbal · LD-000437 · In progress · Sana Malik" },
        { leadId: 2, detail: "Adeel Raza · LD-000002 · In progress · Unassigned" },
      ],
    });
  });

  it("leaves out a match without a lead", () => {
    expect(describeConflict([{ leadId: null }, { ...hamza }]).choices.map((c) => c.leadId)).toEqual([437]);
  });
});
