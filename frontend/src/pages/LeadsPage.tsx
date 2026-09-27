import { useCallback, useEffect, useId, useMemo, useState, type ReactNode } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import type { User } from "../App.tsx";
import { can } from "../features/access/permissions.ts";
import {
  Button,
  DataTable,
  DEFAULT_PAGE_SIZE,
  Dropdown,
  EmptyState,
  FilterBar,
  IconPlus,
  IconUsers,
  ListCard,
  LoadMore,
  Modal,
  NumberField,
  PageHeader,
  Pagination,
  StatSummary,
  StatusBadge,
  TextArea,
  TextField,
  useIsPhone,
  type DataTableColumn,
  type FilterDef,
  type FilterValues,
  type StatCardProps,
  type StatusTone,
} from "../components/ui";
import { CrmAccess, ErrorBanner } from "../features/leads/CrmUi.tsx";
import { apiJson, jsonRequest, loadCrmLookups, loadUnits } from "../features/leads/leadApi.ts";
import { describeConflict, describeDuplicate, type DuplicateMatch } from "../features/leads/duplicateResolution.ts";
import HeldEnquiriesPanel from "../features/leads/HeldEnquiriesPanel.tsx";
import {
  formatDateTime,
  isClosedStage,
  isPastServerTime,
  leadStageGroups,
  leadStages,
  paymentPreferences,
  stageLabel,
  type ClosureReason,
  type Lead,
  type LeadList,
  type LeadSource,
  type ProjectLookup,
  type StaffMember,
  type UnitLookup,
} from "../features/leads/types.ts";

type Props = { user: User | null };
type Lookups = {
  sources: LeadSource[];
  reasons: ClosureReason[];
  staff: StaffMember[];
  projects: ProjectLookup[];
};

const EMPTY_LOOKUPS: Lookups = { sources: [], reasons: [], staff: [], projects: [] };

/** Every URL filter the list honours; Reset clears all of them (sorting stays). */
const FILTER_KEYS = ["search", "stage", "stageGroup", "qualification", "sourceId", "employeeId", "projectId", "paymentPreference", "unitId", "campaign", "unassigned", "overdue", "inactive", "createdFrom", "createdTo"];

export default function LeadsPage({ user }: Props) {
  return <CrmAccess user={user}>{user && <LeadsWorkspace user={user} />}</CrmAccess>;
}

