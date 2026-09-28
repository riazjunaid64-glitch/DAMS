import { Button, Card, cx, IconAlert, IconCheck, IconMapPin, StatusBadge } from "../../components/ui";
import { formatAppointment } from "../../lib/dates.ts";
import { visitOutcomeLabel } from "./labels.ts";
import type { SiteVisit } from "./types.ts";

export type VisitActions = {
  onDone: () => void;
  onReschedule: () => void;
  onMissed: () => void;
  onCancel: () => void;
};

/**
 * A site visit. An upcoming one can be marked done, moved, missed or cancelled; a missed one can
 * still be moved or cancelled, and stays the lead's next step until it is. A done one shows how it
 * went and the next step.
 */
export function SiteVisitItem({ item, actions }: { item: SiteVisit; actions?: VisitActions }) {
  const completed = item.status === "Completed";
  const missed = item.status === "Missed";
  const outcome = item.outcome ? visitOutcomeLabel(item.outcome) : null;
  return (
    <Card className="flex gap-3 md:gap-4">
      <span
        className={cx(
          "flex size-9 shrink-0 items-center justify-center rounded-field",
          completed ? "bg-success-soft text-success" : missed ? "bg-danger-soft text-danger" : "bg-gold-soft text-gold-text",
        )}
      >
        {completed ? <IconCheck size={18} /> : missed ? <IconAlert size={18} /> : <IconMapPin size={18} />}
      </span>
      <div className="min-w-0 flex-1">
        <p className="m-0 flex flex-wrap items-center gap-2">
          <span className={cx("text-body font-extrabold", completed ? "text-ink-muted" : "text-ink")}>{formatAppointment(item.scheduledAt)}</span>
          {outcome && <StatusBadge status={outcome.label} tone={outcome.tone}>{outcome.label}</StatusBadge>}
          {missed && <StatusBadge status="missed" tone="red">Missed</StatusBadge>}
        </p>
        <p className="m-0 mt-0.5 text-small text-ink-muted">
          {[item.meetingLocation, item.assignedEmployeeName && `with ${item.assignedEmployeeName}`].filter(Boolean).join(" · ")}
        </p>
        {item.notes && !completed && <p className="m-0 mt-2 whitespace-pre-wrap break-words text-small text-ink-2">{item.notes}</p>}
        {completed && item.nextAction && (
          <div className="mt-3 rounded-field bg-page p-3">
            <p className="m-0 text-caption font-bold uppercase tracking-[0.4px] text-ink-muted">Next step</p>
            <p className="m-0 mt-0.5 whitespace-pre-wrap break-words text-sm text-ink">{item.nextAction}</p>
          </div>
        )}
        {actions && !completed && (
          <div className="mt-3 grid grid-cols-2 gap-2 md:flex">
            {!missed && <Button size="sm" onClick={actions.onDone}>Visit done</Button>}
            <Button size="sm" variant="outline" onClick={actions.onReschedule}>Reschedule</Button>
            {!missed && <Button size="sm" variant="outline" onClick={actions.onMissed}>Missed</Button>}
            <Button size="sm" variant="outline" onClick={actions.onCancel}>Cancel</Button>
          </div>
        )}
      </div>
    </Card>
  );
}
