import { useCallback, useEffect, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import type { User } from "../App.tsx";
import Button from "../lib/Button.tsx";
import {
  CrmAccess,
  CrmHeader,
  CrmTabs,
  ErrorBanner,
  QualificationBadge,
  StageBadge,
  StatePanel,
} from "../features/leads/CrmUi.tsx";
import { apiJson, downloadLeadDocument, loadCrmLookups } from "../features/leads/leadApi.ts";
import LeadActionDialog, { type LeadAction } from "../features/leads/LeadActionDialog.tsx";
import {
  enumLabel,
  formatDateTime,
  isClosedStage,
  type AssignmentHistory,
  type ClosureReason,
  type Communication,
  type ExternalSubmission,
  type FollowUp,
  type Lead,
  type LeadComment,
  type LeadDocument,
  type LeadSource,
  type ProjectLookup,
  type SiteVisit,
  type StaffMember,
  type Team,
  type TimelineItem,
} from "../features/leads/types.ts";

type Props = { user: User | null };
type DetailData = {
  lead: Lead;
  timeline: TimelineItem[];
  communications: Communication[];
  followUps: FollowUp[];
  visits: SiteVisit[];
  documents: LeadDocument[];
  comments: LeadComment[];
  assignments: AssignmentHistory[];
  submissions: ExternalSubmission[];
};
export type LeadLookups = {
  sources: LeadSource[];
  reasons: ClosureReason[];
  teams: Team[];
  staff: StaffMember[];
  projects: ProjectLookup[];
};

const TABS = [
  ["overview", "Overview"],
  ["timeline", "Timeline"],
  ["communications", "Communications"],
  ["followups", "Follow-ups & tasks"],
  ["visits", "Site visits"],
  ["documents", "Documents"],
  ["collaboration", "Internal collaboration"],
  ["assignments", "Assignment history"],
  ["integration", "Source & integration"],
  ["conversion", "Conversion"],
] as const;

export default function LeadDetailPage({ user }: Props) {
  return <CrmAccess user={user}>{user && <LeadDetailWorkspace user={user} />}</CrmAccess>;
}

function LeadDetailWorkspace({ user }: { user: User }) {
  const { id } = useParams();
  const navigate = useNavigate();
  const leadId = Number(id);
  const [activeTab, setActiveTab] = useState("overview");
  const [data, setData] = useState<DetailData | null>(null);
  const [lookups, setLookups] = useState<LeadLookups>({ sources: [], reasons: [], teams: [], staff: [], projects: [] });
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [action, setAction] = useState<LeadAction | null>(null);

  const load = useCallback(async () => {
    if (!Number.isFinite(leadId) || leadId <= 0) { setError("Invalid lead reference."); setLoading(false); return; }
    setLoading(true); setError(null);
    try {
      const [lead, timeline, communications, followUps, visits, documents, comments, assignments, submissions, refs] = await Promise.all([
        apiJson<Lead>(`/api/leads/${leadId}`),
        apiJson<TimelineItem[]>(`/api/leads/${leadId}/timeline`),
        apiJson<Communication[]>(`/api/leads/${leadId}/communications`),
        apiJson<FollowUp[]>(`/api/leads/${leadId}/follow-ups`),
        apiJson<SiteVisit[]>(`/api/leads/${leadId}/site-visits`),
        apiJson<LeadDocument[]>(`/api/leads/${leadId}/documents`),
        apiJson<LeadComment[]>(`/api/leads/${leadId}/comments`),
        apiJson<AssignmentHistory[]>(`/api/leads/${leadId}/assignment-history`),
        apiJson<ExternalSubmission[]>(`/api/leads/${leadId}/external-submissions`),
        loadCrmLookups(),
      ]);
      setData({ lead, timeline, communications, followUps, visits, documents, comments, assignments, submissions });
      setLookups(refs);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "The lead could not be loaded.");
    } finally { setLoading(false); }
  }, [leadId]);

  useEffect(() => { void load(); }, [load]);

  if (loading && !data) return <StatePanel title="Loading lead" message="Retrieving contact details, activity, follow-ups, and ownership history…" />;
  if (!data) return <StatePanel title="Lead unavailable" message={error ?? "The lead was not found or is outside your permitted scope."} action={<Button onClick={() => navigate("/crm")}>Back to leads</Button>} />;

  const { lead } = data;
  const canManage = user.role === "Admin" || user.role === "Manager";
  const closed = isClosedStage(lead.stage);
  const tabs = TABS.map(([tabId, label]) => ({
    id: tabId,
    label,
    count:
      tabId === "timeline" ? data.timeline.length :
      tabId === "communications" ? data.communications.length :
      tabId === "followups" ? data.followUps.length :
      tabId === "visits" ? data.visits.length :
      tabId === "documents" ? data.documents.length :
      tabId === "collaboration" ? data.comments.length :
      tabId === "assignments" ? data.assignments.length :
      tabId === "integration" ? data.submissions.length :
      undefined,
  }));

  const completeFollowUp = async (item: FollowUp) => {
    setAction({ type: "completeFollowUp", item });
  };
  const visitAction = (type: "completeVisit" | "rescheduleVisit" | "closeVisit", item: SiteVisit, visitDisposition?: "cancel" | "missed") =>
    setAction({ type, item, visitDisposition } as LeadAction);

  return (
    <>
      <CrmHeader
        title={lead.fullName}
        subtitle={`${lead.leadReference} · ${lead.sourceName} · Created ${formatDateTime(lead.createdAt)}`}
        role={user.role}
        actions={
          <>
            <Button variant="outline" onClick={() => navigate("/crm")}>← Leads</Button>
            {!closed && <Button variant="outline" onClick={() => setAction({ type: "communication" })}>Log activity</Button>}
            {!closed && <Button variant="outline" onClick={() => setAction({ type: "followUp" })}>Follow-up</Button>}
            {!closed && <Button onClick={() => setAction({ type: "stage" })}>Move stage</Button>}
          </>
        }
      />

      <div className="mx-auto w-full max-w-[1500px] space-y-5 px-4 py-6 sm:px-6 lg:px-8">
        {error && <ErrorBanner message={error} onRetry={() => void load()} />}

        <section className="grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
          <SummaryCard label="Pipeline"><StageBadge stage={lead.stage} /></SummaryCard>
          <SummaryCard label="Qualification"><QualificationBadge value={lead.qualification} /></SummaryCard>
          <SummaryCard label="Owner"><span>{lead.assignedEmployeeName ?? "Unassigned"}</span><small>{lead.assignedTeamName ?? "No team"}</small></SummaryCard>
          <SummaryCard label="Last activity"><span>{lead.lastActivitySummary ?? "No activity recorded"}</span><small>{formatDateTime(lead.lastActivityAt)}</small></SummaryCard>
          <SummaryCard label="Next action" danger={Boolean(lead.nextActionAt && new Date(lead.nextActionAt) < new Date() && !closed)}><span>{lead.nextActionSummary ?? "Not scheduled"}</span><small>{formatDateTime(lead.nextActionAt)}</small></SummaryCard>
        </section>

        <div className="flex flex-wrap gap-2">
          {!closed && <Button size="sm" variant="outline" onClick={() => setAction({ type: "edit" })}>Edit details</Button>}
          {!closed && <Button size="sm" variant="outline" onClick={() => setAction({ type: "qualification" })}>Qualification</Button>}
          {!closed && <Button size="sm" variant="outline" onClick={() => setAction({ type: "siteVisit" })}>Schedule site visit</Button>}
          {!closed && <Button size="sm" variant="outline" onClick={() => setAction({ type: "comment" })}>Internal note</Button>}
          {!closed && <Button size="sm" variant="outline" onClick={() => setAction({ type: "document" })}>Add document</Button>}
          {canManage && !closed && <Button size="sm" variant="outline" onClick={() => setAction({ type: "assign" })}>Assign / reassign</Button>}
          {canManage && !closed && <Button size="sm" onClick={() => setAction({ type: "convert" })}>Convert</Button>}
          {!closed && <Button size="sm" variant="danger" onClick={() => setAction({ type: "close" })}>Lost / dormant</Button>}
          {canManage && (lead.stage === "Lost" || lead.stage === "Dormant") && <Button size="sm" onClick={() => setAction({ type: "reopen" })}>Reopen lead</Button>}
        </div>

        <CrmTabs items={tabs} active={activeTab} onChange={setActiveTab} />

        <section className="min-h-[340px] rounded-2xl border border-[var(--border)] bg-[var(--bg-card)] p-4 sm:p-6">
          {activeTab === "overview" && <Overview lead={lead} />}
          {activeTab === "timeline" && <Timeline items={data.timeline} />}
          {activeTab === "communications" && <Communications items={data.communications} onAdd={!closed ? () => setAction({ type: "communication" }) : undefined} />}
          {activeTab === "followups" && <FollowUps items={data.followUps} closed={closed} onAdd={() => setAction({ type: "followUp" })} onComplete={completeFollowUp} onReschedule={(item) => setAction({ type: "rescheduleFollowUp", item })} onCancel={(item) => setAction({ type: "cancelFollowUp", item })} />}
          {activeTab === "visits" && <Visits items={data.visits} closed={closed} onAdd={() => setAction({ type: "siteVisit" })} onAction={visitAction} />}
          {activeTab === "documents" && <Documents items={data.documents} closed={closed} onAdd={() => setAction({ type: "document" })} onDownload={(document) => void downloadLeadDocument(document.id, document.fileName).catch((e) => setError(e.message))} />}
          {activeTab === "collaboration" && <Comments items={data.comments} closed={closed} onAdd={() => setAction({ type: "comment" })} />}
          {activeTab === "assignments" && <Assignments items={data.assignments} />}
          {activeTab === "integration" && <ExternalSubmissions items={data.submissions} />}
          {activeTab === "conversion" && <Conversion lead={lead} canManage={canManage} canOpenBooking={user.role === "Admin"} onConvert={() => setAction({ type: "convert" })} />}
        </section>
      </div>

      <LeadActionDialog
        action={action}
        lead={lead}
        lookups={lookups}
        user={user}
        onClose={() => setAction(null)}
        onSaved={async (destination) => {
          setAction(null);
          await load();
          if (destination) navigate(destination);
        }}
      />
    </>
  );
}

