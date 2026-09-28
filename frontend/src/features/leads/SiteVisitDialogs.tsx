import { useId, useState } from "react";
import { ChoiceChips, Dropdown, Modal, RadioGroup, TextArea, TextField } from "../../components/ui";
import { formatAppointment } from "../../lib/dates.ts";
import { visitOutcomeLabel } from "./labels.ts";
import { apiJson, jsonRequest } from "./leadApi.ts";
import { DialogSummary, WhenPicker, type When } from "./LeadDialogParts.tsx";
import { REMINDERS, reminderAt, visitDayChoices, whenValue } from "./leadPage.ts";
import type { Lead, SiteVisit } from "./types.ts";
import { useLeadSave } from "./useLeadSave.ts";

type DialogProps = { onClose: () => void; onSaved: () => void };

const ELSEWHERE = "elsewhere";
const PLACES = [
  { value: "Floria Heights — site office", label: "Floria Heights — site office" },
  { value: "Deen Associates — head office", label: "Deen Associates — head office" },
  { value: ELSEWHERE, label: "Somewhere else" },
];

/** Schedule a site visit: when, where, who meets them and when to remind. */
export function ScheduleVisitDialog({ lead, workers, onClose, onSaved }: DialogProps & {
  lead: Lead;
  workers: { options: { value: string; label: string }[]; chosen: string };
}) {
  const formId = useId();
  const { saving, run } = useLeadSave(onSaved);
  // The coming Saturday by default, when most visits happen; tomorrow if Saturday is not offered.
  const [when, setWhen] = useState<When>(() => {
    const days = visitDayChoices();
    return { date: (days.find((day) => day.label.startsWith("Sat")) ?? days[0]).value, time: "11:00" };
  });
  const [place, setPlace] = useState(PLACES[0].value);
  const [otherPlace, setOtherPlace] = useState("");
  const [meeter, setMeeter] = useState(workers.chosen);
  const [reminder, setReminder] = useState("");
  const [notes, setNotes] = useState("");
  const chosen = meeter || workers.chosen;

  const scheduledAt = whenValue(when);
  const location = place === ELSEWHERE ? otherPlace.trim() : place;
  const ready = !!scheduledAt && location.length >= 3;

  const save = () => void run(
    () => apiJson(`/api/leads/${lead.id}/site-visits`, jsonRequest("POST", {
      scheduledAt,
      meetingLocation: location,
      assignedEmployeeId: chosen ? Number(chosen) : null,
      remindAt: scheduledAt ? reminderAt(scheduledAt, reminder) : null,
      notes: notes.trim() || null,
      projectId: lead.interestedProjectId ?? null,
    })),
    "Site visit scheduled",
  );

  return (
    <Modal
      open
      onClose={onClose}
      title="Schedule site visit"
      size="lg"
      phoneLayout="fullscreen"
      busy={saving}
      primaryAction={{ label: "Schedule visit", form: formId, loading: saving, disabled: !ready }}
    >
      <form id={formId} noValidate onSubmit={(event) => { event.preventDefault(); if (ready) save(); }} className="flex flex-col gap-5">
        <DialogSummary title={lead.fullName} detail={[lead.interestedProjectName, lead.propertyType, lead.phone].filter(Boolean).join(" · ")} />
        <WhenPicker label="When?" required quick="visit" value={when} onChange={setWhen} />
        <div className="flex flex-col gap-2.5">
          <RadioGroup label="Where?" required options={PLACES} value={place} onChange={setPlace} />
          {place === ELSEWHERE && (
            <TextField aria-label="Where they will meet" placeholder="e.g. Customer's home, DHA Phase 2" value={otherPlace} onChange={(e) => setOtherPlace(e.target.value)} />
          )}
        </div>
        <Dropdown label="Who will meet them?" options={workers.options} value={chosen} onChange={setMeeter} placeholder="Loading…" />
        <Dropdown label="Reminder" options={REMINDERS} value={reminder} onChange={setReminder} />
        <TextArea label="Notes" placeholder="Optional — e.g. coming with family, wants to see 2-bed on 5th floor" value={notes} onChange={(e) => setNotes(e.target.value)} />
      </form>
    </Modal>
  );
}

const OUTCOMES = ["ReadyToBook", "VeryInterested", "Interested", "Undecided", "WantsAnotherOption", "NotInterested"]
  .map((value) => ({ value, label: visitOutcomeLabel(value).label }));

/** Visit done: how it went and the next step. */
export function VisitDoneDialog({ item, onClose, onSaved }: DialogProps & { item: SiteVisit }) {
  const formId = useId();
  const { saving, run } = useLeadSave(onSaved);
  const [outcome, setOutcome] = useState("");
  const [nextStep, setNextStep] = useState("");
  const [notes, setNotes] = useState("");
  const ready = !!outcome && nextStep.trim().length >= 3;

  const save = () => void run(
    () => apiJson(`/api/leads/site-visits/${item.id}/complete`, jsonRequest("POST", {
      outcome,
      nextAction: nextStep.trim(),
      outcomeNotes: notes.trim() || null,
    })),
    "Site visit saved",
  );

  return (
    <Modal
      open
      onClose={onClose}
      title="Visit done"
      size="lg"
      phoneLayout="fullscreen"
      busy={saving}
      primaryAction={{ label: "Save", form: formId, loading: saving, disabled: !ready }}
    >
      <form id={formId} noValidate onSubmit={(event) => { event.preventDefault(); if (ready) save(); }} className="flex flex-col gap-5">
        <DialogSummary title="Site visit" detail={`${formatAppointment(item.scheduledAt)} · ${item.meetingLocation}`} />
        <ChoiceChips label="How did it go?" required options={OUTCOMES} value={outcome} onChange={setOutcome} />
        <TextField label="Next step" required placeholder="e.g. Send payment plan on WhatsApp" value={nextStep} onChange={(e) => setNextStep(e.target.value)} />
        <TextArea label="Notes" placeholder="Optional" value={notes} onChange={(e) => setNotes(e.target.value)} />
      </form>
    </Modal>
  );
}
