import { useId, useState } from "react";
import { ChoiceChips, DateField, Dropdown, FieldShell, Modal, TextArea, TextField, TimeField } from "../../components/ui";
import { formatAppointment, formatWhen, karachiDateInput, toKarachiInputs } from "../../lib/dates.ts";
import { channelLabel, reachQuestion } from "./labels.ts";
import { apiJson, jsonRequest } from "./leadApi.ts";
import { DialogSummary, ScheduleNext, WhenPicker, type When } from "./LeadDialogParts.tsx";
import { whenValue } from "./leadPage.ts";
import type { FollowUp, Lead, SiteVisit } from "./types.ts";
import { useLeadSave } from "./useLeadSave.ts";

type DialogProps = { onClose: () => void; onSaved: () => void };

const CHANNELS = ["Phone", "Whatsapp", "OfficeVisit", "Email", "Other"].map((value) => ({ value, label: channelLabel(value) }));
const DIRECTIONS = [
  { value: "Outbound", label: "We contacted them" },
  { value: "Inbound", label: "They contacted us" },
];

/** A follow-up is due tomorrow at 4:00 PM unless someone says otherwise. */
const tomorrowAtFour = (): When => ({ date: karachiDateInput(1), time: "16:00" });

/**
 * Log communication. The answered / replied question is asked only for a call, WhatsApp or email
 * we made; every other contact reached them. A follow-up scheduled here is created by the server
 * with the communication, so it is never posted separately.
 */
export function LogCommunicationDialog({ lead, onClose, onSaved }: {
  lead: Lead;
  onClose: () => void;
  /** Told whether a follow-up was scheduled too, so the page refreshes follow-ups only then. */
  onSaved: (scheduledFollowUp: boolean) => void;
}) {
  const formId = useId();
  const [scheduleNext, setScheduleNext] = useState(false);
  const { saving, run } = useLeadSave(() => onSaved(scheduleNext));
  const [channel, setChannel] = useState("Phone");
  const [direction, setDirection] = useState("Outbound");
  const [reached, setReached] = useState("");
  const [summary, setSummary] = useState("");
  const [customerSaid, setCustomerSaid] = useState("");
  const [when, setWhen] = useState<When>(() => toKarachiInputs(new Date()));
  const [nextTitle, setNextTitle] = useState("");
  const [nextWhen, setNextWhen] = useState<When>(tomorrowAtFour);

  const question = reachQuestion(channel, direction);
  const connected = question === null || reached === "yes";
  const occurredAt = whenValue(when);
  const nextAt = whenValue(nextWhen);
  const ready = summary.trim().length >= 2 && (question === null || reached !== "") && !!occurredAt
    && (!scheduleNext || (nextTitle.trim().length >= 2 && !!nextAt));

  const save = () => void run(
    () => apiJson(`/api/leads/${lead.id}/communications`, jsonRequest("POST", {
      channel,
      direction,
      connected,
      summary: summary.trim(),
      customerResponse: connected ? customerSaid.trim() || null : null,
      occurredAt,
      nextAction: scheduleNext ? nextTitle.trim() : null,
      nextActionAt: scheduleNext ? nextAt : null,
    })),
    scheduleNext ? "Communication and follow-up saved" : "Communication saved",
  );

  return (
    <Modal
      open
      onClose={onClose}
      title="Log communication"
      size="lg"
      phoneLayout="fullscreen"
      busy={saving}
      primaryAction={{ label: "Save", form: formId, loading: saving, disabled: !ready }}
    >
      <form id={formId} noValidate onSubmit={(event) => { event.preventDefault(); if (ready) save(); }} className="flex flex-col gap-5">
        <Dropdown label="How did you talk?" required options={CHANNELS} value={channel} onChange={(value) => { setChannel(value); setReached(""); }} />
        <ChoiceChips label="Who contacted who?" required options={DIRECTIONS} value={direction} onChange={(value) => { setDirection(value); setReached(""); }} />
        {question && (
          <ChoiceChips
            label={question === "answer" ? "Did they answer?" : "Did they reply?"}
            required
            options={question === "answer"
              ? [{ value: "yes", label: "Answered" }, { value: "no", label: "No answer" }]
              : [{ value: "yes", label: "Replied" }, { value: "no", label: "No reply" }]}
            value={reached}
            onChange={setReached}
          />
        )}
        <TextArea label="What was discussed?" required placeholder="e.g. Shared 2-bed price list and payment plan" value={summary} onChange={(e) => setSummary(e.target.value)} />
        {connected && (
          <TextArea label="What did the customer say?" placeholder="Optional" value={customerSaid} onChange={(e) => setCustomerSaid(e.target.value)} />
        )}
        <WhenFields label="When" value={when} onChange={setWhen} />
        <ScheduleNext
          checked={scheduleNext}
          onCheck={setScheduleNext}
          title={nextTitle}
          onTitle={setNextTitle}
          when={nextWhen}
          onWhen={setNextWhen}
          placeholder="e.g. Call back about installments"
        />
      </form>
    </Modal>
  );
}