function SummaryCard({ label, children, danger }: { label: string; children: React.ReactNode; danger?: boolean }) {
  return <div className={`rounded-2xl border p-4 ${danger ? "border-rose-500/30 bg-rose-500/[0.06]" : "border-[var(--border)] bg-[var(--bg-card)]"}`}><p className="mb-2 text-[10px] font-semibold uppercase tracking-wider text-[var(--text-muted)]">{label}</p><div className={danger ? "text-sm font-semibold text-rose-400" : "text-sm font-semibold text-[var(--text-primary)]"}>{children}</div></div>;
}

function Overview({ lead }: { lead: Lead }) {
  return (
    <div className="grid gap-6 lg:grid-cols-3">
      <InfoSection title="Contact">
        <Info label="Phone" value={lead.phone} href={lead.phone ? `tel:${lead.phone}` : undefined} />
        <Info label="WhatsApp" value={lead.whatsappNumber} />
        <Info label="Email" value={lead.email} href={lead.email ? `mailto:${lead.email}` : undefined} />
        <Info label="Location" value={[lead.address, lead.city].filter(Boolean).join(", ")} />
        <Info label="Preferred contact" value={`${enumLabel(lead.preferredContactMethod)}${lead.preferredContactTime ? ` · ${lead.preferredContactTime}` : ""}`} />
      </InfoSection>
      <InfoSection title="Attribution">
        <Info label="Original source" value={lead.sourceName} />
        <Info label="Source details" value={lead.sourceDetails} />
        <Info label="Campaign" value={lead.campaignName} />
        <Info label="Campaign reference" value={lead.campaignReference} />
        <Info label="Website request" value={lead.bookingRequestId ? `Request #${lead.bookingRequestId}` : null} />
      </InfoSection>
      <InfoSection title="Property interest">
        <Info label="Project" value={lead.interestedProjectName} />
        <Info label="Unit" value={lead.interestedUnitNumber} />
        <Info label="Property type" value={lead.propertyType} />
        <Info label="Preferred location" value={lead.preferredLocation} />
        <Info label="Budget" value={lead.budgetMin || lead.budgetMax ? `${lead.budgetMin?.toLocaleString() ?? "—"} – ${lead.budgetMax?.toLocaleString() ?? "—"}` : null} />
        <Info label="Purchase intent" value={enumLabel(lead.purchaseIntent)} />
      </InfoSection>
      <div className="lg:col-span-3">
        <InfoSection title="Notes and outcome">
          <Info label="Notes" value={lead.notes} />
          <Info label="Closure reason" value={lead.closureReasonName} />
          <Info label="Closure notes" value={lead.closureNotes} />
          <Info label="Reactivation" value={formatDateTime(lead.reactivateOn)} />
        </InfoSection>
      </div>
    </div>
  );
}

