import { useCallback, useEffect, useMemo, useState } from "react";
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
import {
  formatDateTime,
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
  const [filterUnits, setFilterUnits] = useState<UnitLookup[]>([]);
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
  useEffect(() => {
    const projectId = Number(params.get("projectId"));
    if (!projectId) { setFilterUnits([]); return; }
    void loadUnits(projectId).then(setFilterUnits).catch(() => setFilterUnits([]));
  }, [params]);

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
            <Button onClick={() => setCreateOpen(true)}>+ New lead</Button>
          </>
        }
      />

      <div className="mx-auto w-full max-w-[1500px] space-y-5 px-4 py-6 sm:px-6 lg:px-8">
        {error && <ErrorBanner message={error} onRetry={() => void load()} />}

        <section aria-label="Attention areas" className="grid grid-cols-2 gap-3 lg:grid-cols-4 xl:grid-cols-6">
          {metrics.map((metric) => (
            <MetricCard
              key={metric.label}
              label={metric.label}
              value={metric.value}
              onClick={metric.filter ? () => updateParam(metric.filter!.key, metric.filter!.value) : undefined}
            />
          ))}
        </section>
        {dashboard && <DashboardInsights role={user.role} dashboard={dashboard} />}

        <section className="rounded-2xl border border-[var(--border)] bg-[var(--bg-card)] p-4">
          <div className="flex flex-col gap-3 lg:flex-row lg:items-center">
            <div className="relative flex-1">
              <span className="pointer-events-none absolute left-3 top-2.5 text-[var(--text-muted)]">⌕</span>
              <input
                className={`${inputClass} pl-9`}
                value={params.get("search") ?? ""}
                onChange={(event) => updateParam("search", event.target.value)}
                placeholder="Search name, phone, email, reference, campaign…"
                aria-label="Search leads"
              />
            </div>
            <div className="flex flex-wrap gap-2">
              <button className={`rounded-lg px-3 py-2 text-sm font-semibold ${view === "list" ? "bg-[var(--accent)] text-white" : "bg-[var(--surface-glass)] text-[var(--text-muted)]"}`} onClick={() => updateParam("view", "list")}>List</button>
              <button className={`rounded-lg px-3 py-2 text-sm font-semibold ${view === "pipeline" ? "bg-[var(--accent)] text-white" : "bg-[var(--surface-glass)] text-[var(--text-muted)]"}`} onClick={() => updateParam("view", "pipeline")}>Pipeline</button>
            </div>
          </div>

          <div className="mt-3 grid gap-2 sm:grid-cols-2 lg:grid-cols-4 xl:grid-cols-6">
            <FilterSelect label="Stage" value={params.get("stage") ?? ""} onChange={(v) => updateParam("stage", v)} options={leadStages.map((v) => [v, stageLabel(v)])} />
            <FilterSelect label="Qualification" value={params.get("qualification") ?? ""} onChange={(v) => updateParam("qualification", v)} options={["Unqualified", "Cold", "Warm", "Hot"].map((v) => [v, v])} />
            <FilterSelect label="Source" value={params.get("sourceId") ?? ""} onChange={(v) => updateParam("sourceId", v)} options={lookups.sources.map((v) => [String(v.id), v.name])} />
            <FilterSelect label="Owner" value={params.get("employeeId") ?? ""} onChange={(v) => updateParam("employeeId", v)} options={lookups.staff.filter((v) => v.canOwnLeads).map((v) => [String(v.employeeId), v.fullName])} />
            <FilterSelect label="Team" value={params.get("teamId") ?? ""} onChange={(v) => updateParam("teamId", v)} options={lookups.teams.map((v) => [String(v.id), v.name])} />
            <FilterSelect label="Project" value={params.get("projectId") ?? ""} onChange={(v) => updateParam("projectId", v)} options={lookups.projects.map((v) => [String(v.id), v.name])} />
            <FilterSelect label="Unit" value={params.get("unitId") ?? ""} onChange={(v) => updateParam("unitId", v)} options={filterUnits.map((v) => [String(v.id), v.number])} />
            <input aria-label="Campaign filter" className={inputClass} value={params.get("campaign") ?? ""} onChange={(e) => updateParam("campaign", e.target.value)} placeholder="Campaign: All" />
            <FilterSelect label="Sort" value={params.get("sortBy") ?? "createdat"} onChange={(v) => updateParam("sortBy", v)} options={[["createdat", "Created date"], ["lastactivity", "Last activity"], ["nextaction", "Next action"], ["stage", "Stage"]]} />
          </div>
          <div className="mt-3 flex flex-wrap items-center gap-2">
            <QuickFilter active={params.get("unassigned") === "true"} onClick={() => updateParam("unassigned", params.get("unassigned") === "true" ? "" : "true")}>Unassigned</QuickFilter>
            <QuickFilter active={params.get("overdue") === "true"} onClick={() => updateParam("overdue", params.get("overdue") === "true" ? "" : "true")}>Overdue next action</QuickFilter>
            <QuickFilter active={params.get("inactive") === "true"} onClick={() => updateParam("inactive", params.get("inactive") === "true" ? "" : "true")}>Inactive</QuickFilter>
            <QuickFilter active={params.get("sortDesc") !== "false"} onClick={() => updateParam("sortDesc", params.get("sortDesc") === "false" ? "true" : "false")}>{params.get("sortDesc") === "false" ? "Ascending" : "Descending"}</QuickFilter>
            <label className="text-xs text-[var(--text-muted)]">From <input type="date" className="ml-1 rounded-lg border border-[var(--border)] bg-[var(--input-bg)] px-2 py-1.5" value={params.get("createdFrom") ?? ""} onChange={(e) => updateParam("createdFrom", e.target.value)} /></label>
            <label className="text-xs text-[var(--text-muted)]">To <input type="date" className="ml-1 rounded-lg border border-[var(--border)] bg-[var(--input-bg)] px-2 py-1.5" value={params.get("createdTo") ?? ""} onChange={(e) => updateParam("createdTo", e.target.value)} /></label>
            {Array.from(params.keys()).some((key) => key !== "view") && (
              <button type="button" className="ml-auto text-xs font-semibold text-[var(--accent)] hover:underline" onClick={() => setParams(view === "pipeline" ? { view: "pipeline" } : {})}>Clear filters</button>
            )}
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

