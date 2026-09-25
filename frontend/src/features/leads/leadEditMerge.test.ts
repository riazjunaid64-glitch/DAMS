import { describe, expect, it } from "vitest";
import { initialForm } from "./leadActionDefaults.ts";
import { describeEditConflict, mergeLeadEdit } from "./leadEditMerge.ts";
import type { Lead } from "./types.ts";

const opened = {
  id: 7, firstName: "Ayesha", lastName: "Khan", phone: "03001234567", email: "ayesha@example.com",
  city: null, preferredContactMethod: "Phone", purchaseIntent: "Unknown", sourceDetails: "Walk-in",
  interestedProjectId: 1, interestedProjectName: "Skyline", interestedUnitId: 10, interestedUnitNumber: "A-10",
  budgetMax: null, notes: null, concurrencyToken: "v1",
} as Lead;

const openForm = () => initialForm({ type: "edit" }, opened);

describe("merging an edit form after the lead changed underneath it", () => {
  it("keeps an external enrichment the form never touched, instead of saving the old blanks over it", () => {
    const enriched = { ...opened, city: "Islamabad", budgetMax: 12_000_000, sourceDetails: "Walk-in | Meta: Spring form", concurrencyToken: "v2" };
    const mine = { ...openForm(), notes: "Called back" };

    const { form, conflicts } = mergeLeadEdit(opened, enriched, mine);

    expect(form.city).toBe("Islamabad");
    expect(form.budgetMax).toBe("12000000");
    expect(form.sourceDetails).toBe("Walk-in | Meta: Spring form");
    expect(form.notes).toBe("Called back");
    expect(conflicts).toEqual([]);
  });

  it("keeps the person's value where both sides changed a field, and reports it", () => {
    const theirs = { ...opened, email: "new@example.com", concurrencyToken: "v2" };
    const mine = { ...openForm(), email: "mine@example.com" };

    const { form, conflicts } = mergeLeadEdit(opened, theirs, mine);

    expect(form.email).toBe("mine@example.com");
    expect(conflicts).toEqual([{ label: "Email", theirs: "new@example.com" }]);
  });

  it("does not report a field both sides changed to the same value", () => {
    const theirs = { ...opened, city: "Lahore", concurrencyToken: "v2" };
    const { conflicts } = mergeLeadEdit(opened, theirs, { ...openForm(), city: "Lahore" });

    expect(conflicts).toEqual([]);
  });

  it("treats project and unit as one choice, so a newer project is never paired with this form's unit", () => {
    const theirs = { ...opened, interestedProjectId: 2, interestedProjectName: "Harbour", interestedUnitId: null, interestedUnitNumber: null, concurrencyToken: "v2" };
    const mine = { ...openForm(), interestedUnitId: "11" };

    const { form, conflicts } = mergeLeadEdit(opened, theirs, mine);

    expect(form.interestedProjectId).toBe("1");
    expect(form.interestedUnitId).toBe("11");
    expect(conflicts).toEqual([
      { label: "Interested project", theirs: "Harbour" },
      { label: "Interested unit", theirs: "" },
    ]);
  });

  it("explains a clean merge and a clash in words the person can act on", () => {
    expect(describeEditConflict([])).toContain("none of them clash");

    const message = describeEditConflict([{ label: "Email", theirs: "new@example.com" }, { label: "City", theirs: "" }]);
    expect(message).toContain('Email (now "new@example.com")');
    expect(message).toContain("City (now empty)");
    expect(message).toContain("Saving again replaces the newer values with yours.");
  });
});
