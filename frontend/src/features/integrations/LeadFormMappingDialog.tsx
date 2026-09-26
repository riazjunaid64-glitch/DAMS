import { useEffect, useState } from "react";
import AppSelect from "../../lib/AppSelect.tsx";
import Button from "../../lib/Button.tsx";
import { CrmModal, ErrorBanner, inputClass, Label } from "../leads/CrmUi.tsx";
import { loadProjects } from "../leads/leadApi.ts";
import { enumLabel, type ProjectLookup } from "../leads/types.ts";
import { getLeadFormMapping, saveLeadFormMapping } from "./metaIntegrationApi.ts";
import {
  answerTargetLabels,
  answerTargetValues,
  buildFormMappingRequest,
  draftFromMapping,
  mappableQuestions,
  questionText,
  withOptionValue,
  withQuestionTarget,
  type FormMappingDraft,
} from "./metaIntegrationState.ts";
import type { LeadFormAnswerTarget, LeadFormMapping, LeadFormQuestion, MetaResource } from "./types.ts";

/**
 * Links a Meta lead form to a project and maps its multiple-choice answers to lead fields.
 * Saved per form, not per connection, and applied only to leads that arrive afterwards.
 */
export default function LeadFormMappingDialog({ form, onClose, onSaved }: {
  form: MetaResource;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [mapping, setMapping] = useState<LeadFormMapping | null>(null);
  const [projects, setProjects] = useState<ProjectLookup[]>([]);
  const [draft, setDraft] = useState<FormMappingDraft>({ projectId: "", questions: {} });
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    let current = true;
    Promise.all([getLeadFormMapping(form.externalId), loadProjects()])
      .then(([loaded, projectList]) => {
        if (!current) return;
        setMapping(loaded);
        setDraft(draftFromMapping(loaded));
        setProjects(projectList);
      })
      .catch((caught: unknown) => {
        if (current) setError(caught instanceof Error ? caught.message : "This form's mapping could not be loaded.");
      });
    return () => {
      current = false;
    };
  }, [form.externalId]);

  const save = async () => {
    if (!mapping) return;
    setSaving(true);
    setError(null);
    try {
      await saveLeadFormMapping(form.externalId, buildFormMappingRequest(mapping, draft));
      onSaved();
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "The mapping could not be saved.");
    } finally {
      setSaving(false);
    }
  };

  const questions = mapping ? mappableQuestions(mapping) : [];

  return (
    <CrmModal
      open
      wide
      title={`Map lead form: ${form.name ?? form.externalId}`}
      subtitle="Applies to leads that arrive from now on. Leads already received are not changed."
      onClose={onClose}
      footer={
        <div className="flex justify-end gap-2">
          <Button variant="ghost" onClick={onClose}>Cancel</Button>
          <Button disabled={!mapping || saving} onClick={() => void save()}>{saving ? "Saving…" : "Save mapping"}</Button>
        </div>
      }
    >
      {error && <ErrorBanner message={error} />}
      {!mapping ? (
        !error && <p className="text-sm text-[var(--text-muted)]">Loading the form…</p>
      ) : (
        <div className="space-y-5">
          <div>
            <Label>Project</Label>
            <AppSelect aria-label="Project" className={inputClass} value={draft.projectId} onChange={(event) => setDraft({ ...draft, projectId: event.target.value })}>
              <option value="">No project</option>
              {projects.map((project) => <option key={project.id} value={String(project.id)}>{project.name}</option>)}
            </AppSelect>
          </div>

          {questions.length === 0 ? (
            <p className="text-sm text-[var(--text-muted)]">
              {mapping.questions.length === 0
                ? "DAMS has not read this form's questions yet. Run Sync now on its Meta connection, then come back to map its answers."
                : "This form has no multiple-choice questions to map."}
            </p>
          ) : questions.map((question) => (
            <QuestionMapping
              key={question.key}
              question={question}
              target={draft.questions[question.key]?.target ?? ""}
              values={draft.questions[question.key]?.values ?? {}}
              onTarget={(target) => setDraft(withQuestionTarget(draft, question.key, target))}
              onValue={(optionKey, value) => setDraft(withOptionValue(draft, question.key, optionKey, value))}
            />
          ))}
        </div>
      )}
    </CrmModal>
  );
}

function QuestionMapping({ question, target, values, onTarget, onValue }: {
  question: LeadFormQuestion;
  target: LeadFormAnswerTarget | "";
  values: Record<string, string>;
  onTarget: (target: LeadFormAnswerTarget | "") => void;
  onValue: (optionKey: string, value: string) => void;
}) {
  return (
    <div className="rounded-xl border border-[var(--border)] p-4">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
        <div className="min-w-0">
          <p className="text-sm font-medium text-[var(--text-primary)]">{questionText(question)}</p>
          <p className="truncate text-xs text-[var(--text-muted)]">{question.key}</p>
        </div>
        <div className="sm:w-56">
          <AppSelect aria-label={`Lead field for ${questionText(question)}`} className={inputClass} value={target} onChange={(event) => onTarget(event.target.value as LeadFormAnswerTarget | "")}>
            <option value="">Keep as an answer only</option>
            {(Object.keys(answerTargetLabels) as LeadFormAnswerTarget[]).map((value) => (
              <option key={value} value={value}>{answerTargetLabels[value]}</option>
            ))}
          </AppSelect>
        </div>
      </div>

      {target && (
        <div className="mt-3 grid gap-2">
          {question.options.map((option) => {
            const optionName = option.value || option.key;
            return (
              <div key={option.key} className="grid items-center gap-2 sm:grid-cols-2">
                <p className="text-sm text-[var(--text-secondary)]">{optionName}</p>
                {target === "PropertyType" ? (
                  <input
                    aria-label={`Property type for ${optionName}`}
                    className={inputClass}
                    maxLength={100}
                    placeholder="Leave empty to keep unmapped"
                    value={values[option.key] ?? ""}
                    onChange={(event) => onValue(option.key, event.target.value)}
                  />
                ) : (
                  <AppSelect aria-label={`${answerTargetLabels[target]} for ${optionName}`} className={inputClass} value={values[option.key] ?? ""} onChange={(event) => onValue(option.key, event.target.value)}>
                    <option value="">Leave unmapped</option>
                    {answerTargetValues[target].map((value) => <option key={value} value={value}>{enumLabel(value)}</option>)}
                  </AppSelect>
                )}
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