function DashboardInsights({ role, dashboard }: { role: string; dashboard: Record<string, unknown> }) {
  const rows = (key: string) => Array.isArray(dashboard[key]) ? dashboard[key] as Record<string, unknown>[] : [];
  const byStage = rows("byStage");
  const performance = rows("byEmployee");
  const bySource = rows("bySource");
  const byCampaign = rows("byCampaign");
  const lossReasons = rows("lossReasons");
  const byTeam = rows("byTeam");
  const maxStage = Math.max(1, ...byStage.map((row) => Number(row.count ?? 0)));
  if (!byStage.length && !performance.length && !bySource.length) return null;
  return (
    <section className="grid gap-3 lg:grid-cols-3" aria-label="CRM reporting">
      <div className="rounded-2xl border border-[var(--border)] bg-[var(--bg-card)] p-4">
        <h2 className="text-sm font-semibold text-[var(--text-heading)]">Pipeline health</h2>
        <div className="mt-4 space-y-3">{byStage.map((row) => <div key={String(row.stage)}><div className="mb-1 flex justify-between text-xs"><span className="text-[var(--text-secondary)]">{stageLabel(String(row.stage))}</span><span className="text-[var(--text-muted)]">{Number(row.count ?? 0)} · {Number(row.averageAgeDays ?? 0).toFixed(1)}d avg</span></div><div className="h-1.5 overflow-hidden rounded-full bg-[var(--surface-glass)]"><div className="h-full rounded-full bg-[var(--accent)]" style={{ width: `${Math.max(3, Number(row.count ?? 0) / maxStage * 100)}%` }} /></div></div>)}</div>
      </div>
      {(role === "Admin" ? bySource : performance).length > 0 && (
        <div className="rounded-2xl border border-[var(--border)] bg-[var(--bg-card)] p-4 lg:col-span-2">
          <h2 className="text-sm font-semibold text-[var(--text-heading)]">{role === "Admin" ? "Source performance" : role === "Manager" ? "Team performance" : "Personal pipeline"}</h2>
          <div className="mt-3 overflow-x-auto">
            <table className="w-full min-w-[520px] text-left text-xs">
              <thead className="uppercase tracking-wider text-[var(--text-muted)]"><tr>{role === "Admin" ? <><th className="py-2">Source</th><th>Total</th><th>Qualified</th><th>Won</th><th>Conversion</th></> : <><th className="py-2">Employee</th><th>Active</th><th>Won</th><th>Overdue</th><th>Conversion</th></>}</tr></thead>
              <tbody>{(role === "Admin" ? bySource : performance).slice(0, 8).map((row, index) => <tr className="border-t border-[var(--border)]" key={index}>{role === "Admin" ? <><td className="py-3 font-medium text-[var(--text-heading)]">{String(row.sourceName ?? "Unknown")}</td><td>{Number(row.totalLeads ?? 0)}</td><td>{Number(row.qualifiedLeads ?? 0)}</td><td>{Number(row.wonLeads ?? 0)}</td><td>{Number(row.sourceToBookingPercent ?? 0).toFixed(1)}%</td></> : <><td className="py-3 font-medium text-[var(--text-heading)]">{String(row.employeeName ?? "Unassigned")}</td><td>{Number(row.activeLeads ?? 0)}</td><td>{Number(row.wonLeads ?? 0)}</td><td>{Number(row.overdueFollowUps ?? 0)}</td><td>{Number(row.conversionRatePercent ?? 0).toFixed(1)}%</td></>}</tr>)}</tbody>
            </table>
          </div>
        </div>
      )}
      {role === "Admin" && (
        <div className="grid gap-3 lg:col-span-3 md:grid-cols-3">
          <ReportList title="Campaign attribution" rows={byCampaign.slice(0, 6).map((row) => [String(row.campaignName ?? "Unattributed"), `${Number(row.totalLeads ?? 0)} leads · ${Number(row.conversionRatePercent ?? 0).toFixed(1)}%`])} />
          <ReportList title="Lost reasons" rows={lossReasons.slice(0, 6).map((row) => [String(row.reasonName ?? "Other"), String(Number(row.count ?? 0))])} />
          <ReportList title="Team conversion" rows={byTeam.slice(0, 6).map((row) => [String(row.teamName ?? "Unassigned"), `${Number(row.wonLeads ?? 0)}/${Number(row.totalLeads ?? 0)} · ${Number(row.conversionRatePercent ?? 0).toFixed(1)}%`])} />
        </div>
      )}
    </section>
  );
}