function Timeline({ items }: { items: TimelineItem[] }) {
  if (!items.length) return <Empty text="No activity has been recorded." />;
  return <div className="relative space-y-0 before:absolute before:bottom-3 before:left-[7px] before:top-3 before:w-px before:bg-[var(--border)]">{items.map((item) => <article key={item.id} className="relative grid grid-cols-[16px_1fr] gap-4 pb-6"><span className="relative z-10 mt-1.5 h-3.5 w-3.5 rounded-full border-2 border-[var(--bg-card)] bg-[var(--accent)]" /><div><div className="flex flex-wrap items-center gap-2"><h3 className="text-sm font-semibold text-[var(--text-heading)]">{item.summary}</h3><span className="text-[10px] uppercase tracking-wider text-[var(--text-muted)]">{enumLabel(item.type)}</span></div><p className="mt-1 text-xs text-[var(--text-muted)]">{item.performedByName ?? (item.isSystemGenerated ? "DAMS" : "Staff")} · {formatDateTime(item.occurredAt)}{item.channel ? ` · ${enumLabel(item.channel)}` : ""}</p>{item.notes && <p className="mt-2 whitespace-pre-wrap text-sm text-[var(--text-secondary)]">{item.notes}</p>}{(item.previousValue || item.newValue) && <p className="mt-2 text-xs text-[var(--text-muted)]">{item.previousValue ?? "—"} → <span className="text-[var(--text-secondary)]">{item.newValue ?? "—"}</span></p>}</div></article>)}</div>;
}