function LeadsWorkspace({ user }: { user: User }) {
  const navigate = useNavigate();
  const isPhone = useIsPhone();
  const [params, setParams] = useSearchParams();
  const [data, setData] = useState<LeadList | null>(null);
  const [dashboard, setDashboard] = useState<Record<string, unknown> | null>(null);
  const [lookups, setLookups] = useState<Lookups>(EMPTY_LOOKUPS);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const page = Number(params.get("page") ?? 1);
  // A salesperson works a four-step pipeline (New, In Progress, Won, Lost) on their own leads;
  // admins and managers keep the detailed stages, qualification and ownership they run the CRM by.
  const isSalesperson = user.role === "Employee";

  const updateParams = (changes: FilterValues) => {
    const next = new URLSearchParams(params);
    for (const [key, value] of Object.entries(changes)) {
      if (value) next.set(key, value); else next.delete(key);
    }
    if (!("page" in changes)) next.set("page", "1");
    setParams(next);
  };
  const updateParam = (key: string, value: string) => updateParams({ [key]: value });

  // Clears every URL filter, including ones with no control on the bar (an old bookmark's
  // "qualification" or "overdue"). Sorting is not a filter and stays.
  const hasFilters = FILTER_KEYS.some((key) => params.get(key));
  const clearFilters = () => updateParams(Object.fromEntries(FILTER_KEYS.map((key) => [key, ""])));

  // The bar exposes search, stage, source and project (and payment, for admins and managers). The rest stay honoured because the URL is
  // an input surface of its own: a saved link, a bookmark or a hand-built query keeps filtering
  // exactly as it did, and the server contract is unchanged.
  const query = useMemo(() => {
    const allowed = [...FILTER_KEYS, "sortBy", "sortDesc"];
    const q = new URLSearchParams();
    for (const key of allowed) {
      const value = params.get(key);
      if (value) q.set(key, value);
    }
    q.set("page", String(Number.isFinite(page) && page > 0 ? page : 1));
    q.set("pageSize", String(DEFAULT_PAGE_SIZE));
    return q.toString();
  }, [page, params]);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const dashboardEndpoint = isSalesperson ? "/api/lead-dashboard/me" : "/api/lead-dashboard/organisation";
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
  }, [query, isSalesperson]);

  useEffect(() => { void load(); }, [load]);

  // Phone lists grow with "Load more" instead of paging. The extra pages belong to the query that
  // fetched them, so a filter change drops them in the same render rather than in an effect.
  const [more, setMore] = useState<{ query: string; items: Lead[]; page: number; loading: boolean }>({ query: "", items: [], page: 0, loading: false });
  const extra = more.query === query ? more : null;
  const loadMore = async () => {
    if (!data) return;
    const nextPage = (extra?.page ?? data.page) + 1;
    const q = new URLSearchParams(query);
    q.set("page", String(nextPage));
    setMore({ query, items: extra?.items ?? [], page: extra?.page ?? data.page, loading: true });
    try {
      const next = await apiJson<LeadList>(`/api/leads?${q.toString()}`);
      setMore((current) => current.query === query
        ? { query, items: [...current.items, ...next.items], page: nextPage, loading: false }
        : current);
    } catch (caught) {
      setMore((current) => ({ ...current, loading: false }));
      setError(caught instanceof Error ? caught.message : "More leads could not be loaded.");
    }
  };
  const leads = data ? (isPhone && extra ? [...data.items, ...extra.items] : data.items) : [];
  // Everything before the first page on screen counts as shown, so "Showing 40 of 437" stays true
  // for a phone that opened a link to page 3.
  const shownOnPhone = data ? (data.page - 1) * data.pageSize + leads.length : 0;

  const metrics = dashboardMetrics(isSalesperson, dashboard);
  const [total, ...cards] = metrics;
  const toStatCard = (metric: Metric): StatCardProps => {
    // A card that switches a filter on has to switch it off again. Stage lands in a dropdown the
    // operator can see and reset, but "unassigned" and "overdue" have no control of their own on
    // this bar — so pressing the card again is the way back, and the card shows it is pressed
    // rather than leaving the list quietly filtered.
    const applied = !!metric.filter && params.get(metric.filter.key) === metric.filter.value;
    return {
      label: metric.label,
      value: metric.value,
      tone: metric.tone,
      selected: applied,
      onClick: metric.filter ? () => updateParam(metric.filter!.key, applied ? "" : metric.filter!.value) : undefined,
    };
  };

  const filters: FilterDef[] = [
    isSalesperson
      ? { type: "select", key: "stageGroup", label: "Stage", options: leadStageGroups.map((v) => ({ value: v, label: stageLabel(v) })) }
      : { type: "select", key: "stage", label: "Stage", options: leadStages.map((v) => ({ value: v, label: stageLabel(v) })) },
    { type: "select", key: "sourceId", label: "Source", options: lookups.sources.map((v) => ({ value: String(v.id), label: v.name })) },
    { type: "select", key: "projectId", label: "Project", options: lookups.projects.map((v) => ({ value: String(v.id), label: v.name })) },
    ...(isSalesperson ? [] : [{
      type: "select" as const,
      key: "paymentPreference",
      label: "Payment",
      options: paymentPreferences.filter((v) => v !== "Unknown").map((v) => ({ value: v, label: stageLabel(v) })),
    }]),
  ];

  return (
    <div className="mx-auto flex w-full max-w-[1500px] flex-col gap-4 px-4 py-5 md:gap-5 md:px-8 md:py-7">
      <PageHeader
        title={isSalesperson ? "My leads" : "Lead CRM"}
        subtitle={isSalesperson ? "Your assigned enquiries and what comes next." : "Capture, assign, work and convert property enquiries."}
        actions={
          <>
            {!isSalesperson && <Button variant="outline" onClick={() => navigate("/crm/settings")}>CRM settings</Button>}
            <Button icon={<IconPlus size={16} />} onClick={() => setCreateOpen(true)} className="max-md:hidden">New lead</Button>
          </>
        }
      />

      {error && <ErrorBanner message={error} onRetry={() => void load()} />}
      {!isSalesperson && <HeldEnquiriesPanel onResolved={() => void load()} />}

      {total && (
        <StatSummary
          total={{ label: total.label, value: total.value }}
          items={cards.map(toStatCard)}
          phoneColumns={cards.length === 3 ? 3 : 2}
        />
      )}

      <FilterBar
        search={{ value: params.get("search") ?? "", onSearch: (value) => updateParam("search", value), placeholder: "Search name, phone, email, campaign…" }}
        filters={filters}
        values={Object.fromEntries(filters.map((filter) => [filter.key, params.get(filter.key) ?? ""]))}
        onChange={updateParams}
        onReset={clearFilters}
        onAdd={() => setCreateOpen(true)}
        addLabel="New lead"
      />

      {loading && !data ? (
        <div className="flex flex-col gap-2.5">{Array.from({ length: 6 }).map((_, i) => <div key={i} className="h-16 animate-pulse rounded-card bg-track" />)}</div>
      ) : !data || data.items.length === 0 ? (
        <EmptyState
          icon={<IconUsers size={26} />}
          title="No leads found"
          message={hasFilters ? "No leads match these filters. Clear the filters or capture a new enquiry." : "No leads yet. Capture a new enquiry to start."}
          action={hasFilters
            ? <Button variant="outline" onClick={clearFilters}>Clear filters</Button>
            : <Button variant="outline" onClick={() => setCreateOpen(true)}>New lead</Button>}
        />
      ) : (
        <>
          <DataTable
            caption="Leads"
            rows={leads}
            rowKey={(lead) => lead.id}
            columns={leadColumns(isSalesperson)}
            onRowClick={(lead) => navigate(`/crm/leads/${lead.id}`)}
            minWidth={isSalesperson ? 860 : 980}
            phoneCard={(lead) => <LeadCard lead={lead} simple={isSalesperson} />}
          />
          <Pagination
            className="max-md:hidden"
            page={data.page}
            pageSize={data.pageSize}
            totalCount={data.totalCount}
            totalPages={data.totalPages}
            itemLabel="leads"
            onPageChange={(next) => updateParams({ page: String(next) })}
          />
          <LoadMore
            className="md:hidden"
            shown={shownOnPhone}
            total={data.totalCount}
            loading={extra?.loading ?? false}
            onLoadMore={() => void loadMore()}
          />
        </>
      )}

      <LeadCreateModal
        open={createOpen}
        onClose={() => setCreateOpen(false)}
        lookups={lookups}
        canAssign={can(user.role, "crm.manage")}
        onCreated={(leadId) => navigate(`/crm/leads/${leadId}`)}
      />
    </div>
  );
}

