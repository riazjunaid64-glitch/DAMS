import { describe, expect, it } from "vitest";
import { initialForm } from "./leadActionDefaults.ts";
import { LeadConflictError } from "./leadApi.ts";
import { describeEditConflict, EDIT_LABELS, mergeLeadEdit, saveLeadEdit, type EditForm } from "./leadEditMerge.ts";
import type { Lead } from "./types.ts";

const opened = {
  id: 7, firstName: "Ayesha", lastName: "Khan", phone: "03001234567", email: "ayesha@example.com",
  city: null, preferredContactMethod: "Phone", purchaseIntent: "Unknown", sourceDetails: "Walk-in",
  interestedProjectId: 1, interestedProjectName: "Skyline", interestedUnitId: 10, interestedUnitNumber: "A-10",
  budgetMax: null, notes: "Prefers evenings", concurrencyToken: "v1",
} as Lead;

const openForm = () => initialForm({ type: "edit" }, opened);

// A repeat enquiry appends on a new line, exactly as LeadService.Append does.
const metaNote = "Meta lead form: wants a 3-bed corner unit near the park, budget flexible, call after 6pm";
const enriched = {
  ...opened, city: "Islamabad", budgetMax: 12_000_000, concurrencyToken: "v2",
  sourceDetails: "Walk-in\nMeta: Spring form", notes: `Prefers evenings\n${metaNote}`,
} as Lead;

describe("merging an edit form after the lead changed underneath it", () => {
  it("keeps an external enrichment the form never touched, instead of saving the old values over it", () => {
    const { form, conflicts, detailsChanged } = mergeLeadEdit(opened, enriched, { ...openForm(), phone: "03009998888" });

    expect(form.city).toBe("Islamabad");
    expect(form.budgetMax).toBe("12000000");
    expect(form.sourceDetails).toBe("Walk-in\nMeta: Spring form");
    expect(form.notes).toBe(`Prefers evenings\n${metaNote}`);
    expect(form.phone).toBe("03009998888");
    expect(conflicts).toEqual([]);
    expect(detailsChanged).toBe(true);
  });

  it("re-appends an enquiry's note after the person's own edit to the notes, so it cannot be wiped unseen", () => {
    const mine = { ...openForm(), notes: "Prefers evenings. Visited site on Monday." };

    const { form, conflicts } = mergeLeadEdit(opened, enriched, mine);

    expect(form.notes).toBe(`Prefers evenings. Visited site on Monday.\n${metaNote}`);
    expect(conflicts).toEqual([]);
  });

  it("appends an enquiry's note to a note the person started on a lead that had none", () => {
    const blank = { ...opened, notes: null } as Lead;
    const latest = { ...blank, notes: metaNote, concurrencyToken: "v2" } as Lead;
    const mine = { ...initialForm({ type: "edit" }, blank), notes: "Called back" };

    expect(mergeLeadEdit(blank, latest, mine).form.notes).toBe(`Called back\n${metaNote}`);
  });

  it("reports the whole newer note when it was rewritten rather than appended to", () => {
    const rewritten = { ...opened, notes: `Owner changed their mind. ${metaNote}`, concurrencyToken: "v2" } as Lead;
    const mine = { ...openForm(), notes: "Mine" };

    const { form, conflicts } = mergeLeadEdit(opened, rewritten, mine);

    expect(form.notes).toBe("Mine");
    expect(conflicts).toEqual([{ label: "Notes", theirs: `Owner changed their mind. ${metaNote}` }]);
  });

  it("reports instead of re-appending when the result would no longer fit the field", () => {
    const mine = { ...openForm(), notes: "x".repeat(1990) };

    const { form, conflicts } = mergeLeadEdit(opened, enriched, mine);

    expect(form.notes).toBe("x".repeat(1990));
    expect(conflicts.map((c) => c.label)).toEqual(["Notes"]);
  });

  it("keeps the person's value where both sides changed a field, and reports it", () => {
    const theirs = { ...opened, email: "new@example.com", concurrencyToken: "v2" } as Lead;
    const mine = { ...openForm(), email: "mine@example.com" };

    const { form, conflicts } = mergeLeadEdit(opened, theirs, mine);

    expect(form.email).toBe("mine@example.com");
    expect(conflicts).toEqual([{ label: "Email", theirs: "new@example.com" }]);
  });

  it("does not report a field both sides changed to the same value", () => {
    const theirs = { ...opened, city: "Lahore", concurrencyToken: "v2" } as Lead;
    const { conflicts } = mergeLeadEdit(opened, theirs, { ...openForm(), city: "Lahore" });

    expect(conflicts).toEqual([]);
  });

  it("treats project and unit as one choice, so a newer project is never paired with this form's unit", () => {
    const theirs = { ...opened, interestedProjectId: 2, interestedProjectName: "Harbour", interestedUnitId: null, interestedUnitNumber: null, concurrencyToken: "v2" } as Lead;
    const mine = { ...openForm(), interestedUnitId: "11" };

    const { form, conflicts } = mergeLeadEdit(opened, theirs, mine);

    expect(form.interestedProjectId).toBe("1");
    expect(form.interestedUnitId).toBe("11");
    expect(conflicts).toEqual([
      { label: "Interested project", theirs: "Harbour" },
      { label: "Interested unit", theirs: "" },
    ]);
  });

  it("says nothing on the form changed when only the lead's activity moved its version", () => {
    const activityOnly = { ...opened, lastActivitySummary: "Follow-up missed", concurrencyToken: "v2" } as Lead;

    expect(mergeLeadEdit(opened, activityOnly, openForm()).detailsChanged).toBe(false);
  });

  it("has a label for every field on the edit form", () => {
    expect(Object.keys(EDIT_LABELS).sort()).toEqual(Object.keys(openForm()).sort());
  });

  it("does not blame another person, since an enquiry or the alert scan may have changed the lead", () => {
    expect(describeEditConflict([])).toContain("None of them clash");
    expect(describeEditConflict([{ label: "Email", theirs: "x" }])).toContain("Saving again replaces the newer values with yours.");
    expect(describeEditConflict([])).not.toContain("Someone else");
  });
});