/** A plain date and time, with no quick picks: when something already happened. */
function WhenFields({ label, value, onChange }: { label: string; value: When; onChange: (value: When) => void }) {
  return (
    <FieldShell as="fieldset" label={label}>
      <div className="grid grid-cols-2 gap-2.5">
        <DateField aria-label="Date" max={karachiDateInput(0)} value={value.date} onChange={(e) => onChange({ ...value, date: e.target.value })} />
        <TimeField aria-label="Time" value={value.time} onChange={(e) => onChange({ ...value, time: e.target.value })} />
      </div>
    </FieldShell>
  );
}

const FOLLOW_UP_TYPES = ["Call", "Whatsapp", "Meeting", "Email", "Other"].map((value) => ({ value, label: value === "Whatsapp" ? "WhatsApp" : value }));
const PRIORITIES = [
  { value: "Medium", label: "Normal" },
  { value: "High", label: "High" },
  { value: "Urgent", label: "Urgent" },
];

/** New follow-up: what, how, when, who and how urgent. */
export function NewFollowUpDialog({ lead, workers, onClose, onSaved }: DialogProps & {
  lead: Lead;
  /** Who it can be given to, and who by default (see workerChoices). */
  workers: { options: { value: string; label: string }[]; chosen: string };
}) {
  const formId = useId();
  const { saving, run } = useLeadSave(onSaved);
  const [title, setTitle] = useState("");
  const [type, setType] = useState("Call");
  const [when, setWhen] = useState<When>(tomorrowAtFour);
  const [assignee, setAssignee] = useState(workers.chosen);
  const [priority, setPriority] = useState("Medium");
  const [notes, setNotes] = useState("");
  // The staff list may arrive after the popup opened.
  const chosen = assignee || workers.chosen;

  const dueAt = whenValue(when);
  const ready = title.trim().length >= 2 && !!dueAt;

  const save = () => void run(
    () => apiJson(`/api/leads/${lead.id}/follow-ups`, jsonRequest("POST", {
      title: title.trim(),
      type,
      dueAt,
      assignedEmployeeId: chosen ? Number(chosen) : null,
      priority,
      notes: notes.trim() || null,
    })),
    "Follow-up saved",
  );

  return (
    <Modal
      open
      onClose={onClose}
      title="New follow-up"
      size="md"
      phoneLayout="fullscreen"
      busy={saving}
      primaryAction={{ label: "Save follow-up", form: formId, loading: saving, disabled: !ready }}
    >
      <form id={formId} noValidate onSubmit={(event) => { event.preventDefault(); if (ready) save(); }} className="flex flex-col gap-5">
        <TextField label="What needs to be done?" required placeholder="e.g. Call back about installments" value={title} onChange={(e) => setTitle(e.target.value)} />
        <Dropdown label="Type" options={FOLLOW_UP_TYPES} value={type} onChange={setType} />
        <WhenPicker label="When?" required value={when} onChange={setWhen} />
        <Dropdown label="Assign to" options={workers.options} value={chosen} onChange={setAssignee} placeholder="Loading…" />
        <ChoiceChips label="Priority" options={PRIORITIES} value={priority} onChange={setPriority} />
        <TextArea label="Notes" placeholder="Optional" value={notes} onChange={(e) => setNotes(e.target.value)} />
      </form>
    </Modal>
  );
}

