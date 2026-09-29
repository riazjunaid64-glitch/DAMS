import { useCallback, useEffect, useRef, useState, type ReactNode } from "react";
import { useNavigate, useParams, useSearchParams } from "react-router-dom";
import type { User } from "../App.tsx";
import {
  ActionsMenu,
  Button,
  ConfirmDialog,
  EmptyState,
  IconCalendar,
  IconCheck,
  IconClose,
  IconMapPin,
  IconPencil,
  IconPhone,
  IconPlus,
  IconUserPlus,
  IconUsers,
  InfoCard,
  Notice,
  PageHeader,
  Tabs,
  useIsPhone,
  useToast,
  type ActionItem,
} from "../components/ui";
import { can } from "../features/access/permissions.ts";
import { CommunicationItem } from "../features/leads/CommunicationItem.tsx";
import { LogCommunicationDialog, MarkDoneDialog, NewFollowUpDialog, RescheduleDialog } from "../features/leads/EngagementDialogs.tsx";
import { FollowUpItem } from "../features/leads/FollowUpItem.tsx";
import { lastContactText, leadStatus, type LeadStatus } from "../features/leads/labels.ts";
import { apiJson, jsonRequest, loadCrmLookups, type CrmLookups } from "../features/leads/leadApi.ts";
import { AssignLeadDialog, CloseLeadDialog, ConvertLeadDialog, EditLeadDialog } from "../features/leads/LeadDialogs.tsx";
import { LeadOverview } from "../features/leads/LeadOverview.tsx";
import { assignChoices, followUpGroups, reopenBody, requirementDetail, visitGroups, workerChoices } from "../features/leads/leadPage.ts";
import { nextFollowUp } from "../features/leads/leadRow.ts";
import { ScheduleVisitDialog, VisitDoneDialog } from "../features/leads/SiteVisitDialogs.tsx";
import { SiteVisitItem } from "../features/leads/SiteVisitItem.tsx";
import { TimelineList } from "../features/leads/TimelineList.tsx";
import type { Communication, ExternalSubmission, FollowUp, LeadDetail, SiteVisit, TimelineItem } from "../features/leads/types.ts";
import { useLeadSave } from "../features/leads/useLeadSave.ts";
import { formatAppointment, formatDay, formatMonthDay, formatTime, formatWhen } from "../lib/dates.ts";

type Props = { user: User | null };

const PAGE = "mx-auto flex w-full max-w-[1500px] flex-col gap-4 px-4 py-5 md:gap-5 md:px-8 md:py-7";

/** The sections loaded only when first opened, and kept after. */
type Part = "timeline" | "communications" | "followUps" | "visits";
type Parts = {
  timeline: { items: TimelineItem[]; hasMore: boolean };
  communications: Communication[];
  followUps: FollowUp[];
  visits: SiteVisit[];
};
type Tab = "overview" | Part;
const TABS: readonly Tab[] = ["overview", "timeline", "communications", "followUps", "visits"];
const TIMELINE_PAGE = 50;

type Dialog =
  | { type: "communication" }
  | { type: "followUp" }
  | { type: "markDone"; item: FollowUp }
  | { type: "rescheduleFollowUp"; item: FollowUp }
  | { type: "cancelFollowUp"; item: FollowUp }
  | { type: "visit" }
  | { type: "visitDone"; item: SiteVisit }
  | { type: "rescheduleVisit"; item: SiteVisit }
  | { type: "missed"; item: SiteVisit }
  | { type: "cancelVisit"; item: SiteVisit }
  | { type: "edit" }
  | { type: "assign" }
  | { type: "close" }
  | { type: "convert" }
  | { type: "reopen" };

export default function LeadDetailPage({ user }: Props) {
  const { id } = useParams();
  if (!user || !can(user.role, "crm")) {
    return (
      <div className={PAGE}>
        <EmptyState
          icon={<IconUsers size={26} />}
          title={user ? "The Lead CRM is not part of your role" : "Sign in required"}
          message={user ? undefined : "Sign in with a staff account to open the Lead CRM."}
        />
      </div>
    );
  }
  // A new lead starts from nothing: no tab data, popup or failure carries over from the last one.
  return <LeadWorkspace key={id} user={user} leadId={Number(id)} />;
}

