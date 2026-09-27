import { Link } from "react-router-dom";
import { KeyValueGrid, StatusBadge } from "../../components/ui";
import { formatDay, formatWhen } from "../../lib/dates.ts";
import { leadStatus } from "./labels.ts";
import { nextFollowUp, requirementText } from "./leadRow.ts";
import type { LeadListItem } from "./types.ts";

/** A lead on the phone list. The whole card opens the lead. */
export function LeadCard({ lead, showOwner }: { lead: LeadListItem; showOwner: boolean }) {
  const followUp = nextFollowUp(lead);
  return (
    <Link
      to={`/crm/leads/${lead.id}`}
      className="block rounded-card border border-line bg-card p-4 font-ui text-ink no-underline transition-colors hover:border-ink-faint focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary"
    >
      <div className="flex items-start justify-between gap-2">
        <span className="min-w-0 text-body font-extrabold text-ink">{lead.fullName}</span>
        <StatusBadge status={leadStatus(lead)} />
      </div>
      <p className="m-0 mt-0.5 text-small text-ink-muted">{[lead.leadReference, lead.phone].filter(Boolean).join(" · ")}</p>
      <p className="m-0 text-small font-bold text-gold-text">{[lead.city, lead.sourceName].filter(Boolean).join(" · ")}</p>
      <KeyValueGrid
        className="mt-3"
        items={[
          { label: "Requirement", value: requirementText(lead) },
          showOwner
            ? { label: "Assigned", value: lead.assignedEmployeeName ?? "Unassigned" }
            : { label: "Next follow-up", value: followUp && <span className={followUp.overdue ? "text-danger" : undefined}>{followUp.text}</span> },
        ]}
      />
      {lead.lastActivitySummary && (
        <p className="m-0 mt-3 rounded-field bg-page px-3 py-2 text-small text-ink-2">
          {lead.lastActivitySummary}
          {lead.lastActivityAt && <span className="block text-ink-muted">{formatWhen(lead.lastActivityAt)}</span>}
        </p>
      )}
      <p className="m-0 mt-3 text-small text-ink-muted">Created {formatDay(lead.createdAt)}</p>
    </Link>
  );
}
