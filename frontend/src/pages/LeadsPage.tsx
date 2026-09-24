import AppSelect from "../lib/AppSelect.tsx";
import { useCallback, useEffect, useMemo, useState, type ReactNode } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import type { User } from "../App.tsx";
import Button from "../lib/Button.tsx";
import {
  CrmAccess,
  CrmHeader,
  CrmModal,
  ErrorBanner,
  inputClass,
  Label,
  MetricCard,
  QualificationBadge,
  StageBadge,
  StatePanel,
} from "../features/leads/CrmUi.tsx";
import { apiJson, jsonRequest, loadCrmLookups, loadUnits } from "../features/leads/leadApi.ts";
import { describeDuplicate, type DuplicateMatch } from "../features/leads/duplicateResolution.ts";
import {
  formatDateTime,
  isClosedStage,
  leadStages,
  stageLabel,
  type ClosureReason,
  type Lead,
  type LeadList,
  type LeadSource,
  type ProjectLookup,
  type StaffMember,
  type Team,
  type UnitLookup,
} from "../features/leads/types.ts";

type Props = { user: User | null };
type Lookups = {
  sources: LeadSource[];
  reasons: ClosureReason[];
  teams: Team[];
  staff: StaffMember[];
  projects: ProjectLookup[];
};

const EMPTY_LOOKUPS: Lookups = { sources: [], reasons: [], teams: [], staff: [], projects: [] };

export default function LeadsPage({ user }: Props) {
  return <CrmAccess user={user}>{user && <LeadsWorkspace user={user} />}</CrmAccess>;
}