/** Mark a follow-up done: what happened, and optionally the next one. */
export function MarkDoneDialog({ item, onClose, onSaved }: DialogProps & { item: FollowUp }) {
  const formId = useId();
  const { saving, run } = useLeadSave(onSaved);
  const [outcome, setOutcome] = useState("");
  const [scheduleNext, setScheduleNext] = useState(false);
  const [nextTitle, setNextTitle] = useState("");
  const [nextWhen, setNextWhen] = useState<When>(tomorrowAtFour);

  const nextAt = whenValue(nextWhen);
  const ready = outcome.trim().length >= 2 && (!scheduleNext || (nextTitle.trim().length >= 2 && !!nextAt));

  const save = () => void run(
    () => apiJson(`/api/leads/follow-ups/${item.id}/complete`, jsonRequest("POST", {
      outcome: outcome.trim(),
      nextFollowUpTitle: scheduleNext ? nextTitle.trim() : null,
      nextFollowUpAt: scheduleNext ? nextAt : null,
    })),
    "Follow-up done",
  );

  return (
    <Modal
      open
      onClose={onClose}
      title="Mark follow-up done"
      size="md"
      phoneLayout="fullscreen"
      busy={saving}
      primaryAction={{ label: "Mark as done", form: formId, loading: saving, disabled: !ready }}
    >
      <form id={formId} noValidate onSubmit={(event) => { event.preventDefault(); if (ready) save(); }} className="flex flex-col gap-5">
        <DialogSummary title={item.title} detail={[`Due ${formatWhen(item.dueAt)}`, item.assignedEmployeeName].filter(Boolean).join(" · ")} />
        <TextArea label="What happened?" required placeholder="e.g. Explained 5-year plan, customer wants to visit on Saturday" value={outcome} onChange={(e) => setOutcome(e.target.value)} />
        <ScheduleNext
          checked={scheduleNext}
          onCheck={setScheduleNext}
          title={nextTitle}
          onTitle={setNextTitle}
          when={nextWhen}
          onWhen={setNextWhen}
          placeholder="e.g. Confirm Saturday site visit"
        />
      </form>
    </Modal>
  );
}

/** Reschedule a follow-up or a site visit: only the new time, no reason. */
export function RescheduleDialog({ target, onClose, onSaved }: DialogProps & {
  target: { kind: "followUp"; item: FollowUp } | { kind: "visit"; item: SiteVisit };
}) {
  const formId = useId();
  const { saving, run } = useLeadSave(onSaved);
  const current = target.kind === "followUp" ? target.item.dueAt : target.item.scheduledAt;
  // Tomorrow, at the time it was set for.
  const [when, setWhen] = useState<When>(() => ({ date: karachiDateInput(1), time: toKarachiInputs(current).time || "16:00" }));
  const at = whenValue(when);

  const save = () => void run(
    () => target.kind === "followUp"
      ? apiJson(`/api/leads/follow-ups/${target.item.id}/reschedule`, jsonRequest("POST", { dueAt: at }))
      : apiJson(`/api/leads/site-visits/${target.item.id}/reschedule`, jsonRequest("POST", { scheduledAt: at })),
    target.kind === "followUp" ? "Follow-up moved" : "Site visit moved",
  );

  return (
    <Modal
      open
      onClose={onClose}
      title="Reschedule"
      size="sm"
      phoneLayout="popup"
      busy={saving}
      primaryAction={{ label: "Save new time", form: formId, loading: saving, disabled: !at }}
    >
      <form id={formId} noValidate onSubmit={(event) => { event.preventDefault(); if (at) save(); }} className="flex flex-col gap-5">
        <DialogSummary
          title={target.kind === "followUp" ? target.item.title : "Site visit"}
          detail={`Now: ${target.kind === "followUp" ? formatWhen(current) : formatAppointment(current)}`}
        />
        <WhenPicker label="New date & time" required value={when} onChange={setWhen} />
      </form>
    </Modal>
  );
}