function Communications({ items, onAdd }: { items: Communication[]; onAdd?: () => void }) {
  return <SectionList title="Customer communications" action={onAdd && <Button size="sm" onClick={onAdd}>Log communication</Button>}>{items.length ? items.map((item) => <article key={item.id} className="rounded-xl border border-[var(--border)] p-4"><div className="flex flex-wrap items-center justify-between gap-2"><p className="font-semibold text-[var(--text-heading)]">{enumLabel(item.channel)} · {item.direction}</p><time className="text-xs text-[var(--text-muted)]">{formatDateTime(item.occurredAt)}</time></div><p className="mt-2 text-sm text-[var(--text-secondary)]">{item.summary}</p>{item.customerResponse && <p className="mt-2 text-sm"><span className="text-[var(--text-muted)]">Customer response:</span> {item.customerResponse}</p>}{item.nextAction && <p className="mt-2 text-xs text-[var(--accent)]">Next: {item.nextAction} · {formatDateTime(item.nextActionAt)}</p>}</article>) : <Empty text="No customer communication recorded." />}</SectionList>;
}

function FollowUps({ items, closed, onAdd, onComplete, onReschedule, onCancel }: { items: FollowUp[]; closed: boolean; onAdd: () => void; onComplete: (item: FollowUp) => void; onReschedule: (item: FollowUp) => void; onCancel: (item: FollowUp) => void }) {
  return <SectionList title="Follow-ups and tasks" action={!closed && <Button size="sm" onClick={onAdd}>New follow-up</Button>}>{items.length ? items.map((item) => <article key={item.id} className="rounded-xl border border-[var(--border)] p-4"><div className="flex flex-wrap items-start justify-between gap-3"><div><p className="font-semibold text-[var(--text-heading)]">{item.title}</p><p className="mt-1 text-xs text-[var(--text-muted)]">{enumLabel(item.type)} · {item.assignedEmployeeName} · {item.priority}</p></div><span className={`rounded-full px-2.5 py-1 text-xs font-semibold ${item.status === "Pending" && new Date(item.dueAt) < new Date() ? "bg-rose-500/10 text-rose-400" : "bg-[var(--surface-glass)] text-[var(--text-muted)]"}`}>{item.status}</span></div><p className="mt-3 text-sm text-[var(--text-secondary)]">{item.notes ?? "No notes"}</p><div className="mt-3 flex flex-wrap items-center justify-between gap-2 text-xs text-[var(--text-muted)]"><span>Due {formatDateTime(item.dueAt)}</span>{item.status === "Pending" && !closed && <div className="flex flex-wrap gap-2"><Button size="sm" onClick={() => onComplete(item)}>Complete</Button><Button size="sm" variant="outline" onClick={() => onReschedule(item)}>Reschedule</Button><Button size="sm" variant="danger" onClick={() => onCancel(item)}>Cancel</Button></div>}</div>{item.outcome && <p className="mt-3 rounded-lg bg-[var(--surface-glass)] p-3 text-sm text-[var(--text-secondary)]">Outcome: {item.outcome}</p>}</article>) : <Empty text="No follow-ups or tasks yet." />}</SectionList>;
}

