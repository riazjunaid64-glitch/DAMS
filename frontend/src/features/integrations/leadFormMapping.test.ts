import { describe, expect, it } from "vitest";
import type { MetaResource } from "../leads/types.ts";
import {
  buildFormMappingRequest,
  draftFromMapping,
  formMappingSummary,
  isMappableForm,
  mappableQuestions,
  withOptionValue,
  withQuestionTarget,
} from "./metaIntegrationState.ts";
import type { LeadFormMapping } from "./types.ts";

// The Floria Heights form, as the form sync reads it from Meta.
const floria = (overrides: Partial<LeadFormMapping> = {}): LeadFormMapping => ({
  formExternalId: "1180848317322015",
  formName: "Untitled form 31/03/2026, 19:17",
  questions: [
    {
      key: "are_you_buying_for_?",
      label: "Are you buying for ?",
      type: "CUSTOM",
      options: [
        { key: "investment", value: "Investment" },
        { key: "personal_living", value: "Personal Living" },
      ],
    },
    { key: "full_name", label: "Full name", type: "FULL_NAME", options: [] },
  ],
  answers: [],
  ...overrides,
});

const form = (overrides: Partial<MetaResource> = {}): MetaResource => ({
  id: 9,
  resourceType: "lead_form",
  externalId: "1180848317322015",
  isEnabled: false,
  isActive: true,
  isSubscribed: false,
  ...overrides,
});

describe("lead form mapping", () => {
  it("offers mapping on lead forms only", () => {
    expect(isMappableForm(form())).toBe(true);
    expect(isMappableForm(form({ resourceType: "facebook_page" }))).toBe(false);
  });

  it("says which project a form is linked to", () => {
    expect(formMappingSummary(form())).toBe("Not linked to a project");
    expect(formMappingSummary(form({ hasFormMapping: true, formMappingProjectName: "Floria Heights" }))).toBe("Project: Floria Heights");
    expect(formMappingSummary(form({ hasFormMapping: true }))).toBe("Answers mapped · no project");
  });

  it("only lists questions that have options to map", () => {
    expect(mappableQuestions(floria()).map((q) => q.key)).toEqual(["are_you_buying_for_?"]);
  });

  it("round-trips a saved mapping through the draft", () => {
    const saved = floria({
      interestedProjectId: 4,
      version: "AAAAAAAAB9E=",
      answers: [{
        questionKey: "are_you_buying_for_?",
        target: "PurchaseIntent",
        options: [
          { optionKey: "investment", optionLabel: "Investment", value: "Investment" },
          { optionKey: "personal_living", optionLabel: "Personal Living", value: "SelfUse" },
        ],
      }],
    });

    expect(buildFormMappingRequest(saved, draftFromMapping(saved))).toEqual({
      interestedProjectId: 4,
      version: "AAAAAAAAB9E=",
      answers: saved.answers,
    });
  });

  it("leaves out questions with no field and options with no value", () => {
    const mapping = floria();
    let draft = draftFromMapping(mapping);
    draft = withQuestionTarget(draft, "are_you_buying_for_?", "PurchaseIntent");
    draft = withOptionValue(draft, "are_you_buying_for_?", "investment", "Investment");

    expect(buildFormMappingRequest(mapping, draft)).toEqual({
      interestedProjectId: null,
      version: null,
      answers: [{
        questionKey: "are_you_buying_for_?",
        target: "PurchaseIntent",
        options: [{ optionKey: "investment", optionLabel: "Investment", value: "Investment" }],
      }],
    });

    expect(buildFormMappingRequest(mapping, withQuestionTarget(draft, "are_you_buying_for_?", "")).answers).toEqual([]);
  });

  it("clears a question's values when its field changes", () => {
    let draft = withQuestionTarget(draftFromMapping(floria()), "are_you_buying_for_?", "PurchaseIntent");
    draft = withOptionValue(draft, "are_you_buying_for_?", "investment", "Investment");
    draft = withQuestionTarget(draft, "are_you_buying_for_?", "PropertyType");

    expect(draft.questions["are_you_buying_for_?"]).toEqual({ target: "PropertyType", values: {} });
  });
});
