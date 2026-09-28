import { useEffect, useState } from "react";
import { Button, Dropdown, IconChevronRight, Modal, useToast } from "../../components/ui";
import { loadProjects } from "../leads/leadApi.ts";
import type { ProjectLookup } from "../leads/types.ts";
import { getLeadFormMapping, saveLeadFormMapping, syncMetaConnection } from "./metaIntegrationApi.ts";
import {
  answerTargetLabels,
  answerTargetValues,
  buildFormMappingRequest,
  initialFormDraft,
  mappableQuestions,
  questionText,
  withOptionValue,
  withQuestionTarget,
  type FormMappingDraft,
} from "./metaIntegrationState.ts";
import type { LeadFormAnswerTarget, LeadFormMapping, LeadFormQuestion, MetaResource } from "./types.ts";

const TARGETS = [
  { value: "", label: "Don't save" },
  ...(Object.keys(answerTargetLabels) as LeadFormAnswerTarget[]).map((value) => ({ value, label: answerTargetLabels[value] })),
];

/**
 * Sets how a Facebook form's multiple-choice answers are saved on new leads.
 * A saved mapping is kept; obvious answers are pre-selected when nothing is saved yet.
 */
export default function LeadFormMappingDialog({ form, connectionId, onClose, onSaved }: {
  form: MetaResource;
  connectionId: number;
  onClose: () => void;
  onSaved: () => void;
}) {
  const toast = useToast();
  const [mapping, setMapping] = useState<LeadFormMapping | null>(null);
  const [projects, setProjects] = useState<ProjectLookup[]>([]);
  const [draft, setDraft] = useState<FormMappingDraft>({ projectId: "", questions: {} });
  const [saving, setSaving] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);

  const apply = (loaded: LeadFormMapping, projectList: ProjectLookup[]) => {
    setMapping(loaded);
    setProjects(projectList);
    setDraft(initialFormDraft(loaded, projectList));
  };

  useEffect(() => {
    let current = true;
    setLoadError(null);
    Promise.all([getLeadFormMapping(form.externalId), loadProjects()])
      .then(([loaded, projectList]) => { if (current) apply(loaded, projectList); })
      .catch((caught: unknown) => {
        if (!current) return;
        const message = caught instanceof Error ? caught.message : "This form's answers could not be loaded.";
        setLoadError(message);
        toast.error(message);
      });
    return () => { current = false; };
  }, [form.externalId, toast]);

  const refresh = async () => {
    setRefreshing(true);
    try {
      const result = await syncMetaConnection(connectionId);
      const loaded = await getLeadFormMapping(form.externalId);
      apply(loaded, projects);
      if (result.warning) toast.error(result.warning);
      else toast.success("Forms refreshed");
    } catch (caught) {
      toast.error(caught instanceof Error ? caught.message : "Forms could not be refreshed.");
    } finally {
      setRefreshing(false);
    }
  };

  const save = async () => {
    if (!mapping) return;
    setSaving(true);
    try {
      await saveLeadFormMapping(form.externalId, buildFormMappingRequest(mapping, draft));
      toast.success("Answers saved");
      onSaved();
    } catch (caught) {
      const message = caught instanceof Error ? caught.message : "The answers could not be saved.";
      toast.error(message);
      if (/reload/i.test(message)) {
        try {
          const loaded = await getLeadFormMapping(form.externalId);
          apply(loaded, projects);
        } catch {
          // The toast already says to reload; closing is still available.
        }
      }
      setSaving(false);
    }
  };

  const questions = mapping ? mappableQuestions(mapping) : [];
  const unread = mapping !== null && mapping.questions.length === 0;
  const subtitle = `${form.name ?? "Lead form"} · applies to new leads`;

  return (
    <Modal
      open
      onClose={onClose}
      size="lg"
      phoneLayout="fullscreen"
      busy={saving}
      title={<span className="block">Set up answers<span className="mt-1 block truncate text-small font-bold text-ink-muted">{subtitle}</span></span>}
      primaryAction={{ label: "Save", onClick: () => void save(), disabled: !mapping || unread, loading: saving }}
    >
      {!mapping ? (
        loadError
          ? <p className="m-0 text-sm font-bold text-danger">{loadError}</p>
          : <div className="h-16 animate-pulse rounded-card bg-track" />
      ) : unread ? (
        <div className="flex flex-col items-start gap-3">
          <p className="m-0 text-sm font-bold text-ink">Refresh forms first</p>
          <Button variant="outline" loading={refreshing} onClick={() => void refresh()}>Refresh</Button>
        </div>
      ) : (
        <div className="flex flex-col gap-4">
          <Dropdown
            label="Project"
            value={draft.projectId}
            onChange={(value) => setDraft({ ...draft, projectId: value })}
            options={projects.map((project) => ({ value: String(project.id), label: project.name }))}
            placeholder="Select a project"
          />
          {questions.map((question) => (
            <QuestionCard
              key={question.key}
              question={question}
              target={draft.questions[question.key]?.target ?? ""}
              values={draft.questions[question.key]?.values ?? {}}
              onTarget={(target) => setDraft(withQuestionTarget(draft, question.key, target, question.options))}
              onValue={(optionKey, value) => setDraft(withOptionValue(draft, question.key, optionKey, value))}
            />
          ))}
        </div>
      )}
    </Modal>
  );
}

function QuestionCard({ question, target, values, onTarget, onValue }: {
  question: LeadFormQuestion;
  target: LeadFormAnswerTarget | "";
  values: Record<string, string>;
  onTarget: (target: LeadFormAnswerTarget | "") => void;
  onValue: (optionKey: string, value: string) => void;
}) {
  const name = questionText(question);
  return (
    <div className="rounded-card border border-line p-4">
      <p className="m-0 text-body font-extrabold text-ink">{name}</p>
      <div className="mt-3 max-w-sm">
        <Dropdown label="Save answer as" value={target} onChange={(value) => onTarget(value as LeadFormAnswerTarget | "")} options={TARGETS} />
      </div>
      {target && (
        <div className="mt-3 flex flex-col gap-2">
          {question.options.map((option) => {
            const optionName = option.value || option.key;
            return (
              <div key={option.key} className="grid grid-cols-[minmax(0,1fr)_auto_minmax(8rem,1fr)] items-center gap-2">
                <p className="m-0 text-sm font-bold text-ink">{optionName}</p>
                <IconChevronRight size={16} className="text-ink-faint" />
                <Dropdown
                  aria-label={`${answerTargetLabels[target]} for ${optionName}`}
                  value={values[option.key] ?? ""}
                  onChange={(value) => onValue(option.key, value)}
                  options={[{ value: "", label: "Don't save" }, ...answerTargetValues[target]]}
                />
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