function LeadWorkspace({ user, leadId }: { user: User; leadId: number }) {
  const navigate = useNavigate();
  const toast = useToast();
  const isPhone = useIsPhone();
  const [searchParams, setSearchParams] = useSearchParams();
  const tabParam = searchParams.get("tab") as Tab | null;
  const tab: Tab = tabParam && TABS.includes(tabParam) ? tabParam : "overview";

  const [lead, setLead] = useState<LeadDetail | null>(null);
  const [submissions, setSubmissions] = useState<ExternalSubmission[]>([]);
  const [submissionsError, setSubmissionsError] = useState<string | null>(null);
  const [submissionsAttempt, setSubmissionsAttempt] = useState(0);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);
  const partGeneration = useRef<Record<Part, number>>({ timeline: 0, communications: 0, followUps: 0, visits: 0 });
  const leadGeneration = useRef(0);
  const submissionsGeneration = useRef(0);
  const [parts, setParts] = useState<Partial<Parts>>({});
  const [partErrors, setPartErrors] = useState<Partial<Record<Part, string>>>({});
  const [loadingOlder, setLoadingOlder] = useState(false);
  const [lookups, setLookups] = useState<CrmLookups | null>(null);
  const [dialog, setDialog] = useState<Dialog | null>(null);

  // Opening a lead reads the lead (header, cards, counts). Source details load separately.
  useEffect(() => {
    if (!Number.isFinite(leadId) || leadId <= 0) return;
    const controller = new AbortController();
    apiJson<LeadDetail>(`/api/leads/${leadId}`, { signal: controller.signal })
      .then((nextLead) => {
        setLead(nextLead);
        setLoadError(null);
      })
      .catch((caught) => {
        if (!controller.signal.aborted) setLoadError(caught instanceof Error ? caught.message : "The lead could not be loaded.");
      });
    return () => controller.abort();
  }, [leadId, attempt]);

  useEffect(() => {
    if (!lead || !Number.isFinite(leadId) || leadId <= 0) return;
    const controller = new AbortController();
    const generation = ++submissionsGeneration.current;
    apiJson<ExternalSubmission[]>(`/api/leads/${leadId}/external-submissions`, { signal: controller.signal })
      .then((nextSubmissions) => {
        if (generation !== submissionsGeneration.current) return;
        setSubmissions(nextSubmissions);
        setSubmissionsError(null);
      })
      .catch((caught) => {
        if (controller.signal.aborted || generation !== submissionsGeneration.current) return;
        setSubmissionsError(caught instanceof Error ? caught.message : "Source details could not be loaded.");
      });
    return () => controller.abort();
  }, [lead, leadId, submissionsAttempt]);

  const fetchPart = useCallback(async (part: Part, signal?: AbortSignal): Promise<Parts[Part]> => {
    switch (part) {
      case "timeline": {
        const items = await apiJson<TimelineItem[]>(`/api/leads/${leadId}/timeline?take=${TIMELINE_PAGE}`, { signal });
        return { items, hasMore: items.length === TIMELINE_PAGE };
      }
      case "communications": return apiJson<Communication[]>(`/api/leads/${leadId}/communications`, { signal });
      case "followUps": return apiJson<FollowUp[]>(`/api/leads/${leadId}/follow-ups`, { signal });
      case "visits": return apiJson<SiteVisit[]>(`/api/leads/${leadId}/site-visits`, { signal });
    }
  }, [leadId]);

  const loadPart = useCallback(async (part: Part) => {
    const generation = ++partGeneration.current[part];
    try {
      const data = await fetchPart(part);
      if (generation !== partGeneration.current[part]) return;
      setParts((current) => ({ ...current, [part]: data }));
      setPartErrors((current) => ({ ...current, [part]: undefined }));
    } catch (caught) {
      if (generation !== partGeneration.current[part]) return;
      setPartErrors((current) => ({ ...current, [part]: caught instanceof Error ? caught.message : "This could not be loaded." }));
    }
  }, [fetchPart]);

  // A section loads the first time it is opened, and is kept.
  const openPart = tab === "overview" ? null : tab;
  const openPartLoaded = openPart !== null && parts[openPart] !== undefined;
  const openPartFailed = openPart !== null && !!partErrors[openPart];
  useEffect(() => {
    if (openPart && !openPartLoaded && !openPartFailed && lead) void loadPart(openPart);
  }, [openPart, openPartLoaded, openPartFailed, lead, loadPart]);

  const showOlder = async () => {
    const shown = parts.timeline;
    const oldest = shown?.items[shown.items.length - 1];
    if (!shown || !oldest) return;
    setLoadingOlder(true);
    try {
      const older = await apiJson<TimelineItem[]>(`/api/leads/${leadId}/timeline?take=${TIMELINE_PAGE}&before=${oldest.id}`);
      setParts((current) => ({ ...current, timeline: { items: [...(current.timeline?.items ?? []), ...older], hasMore: older.length === TIMELINE_PAGE } }));
    } catch (caught) {
      toast.error(caught instanceof Error ? caught.message : "Older entries could not be loaded.");
    } finally {
      setLoadingOlder(false);
    }
  };

  /**
   * After an action: the lead (header, cards, counts) and the section it changed. The section on
   * screen reloads now with the old copy kept meanwhile; one not on screen is dropped and loads
   * when next opened. The timeline changes with every action.
   */
  const refresh = (changed: Part[]) => {
    setDialog(null);
    const generation = ++leadGeneration.current;
    apiJson<LeadDetail>(`/api/leads/${leadId}`)
      .then((nextLead) => {
        if (generation !== leadGeneration.current) return;
        setLead(nextLead);
      })
      .catch((caught) => toast.error(caught instanceof Error ? caught.message : "The lead could not be refreshed."));
    for (const part of new Set<Part>([...changed, "timeline"])) {
      if (part === openPart) void loadPart(part);
      else setParts((current) => ({ ...current, [part]: undefined }));
    }
  };

  const account = `${user.userId}:${user.role}`;
  const open = (next: Dialog) => {
    setDialog(next);
    // Staff, reasons and apartment types come from the shared CRM cache, fetched only when a popup needs them.
    if (!lookups) {
      loadCrmLookups(account)
        .then(setLookups)
        .catch((caught) => toast.error(caught instanceof Error ? caught.message : "The staff list could not be loaded."));
    }
  };

  const confirm = useLeadSave(() => {
    const kind = dialog?.type;
    refresh(kind === "cancelFollowUp" ? ["followUps"] : kind === "missed" || kind === "cancelVisit" ? ["visits"] : []);
  });

  if (!lead) {
    if (loadError || !Number.isFinite(leadId) || leadId <= 0) {
      return (
        <div className={PAGE}>
          <EmptyState
            icon={<IconUsers size={26} />}
            title="This lead could not be opened"
            message={loadError ?? "The lead was not found."}
            action={
              <div className="flex flex-wrap justify-center gap-2">
                <Button variant="outline" onClick={() => navigate("/crm")}>Back to leads</Button>
                {loadError && <Button onClick={() => { setLoadError(null); setAttempt((n) => n + 1); }}>Try again</Button>}
              </div>
            }
          />
        </div>
      );
    }
    return (
      <div className={PAGE} aria-busy="true">
        <div className="h-16 animate-pulse rounded-card bg-track" />
        <div className="grid grid-cols-2 gap-2.5 md:grid-cols-4 md:gap-4">
          {[0, 1, 2, 3].map((n) => <div key={n} className="h-16 animate-pulse rounded-card bg-track" />)}
        </div>
        <div className="h-16 animate-pulse rounded-card bg-track" />
      </div>
    );
  }

  const status = leadStatus(lead);
  const closed = status !== "InProgress";
  const canManage = can(user.role, "crm.manage");
  const me = lookups?.staff.find((member) => member.userId === Number(user.userId)) ?? null;
  const workers = workerChoices(lookups?.staff ?? [], lead, me, canManage);

  const actions: (ActionItem & { variant?: "outline" | "danger" | "success"; show: boolean })[] = [
    { label: "Follow-up", icon: <IconCalendar size={18} />, onSelect: () => open({ type: "followUp" }), show: true },
    { label: "Edit details", icon: <IconPencil size={18} />, onSelect: () => open({ type: "edit" }), show: true },
    { label: "Assign / reassign", icon: <IconUserPlus size={18} />, onSelect: () => open({ type: "assign" }), show: canManage },
    { label: "Schedule site visit", icon: <IconMapPin size={18} />, onSelect: () => open({ type: "visit" }), show: true },
    { label: "Lost / dormant", icon: <IconClose size={18} />, danger: true, variant: "danger", onSelect: () => open({ type: "close" }), show: true },
    { label: "Convert to booking", icon: <IconCheck size={18} />, variant: "success", onSelect: () => open({ type: "convert" }), show: true },
  ];
  const shownActions = actions.filter((action) => action.show);
  // The phone menu's order: follow-up and visit first, the lead-level changes after.
  const phoneOrder = ["Follow-up", "Schedule site visit", "Assign / reassign", "Edit details", "Convert to booking", "Lost / dormant"];

  const next = nextFollowUp(lead);
  const last = lead.lastCommunication;

  const tabs = [
    { id: "overview", label: "Overview" },
    { id: "timeline", label: "Timeline", count: lead.counts?.timeline },
    { id: "communications", label: "Communications", count: lead.counts?.communications },
    { id: "followUps", label: "Follow-ups", count: lead.counts?.followUps },
    { id: "visits", label: "Site visits", count: lead.counts?.siteVisits },
  ];

  const setTab = (next: string) => setSearchParams((current) => {
    const params = new URLSearchParams(current);
    if (next === "overview") params.delete("tab");
    else params.set("tab", next);
    return params;
  }, { replace: true });

  return (
    <div className={PAGE}>
      <PageHeader
        back={{ to: "/crm", label: "Leads" }}
        title={lead.fullName}
        status={status}
        subtitle={lead.leadReference}
        actions={!isPhone && !closed ? <Button variant="outline" onClick={() => open({ type: "followUp" })}>Follow-up</Button> : undefined}
      />

      {!isPhone && !closed && (
        <div className="flex flex-wrap items-center gap-2">
          {shownActions.filter((action) => action.label !== "Follow-up").map((action) => (
            <Button
              key={String(action.label)}
              variant={action.variant ?? "outline"}
              className={action.danger ? "ml-auto" : undefined}
              onClick={action.onSelect}
            >
              {action.label}
            </Button>
          ))}
        </div>
      )}

      {isPhone && (
        <div className={closed ? "grid grid-cols-1" : "grid grid-cols-2 gap-2"}>
          <Button variant="outline" size="lg" icon={<IconPhone size={18} />} disabled={!lead.phone} onClick={() => { window.location.href = `tel:${lead.phone}`; }}>
            Call
          </Button>
          {!closed && (
            <ActionsMenu
              trigger="button"
              label="Actions"
              className="w-full [&>button]:h-12 [&>button]:w-full"
              items={[...shownActions].sort((a, b) => phoneOrder.indexOf(String(a.label)) - phoneOrder.indexOf(String(b.label)))}
            />
          )}
        </div>
      )}

      {closed && <ClosedBar lead={lead} status={status} canOpenBooking={can(user.role, "bookings")} onOpenBooking={(id) => navigate(`/confirmed-bookings/${id}`)} onReopen={() => open({ type: "reopen" })} />}

      <section className="grid grid-cols-2 gap-2.5 md:grid-cols-4 md:gap-4">
        <InfoCard
          label="Assigned to"
          value={lead.assignedEmployeeName ?? <span className="text-danger">Unassigned</span>}
          detail={lead.assignedEmployeeName && lead.assignedAt ? `since ${formatMonthDay(lead.assignedAt)}, ${formatTime(lead.assignedAt)}` : undefined}
        />
        <InfoCard label="Requirement" value={lead.propertyType || "—"} detail={requirementDetail(lead) || undefined} />
        <InfoCard
          label="Last contact"
          value={last ? <Clamp>{lastContactText(last)}</Clamp> : "No contact yet"}
          detail={last ? formatWhen(last.occurredAt) : undefined}
        />
        <InfoCard
          highlight
          label="Next follow-up"
          value={closed ? "—" : <Clamp>{lead.nextActionSummary || "—"}</Clamp>}
          detail={closed ? "None, lead is closed" : next ? <span className={next.overdue ? "text-danger" : undefined}>{next.text}</span> : "Nothing scheduled"}
        />
      </section>

      <Tabs items={tabs} value={tab} onChange={setTab} aria-label="Lead sections" />

      {tab === "overview" && (
        <LeadOverview
          lead={lead}
          submissions={submissions}
          submissionsError={submissionsError}
          onRetrySubmissions={() => setSubmissionsAttempt((n) => n + 1)}
        />
      )}

      {tab !== "overview" && (
        <PartView data={parts[tab]} error={partErrors[tab]} onRetry={() => void loadPart(tab)}>
          {tab === "timeline" && parts.timeline && (
            <>
              <SectionHeader title="Timeline" />
              {parts.timeline.items.length
                ? <TimelineList items={parts.timeline.items} loadingOlder={loadingOlder} onShowOlder={parts.timeline.hasMore ? () => void showOlder() : undefined} />
                : <EmptyState title="Nothing has happened on this lead yet" />}
            </>
          )}

          {tab === "communications" && parts.communications && (
            <>
              <SectionHeader
                title="Communications"
                count={parts.communications.length}
                action={!closed && <Button icon={<IconPlus size={16} />} onClick={() => open({ type: "communication" })}>{isPhone ? "Log" : "Log communication"}</Button>}
              />
              {parts.communications.length
                ? parts.communications.map((item) => <CommunicationItem key={item.id} item={item} />)
                : <EmptyState title="No communications yet" />}
            </>
          )}

          {tab === "followUps" && parts.followUps && (
            <FollowUpsSection
              items={parts.followUps}
              isPhone={isPhone}
              closed={closed}
              stale={!!partErrors.followUps}
              onNew={() => open({ type: "followUp" })}
              onDone={(item) => open({ type: "markDone", item })}
              onReschedule={(item) => open({ type: "rescheduleFollowUp", item })}
              onCancel={(item) => open({ type: "cancelFollowUp", item })}
            />
          )}

          {tab === "visits" && parts.visits && (
            <VisitsSection
              items={parts.visits}
              isPhone={isPhone}
              closed={closed}
              stale={!!partErrors.visits}
              onNew={() => open({ type: "visit" })}
              onAction={(type, item) => open({ type, item })}
            />
          )}
        </PartView>
      )}

      {dialog?.type === "communication" && (
        <LogCommunicationDialog lead={lead} onClose={() => setDialog(null)} onSaved={(scheduled) => refresh(scheduled ? ["communications", "followUps"] : ["communications"])} />
      )}
      {dialog?.type === "followUp" && (
        <NewFollowUpDialog lead={lead} workers={workers} onClose={() => setDialog(null)} onSaved={() => refresh(["followUps"])} />
      )}
      {dialog?.type === "markDone" && <MarkDoneDialog item={dialog.item} onClose={() => setDialog(null)} onSaved={() => refresh(["followUps"])} />}
      {dialog?.type === "rescheduleFollowUp" && (
        <RescheduleDialog target={{ kind: "followUp", item: dialog.item }} onClose={() => setDialog(null)} onSaved={() => refresh(["followUps"])} />
      )}
      {dialog?.type === "visit" && (
        <ScheduleVisitDialog lead={lead} workers={workers} onClose={() => setDialog(null)} onSaved={() => refresh(["visits"])} />
      )}
      {dialog?.type === "visitDone" && <VisitDoneDialog item={dialog.item} onClose={() => setDialog(null)} onSaved={() => refresh(["visits"])} />}
      {dialog?.type === "rescheduleVisit" && (
        <RescheduleDialog target={{ kind: "visit", item: dialog.item }} onClose={() => setDialog(null)} onSaved={() => refresh(["visits"])} />
      )}
      {dialog?.type === "edit" && (
        <EditLeadDialog lead={lead} apartmentTypes={lookups?.apartmentTypes ?? []} onClose={() => setDialog(null)} onSaved={() => refresh([])} />
      )}
      {dialog?.type === "assign" && (
        <AssignLeadDialog lead={lead} choices={assignChoices(lookups?.staff ?? [], lead, me)} onClose={() => setDialog(null)} onSaved={() => refresh(["followUps", "visits"])} />
      )}
      {dialog?.type === "close" && (
        <CloseLeadDialog lead={lead} reasons={lookups?.reasons ?? null} onClose={() => setDialog(null)} onSaved={() => refresh(["followUps", "visits"])} />
      )}
      {dialog?.type === "convert" && <ConvertLeadDialog lead={lead} onClose={() => setDialog(null)} onSaved={() => refresh(["followUps", "visits"])} />}

      <ConfirmDialog
        open={dialog?.type === "cancelFollowUp"}
        onClose={() => setDialog(null)}
        title="Cancel this follow-up?"
        message={dialog?.type === "cancelFollowUp" ? dialog.item.title : ""}
        confirmLabel="Yes, cancel it"
        danger
        loading={confirm.saving}
        onConfirm={() => dialog?.type === "cancelFollowUp" && void confirm.run(
          () => apiJson(`/api/leads/follow-ups/${dialog.item.id}/cancel`, jsonRequest("POST", {})), "Follow-up cancelled")}
      />
      <ConfirmDialog
        open={dialog?.type === "missed"}
        onClose={() => setDialog(null)}
        title="Did the customer not come?"
        message={dialog?.type === "missed" ? visitLine(dialog.item) : ""}
        confirmLabel="Mark as missed"
        loading={confirm.saving}
        onConfirm={() => dialog?.type === "missed" && void confirm.run(
          () => apiJson(`/api/leads/site-visits/${dialog.item.id}/missed`, jsonRequest("POST", {})), "Site visit marked as missed")}
      />
      <ConfirmDialog
        open={dialog?.type === "cancelVisit"}
        onClose={() => setDialog(null)}
        title="Cancel this site visit?"
        message={dialog?.type === "cancelVisit" ? visitLine(dialog.item) : ""}
        confirmLabel="Yes, cancel it"
        danger
        loading={confirm.saving}
        onConfirm={() => dialog?.type === "cancelVisit" && void confirm.run(
          () => apiJson(`/api/leads/site-visits/${dialog.item.id}/cancel`, jsonRequest("POST", {})), "Site visit cancelled")}
      />
      <ConfirmDialog
        open={dialog?.type === "reopen"}
        onClose={() => setDialog(null)}
        title="Reopen this lead?"
        message={`${lead.fullName} goes back to In progress${lead.assignedEmployeeName ? ` and stays with ${lead.assignedEmployeeName}` : ""}.`}
        confirmLabel="Reopen lead"
        loading={confirm.saving}
        onConfirm={() => void confirm.run(() => apiJson(`/api/leads/${lead.id}/reopen`, jsonRequest("POST", reopenBody(lead))), "Lead reopened")}
      />
    </div>
  );
}