/** Badge colour: Dormant is orange; otherwise the stage group the server sent decides. */
function stageTone(lead: Lead): StatusTone {
  if (lead.stage === "Dormant") return "orange";
  return lead.stageGroup === "Won" ? "green" : lead.stageGroup === "Lost" ? "red" : "blue";
}

/** A salesperson sees the simple stage the server grouped the lead into; others see the detailed stage. */
function StageBadge({ lead, simple }: { lead: Lead; simple: boolean }) {
  return <StatusBadge status={lead.stage} tone={stageTone(lead)}>{stageLabel(simple ? lead.stageGroup : lead.stage)}</StatusBadge>;
}

const QUALIFICATION_TONES: Record<string, StatusTone> = { Hot: "red", Warm: "orange", Cold: "blue" };

const contactOf = (lead: Lead) => lead.phone ?? lead.whatsappNumber ?? lead.email;

/** A closed lead has nothing scheduled by design, so it says so rather than reading as an omission. */
function NextAction({ lead }: { lead: Lead }) {
  const closed = isClosedStage(lead.stage);
  const overdue = isPastServerTime(lead.nextActionAt) && !closed;
  return (
    <span className={overdue ? "font-bold text-danger" : undefined}>
      {lead.nextActionSummary ?? "—"}
      <span className={`block text-label ${overdue ? "" : "text-ink-muted"}`}>
        {closed ? "Completed" : lead.nextActionAt ? formatDateTime(lead.nextActionAt) : "Not scheduled"}
      </span>
    </span>
  );
}

/**
 * A salesperson's table (simple) carries only what decides their next move: who, from where, for
 * what, how far along, and what happened last and comes next. Qualification and owner are the
 * admin and manager's concern and stay on their table and on the lead itself.
 */
