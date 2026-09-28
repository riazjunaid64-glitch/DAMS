import { Button, Card, cx, IconCalendar, IconCheck, IconMail, IconPhone, IconUsers, IconWhatsapp, StatusBadge } from "../../components/ui";
import { formatWhen } from "../../lib/dates.ts";
import { followUpTypeLabel, priorityTone } from "./labels.ts";
import { followUpDue } from "./leadPage.ts";
import type { FollowUp } from "./types.ts";

function TypeIcon({ type }: { type: string }) {
  switch (type) {
    case "Call": return <IconPhone size={18} />;
    case "Whatsapp": return <IconWhatsapp size={18} />;
    case "Email": return <IconMail size={18} />;
    case "Meeting": return <IconUsers size={18} />;
    default: return <IconCalendar size={18} />;
  }
}

/** A follow-up to do (with its buttons while the lead is open) or one already done, with what happened. */
export function FollowUpItem({ item, onDone, onReschedule, onCancel }: {
  item: FollowUp;
  /** Omitted when the lead is closed: nothing is left to do. */
  onDone?: () => void;
  onReschedule?: () => void;
  onCancel?: () => void;
}) {
  const done = item.status === "Completed";
  const due = followUpDue(item);
  const priority = priorityTone(item.priority);
  return (
    <Card className={cx("flex gap-3 md:gap-4", !done && due.overdue && "border-danger-line")}>
      <span className={cx("flex size-9 shrink-0 items-center justify-center rounded-field", done ? "bg-success-soft text-success" : "bg-page text-ink-2")}>
        {done ? <IconCheck size={18} /> : <TypeIcon type={item.type} />}
      </span>
      <div className="min-w-0 flex-1">
        <div className="flex items-start justify-between gap-2">
          <p className={cx("m-0 min-w-0 break-words text-body font-extrabold", done ? "text-ink-muted" : "text-ink")}>{item.title}</p>
          {!done && priority && <StatusBadge status={item.priority} tone={priority} />}
        </div>
        <p className="m-0 mt-1 flex flex-wrap items-center gap-x-2 gap-y-1 text-small text-ink-muted">
          {done
            ? <StatusBadge status="done" tone="green">Done · {formatWhen(item.completedAt ?? item.dueAt)}</StatusBadge>
            : <StatusBadge status="due" tone={due.overdue ? "red" : "gold"}>{due.text}</StatusBadge>}
          <span>{[followUpTypeLabel(item.type), item.assignedEmployeeName].filter(Boolean).join(" · ")}</span>
        </p>
        {item.notes && !done && <p className="m-0 mt-2 whitespace-pre-wrap break-words text-small text-ink-2">{item.notes}</p>}
        {done && item.outcome && (
          <div className="mt-3 rounded-field bg-page p-3">
            <p className="m-0 text-caption font-bold uppercase tracking-[0.4px] text-ink-muted">Outcome</p>
            <p className="m-0 mt-0.5 whitespace-pre-wrap break-words text-sm text-ink">{item.outcome}</p>
          </div>
        )}
        {!done && onDone && (
          <div className="mt-3 grid grid-cols-2 gap-2 md:flex">
            <Button size="sm" icon={<IconCheck size={16} />} onClick={onDone}>Mark done</Button>
            <Button size="sm" variant="outline" onClick={onReschedule}>Reschedule</Button>
            <Button size="sm" variant="outline" onClick={onCancel}>Cancel</Button>
          </div>
        )}
      </div>
    </Card>
  );
}