const visitLine = (visit: SiteVisit) => `${formatAppointment(visit.scheduledAt)} at ${visit.meetingLocation}`;

/** Long text in a small card: two lines, then an ellipsis. */
function Clamp({ children }: { children: ReactNode }) {
  return <span className="line-clamp-2 break-words">{children}</span>;
}

/** Won, Lost or Dormant: what happened and when, with the one thing left to do. */
function ClosedBar({ lead, status, canOpenBooking, onOpenBooking, onReopen }: {
  lead: LeadDetail;
  status: LeadStatus;
  canOpenBooking: boolean;
  onOpenBooking: (bookingId: number) => void;
  onReopen: () => void;
}) {
  if (status === "Won") {
    const converted = [lead.convertedAt && `converted ${formatMonthDay(lead.convertedAt)}`, lead.convertedByName && `by ${lead.convertedByName}`].filter(Boolean).join(" ");
    return (
      <Notice
        tone="green"
        title={lead.convertedBookingReference ? `Won · Booking ${lead.convertedBookingReference}` : "Won"}
        message={[lead.convertedUnitNumber && `Unit ${lead.convertedUnitNumber}`, converted].filter(Boolean).join(" · ") || undefined}
        action={canOpenBooking && lead.convertedBookingId
          ? <Button variant="success" onClick={() => onOpenBooking(lead.convertedBookingId!)}>Open booking</Button>
          : undefined}
      />
    );
  }
  const dormant = status === "Dormant";
  const closedLine = [lead.closedAt && `Closed ${formatMonthDay(lead.closedAt)}`, lead.closedByName && `by ${lead.closedByName}`].filter(Boolean).join(" ");
  return (
    <Notice
      tone={dormant ? "orange" : "red"}
      title={[status, lead.closureReasonName].filter(Boolean).join(" · ")}
      message={[closedLine, dormant && lead.reactivateOn && `Bring back on ${formatDay(lead.reactivateOn)}`].filter(Boolean).join(" · ") || undefined}
      action={<Button variant={dormant ? "outline" : "danger"} onClick={onReopen}>Reopen lead</Button>}
    />
  );
}