function leadColumns(simple: boolean): DataTableColumn<Lead>[] {
  const muted = "block text-label text-ink-muted";
  const columns: (DataTableColumn<Lead> | false)[] = [
    {
      key: "lead",
      header: "Lead",
      className: "min-w-[190px]",
      render: (lead) => (
        <>
          <Link className="font-extrabold text-ink no-underline hover:underline" to={`/crm/leads/${lead.id}`}>{lead.fullName}</Link>
          {simple
            ? <span className={muted}>{contactOf(lead) ?? "No contact details"}</span>
            : <>
                <span className={muted}>{lead.leadReference}{contactOf(lead) && ` · ${contactOf(lead)}`}</span>
                <span className={muted}>{lead.sourceName}</span>
              </>}
        </>
      ),
    },
    simple && { key: "source", header: "Source", render: (lead) => lead.sourceName },
    {
      key: "interest",
      header: simple ? "Project / Interest" : "Interest",
      className: "max-w-[190px]",
      render: (lead) => (
        <>
          {lead.interestedProjectName ?? lead.preferredLocation ?? "General enquiry"}
          {lead.interestedUnitNumber && <span className={muted}>Unit {lead.interestedUnitNumber}</span>}
        </>
      ),
    },
    { key: "stage", header: "Stage", className: "whitespace-nowrap", render: (lead) => <StageBadge lead={lead} simple={simple} /> },
    !simple && {
      key: "qualification",
      header: "Qualification",
      className: "whitespace-nowrap",
      render: (lead) => <StatusBadge status={lead.qualification} tone={QUALIFICATION_TONES[lead.qualification] ?? "grey"} />,
    },
    !simple && {
      key: "owner",
      header: "Owner",
      render: (lead) => lead.assignedEmployeeName ?? "Unassigned",
    },
    {
      key: "activity",
      header: "Last activity",
      className: "max-w-[220px]",
      render: (lead) => <>{lead.lastActivitySummary ?? "No activity"}<span className={muted}>{formatDateTime(lead.lastActivityAt)}</span></>,
    },
    { key: "next", header: "Next action", className: "max-w-[190px]", render: (lead) => <NextAction lead={lead} /> },
    !simple && { key: "created", header: "Created", className: "whitespace-nowrap text-label text-ink-muted", render: (lead) => formatDateTime(lead.createdAt) },
  ];
  return columns.filter((column): column is DataTableColumn<Lead> => column !== false);
}

function LeadCard({ lead, simple }: { lead: Lead; simple: boolean }) {
  return (
    <ListCard
      to={`/crm/leads/${lead.id}`}
      reference={simple ? lead.sourceName : lead.leadReference}
      badge={<StageBadge lead={lead} simple={simple} />}
      title={lead.fullName}
      detail={[lead.interestedProjectName ?? lead.preferredLocation ?? "General enquiry", contactOf(lead) ?? "No contact details"].join(" · ")}
      value={<NextAction lead={lead} />}
    />
  );
}