function ReportList({ title, rows }: { title: string; rows: string[][] }) {
  return <div className="rounded-2xl border border-[var(--border)] bg-[var(--bg-card)] p-4"><h2 className="text-sm font-semibold text-[var(--text-heading)]">{title}</h2>{rows.length ? <div className="mt-3 space-y-2">{rows.map(([label, value]) => <div key={label} className="flex items-center justify-between gap-3 border-t border-[var(--border)] pt-2 text-xs"><span className="truncate text-[var(--text-secondary)]">{label}</span><span className="shrink-0 text-[var(--text-muted)]">{value}</span></div>)}</div> : <p className="mt-4 text-xs text-[var(--text-muted)]">No data in this period.</p>}</div>;
}

function LeadTable({ leads }: { leads: Lead[] }) {
  return (
    <>
      <div className="hidden overflow-x-auto rounded-2xl border border-[var(--border)] bg-[var(--bg-card)] md:block">
        <table className="w-full min-w-[1120px] text-left">
          <thead className="border-b border-[var(--border)] bg-[var(--surface-glass)] text-[10px] uppercase tracking-wider text-[var(--text-muted)]">
            <tr>{["Lead", "Interest", "Stage", "Qualification", "Owner / Team", "Last activity", "Next action", "Created"].map((h) => <th className="px-4 py-3" key={h}>{h}</th>)}</tr>
          </thead>
          <tbody>
            {leads.map((lead) => {
              const overdue = lead.nextActionAt && new Date(lead.nextActionAt) < new Date() && !["Won", "Lost", "Dormant"].includes(lead.stage);
              return (
                <tr key={lead.id} className="border-b border-[var(--border)] last:border-0 hover:bg-[var(--surface-glass-hover)]">
                  <td className="px-4 py-4">
                    <Link className="font-semibold text-[var(--accent)] hover:underline" to={`/crm/leads/${lead.id}`}>{lead.fullName}</Link>
                    <p className="mt-1 text-xs text-[var(--text-muted)]">{lead.leadReference} · {lead.phone}</p>
                    <p className="text-xs text-[var(--text-muted)]">{lead.sourceName}</p>
                  </td>
                  <td className="max-w-[190px] px-4 py-4 text-sm text-[var(--text-secondary)]">{lead.interestedProjectName ?? lead.preferredLocation ?? "General enquiry"}{lead.interestedUnitNumber && <p className="text-xs text-[var(--text-muted)]">Unit {lead.interestedUnitNumber}</p>}</td>
                  <td className="px-4 py-4"><StageBadge stage={lead.stage} /></td>
                  <td className="px-4 py-4"><QualificationBadge value={lead.qualification} /></td>
                  <td className="px-4 py-4 text-sm text-[var(--text-secondary)]">{lead.assignedEmployeeName ?? "Unassigned"}<p className="text-xs text-[var(--text-muted)]">{lead.assignedTeamName ?? "No team"}</p></td>
                  <td className="max-w-[190px] px-4 py-4 text-sm text-[var(--text-secondary)]">{lead.lastActivitySummary ?? "No activity"}<p className="text-xs text-[var(--text-muted)]">{formatDateTime(lead.lastActivityAt)}</p></td>
                  <td className={`max-w-[190px] px-4 py-4 text-sm ${overdue ? "font-semibold text-rose-400" : "text-[var(--text-secondary)]"}`}>{lead.nextActionSummary ?? "Not scheduled"}<p className="text-xs">{formatDateTime(lead.nextActionAt)}</p></td>
                  <td className="px-4 py-4 text-xs text-[var(--text-muted)]">{formatDateTime(lead.createdAt)}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
      <div className="grid gap-3 md:hidden">
        {leads.map((lead) => (
          <Link key={lead.id} to={`/crm/leads/${lead.id}`} className="rounded-2xl border border-[var(--border)] bg-[var(--bg-card)] p-4">
            <div className="flex items-start justify-between gap-3"><div><p className="font-semibold text-[var(--text-heading)]">{lead.fullName}</p><p className="text-xs text-[var(--text-muted)]">{lead.leadReference} · {lead.phone}</p></div><StageBadge stage={lead.stage} /></div>
            <div className="mt-4 grid grid-cols-2 gap-3 text-xs text-[var(--text-muted)]"><div><p className="uppercase">Owner</p><p className="mt-1 text-sm text-[var(--text-secondary)]">{lead.assignedEmployeeName ?? "Unassigned"}</p></div><div><p className="uppercase">Next action</p><p className="mt-1 text-sm text-[var(--text-secondary)]">{formatDateTime(lead.nextActionAt)}</p></div></div>
          </Link>
        ))}
      </div>
    </>
  );
}

function Pipeline({ leads }: { leads: Lead[] }) {
  const activeStages = leadStages.filter((stage) => !["Won", "Lost", "Dormant"].includes(stage));
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
  const [duplicate, setDuplicate] = useState<{ leadId?: number | null; leadReference?: string | null; matchedOn?: string } | null>(null);

  const set = (key: keyof typeof form, value: string) => setForm((current) => ({ ...current, [key]: value }));

  useEffect(() => {
    const id = Number(form.interestedProjectId);
    if (!id) { setUnits([]); return; }
    void loadUnits(id).then(setUnits).catch(() => setUnits([]));
  }, [form.interestedProjectId]);

  const submit = async (allowDuplicate = false) => {
    if (!form.firstName.trim() || form.phone.trim().length < 7) { setError("First name and a valid phone number are required."); return; }
    setSaving(true); setError(null); setDuplicate(null);
    try {
      const result = await apiJson<{ isDuplicate: boolean; match?: { leadId?: number | null; leadReference?: string | null; matchedOn?: string }; lead?: Lead }>(
        "/api/leads",
        jsonRequest("POST", {
          ...form,
          interestedProjectId: form.interestedProjectId ? Number(form.interestedProjectId) : null,
          interestedUnitId: form.interestedUnitId ? Number(form.interestedUnitId) : null,
          budgetMin: form.budgetMin ? Number(form.budgetMin) : null,
          budgetMax: form.budgetMax ? Number(form.budgetMax) : null,
          assignedEmployeeId: canAssign && form.assignedEmployeeId ? Number(form.assignedEmployeeId) : null,
          assignedTeamId: canAssign && form.assignedTeamId ? Number(form.assignedTeamId) : null,
          allowDuplicate,
        }),
      );
      if (result.isDuplicate && !result.lead) { setDuplicate(result.match ?? {}); return; }
      if (result.lead) onCreated(result.lead.id);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "The lead could not be created. Your form values have been preserved.");
    } finally { setSaving(false); }
  };

  return (
    <CrmModal open={open} onClose={onClose} wide title="Capture a new lead" subtitle="Duplicate matching runs before a new prospect is created." footer={<div className="flex justify-end gap-2"><Button variant="ghost" onClick={onClose} disabled={saving}>Cancel</Button><Button onClick={() => void submit()} disabled={saving}>{saving ? "Checking…" : "Create lead"}</Button></div>}>
      <form onSubmit={(e) => { e.preventDefault(); void submit(); }} className="space-y-5">
        {error && <ErrorBanner message={error} />}
        {duplicate && (
          <div className="rounded-xl border border-amber-500/25 bg-amber-500/[0.08] p-4 text-sm text-amber-200">
            <p className="font-semibold">Possible duplicate matched on {duplicate.matchedOn ?? "contact details"}.</p>
            <p className="mt-1">DAMS will not merge an uncertain manual entry automatically.</p>
            <div className="mt-3 flex flex-wrap gap-2">
              {duplicate.leadId && <Button size="sm" onClick={() => onCreated(duplicate.leadId!)}>Open {duplicate.leadReference ?? "existing lead"}</Button>}
              <Button size="sm" variant="outline" onClick={() => void submit(true)}>Create separate lead</Button>
            </div>
          </div>
        )}
        <FormSection title="Contact">
          <TextField label="First name" required value={form.firstName} onChange={(v) => set("firstName", v)} />
          <TextField label="Last name" value={form.lastName} onChange={(v) => set("lastName", v)} />
          <TextField label="Phone" required value={form.phone} onChange={(v) => set("phone", v)} inputMode="tel" />
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

function dashboardMetrics(role: string, dashboard: Record<string, unknown> | null) {
  const d = dashboard ?? {};
  const n = (key: string) => Number(d[key] ?? 0);
  if (role === "Admin") return [
    { label: "All leads", value: n("totalLeads") },
    { label: "Open pipeline", value: n("openLeads") },
    { label: "Unassigned", value: n("unassignedLeads"), filter: { key: "unassigned", value: "true" } },
    { label: "Won", value: n("wonLeads"), filter: { key: "stage", value: "Won" } },
    { label: "Lost", value: n("lostLeads"), filter: { key: "stage", value: "Lost" } },
    { label: "Conversion", value: `${n("conversionRatePercent").toFixed(1)}%` },
  ];
  if (role === "Manager") return [
    { label: "Team leads", value: n("teamLeads") },
    { label: "Unassigned", value: n("unassignedLeads"), filter: { key: "unassigned", value: "true" } },
    { label: "First contacts overdue", value: n("overdueFirstContacts") },
    { label: "Follow-ups overdue", value: n("overdueFollowUps"), filter: { key: "overdue", value: "true" } },
    { label: "Inactive", value: n("inactiveLeads"), filter: { key: "inactive", value: "true" } },
    { label: "Site visits", value: n("upcomingSiteVisits") },
  ];
  return [
    { label: "Newly assigned", value: n("newLeads"), filter: { key: "stage", value: "New" } },
    { label: "Active leads", value: n("activeLeads") },
    { label: "Due today", value: n("followUpsDueToday") },
    { label: "Overdue", value: n("overdueFollowUps"), filter: { key: "overdue", value: "true" } },
    { label: "Upcoming visits", value: n("upcomingSiteVisits") },
    { label: "Inactive", value: n("leadsWithoutRecentActivity"), filter: { key: "inactive", value: "true" } },
  ];
}

function FilterSelect({ label, value, onChange, options }: { label: string; value: string; onChange: (value: string) => void; options: string[][] }) {
  return <select aria-label={label} className={inputClass} value={value} onChange={(e) => onChange(e.target.value)}><option value="">{label}: All</option>{options.map(([v, l]) => <option key={v} value={v}>{l}</option>)}</select>;
}
function QuickFilter({ active, onClick, children }: { active: boolean; onClick: () => void; children: string }) {
  return <button type="button" aria-pressed={active} onClick={onClick} className={`rounded-full border px-3 py-1.5 text-xs font-semibold ${active ? "border-[var(--accent)] bg-[var(--accent-glow)] text-[var(--accent)]" : "border-[var(--border)] text-[var(--text-muted)]"}`}>{children}</button>;
}
function FormSection({ title, children }: { title: string; children: React.ReactNode }) {
  return <fieldset><legend className="mb-3 text-sm font-semibold text-[var(--text-heading)]">{title}</legend><div className="grid gap-3 sm:grid-cols-2">{children}</div></fieldset>;
}
function TextField({ label, value, onChange, required, wide, type = "text", inputMode }: { label: string; value: string; onChange: (value: string) => void; required?: boolean; wide?: boolean; type?: string; inputMode?: React.HTMLAttributes<HTMLInputElement>["inputMode"] }) {
  return <div className={wide ? "sm:col-span-2" : ""}><Label required={required}>{label}</Label><input required={required} className={inputClass} value={value} onChange={(e) => onChange(e.target.value)} type={type} inputMode={inputMode} /></div>;
}
function SelectField({ label, value, onChange, options, required }: { label: string; value: string; onChange: (value: string) => void; options: string[][]; required?: boolean }) {
  return <div><Label required={required}>{label}</Label><select required={required} className={inputClass} value={value} onChange={(e) => onChange(e.target.value)}><option value="">Select…</option>{options.map(([v, l]) => <option key={v} value={v}>{l}</option>)}</select></div>;
}