describe("saving an edit", () => {
  const conflict = () => new LeadConflictError("This lead changed while you were working on it. Reload and try again.");

  // A fake PUT that answers each call from a script, recording the version it was sent.
  function server(answers: ("ok" | "conflict" | Error)[], reloads: Lead[]) {
    const sent: string[] = [];
    const put = async (token: string) => {
      sent.push(token);
      const answer = answers.shift();
      if (answer === "conflict") throw conflict();
      if (answer instanceof Error) throw answer;
    };
    const reload = async () => reloads.shift()!;
    return { sent, put, reload };
  }

  const mine: EditForm = { ...openForm(), city: "Lahore" };

  it("saves against the version the form was opened on", async () => {
    const api = server(["ok"], []);

    expect(await saveLeadEdit(opened, mine, api.put, api.reload)).toEqual({ saved: true });
    expect(api.sent).toEqual(["v1"]);
  });

  it("saves again once against the newer version when only activity changed the lead", async () => {
    const activityOnly = { ...opened, lastActivitySummary: "Call logged", concurrencyToken: "v2" } as Lead;
    const api = server(["conflict", "ok"], [activityOnly]);

    expect(await saveLeadEdit(opened, mine, api.put, api.reload)).toEqual({ saved: true });
    expect(api.sent).toEqual(["v1", "v2"]);
  });

  it("hands the merge back for review, without saving, when details on the form changed", async () => {
    const api = server(["conflict"], [enriched]);

    const outcome = await saveLeadEdit(opened, mine, api.put, api.reload);

    expect(api.sent).toEqual(["v1"]);
    expect(outcome).toMatchObject({ saved: false, base: enriched, conflicts: [{ label: "City", theirs: "Islamabad" }] });
  });

  it("stops after one automatic retry and merges whatever the lead has become", async () => {
    const activityOnly = { ...opened, lastActivitySummary: "Call logged", concurrencyToken: "v2" } as Lead;
    const api = server(["conflict", "conflict"], [activityOnly, enriched]);

    const outcome = await saveLeadEdit(opened, mine, api.put, api.reload);

    expect(api.sent).toEqual(["v1", "v2"]);
    expect(outcome).toMatchObject({ saved: false, base: enriched });
  });

  it("does not treat any other failure as a conflict", async () => {
    const api = server([new Error("That phone number is too short to be usable.")], []);

    await expect(saveLeadEdit(opened, mine, api.put, api.reload)).rejects.toThrow("too short");
    expect(api.sent).toEqual(["v1"]);
  });
});