function LeadCreateModal({ open, onClose, lookups, canAssign, onCreated }: { open: boolean; onClose: () => void; lookups: Lookups; canAssign: boolean; onCreated: (id: number) => void }) {
  const initial = { firstName: "", lastName: "", phone: "", whatsappNumber: "", email: "", city: "", address: "", preferredContactMethod: "Phone", preferredContactTime: "", sourceCode: "manual", sourceDetails: "", campaignName: "", interestedProjectId: "", interestedUnitId: "", propertyType: "", preferredLocation: "", budgetMin: "", budgetMax: "", purchaseIntent: "Unknown", paymentPreference: "Unknown", notes: "", assignedEmployeeId: "" };
  const [form, setForm] = useState(initial);
  const formId = useId();
  const [units, setUnits] = useState<UnitLookup[]>([]);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [duplicate, setDuplicate] = useState<DuplicateMatch | null>(null);
  const duplicateResolution = duplicate ? describeDuplicate(duplicate) : null;
  // Set when the details match several open leads at once, e.g. the phone one and the email another.
  const [conflict, setConflict] = useState<DuplicateMatch[] | null>(null);
  const conflictResolution = conflict ? describeConflict(conflict) : null;

  const set = (key: keyof typeof form, value: string) => {
    setForm((current) => ({ ...current, [key]: value }));
    // The duplicate choices describe the lead these contact details matched. Once they change,
    // resubmitting could match nothing and create a lead the "add to" button never promised.
    if (key === "phone" || key === "whatsappNumber" || key === "email") { setDuplicate(null); setConflict(null); }
  };

  const assignableStaff = lookups.staff.filter((member) => member.canOwnLeads);
  const setAssignmentEmployee = (employeeId: string) => set("assignedEmployeeId", employeeId);

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
    setSaving(true); setError(null); setDuplicate(null); setConflict(null);
    try {
      const result = await apiJson<{ isDuplicate: boolean; identityConflict?: boolean; conflictingMatches?: DuplicateMatch[]; message?: string; match?: DuplicateMatch; lead?: Lead }>(
        "/api/leads",
        jsonRequest("POST", {
          ...form,
          interestedProjectId: form.interestedProjectId ? Number(form.interestedProjectId) : null,
          interestedUnitId: form.interestedUnitId ? Number(form.interestedUnitId) : null,
          budgetMin: form.budgetMin ? Number(form.budgetMin) : null,
          budgetMax: form.budgetMax ? Number(form.budgetMax) : null,
          assignedEmployeeId: canAssign && form.assignedEmployeeId ? Number(form.assignedEmployeeId) : null,
          allowDuplicate: addToLeadId != null,
          expectedExistingLeadId: addToLeadId ?? null,
        }),
      );
      if (result.identityConflict && !result.lead) {
        setConflict(result.conflictingMatches ?? []);
        if (addToLeadId != null && result.message) setError(result.message);
        return;
      }
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
    <Modal
      open={open}
      onClose={onClose}
      title="New lead"
      size="lg"
      phoneLayout="fullscreen"
      busy={saving}
      primaryAction={{ label: saving ? "Checking…" : "Create lead", form: formId, loading: saving }}
    >
      <form id={formId} onSubmit={(e) => { e.preventDefault(); void submit(); }} className="flex flex-col gap-6">
        <p className="m-0 text-small text-ink-muted">Duplicate matching runs before a new prospect is created.</p>
        {error && <ErrorBanner message={error} />}
        {conflictResolution && (
          <div className="rounded-card border border-gold-line bg-gold-soft p-4 text-sm text-warning">
            <p className="font-semibold">{conflictResolution.heading}</p>
            <p className="mt-1">{conflictResolution.explanation}</p>
            <p className="mt-1">{conflictResolution.addOutcome}</p>
            <ul className="mt-3 space-y-2">
              {conflictResolution.choices.map((choice) => (
                <li key={choice.leadId} className="flex flex-wrap items-center gap-2">
                  <span className="mr-auto">{choice.detail}</span>
                  <Button size="sm" onClick={() => onCreated(choice.leadId)}>{choice.openLabel}</Button>
                  <Button size="sm" variant="outline" onClick={() => void submit(choice.leadId)} disabled={saving}>{choice.addLabel}</Button>
                </li>
              ))}
            </ul>
          </div>
        )}
        {duplicateResolution && (
          <div className="rounded-card border border-gold-line bg-gold-soft p-4 text-sm text-warning">
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
          <TextField label="First name" required value={form.firstName} onChange={(e) => set("firstName", e.target.value)} />
          <TextField label="Last name" value={form.lastName} onChange={(e) => set("lastName", e.target.value)} />
          <TextField label="Phone" value={form.phone} onChange={(e) => set("phone", e.target.value)} type="tel" />
          <TextField label="WhatsApp" value={form.whatsappNumber} onChange={(e) => set("whatsappNumber", e.target.value)} type="tel" />
          <TextField label="Email" value={form.email} onChange={(e) => set("email", e.target.value)} type="email" />
          <TextField label="City" value={form.city} onChange={(e) => set("city", e.target.value)} />
          <TextField label="Address" value={form.address} onChange={(e) => set("address", e.target.value)} className="sm:col-span-2" />
          <SelectField label="Preferred contact" value={form.preferredContactMethod} onChange={(v) => set("preferredContactMethod", v)} options={["Phone", "Whatsapp", "Email", "Sms", "InPerson"].map((v) => [v, stageLabel(v)])} />
          <TextField label="Preferred time" value={form.preferredContactTime} onChange={(e) => set("preferredContactTime", e.target.value)} />
        </FormSection>
        <FormSection title="Source and attribution">
          <SelectField label="Lead source" required value={form.sourceCode} onChange={(v) => set("sourceCode", v)} options={lookups.sources.map((v) => [v.code, v.name])} />
          <TextField label="Source details" value={form.sourceDetails} onChange={(e) => set("sourceDetails", e.target.value)} />
          <TextField label="Campaign" value={form.campaignName} onChange={(e) => set("campaignName", e.target.value)} />
        </FormSection>
        <FormSection title="Property interest">
          <SelectField label="Project" value={form.interestedProjectId} onChange={(v) => { set("interestedProjectId", v); set("interestedUnitId", ""); }} options={lookups.projects.map((v) => [String(v.id), v.name])} />
          <SelectField label="Unit" value={form.interestedUnitId} onChange={(v) => set("interestedUnitId", v)} options={units.map((v) => [String(v.id), v.number])} />
          <TextField label="Property type" value={form.propertyType} onChange={(e) => set("propertyType", e.target.value)} />
          <TextField label="Preferred location" value={form.preferredLocation} onChange={(e) => set("preferredLocation", e.target.value)} />
          <NumberField label="Minimum budget" prefix="Rs" value={form.budgetMin} onChange={(v) => set("budgetMin", v)} />
          <NumberField label="Maximum budget" prefix="Rs" value={form.budgetMax} onChange={(v) => set("budgetMax", v)} />
          <SelectField label="Purchase intent" value={form.purchaseIntent} onChange={(v) => set("purchaseIntent", v)} options={["Unknown", "SelfUse", "Investment", "Rental", "Resale"].map((v) => [v, stageLabel(v)])} />
          <SelectField label="Payment preference" value={form.paymentPreference} onChange={(v) => set("paymentPreference", v)} options={paymentPreferences.map((v) => [v, stageLabel(v)])} />
        </FormSection>
        {canAssign && <FormSection title="Ownership"><SelectField label="Employee" value={form.assignedEmployeeId} onChange={setAssignmentEmployee} options={assignableStaff.map((v) => [String(v.employeeId), v.fullName])} /></FormSection>}
        <TextArea label="Initial notes" value={form.notes} onChange={(e) => set("notes", e.target.value)} />
      </form>
    </Modal>
  );
}

type Metric = { id: string; label: string; value: number | string; tone?: StatusTone; filter?: { key: string; value: string } };

/**
 * Four figures, one per card, in the shape the design asks for: how many, how many closed each way,
 * and the rate that falls out of the two; admins and managers also get the queue waiting for an owner. A salesperson gets their own leads by the four simple stages;
 * what is due or overdue shows on each lead's Next action instead of as a card of its own.
 */
function dashboardMetrics(isSalesperson: boolean, dashboard: Record<string, unknown> | null): Metric[] {
  const d = dashboard ?? {};
  const n = (key: string) => Number(d[key] ?? 0);
  if (isSalesperson) return [
    { id: "total", label: "Total assigned", value: n("totalAssigned") },
    { id: "inProgress", label: "In progress", value: n("inProgressLeads"), tone: "blue", filter: { key: "stageGroup", value: "InProgress" } },
    { id: "won", label: "Won", value: n("conversions"), tone: "green", filter: { key: "stageGroup", value: "Won" } },
    { id: "lost", label: "Lost", value: n("lostLeads"), tone: "red", filter: { key: "stageGroup", value: "Lost" } },
  ];
  return [
    { id: "total", label: "All leads", value: n("totalLeads") },
    { id: "unassigned", label: "Unassigned", value: n("unassignedLeads"), tone: "orange", filter: { key: "unassigned", value: "true" } },
    { id: "won", label: "Won", value: n("wonLeads"), tone: "green", filter: { key: "stage", value: "Won" } },
    { id: "lost", label: "Lost", value: n("lostLeads"), tone: "red", filter: { key: "stage", value: "Lost" } },
    { id: "rate", label: "Conversion", value: `${n("conversionRatePercent").toFixed(1)}%` },
  ];
}

function FormSection({ title, children }: { title: string; children: ReactNode }) {
  return <fieldset className="m-0 border-0 p-0"><legend className="mb-3 p-0 text-body font-extrabold text-ink">{title}</legend><div className="grid gap-4 sm:grid-cols-2">{children}</div></fieldset>;
}
function SelectField({ label, value, onChange, options, required }: { label: string; value: string; onChange: (value: string) => void; options: string[][]; required?: boolean }) {
  return <Dropdown label={label} required={required} value={value} onChange={onChange} placeholder="Select…" options={[{ value: "", label: "Select…" }, ...options.map(([v, l]) => ({ value: v, label: l }))]} />;
}