function Visits({ items, closed, onAdd, onAction }: { items: SiteVisit[]; closed: boolean; onAdd: () => void; onAction: (type: "completeVisit" | "rescheduleVisit" | "closeVisit", item: SiteVisit, disposition?: "cancel" | "missed") => void }) {
  return <SectionList title="Site visits" action={!closed && <Button size="sm" onClick={onAdd}>Schedule visit</Button>}>{items.length ? items.map((item) => <article key={item.id} className="rounded-xl border border-[var(--border)] p-4"><div className="flex flex-wrap items-start justify-between gap-2"><div><p className="font-semibold text-[var(--text-heading)]">{item.projectName ?? "Property visit"}{item.unitNumber ? ` · Unit ${item.unitNumber}` : ""}</p><p className="mt-1 text-xs text-[var(--text-muted)]">{item.meetingLocation} · {item.assignedEmployeeName}</p></div><span className="rounded-full bg-violet-500/10 px-2.5 py-1 text-xs font-semibold text-violet-400">{item.status}</span></div><p className="mt-3 text-sm text-[var(--text-secondary)]">{formatDateTime(item.scheduledAt)}{item.notes ? ` · ${item.notes}` : ""}</p>{["Scheduled", "Rescheduled"].includes(item.status) && !closed && <div className="mt-3 flex flex-wrap gap-2"><Button size="sm" onClick={() => onAction("completeVisit", item)}>Complete</Button><Button size="sm" variant="outline" onClick={() => onAction("rescheduleVisit", item)}>Reschedule</Button><Button size="sm" variant="outline" onClick={() => onAction("closeVisit", item, "missed")}>Missed</Button><Button size="sm" variant="danger" onClick={() => onAction("closeVisit", item, "cancel")}>Cancel</Button></div>}{item.outcome && <p className="mt-3 rounded-lg bg-[var(--surface-glass)] p-3 text-sm">Outcome: {enumLabel(item.outcome)} · {item.nextAction}</p>}</article>) : <Empty text="No site visits scheduled." />}</SectionList>;
}

function Documents({ items, closed, onAdd, onDownload }: { items: LeadDocument[]; closed: boolean; onAdd: () => void; onDownload: (item: LeadDocument) => void }) {
  return (
    <SectionList title="Secure lead documents" action={!closed && <Button size="sm" onClick={onAdd}>Upload document</Button>}>
      {items.length ? (
        <div className="overflow-x-auto">
          <table className="w-full min-w-[650px] text-left text-sm">
            <thead className="text-xs uppercase text-[var(--text-muted)]"><tr><th className="pb-3">Document</th><th className="pb-3">Type</th><th className="pb-3">Access</th><th className="pb-3">Added by</th><th className="pb-3">Added</th><th /></tr></thead>
            <tbody>{items.map((item) => (
              <tr key={item.id} className="border-t border-[var(--border)]">
                <td className="py-3 font-medium text-[var(--text-heading)]">{item.fileName}<p className="text-xs text-[var(--text-muted)]">{item.description}</p></td>
                <td>{enumLabel(item.category)}</td>
                <td><span className="rounded-md bg-slate-500/10 px-2 py-1 text-xs text-slate-400">Internal staff</span></td>
                <td>{item.uploadedByName ?? "Staff"}</td>
                <td>{formatDateTime(item.uploadedAt)}</td>
                <td className="text-right"><Button size="sm" variant="outline" onClick={() => onDownload(item)}>Download</Button></td>
              </tr>
            ))}</tbody>
          </table>
        </div>
      ) : <Empty text="No documents uploaded." />}
    </SectionList>
  );
}

