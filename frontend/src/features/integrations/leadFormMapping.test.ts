import { describe, expect, it } from "vitest";
import type { MetaResource } from "../leads/types.ts";
import {
  buildFormMappingRequest,
  draftFromMapping,
  formMappingSummary,
  initialFormDraft,
  isMappableForm,
  mappableQuestions,
  suggestOptionValue,
  suggestTarget,
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

describe("suggested answers for the Floria Heights form", () => {
  const questions = [
    {
      key: "are_you_interested_in_a_5-year_installment_plan?",
      label: "Are you interested in a 5-year installment plan?",
      options: [
        { key: "yes", value: "Yes" },
        { key: "need_more_details", value: "Need more details" },
        { key: "no_(_on_cash)", value: "No ( On Cash)" },
      ],
    },
    {
      key: "which_apartment_type_are_you_interested_in?",
      label: "Which apartment type are you interested in?",
      options: [
        { key: "studio_apartment", value: "Studio Apartment" },
        { key: "1_bedroom_apartment", value: "1 Bedroom Apartment" },
        { key: "2_bedroom_apartment", value: "2 Bedroom Apartment" },
        { key: "3_bedroom_apartment", value: "3 Bedroom Apartment" },
      ],
    },
    {
      key: "are_you_buying_for_?",
      label: "Are you buying for ?",
      options: [
        { key: "investment", value: "Investment" },
        { key: "personal_living", value: "Personal Living" },
      ],
    },
  ];

  it("picks the lead field and the fixed value the answer text already says", () => {
    expect(suggestTarget(questions[0])).toBe("PaymentPreference");
    expect(suggestTarget(questions[1])).toBe("PropertyType");
    expect(suggestTarget(questions[2])).toBe("PurchaseIntent");
    expect(suggestOptionValue("PaymentPreference", questions[0].options[0])).toBe("Installments");
    expect(suggestOptionValue("PaymentPreference", questions[0].options[1])).toBe("NeedsDetails");
    expect(suggestOptionValue("PaymentPreference", questions[0].options[2])).toBe("Cash");
    expect(suggestOptionValue("PropertyType", questions[1].options[1])).toBe("1 Bed");
    expect(suggestOptionValue("PropertyType", questions[1].options[2])).toBe("2 Bed");
    expect(suggestOptionValue("PurchaseIntent", questions[2].options[1])).toBe("SelfUse");
  });

  it("opens a new form on those suggestions and on Floria Heights", () => {
    const mapping: LeadFormMapping = {
      formExternalId: "form-1",
      questions: [...questions, { key: "full_name", label: "Full name", options: [] }],
      answers: [],
    };
    const draft = initialFormDraft(mapping, [{ id: 4, name: "Floria Heights" }, { id: 5, name: "Other" }]);
    expect(draft.projectId).toBe("4");
    expect(draft.questions[questions[1].key]).toEqual({
      target: "PropertyType",
      values: {
        studio_apartment: "Studio",
        "1_bedroom_apartment": "1 Bed",
        "2_bedroom_apartment": "2 Bed",
        "3_bedroom_apartment": "3 Bed",
      },
    });
  });

  it("keeps a saved mapping instead of overwriting it with a suggestion", () => {
    const mapping = floria({
      interestedProjectId: 9,
      answers: [{
        questionKey: "are_you_buying_for_?",
        target: "PurchaseIntent",
        options: [{ optionKey: "investment", optionLabel: "Investment", value: "Investment" }],
      }],
    });
    const draft = initialFormDraft(mapping, [{ id: 4, name: "Floria Heights" }]);
    expect(draft.projectId).toBe("9");
    expect(draft.questions["are_you_buying_for_?"].values).toEqual({ investment: "Investment" });
  });
});