function LeadsWorkspace({ user }: { user: User }) {
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();
  const [data, setData] = useState<LeadList | null>(null);
  const [dashboard, setDashboard] = useState<Record<string, unknown> | null>(null);
  const [lookups, setLookups] = useState<Lookups>(EMPTY_LOOKUPS);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const view = params.get("view") === "pipeline" ? "pipeline" : "list";
  const page = Number(params.get("page") ?? 1);

  const updateParam = (key: string, value: string) => {
    const next = new URLSearchParams(params);
    if (value) next.set(key, value); else next.delete(key);
    if (key !== "page") next.set("page", "1");
    setParams(next);
  };

  // The bar exposes search, stage, source and project. The rest stay honoured because the URL is
  // an input surface of its own: a saved link, a bookmark or a hand-built query keeps filtering
  // exactly as it did, and the server contract is unchanged.
  const query = useMemo(() => {
    const allowed = ["search", "stage", "qualification", "sourceId", "employeeId", "teamId", "projectId", "unitId", "campaign", "unassigned", "overdue", "inactive", "createdFrom", "createdTo", "sortBy", "sortDesc"];
    const q = new URLSearchParams();
    for (const key of allowed) {
      const value = params.get(key);
      if (value) q.set(key, value);
    }
    q.set("page", String(Number.isFinite(page) && page > 0 ? page : 1));
    q.set("pageSize", view === "pipeline" ? "100" : "20");
    return q.toString();
  }, [page, params, view]);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const dashboardEndpoint =
        user.role === "Admin" ? "/api/lead-dashboard/organisation" :
        user.role === "Manager" ? "/api/lead-dashboard/team" :
        "/api/lead-dashboard/me";
      const [rows, metrics, refs] = await Promise.all([
        apiJson<LeadList>(`/api/leads?${query}`),
        apiJson<Record<string, unknown>>(dashboardEndpoint),
        loadCrmLookups(),
      ]);
      setData(rows);
      setDashboard(metrics);
      setLookups(refs);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "The lead workspace could not be loaded.");
    } finally {
      setLoading(false);
    }
  }, [query, user.role]);

  useEffect(() => { void load(); }, [load]);

  const metrics = dashboardMetrics(user.role, dashboard);

  return (
    <>
      <CrmHeader
        title={user.role === "Employee" ? "My lead workspace" : user.role === "Manager" ? "Team lead workspace" : "Lead management"}
        subtitle="Capture, assign, work, and convert property enquiries while preserving every customer interaction."
        role={user.role}
        actions={
          <>
            {user.role === "Admin" && <Button variant="outline" onClick={() => navigate("/crm/settings")}>CRM settings</Button>}
            <Button onClick={() => setCreateOpen(true)}><IconPlus className="h-4 w-4" />New lead</Button>
          </>
        }
      />

      <div className="mx-auto w-full max-w-[1500px] space-y-5 px-4 py-6 sm:px-6 lg:px-8">
        {error && <ErrorBanner message={error} onRetry={() => void load()} />}

        <section aria-label="Lead summary" className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
          {metrics.map((metric) => {
            // A card that switches a filter on has to switch it off again. Stage lands in a
            // dropdown the operator can see and reset, but "unassigned" and "overdue" have no
            // control of their own on this bar — so pressing the card again is the way back, and
            // the card shows it is pressed rather than leaving the list quietly filtered.
            const applied = !!metric.filter && params.get(metric.filter.key) === metric.filter.value;
            return (
              <MetricCard
                key={metric.label}
                label={metric.label}
                value={metric.value}
                icon={METRIC_ICONS[metric.id]}
                active={applied}
                onClick={metric.filter ? () => updateParam(metric.filter!.key, applied ? "" : metric.filter!.value) : undefined}
              />
            );
          })}
        </section>

        <section className="rounded-2xl border border-[var(--border)] bg-[var(--bg-card)] p-4">
          <div className="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
            <div className="relative w-full lg:max-w-md">
              <span aria-hidden="true" className="pointer-events-none absolute left-3.5 top-1/2 -translate-y-1/2 text-[var(--text-muted)]"><IconSearch className="h-4 w-4" /></span>
              <input
                className={`${inputClass} pl-10`}
                value={params.get("search") ?? ""}
                onChange={(event) => updateParam("search", event.target.value)}
                placeholder="Search name, phone, email, campaign…"
                aria-label="Search leads"
              />
            </div>
            <div className="flex flex-wrap items-center gap-3">
              <BarSelect label="Stage" icon={<IconLayers className="h-4 w-4" />} value={params.get("stage") ?? ""} onChange={(v) => updateParam("stage", v)} options={leadStages.map((v) => [v, stageLabel(v)])} />
              <BarSelect label="Source" icon={<IconMegaphone className="h-4 w-4" />} value={params.get("sourceId") ?? ""} onChange={(v) => updateParam("sourceId", v)} options={lookups.sources.map((v) => [String(v.id), v.name])} />
              <BarSelect label="Project" icon={<IconBuilding className="h-4 w-4" />} value={params.get("projectId") ?? ""} onChange={(v) => updateParam("projectId", v)} options={lookups.projects.map((v) => [String(v.id), v.name])} />
              <div className="inline-flex shrink-0 rounded-xl border border-[var(--border)] bg-[var(--input-bg)] p-1" role="group" aria-label="Lead view">
                <ViewButton active={view === "list"} onClick={() => updateParam("view", "list")}>List</ViewButton>
                <ViewButton active={view === "pipeline"} onClick={() => updateParam("view", "pipeline")}>Pipeline</ViewButton>
              </div>
            </div>
          </div>
        </section>

        {loading && !data ? (
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">{Array.from({ length: 6 }).map((_, i) => <div key={i} className="h-32 animate-pulse rounded-2xl bg-[var(--surface-glass)]" />)}</div>
        ) : !data || data.items.length === 0 ? (
          <StatePanel title="No leads found" message="No accessible leads match these filters. Clear the filters or capture a new enquiry." action={<Button onClick={() => setCreateOpen(true)}>Create lead</Button>} />
        ) : view === "pipeline" ? (
          <Pipeline leads={data.items} />
        ) : (
          <LeadTable leads={data.items} />
        )}

        {data && data.totalPages > 1 && (
          <div className="flex items-center justify-between text-sm text-[var(--text-muted)]">
            <span>{data.totalCount} leads</span>
            <div className="flex items-center gap-2">
              <Button size="sm" variant="outline" disabled={page <= 1} onClick={() => updateParam("page", String(page - 1))}>Previous</Button>
              <span>Page {data.page} of {data.totalPages}</span>
              <Button size="sm" variant="outline" disabled={page >= data.totalPages} onClick={() => updateParam("page", String(page + 1))}>Next</Button>
            </div>
          </div>
        )}
      </div>

      <LeadCreateModal
        open={createOpen}
        onClose={() => setCreateOpen(false)}
        lookups={lookups}
        canAssign={user.role === "Admin" || user.role === "Manager"}
        onCreated={(leadId) => navigate(`/crm/leads/${leadId}`)}
      />
    </>
  );
}