function Comments({ items, closed, onAdd }: { items: LeadComment[]; closed: boolean; onAdd: () => void }) {
  return <SectionList title="Internal collaboration" action={!closed && <Button size="sm" onClick={onAdd}>Add internal note</Button>}>{items.length ? items.map((item) => <article key={item.id} className="rounded-xl border border-[var(--border)] p-4"><div className="flex flex-wrap items-center gap-2"><p className="font-semibold text-[var(--text-heading)]">{item.authorName ?? "Staff"}</p>{item.isManagerReviewRequest && <span className="rounded bg-amber-500/10 px-2 py-0.5 text-xs text-amber-400">Manager review</span>}{item.isDecisionRecord && <span className="rounded bg-indigo-500/10 px-2 py-0.5 text-xs text-indigo-400">Decision</span>}<time className="ml-auto text-xs text-[var(--text-muted)]">{formatDateTime(item.createdAt)}</time></div><p className="mt-3 whitespace-pre-wrap text-sm text-[var(--text-secondary)]">{item.body}</p>{item.mentions.length > 0 && <p className="mt-2 text-xs text-[var(--accent)]">Mentioned: {item.mentions.map((m) => m.name ?? `User ${m.userId}`).join(", ")}</p>}</article>) : <Empty text="No internal comments or guidance." />}</SectionList>;
}

function Assignments({ items }: { items: AssignmentHistory[] }) {
  return <SectionList title="Ownership history">{items.length ? items.map((item) => <article key={item.id} className="rounded-xl border border-[var(--border)] p-4"><p className="text-sm text-[var(--text-secondary)]"><span className="font-semibold text-[var(--text-heading)]">{item.previousEmployeeName ?? "Unassigned"}</span> → <span className="font-semibold text-[var(--accent)]">{item.assignedEmployeeName ?? "Unassigned"}</span></p><p className="mt-2 text-xs text-[var(--text-muted)]">{item.assignedByName ?? "System"} · {formatDateTime(item.assignedAt)}</p>{item.reason && <p className="mt-2 text-sm text-[var(--text-secondary)]">{item.reason}</p>}</article>) : <Empty text="No ownership changes recorded." />}</SectionList>;
}

/**
 * The provider enquiries behind this lead.
 *
 * Every answer is shown, including ones DAMS has no field for — those are the reason the
 * raw answers are kept at all, and hiding them would defeat the point.
 */
function ExternalSubmissions({ items }: { items: ExternalSubmission[] }) {
  if (items.length === 0)
    return <SectionList title="Source & integration"><Empty text="This lead did not arrive through a connected integration." /></SectionList>;

  return (
    <SectionList title="Source & integration">
      {items.map((item) => (
        <article key={item.id} className="rounded-xl border border-[var(--border)] p-4">
          <div className="flex flex-wrap items-center gap-2">
            <span className="rounded-full border border-[var(--border)] px-2.5 py-0.5 text-xs text-[var(--text-secondary)]">
              {platformLabel(item)}
            </span>
            <p className="text-xs text-[var(--text-muted)]">
              Submitted {formatDateTime(item.externalSubmittedAt ?? item.receivedAt)} · Reference {item.externalLeadId}
            </p>
          </div>

          <dl className="mt-3 grid gap-x-6 gap-y-2 sm:grid-cols-2">
            <Attribution label="Page" value={item.pageName} />
            <Attribution label="Form" value={item.externalFormName ?? item.externalFormReference} />
            <Attribution label="Campaign" value={item.campaignName} />
            <Attribution label="Ad set" value={item.adSetName} />
            <Attribution label="Ad" value={item.adName} />
            <Attribution label="Account" value={item.connectionDisplayName} />
          </dl>

          {item.fieldData.length > 0 && (
            <div className="mt-4 border-t border-[var(--border)] pt-3">
              <p className="mb-2 text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)]">Form answers</p>
              <dl className="grid gap-x-6 gap-y-2 sm:grid-cols-2">
                {item.fieldData.map((answer, index) => (
                  <div key={`${answer.name}-${index}`}>
                    <dt className="text-xs text-[var(--text-muted)]">
                      {answer.name}
                      {!answer.isMapped && <span className="ml-1.5 opacity-70">· not mapped</span>}
                    </dt>
                    <dd className="mt-0.5 whitespace-pre-wrap text-sm text-[var(--text-secondary)]">{answer.value || "—"}</dd>
                  </div>
                ))}
              </dl>
            </div>
          )}
        </article>
      ))}
    </SectionList>
  );
}