function SectionHeader({ title, count, action }: { title: string; count?: number; action?: ReactNode }) {
  return (
    <div className="flex min-h-11 items-center justify-between gap-3">
      <h2 className="m-0 text-section font-extrabold text-ink">
        {title}
        {count != null && <span className="ml-1.5 text-ink-faint">{count.toLocaleString("en-PK")}</span>}
      </h2>
      {action}
    </div>
  );
}

function GroupHeading({ children }: { children: ReactNode }) {
  return <h3 className="m-0 mt-1 text-caption font-bold uppercase tracking-[0.4px] text-ink-muted">{children}</h3>;
}

/** A section's content once loaded; grey blocks the first time, and a retry if it failed. */
function PartView({ data, error, onRetry, children }: { data: unknown; error?: string; onRetry: () => void; children: ReactNode }) {
  if (data === undefined && error) {
    return <EmptyState title="This could not be loaded" message={error} action={<Button onClick={onRetry}>Try again</Button>} />;
  }
  if (data === undefined) {
    return (
      <div className="flex flex-col gap-3" aria-busy="true">
        {[0, 1, 2].map((n) => <div key={n} className="h-16 animate-pulse rounded-card bg-track" />)}
      </div>
    );
  }
  const stale = !!error;
  return (
    <div className="flex flex-col gap-3">
      {stale && (
        <Notice
          tone="orange"
          title="Couldn't refresh — showing older data"
          action={<Button variant="outline" onClick={onRetry}>Try again</Button>}
        />
      )}
      {children}
    </div>
  );
}