function LeadTable({ leads }: { leads: Lead[] }) {
  return (
    <>
      <div className="hidden overflow-hidden rounded-2xl border border-[var(--border)] bg-[var(--bg-card)] md:block">
        <div className="overflow-x-auto">
          <table className="w-full min-w-[980px] text-left">
            <thead className="border-b border-[var(--border)] bg-[var(--surface-glass)] text-[10px] font-semibold uppercase tracking-[0.09em] text-[var(--text-muted)]">
              <tr>{["Lead", "Interest", "Stage", "Qualification", "Owner / Team", "Last activity", "Next action", "Created"].map((h) => <th className="px-4 py-4 align-bottom" key={h}>{h}</th>)}</tr>
            </thead>
            <tbody>
              {leads.map((lead) => {
                const closed = isClosedStage(lead.stage);
                const overdue = lead.nextActionAt && new Date(lead.nextActionAt) < new Date() && !closed;
                return (
                  <tr key={lead.id} className="border-b border-[var(--border)] align-top transition last:border-0 hover:bg-[var(--surface-glass-hover)]">
                    <td className="px-4 py-4">
                      <Link className="font-semibold text-[var(--text-heading)] transition hover:text-[var(--accent)]" to={`/crm/leads/${lead.id}`}>{lead.fullName}</Link>
                      <p className="mt-1 text-xs text-[var(--text-muted)]">{lead.leadReference}{(lead.phone ?? lead.whatsappNumber ?? lead.email) && ` · ${lead.phone ?? lead.whatsappNumber ?? lead.email}`}</p>
                      <p className="text-xs text-[var(--text-muted)]">{lead.sourceName}</p>
                    </td>
                    <td className="max-w-[190px] px-4 py-4 text-sm text-[var(--text-secondary)]">{lead.interestedProjectName ?? lead.preferredLocation ?? "General enquiry"}{lead.interestedUnitNumber && <p className="mt-1 text-xs text-[var(--text-muted)]">Unit {lead.interestedUnitNumber}</p>}</td>
                    <td className="whitespace-nowrap px-4 py-4"><StageBadge stage={lead.stage} /></td>
                    <td className="whitespace-nowrap px-4 py-4"><QualificationBadge value={lead.qualification} /></td>
                    <td className="px-4 py-4 text-sm text-[var(--text-secondary)]">{lead.assignedEmployeeName ?? "Unassigned"}<p className="mt-1 text-xs text-[var(--text-muted)]">{lead.assignedTeamName ?? "No team"}</p></td>
                    <td className="max-w-[220px] px-4 py-4 text-sm text-[var(--text-secondary)]">{lead.lastActivitySummary ?? "No activity"}<p className="mt-1 text-xs text-[var(--text-muted)]">{formatDateTime(lead.lastActivityAt)}</p></td>
                    {/* A closed lead has nothing scheduled by design, so it says so rather than
                        reading as an omission next to leads that really are missing a next step. */}
                    <td className={`max-w-[190px] px-4 py-4 text-sm ${overdue ? "font-semibold text-rose-400" : "text-[var(--text-secondary)]"}`}>{lead.nextActionSummary ?? "—"}<p className={`mt-1 text-xs ${overdue ? "" : "text-[var(--text-muted)]"}`}>{closed ? "Completed" : lead.nextActionAt ? formatDateTime(lead.nextActionAt) : "Not scheduled"}</p></td>
                    <td className="px-4 py-4 text-xs text-[var(--text-muted)]">{formatDateTime(lead.createdAt)}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </div>
      <div className="grid gap-3 md:hidden">
        {leads.map((lead) => (
          <Link key={lead.id} to={`/crm/leads/${lead.id}`} className="rounded-2xl border border-[var(--border)] bg-[var(--bg-card)] p-4">
            <div className="flex items-start justify-between gap-3"><div><p className="font-semibold text-[var(--text-heading)]">{lead.fullName}</p><p className="text-xs text-[var(--text-muted)]">{lead.leadReference} · {lead.phone ?? lead.whatsappNumber ?? lead.email ?? "No contact details"}</p></div><StageBadge stage={lead.stage} /></div>
            <div className="mt-4 grid grid-cols-2 gap-3 text-xs text-[var(--text-muted)]"><div><p className="uppercase">Owner</p><p className="mt-1 text-sm text-[var(--text-secondary)]">{lead.assignedEmployeeName ?? "Unassigned"}</p></div><div><p className="uppercase">Next action</p><p className="mt-1 text-sm text-[var(--text-secondary)]">{isClosedStage(lead.stage) ? "Completed" : formatDateTime(lead.nextActionAt)}</p></div></div>
          </Link>
        ))}
      </div>
    </>
  );
}

function Pipeline({ leads }: { leads: Lead[] }) {
  const activeStages = leadStages.filter((stage) => !isClosedStage(stage));
  return (
    <div className="flex snap-x gap-3 overflow-x-auto pb-3" aria-label="Lead pipeline">
      {activeStages.map((stage) => {
        const items = leads.filter((lead) => lead.stage === stage);
        return (
          <section key={stage} className="w-[290px] shrink-0 snap-start rounded-2xl border border-[var(--border)] bg-[var(--surface-glass)] p-3">
            <div className="mb-3 flex items-center justify-between"><h2 className="text-sm font-semibold text-[var(--text-heading)]">{stageLabel(stage)}</h2><span className="rounded-full bg-[var(--bg-card)] px-2 py-0.5 text-xs text-[var(--text-muted)]">{items.length}</span></div>
            <div className="space-y-2">
              {items.length === 0 && <p className="rounded-xl border border-dashed border-[var(--border)] px-3 py-8 text-center text-xs text-[var(--text-muted)]">No leads</p>}
              {items.map((lead) => (
                <Link key={lead.id} to={`/crm/leads/${lead.id}`} className="block rounded-xl border border-[var(--border)] bg-[var(--bg-card)] p-3 transition hover:border-[var(--accent)]/40">
                  <div className="flex items-start justify-between gap-2"><p className="font-semibold text-[var(--text-heading)]">{lead.fullName}</p><QualificationBadge value={lead.qualification} /></div>
                  <p className="mt-1 text-xs text-[var(--text-muted)]">{lead.leadReference} · {lead.sourceName}</p>
                  <p className="mt-3 truncate text-xs text-[var(--text-secondary)]">{lead.interestedProjectName ?? "General property enquiry"}</p>
                  <div className="mt-3 border-t border-[var(--border)] pt-2 text-xs text-[var(--text-muted)]"><p>{lead.assignedEmployeeName ?? "Unassigned"}</p><p className={lead.nextActionAt && new Date(lead.nextActionAt) < new Date() ? "mt-1 text-rose-400" : "mt-1"}>{lead.nextActionAt ? `Next: ${formatDateTime(lead.nextActionAt)}` : "No next action"}</p></div>
                </Link>
              ))}
            </div>
          </section>
        );
      })}
    </div>
  );
}

function LeadCreateModal({ open, onClose, lookups, canAssign, onCreated }: { open: boolean; onClose: () => void; lookups: Lookups; canAssign: boolean; onCreated: (id: number) => void }) {
  const initial = { firstName: "", lastName: "", phone: "", whatsappNumber: "", email: "", city: "", address: "", preferredContactMethod: "Phone", preferredContactTime: "", sourceCode: "manual", sourceDetails: "", campaignName: "", interestedProjectId: "", interestedUnitId: "", propertyType: "", preferredLocation: "", budgetMin: "", budgetMax: "", purchaseIntent: "Unknown", notes: "", assignedEmployeeId: "", assignedTeamId: "" };
  const [form, setForm] = useState(initial);
  const [units, setUnits] = useState<UnitLookup[]>([]);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [duplicate, setDuplicate] = useState<DuplicateMatch | null>(null);
  const duplicateResolution = duplicate ? describeDuplicate(duplicate) : null;

  const set = (key: keyof typeof form, value: string) => {
    setForm((current) => ({ ...current, [key]: value }));
    // The duplicate choices describe the lead these contact details matched. Once they change,
    // resubmitting could match nothing and create a lead the "add to" button never promised.
    if (key === "phone" || key === "whatsappNumber" || key === "email") setDuplicate(null);
  };

  useEffect(() => {
    const id = Number(form.interestedProjectId);
    if (!id) { setUnits([]); return; }
    void loadUnits(id).then(setUnits).catch(() => setUnits([]));
  }, [form.interestedProjectId]);

  // With addToLeadId, the enquiry may only be added to that lead; the API writes nothing if it no
  // longer matches, rather than enriching another lead or creating a new one.
  const submit = async (addToLeadId?: number) => {
    if (!form.firstName.trim()) { setError("A first name is required."); return; }
    // Staff capturing a lead by hand have the person in front of them, so require a way to
    // reach them — but any one channel will do, matching what the API enforces.
    const hasPhone = form.phone.trim().length >= 7;
    if (!hasPhone && !form.whatsappNumber.trim() && !form.email.trim()) {
      setError("Record at least one way to reach this person: a phone number, a WhatsApp number, or an email address.");
      return;
    }
    if (form.phone.trim() && !hasPhone) { setError("That phone number is too short to be usable."); return; }
    setSaving(true); setError(null); setDuplicate(null);
    try {
      const result = await apiJson<{ isDuplicate: boolean; message?: string; match?: DuplicateMatch; lead?: Lead }>(
        "/api/leads",
        jsonRequest("POST", {
          ...form,
          interestedProjectId: form.interestedProjectId ? Number(form.interestedProjectId) : null,
          interestedUnitId: form.interestedUnitId ? Number(form.interestedUnitId) : null,
          budgetMin: form.budgetMin ? Number(form.budgetMin) : null,
          budgetMax: form.budgetMax ? Number(form.budgetMax) : null,
          assignedEmployeeId: canAssign && form.assignedEmployeeId ? Number(form.assignedEmployeeId) : null,
          assignedTeamId: canAssign && form.assignedTeamId ? Number(form.assignedTeamId) : null,
          allowDuplicate: addToLeadId != null,
          expectedExistingLeadId: addToLeadId ?? null,
        }),
      );
      if (result.isDuplicate && !result.lead) {
        setDuplicate(result.match ?? {});
        // Only an add that was refused needs explaining; a first-time match speaks for itself.
        if (addToLeadId != null && result.message) setError(result.message);
        return;
      }
      if (result.lead) { onCreated(result.lead.id); return; }
      setError(result.message || "Nothing was saved. Review the details and try again.");
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "The lead could not be created. Your form values have been preserved.");
    } finally { setSaving(false); }
  };

  return (
    <CrmModal open={open} onClose={onClose} wide title="Capture a new lead" subtitle="Duplicate matching runs before a new prospect is created." footer={<div className="flex justify-end gap-2"><Button variant="ghost" onClick={onClose} disabled={saving}>Cancel</Button><Button onClick={() => void submit()} disabled={saving}>{saving ? "Checking…" : "Create lead"}</Button></div>}>
      <form onSubmit={(e) => { e.preventDefault(); void submit(); }} className="space-y-5">
        {error && <ErrorBanner message={error} />}
        {duplicateResolution && (
          <div className="rounded-xl border border-amber-500/25 bg-amber-500/[0.08] p-4 text-sm text-amber-200">
            <p className="font-semibold">{duplicateResolution.heading}</p>
            <p className="mt-1">{duplicateResolution.explanation}</p>
            {duplicateResolution.addLabel && <p className="mt-1">{duplicateResolution.addOutcome}</p>}
            <div className="mt-3 flex flex-wrap gap-2">
              {duplicate?.leadId && <Button size="sm" onClick={() => onCreated(duplicate.leadId!)}>{duplicateResolution.openLabel}</Button>}
              {duplicateResolution.addLabel && <Button size="sm" variant="outline" onClick={() => void submit(duplicate!.leadId!)} disabled={saving}>{duplicateResolution.addLabel}</Button>}
            </div>
          </div>
        )}
        <FormSection title="Contact">
          <TextField label="First name" required value={form.firstName} onChange={(v) => set("firstName", v)} />
          <TextField label="Last name" value={form.lastName} onChange={(v) => set("lastName", v)} />
          <TextField label="Phone" value={form.phone} onChange={(v) => set("phone", v)} inputMode="tel" />
          <TextField label="WhatsApp" value={form.whatsappNumber} onChange={(v) => set("whatsappNumber", v)} inputMode="tel" />
          <TextField label="Email" value={form.email} onChange={(v) => set("email", v)} type="email" />
          <TextField label="City" value={form.city} onChange={(v) => set("city", v)} />
          <TextField label="Address" value={form.address} onChange={(v) => set("address", v)} wide />
          <SelectField label="Preferred contact" value={form.preferredContactMethod} onChange={(v) => set("preferredContactMethod", v)} options={["Phone", "Whatsapp", "Email", "Sms", "InPerson"].map((v) => [v, stageLabel(v)])} />
          <TextField label="Preferred time" value={form.preferredContactTime} onChange={(v) => set("preferredContactTime", v)} />
        </FormSection>
        <FormSection title="Source and attribution">
          <SelectField label="Lead source" required value={form.sourceCode} onChange={(v) => set("sourceCode", v)} options={lookups.sources.map((v) => [v.code, v.name])} />
          <TextField label="Source details" value={form.sourceDetails} onChange={(v) => set("sourceDetails", v)} />
          <TextField label="Campaign" value={form.campaignName} onChange={(v) => set("campaignName", v)} />
        </FormSection>
        <FormSection title="Property interest">
          <SelectField label="Project" value={form.interestedProjectId} onChange={(v) => { set("interestedProjectId", v); set("interestedUnitId", ""); }} options={lookups.projects.map((v) => [String(v.id), v.name])} />
          <SelectField label="Unit" value={form.interestedUnitId} onChange={(v) => set("interestedUnitId", v)} options={units.map((v) => [String(v.id), v.number])} />
          <TextField label="Property type" value={form.propertyType} onChange={(v) => set("propertyType", v)} />
          <TextField label="Preferred location" value={form.preferredLocation} onChange={(v) => set("preferredLocation", v)} />
          <TextField label="Minimum budget" value={form.budgetMin} onChange={(v) => set("budgetMin", v)} type="number" />
          <TextField label="Maximum budget" value={form.budgetMax} onChange={(v) => set("budgetMax", v)} type="number" />
          <SelectField label="Purchase intent" value={form.purchaseIntent} onChange={(v) => set("purchaseIntent", v)} options={["Unknown", "SelfUse", "Investment", "Rental", "Resale"].map((v) => [v, stageLabel(v)])} />
        </FormSection>
        {canAssign && <FormSection title="Ownership"><SelectField label="Team" value={form.assignedTeamId} onChange={(v) => set("assignedTeamId", v)} options={lookups.teams.map((v) => [String(v.id), v.name])} /><SelectField label="Employee" value={form.assignedEmployeeId} onChange={(v) => set("assignedEmployeeId", v)} options={lookups.staff.filter((v) => v.canOwnLeads).map((v) => [String(v.employeeId), v.fullName])} /></FormSection>}
        <div><Label>Initial notes</Label><textarea className={`${inputClass} min-h-24 resize-y`} value={form.notes} onChange={(e) => set("notes", e.target.value)} /></div>
      </form>
    </CrmModal>
  );
}

/**
 * Four figures, one per card, in the shape the design asks for: how many, how many closed each way,
 * and the rate that falls out of the two. Each role gets the four that decide what it does next.
 */
function dashboardMetrics(role: string, dashboard: Record<string, unknown> | null) {
  const d = dashboard ?? {};
  const n = (key: string) => Number(d[key] ?? 0);
  if (role === "Admin") return [
    { id: "total", label: "All leads", value: n("totalLeads") },
    { id: "won", label: "Won", value: n("wonLeads"), filter: { key: "stage", value: "Won" } },
    { id: "lost", label: "Lost", value: n("lostLeads"), filter: { key: "stage", value: "Lost" } },
    { id: "rate", label: "Conversion", value: `${n("conversionRatePercent").toFixed(1)}%` },
  ];
  if (role === "Manager") return [
    { id: "total", label: "Team leads", value: n("teamLeads") },
    { id: "unassigned", label: "Unassigned", value: n("unassignedLeads"), filter: { key: "unassigned", value: "true" } },
    { id: "overdue", label: "Follow-ups overdue", value: n("overdueFollowUps"), filter: { key: "overdue", value: "true" } },
    { id: "visits", label: "Site visits", value: n("upcomingSiteVisits") },
  ];
  return [
    { id: "total", label: "Active leads", value: n("activeLeads") },
    { id: "due", label: "Due today", value: n("followUpsDueToday") },
    { id: "overdue", label: "Overdue", value: n("overdueFollowUps"), filter: { key: "overdue", value: "true" } },
    { id: "visits", label: "Upcoming visits", value: n("upcomingSiteVisits") },
  ];
}

const METRIC_ICONS: Record<string, ReactNode> = {
  total: <IconUsers className="h-5 w-5" />,
  won: <IconTrophy className="h-5 w-5" />,
  lost: <IconCircleX className="h-5 w-5" />,
  rate: <IconTrendingUp className="h-5 w-5" />,
  unassigned: <IconUserQuestion className="h-5 w-5" />,
  overdue: <IconClock className="h-5 w-5" />,
  due: <IconClock className="h-5 w-5" />,
  visits: <IconCalendar className="h-5 w-5" />,
};

/**
 * A native select wearing the mock's chip: the platform's own dropdown on every device — keyboard,
 * screen reader and mobile picker included — with only the arrow replaced so the chip keeps a fixed
 * width instead of growing to its longest option.
 */
function BarSelect({ label, icon, value, onChange, options }: { label: string; icon: ReactNode; value: string; onChange: (value: string) => void; options: readonly (readonly string[])[] }) {
  return (
    <div className="relative shrink-0">
      <span aria-hidden="true" className="pointer-events-none absolute -top-2 left-2.5 z-10 rounded bg-[var(--bg-card)] px-1 text-[10px] font-semibold uppercase tracking-wider text-[var(--text-muted)]">{label}</span>
      <span aria-hidden="true" className="pointer-events-none absolute left-3 top-1/2 z-10 -translate-y-1/2 text-[var(--accent)]">{icon}</span>
      <AppSelect
        aria-label={label}
        className="w-[124px] cursor-pointer appearance-none rounded-xl border border-[var(--border)] bg-[var(--input-bg)] py-2.5 pl-9 pr-8 text-sm font-medium text-[var(--text-primary)] outline-none transition hover:border-[var(--border-hover)] focus:border-[var(--accent)] focus:ring-2 focus:ring-[var(--accent-glow)]"
        value={value}
        onChange={(event) => onChange(event.target.value)}
      >
        <option value="">All</option>
        {options.map(([v, l]) => <option key={v} value={v}>{l}</option>)}
      </AppSelect>
      <span aria-hidden="true" className="pointer-events-none absolute right-3 top-1/2 -translate-y-1/2 text-[var(--text-muted)]"><IconChevronDown className="h-3.5 w-3.5" /></span>
    </div>
  );
}

function ViewButton({ active, onClick, children }: { active: boolean; onClick: () => void; children: string }) {
  return (
    <button
      type="button"
      aria-pressed={active}
      onClick={onClick}
      className={`cursor-pointer rounded-lg px-4 py-1.5 text-sm font-semibold transition ${active ? "bg-[var(--accent)] text-[#1c1810]" : "text-[var(--text-muted)] hover:text-[var(--text-primary)]"}`}
    >
      {children}
    </button>
  );
}

function FormSection({ title, children }: { title: string; children: React.ReactNode }) {
  return <fieldset><legend className="mb-3 text-sm font-semibold text-[var(--text-heading)]">{title}</legend><div className="grid gap-3 sm:grid-cols-2">{children}</div></fieldset>;
}
function TextField({ label, value, onChange, required, wide, type = "text", inputMode }: { label: string; value: string; onChange: (value: string) => void; required?: boolean; wide?: boolean; type?: string; inputMode?: React.HTMLAttributes<HTMLInputElement>["inputMode"] }) {
  return <div className={wide ? "sm:col-span-2" : ""}><Label required={required}>{label}</Label><input required={required} className={inputClass} value={value} onChange={(e) => onChange(e.target.value)} type={type} inputMode={inputMode} /></div>;
}
function SelectField({ label, value, onChange, options, required }: { label: string; value: string; onChange: (value: string) => void; options: string[][]; required?: boolean }) {
  return <div><Label required={required}>{label}</Label><AppSelect required={required} className={inputClass} value={value} onChange={(e) => onChange(e.target.value)}><option value="">Select…</option>{options.map(([v, l]) => <option key={v} value={v}>{l}</option>)}</AppSelect></div>;
}

/* Icons are inline SVG on purpose: nothing to install, version or ship, no runtime cost beyond the
   markup, and each glyph travels inside this page's own chunk. One 24px stroked grid throughout. */
const ico = (className?: string) => ({
  className: className ?? "h-4 w-4", viewBox: "0 0 24 24", fill: "none", stroke: "currentColor",
  strokeWidth: 1.8, strokeLinecap: "round" as const, strokeLinejoin: "round" as const, "aria-hidden": true,
});
type IconProps = { className?: string };

function IconPlus({ className }: IconProps) { return <svg {...ico(className)} strokeWidth={2.2}><path d="M12 5v14" /><path d="M5 12h14" /></svg>; }
function IconSearch({ className }: IconProps) { return <svg {...ico(className)}><circle cx="11" cy="11" r="7" /><path d="m20 20-3.2-3.2" /></svg>; }
function IconChevronDown({ className }: IconProps) { return <svg {...ico(className)} strokeWidth={2.4}><path d="m6 9 6 6 6-6" /></svg>; }
function IconLayers({ className }: IconProps) { return <svg {...ico(className)}><path d="m12 3 9 5-9 5-9-5 9-5Z" /><path d="m3 14 9 5 9-5" /></svg>; }
function IconMegaphone({ className }: IconProps) { return <svg {...ico(className)}><path d="M3 11v2a1 1 0 0 0 1 1h2l5 4V6L6 10H4a1 1 0 0 0-1 1Z" /><path d="M16 9a4 4 0 0 1 0 6" /><path d="M19 6a8 8 0 0 1 0 12" /></svg>; }
function IconBuilding({ className }: IconProps) { return <svg {...ico(className)}><rect x="4" y="3" width="16" height="18" rx="2" /><path d="M9 7h1M14 7h1M9 11h1M14 11h1M9 15h1M14 15h1" /><path d="M10 21v-3h4v3" /></svg>; }
function IconUsers({ className }: IconProps) { return <svg {...ico(className)}><circle cx="9" cy="8" r="3.2" /><path d="M3 20a6 6 0 0 1 12 0" /><path d="M16.5 5.5a3.2 3.2 0 0 1 0 5.6" /><path d="M18 14.6A6 6 0 0 1 21 20" /></svg>; }
function IconTrophy({ className }: IconProps) { return <svg {...ico(className)}><path d="M7 4h10v5a5 5 0 0 1-10 0V4Z" /><path d="M7 6H4v1a3 3 0 0 0 3 3" /><path d="M17 6h3v1a3 3 0 0 1-3 3" /><path d="M12 14v3" /><path d="M8.5 20h7" /><path d="M10 17h4l.5 3h-5l.5-3Z" /></svg>; }
function IconCircleX({ className }: IconProps) { return <svg {...ico(className)}><circle cx="12" cy="12" r="9" /><path d="m15 9-6 6" /><path d="m9 9 6 6" /></svg>; }
function IconTrendingUp({ className }: IconProps) { return <svg {...ico(className)}><path d="m3 17 6-6 4 4 8-8" /><path d="M15 7h6v6" /></svg>; }
function IconUserQuestion({ className }: IconProps) { return <svg {...ico(className)}><circle cx="10" cy="8" r="3.2" /><path d="M4 20a6 6 0 0 1 12 0" /><path d="M18.5 8.5a1.6 1.6 0 1 1 2.2 1.5c-.5.3-.7.7-.7 1.2" /><path d="M20 14h.01" /></svg>; }
function IconClock({ className }: IconProps) { return <svg {...ico(className)}><circle cx="12" cy="12" r="9" /><path d="M12 7v5.2l3.2 2" /></svg>; }
function IconCalendar({ className }: IconProps) { return <svg {...ico(className)}><rect x="3" y="5" width="18" height="16" rx="2" /><path d="M3 10h18" /><path d="M8 3v4" /><path d="M16 3v4" /></svg>; }
