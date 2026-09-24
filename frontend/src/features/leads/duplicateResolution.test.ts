import { describe, expect, it } from "vitest";
import { describeConflict, describeDuplicate } from "./duplicateResolution.ts";

describe("describeDuplicate", () => {
  it("offers to add the enquiry to the matched lead, never to create a separate one", () => {
    const resolution = describeDuplicate({ leadId: 12, leadReference: "LD-000012", matchedOn: "phone" });

    expect(resolution.addLabel).toBe("Add this enquiry to LD-000012");
    expect(resolution.openLabel).toBe("Open LD-000012");
    // The resubmission enriches the existing lead, so no choice may read as creating one.
    for (const text of [resolution.addLabel, resolution.openLabel]) {
      expect(text).not.toMatch(/separate|new lead|create/i);
    }
  });

  it("explains what adding the enquiry changes and what it leaves alone", () => {
    const { explanation, addOutcome } = describeDuplicate({ leadId: 12, leadReference: "LD-000012" });

    expect(explanation).toMatch(/one open lead per person/);
    expect(explanation).toMatch(/separate lead cannot be created/);
    expect(addOutcome).toMatch(/source and notes are recorded on LD-000012/);
    expect(addOutcome).toMatch(/owner, stage and history do not change/);
    // Edits made after the match are what gets added, and a stale match adds nothing.
    expect(addOutcome).toMatch(/details in this form as they are now/);
    expect(addOutcome).toMatch(/no longer match it when you add, nothing is saved/);
  });

  it.each([
    ["phone", "phone number"],
    ["whatsapp", "WhatsApp number"],
    ["email", "email address"],
    [undefined, "contact details"],
    ["something-new", "contact details"],
  ])("names the matched channel %s as %s", (matchedOn, label) => {
    expect(describeDuplicate({ leadId: 1, leadReference: "LD-000001", matchedOn }).heading)
      .toBe(`An open lead (LD-000001) already uses this ${label}.`);
  });

  it("offers no add action when there is no lead to add to", () => {
    const resolution = describeDuplicate({ leadId: null, leadReference: null, matchedOn: "email" });

    expect(resolution.addLabel).toBeNull();
    expect(resolution.openLabel).toBe("Open the existing lead");
  });
});

describe("describeConflict", () => {
  const conflict = describeConflict([
    { leadId: 1, leadReference: "LD-000001", matchedOn: "phone" },
    { leadId: 2, leadReference: "LD-000002", matchedOn: "email" },
  ]);

  it("shows every matched lead with its own open and add choice", () => {
    expect(conflict.heading).toBe("These details match 2 different open leads.");
    expect(conflict.choices).toEqual([
      { leadId: 1, detail: "LD-000001 matches this phone number.", openLabel: "Open LD-000001", addLabel: "Add this enquiry to LD-000001" },
      { leadId: 2, detail: "LD-000002 matches this email address.", openLabel: "Open LD-000002", addLabel: "Add this enquiry to LD-000002" },
    ]);
  });

  it("says nothing was changed and that adding touches only the chosen lead", () => {
    expect(conflict.explanation).toMatch(/nothing has been changed/);
    expect(conflict.addOutcome).toMatch(/changes only the lead you choose/);
    for (const choice of conflict.choices) expect(choice.addLabel).not.toMatch(/separate|new lead|create/i);
  });

  it("offers no choice for a match without a lead", () => {
    expect(describeConflict([{ leadId: null, leadReference: null }, { leadId: 3, leadReference: "LD-000003" }]).choices)
      .toHaveLength(1);
  });
});