function FollowUpsSection({ items, isPhone, closed, stale, onNew, onDone, onReschedule, onCancel }: {
  items: FollowUp[];
  isPhone: boolean;
  closed: boolean;
  stale?: boolean;
  onNew: () => void;
  onDone: (item: FollowUp) => void;
  onReschedule: (item: FollowUp) => void;
  onCancel: (item: FollowUp) => void;
}) {
  const { todo, done } = followUpGroups(items);
  return (
    <>
      <SectionHeader title="Follow-ups" action={!closed && <Button icon={<IconPlus size={16} />} onClick={onNew}>{isPhone ? "New" : "New follow-up"}</Button>} />
      {todo.length + done.length === 0 && <EmptyState title="No follow-ups yet" />}
      {todo.length > 0 && <GroupHeading>To do · {todo.length}</GroupHeading>}
      {todo.map((item) => (
        <FollowUpItem
          key={item.id}
          item={item}
          onDone={closed || stale ? undefined : () => onDone(item)}
          onReschedule={stale ? undefined : () => onReschedule(item)}
          onCancel={stale ? undefined : () => onCancel(item)}
        />
      ))}
      {done.length > 0 && <GroupHeading>Done · {done.length}</GroupHeading>}
      {done.map((item) => <FollowUpItem key={item.id} item={item} />)}
    </>
  );
}