function Attribution({ label, value }: { label: string; value?: string | null }) {
  if (!value) return null;
  return <div><dt className="text-xs text-[var(--text-muted)]">{label}</dt><dd className="mt-0.5 text-sm text-[var(--text-secondary)]">{value}</dd></div>;
}

// Never guesses. An enquiry Meta did not attribute to a surface is shown as "Meta", not as
// Facebook, because a lead-ad webhook always arrives through a Page either way.
function platformLabel(item: ExternalSubmission) {
  if (item.platform === "instagram") return "Instagram";
  if (item.platform === "facebook") return "Facebook";
  return item.provider === "meta" ? "Meta" : item.provider;
}

function Conversion({ lead, canManage, canOpenBooking, onConvert }: { lead: Lead; canManage: boolean; canOpenBooking: boolean; onConvert: () => void }) {
  if (lead.stage === "Won") return <div className="rounded-2xl border border-emerald-500/25 bg-emerald-500/[0.06] p-6"><h2 className="text-lg font-semibold text-emerald-400">Successfully converted</h2><p className="mt-2 text-sm text-[var(--text-secondary)]">The lead remains available as a historical CRM record. Customer #{lead.convertedCustomerId} · Booking {lead.convertedBookingReference ?? `#${lead.convertedBookingId}`}</p>{canOpenBooking && <div className="mt-4 flex flex-wrap gap-2">{lead.convertedCustomerId && <Link to={`/customers/${lead.convertedCustomerId}`}><Button variant="outline">Open customer</Button></Link>}{lead.convertedBookingId && <Link to={`/confirmed-bookings/${lead.convertedBookingId}`}><Button>Open booking {lead.convertedBookingReference}</Button></Link>}</div>}</div>;
  return <div className="max-w-2xl"><h2 className="text-lg font-semibold text-[var(--text-heading)]">Lead conversion</h2><p className="mt-2 text-sm leading-6 text-[var(--text-muted)]">Conversion resolves an existing or new customer and creates the booking in one backend transaction. A unit is never reserved by an inquiry, and Won is set only after this succeeds.</p>{canManage ? <Button className="mt-5" onClick={onConvert}>Start safe conversion</Button> : <p className="mt-4 rounded-xl bg-[var(--surface-glass)] p-4 text-sm text-[var(--text-secondary)]">Ask a Sales Manager or Admin to complete conversion.</p>}</div>;
}

function InfoSection({ title, children }: { title: string; children: React.ReactNode }) { return <section><h2 className="mb-3 text-xs font-semibold uppercase tracking-wider text-[var(--text-muted)]">{title}</h2><dl className="space-y-3">{children}</dl></section>; }
function Info({ label, value, href }: { label: string; value?: string | null; href?: string }) { const content = value || "—"; return <div><dt className="text-xs text-[var(--text-muted)]">{label}</dt><dd className="mt-0.5 whitespace-pre-wrap text-sm text-[var(--text-secondary)]">{href && value ? <a className="text-[var(--accent)] hover:underline" href={href}>{content}</a> : content}</dd></div>; }
function Empty({ text }: { text: string }) { return <div className="rounded-xl border border-dashed border-[var(--border)] px-4 py-12 text-center text-sm text-[var(--text-muted)]">{text}</div>; }
function SectionList({ title, action, children }: { title: string; action?: React.ReactNode; children: React.ReactNode }) { return <div><div className="mb-4 flex items-center justify-between gap-3"><h2 className="text-lg font-semibold text-[var(--text-heading)]">{title}</h2>{action}</div><div className="space-y-3">{children}</div></div>; }