function VisitsSection({ items, isPhone, closed, stale, onNew, onAction }: {
  items: SiteVisit[];
  isPhone: boolean;
  closed: boolean;
  stale?: boolean;
  onNew: () => void;
  onAction: (type: "visitDone" | "rescheduleVisit" | "missed" | "cancelVisit", item: SiteVisit) => void;
}) {
  const { upcoming, past } = visitGroups(items);
  const actionsFor = (item: SiteVisit) => closed || stale ? undefined : {
    onDone: () => onAction("visitDone", item),
    onReschedule: () => onAction("rescheduleVisit", item),
    onMissed: () => onAction("missed", item),
    onCancel: () => onAction("cancelVisit", item),
  };
  return (
    <>
      <SectionHeader title="Site visits" action={!closed && <Button icon={<IconPlus size={16} />} onClick={onNew}>{isPhone ? "Schedule" : "Schedule site visit"}</Button>} />
      {upcoming.length + past.length === 0 && <EmptyState title="No site visits yet" />}
      {upcoming.length > 0 && <GroupHeading>Upcoming</GroupHeading>}
      {upcoming.map((item) => <SiteVisitItem key={item.id} item={item} actions={actionsFor(item)} />)}
      {past.length > 0 && <GroupHeading>Past</GroupHeading>}
      {past.map((item) => <SiteVisitItem key={item.id} item={item} actions={actionsFor(item)} />)}
    </>
  );
}
