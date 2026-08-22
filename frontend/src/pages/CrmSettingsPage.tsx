import { useCallback, useEffect, useState } from "react";
import type { User } from "../App.tsx";
import Button from "../lib/Button.tsx";
import {
  CrmAccess,
  CrmHeader,
  CrmModal,
  CrmTabs,
  ErrorBanner,
  inputClass,
  Label,
  StatePanel,
} from "../features/leads/CrmUi.tsx";
import MetaIntegrationsPanel from "../features/integrations/MetaIntegrationsPanel.tsx";
import { apiJson, jsonRequest } from "../features/leads/leadApi.ts";
import type {
  ClosureReason,
  LeadSource,
  StaffAccount,
  StaffAccountProvisionResult,
  StaffInvitationResult,
  StaffMember,
  Team,
} from "../features/leads/types.ts";
import {
  accessLabel,
  accessTone,
  buildProvisionPayload,
  buildUpdatePayload,
  canManageAccount,
  canProvisionAccess,
  canResendInvitation,
  describeProvisionOutcome,
  describeResendOutcome,
  invitationState,
  invitationSummary,
  newManageForm,
  newProvisionForm,
  provisionableEmployees,
} from "../features/staff/staffAccessState.ts";
import type { Notice } from "../features/staff/staffAccessState.ts";

type Props = { user: User | null };
type LinkableUser = { userId: number; fullName: string; email: string; role: string };

/**
 * Which staff flow the modal is in. Provisioning creates or connects a login through POST;
 * managing edits one that already exists through PUT. An employee with no account only ever
 * reaches the first, because the second has nothing to update.
 */
type StaffIntent =
  | { kind: "provision"; employee: StaffAccount | null }
  | { kind: "manage"; account: StaffAccount };

export default function CrmSettingsPage({ user }: Props) {
  return <CrmAccess user={user}>{user && (user.role === "Admin" ? <SettingsWorkspace user={user} /> : <StatePanel title="Admin access required" message="Only an Admin can manage staff accounts, lead sources, closure reasons, and sales teams." />)}</CrmAccess>;
}

function SettingsWorkspace({ user }: { user: User }) {
  // Returning from Meta's consent screen should land on the tab that sent you there.
  const [tab, setTab] = useState(() => {
    const params = new URLSearchParams(window.location.search);
    return params.get("tab") === "integrations" || params.has("meta") ? "integrations" : "staff";
  });
  const [staff, setStaff] = useState<StaffAccount[]>([]);
  const [linkable, setLinkable] = useState<LinkableUser[]>([]);
  const [sources, setSources] = useState<LeadSource[]>([]);
  const [reasons, setReasons] = useState<ClosureReason[]>([]);
  const [teams, setTeams] = useState<Team[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [modal, setModal] = useState<"source" | "reason" | "team" | null>(null);
  const [editing, setEditing] = useState<LeadSource | ClosureReason | Team | null>(null);
  const [staffIntent, setStaffIntent] = useState<StaffIntent | null>(null);
  // Feedback for things the page cannot show by reloading: an account that was created but
  // whose activation email failed looks identical in the table to one that was emailed.
  const [notice, setNotice] = useState<Notice | null>(null);
  const [resending, setResending] = useState<number | null>(null);

  const load = useCallback(async () => {
    setLoading(true); setError(null);
    try {
      const [staffRows, users, sourceRows, reasonRows, teamRows] = await Promise.all([
        apiJson<StaffAccount[]>("/api/staff/accounts"),
        apiJson<LinkableUser[]>("/api/staff/linkable-users"),
        apiJson<LeadSource[]>("/api/lead-config/sources?includeInactive=true"),
        apiJson<ClosureReason[]>("/api/lead-config/closure-reasons?includeInactive=true"),
        apiJson<Team[]>("/api/lead-config/teams"),
      ]);
      setStaff(staffRows); setLinkable(users); setSources(sourceRows); setReasons(reasonRows); setTeams(teamRows);
    } catch (caught) { setError(caught instanceof Error ? caught.message : "CRM settings could not be loaded."); }
    finally { setLoading(false); }
  }, []);
  useEffect(() => { void load(); }, [load]);

  const open = (kind: typeof modal, value: typeof editing = null) => { setEditing(value); setModal(kind); };

  /**
   * Sends a waiting employee a fresh link. The reload runs whichever way it went: a stored
   * invitation replaces the previous one even when the email fails, so the expiry on screen
   * is stale either way.
   */
  const resendInvitation = async (account: StaffAccount) => {
    if (resending !== null) return;
    setResending(account.employeeId); setNotice(null);
    try {
      const result = await apiJson<StaffInvitationResult>(`/api/staff/accounts/${account.employeeId}/resend-invitation`, { method: "POST" });
      setNotice(describeResendOutcome(result));
    } catch (caught) {
      setNotice({ tone: "error", message: caught instanceof Error ? caught.message : "The invitation could not be sent." });
    } finally {
      setResending(null);
      await load();
    }
  };

  const finishStaff = async (outcome: Notice) => { setStaffIntent(null); setNotice(outcome); await load(); };

  return (
    <>
      <CrmHeader title="CRM administration" subtitle="Manage secure staff access and the controlled configuration used by the Lead workflow." role={user.role} actions={<Button variant="outline" onClick={() => location.assign("/crm")}>← Lead workspace</Button>} />
      <div className="mx-auto w-full max-w-[1350px] space-y-5 px-4 py-6 sm:px-6 lg:px-8">
        {error && <ErrorBanner message={error} onRetry={() => void load()} />}
        {notice && <NoticeBanner notice={notice} onDismiss={() => setNotice(null)} />}
        <CrmTabs active={tab} onChange={setTab} items={[{ id: "staff", label: "Staff accounts", count: staff.length }, { id: "teams", label: "Sales teams", count: teams.length }, { id: "sources", label: "Lead sources", count: sources.length }, { id: "reasons", label: "Closure reasons", count: reasons.length }, { id: "integrations", label: "Integrations" }]} />
        <section className="rounded-2xl border border-[var(--border)] bg-[var(--bg-card)] p-4 sm:p-6">
          {/* Integrations loads its own data, so it stays usable even if the shared
              configuration fetch above failed. */}
          {tab === "integrations" ? <MetaIntegrationsPanel /> : loading ? <p className="py-16 text-center text-sm text-[var(--text-muted)]">Loading configuration…</p> : (
            <>
              {tab === "staff" && <SettingsTable title="Staff accounts" description="Give an employee a DAMS login and they are emailed an activation link to choose their own password. Nobody, Admins included, ever sets or sees somebody else's password." addLabel="Give DAMS access" onAdd={() => setStaffIntent({ kind: "provision", employee: null })} headers={["Employee", "Login", "Role", "Team", "DAMS access", "Employment", ""]} rows={staff.map((item) => [<div><p className="font-semibold text-[var(--text-heading)]">{item.fullName}</p><p className="text-xs text-[var(--text-muted)]">{item.jobTitle} · {item.department}</p></div>, item.email ?? "—", roleLabel(item.role), item.teamName ?? "No team", <AccessCell account={item} />, item.status, <div className="flex flex-wrap gap-2">{canProvisionAccess(item.access) && <Button size="sm" onClick={() => setStaffIntent({ kind: "provision", employee: item })}>Give DAMS access</Button>}{canManageAccount(item.access) && <Button size="sm" variant="outline" onClick={() => setStaffIntent({ kind: "manage", account: item })}>Manage</Button>}{canResendInvitation(item.access) && <Button size="sm" variant="outline" disabled={resending !== null} onClick={() => void resendInvitation(item)}>{resending === item.employeeId ? "Sending…" : "Resend invitation"}</Button>}</div>])} empty="No employees exist yet." />}
              {tab === "teams" && <SettingsTable title="Sales teams" description="Managers see the team they belong to and any team they manage." addLabel="Create team" onAdd={() => open("team")} headers={["Team", "Manager", "Members", "Status", ""]} rows={teams.map((item) => [item.name, item.managerName ?? "Not assigned", item.memberCount, item.isActive ? "Active" : "Inactive", <Button size="sm" variant="outline" onClick={() => open("team", item)}>Edit</Button>])} empty="No sales teams configured." />}
              {tab === "sources" && <SettingsTable title="Lead sources" description="The original source is preserved when repeat enquiries enrich a lead." addLabel="Create source" onAdd={() => open("source")} headers={["Source", "Code", "Customer mapping", "Status", ""]} rows={sources.map((item) => [item.name, <code className="text-xs">{item.code}</code>, item.customerSource, item.isActive ? "Active" : "Inactive", <Button size="sm" variant="outline" onClick={() => open("source", item)}>Edit</Button>])} empty="No lead sources configured." />}
              {tab === "reasons" && <SettingsTable title="Closure reasons" description="Lost and Dormant cannot be selected without an active configured reason." addLabel="Create reason" onAdd={() => open("reason")} headers={["Reason", "Code", "Applies to", "Status", ""]} rows={reasons.map((item) => [item.name, <code className="text-xs">{item.code}</code>, item.kind, item.isActive ? "Active" : "Inactive", <Button size="sm" variant="outline" onClick={() => open("reason", item)}>Edit</Button>])} empty="No closure reasons configured." />}
            </>
          )}
        </section>
      </div>

      {staffIntent && <StaffModal intent={staffIntent} staff={staff} users={linkable} teams={teams} onClose={() => setStaffIntent(null)} onDone={finishStaff} />}
      {modal === "source" && <SourceModal item={editing as LeadSource | null} onClose={() => setModal(null)} onSaved={async () => { setModal(null); await load(); }} />}
      {modal === "reason" && <ReasonModal item={editing as ClosureReason | null} onClose={() => setModal(null)} onSaved={async () => { setModal(null); await load(); }} />}
      {modal === "team" && <TeamModal item={editing as Team | null} staff={staff} onClose={() => setModal(null)} onSaved={async () => { setModal(null); await load(); }} />}
    </>
  );
}

function SettingsTable({ title, description, addLabel, onAdd, headers, rows, empty }: { title: string; description: string; addLabel: string; onAdd: () => void; headers: string[]; rows: React.ReactNode[][]; empty: string }) {
  return <div><div className="mb-5 flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between"><div><h2 className="text-lg font-semibold text-[var(--text-heading)]">{title}</h2><p className="mt-1 text-sm text-[var(--text-muted)]">{description}</p></div><Button size="sm" onClick={onAdd}>+ {addLabel}</Button></div>{rows.length === 0 ? <p className="rounded-xl border border-dashed border-[var(--border)] py-16 text-center text-sm text-[var(--text-muted)]">{empty}</p> : <div className="overflow-x-auto"><table className="w-full min-w-[720px] text-left text-sm"><thead><tr className="border-b border-[var(--border)] text-xs uppercase tracking-wider text-[var(--text-muted)]">{headers.map((h, i) => <th className="px-3 py-3" key={`${h}-${i}`}>{h}</th>)}</tr></thead><tbody>{rows.map((row, i) => <tr className="border-b border-[var(--border)] last:border-0" key={i}>{row.map((cell, j) => <td className="px-3 py-4 text-[var(--text-secondary)]" key={j}>{cell}</td>)}</tr>)}</tbody></table></div>}</div>;
}

function StaffModal({ intent, staff, users, teams, onClose, onDone }: { intent: StaffIntent; staff: StaffAccount[]; users: LinkableUser[]; teams: Team[]; onClose: () => void; onDone: (outcome: Notice) => void | Promise<void> }) {
  const managing = intent.kind === "manage" ? intent.account : null;
  // An employee the Admin reached this modal through stays fixed. Letting the selection drift
  // to somebody else is how the wrong person ends up with a login.
  const [locked] = useState(() => (intent.kind === "provision" ? intent.employee : null));
  const [form, setForm] = useState(() => (managing ? newManageForm(managing) : newProvisionForm(locked)));
  const [saving, setSaving] = useState(false); const [error, setError] = useState<string | null>(null);
  const set = (key: keyof typeof form, value: string) => setForm((f) => ({ ...f, [key]: value }));
  const pickEmployee = (id: string) => { const employee = staff.find((e) => e.employeeId === Number(id)); setForm((f) => ({ ...f, existingEmployeeId: id, fullName: employee?.fullName ?? f.fullName, email: employee?.email ?? f.email, phone: employee?.phone ?? f.phone, jobTitle: employee?.jobTitle ?? f.jobTitle, department: employee?.department ?? f.department, teamId: employee?.teamId?.toString() ?? f.teamId })); };
  const pickUser = (id: string) => { const account = users.find((u) => u.userId === Number(id)); setForm((f) => ({ ...f, existingUserId: id, fullName: account?.fullName ?? f.fullName, email: account?.email ?? f.email })); };
  const save = async () => {
    setSaving(true); setError(null);
    try {
      if (managing) {
        await apiJson(`/api/staff/accounts/${managing.employeeId}`, jsonRequest("PUT", buildUpdatePayload(form)));
        await onDone({ tone: "success", message: `Saved role, team and employment status for ${managing.fullName}.` });
      } else {
        const result = await apiJson<StaffAccountProvisionResult>("/api/staff/accounts", jsonRequest("POST", buildProvisionPayload(form)));
        // The account exists now whatever the email did, so the form closes either way.
        // Leaving it open after a failed send is what invites a second account for one person.
        await onDone(describeProvisionOutcome(result));
      }
    } catch (caught) {
      // Nothing was saved, so this belongs in the form where it can still be corrected.
      setError(caught instanceof Error ? caught.message : "Staff account could not be saved.");
    } finally { setSaving(false); }
  };
  const fixedIdentity = Boolean(managing) || Boolean(locked);
  return <CrmModal open title={managing ? "Manage staff account" : "Give DAMS access"} subtitle={managing ? "Role, team and employment status. A password belongs to the person who owns the account and cannot be set or reset from here." : "The employee is emailed an activation link and chooses their own password. Public sign-up remains customer-only."} onClose={onClose} footer={<ModalFooter saving={saving} onClose={onClose} onSave={() => void save()} />}>
    <div className="space-y-4">
      {error && <ErrorBanner message={error} />}
      {!managing && (locked
        ? <ReadOnlyField label="Employee" value={`${locked.fullName}${locked.jobTitle ? ` · ${locked.jobTitle}` : ""}`} />
        : <div>
            <SelectField label="Employee" value={form.existingEmployeeId} onChange={pickEmployee} options={provisionableEmployees(staff).map((e) => [String(e.employeeId), e.fullName])} empty="Create a new employee record too" />
            <p className="mt-1.5 text-xs text-[var(--text-muted)]">Employees are normally created in the Employees module first; this screen gives one of them a login.</p>
          </div>)}
      {!managing && <div>
        <SelectField label="Connect existing login" value={form.existingUserId} onChange={pickUser} options={users.map((u) => [String(u.userId), `${u.fullName} · ${u.email}`])} empty="Create a new login" />
        {form.existingUserId && <p className="mt-1.5 text-xs text-[var(--text-muted)]">This login already exists and keeps whatever password it has. Whether an activation email is needed is decided by DAMS, not here.</p>}
      </div>}
      <div className="grid gap-4 sm:grid-cols-2">
        <TextField label="Full name" required disabled={fixedIdentity} value={form.fullName} onChange={(v) => set("fullName", v)} />
        <TextField label="Email" required disabled={fixedIdentity} type="email" value={form.email} onChange={(v) => set("email", v)} />
        <SelectField label="CRM role" required value={form.role} onChange={(v) => set("role", v)} options={[["Admin", "Admin"], ["Manager", "Sales Manager"], ["Employee", "Sales Employee"]]} />
        <SelectField label="Team" value={form.teamId} onChange={(v) => set("teamId", v)} options={teams.filter((t) => t.isActive).map((t) => [String(t.id), t.name])} empty="No team" />
        {!managing && <>
          <TextField label="Job title" required value={form.jobTitle} onChange={(v) => set("jobTitle", v)} />
          <TextField label="Department" required value={form.department} onChange={(v) => set("department", v)} />
          <TextField label="Phone" required value={form.phone} onChange={(v) => set("phone", v)} />
          <TextField label="Join date" required type="date" value={form.joinDate} onChange={(v) => set("joinDate", v)} />
        </>}
        {managing && <SelectField label="Employment status" value={form.status} onChange={(v) => set("status", v)} options={[["Active", "Active"], ["OnLeave", "On leave"], ["Terminated", "Terminated"]]} />}
      </div>
      {managing && <p className="text-xs text-[var(--text-muted)]">DAMS access: {accessLabel(managing.access)}. {managing.access === "Invited" ? "Use Resend invitation on the row to send a new activation link — activation is the employee's own step and cannot be done for them." : managing.access === "Disabled" ? "A disabled login cannot sign in, and re-enabling one is not available from this screen." : "This employee has activated their login and manages their own password."}</p>}
    </div>
  </CrmModal>;
}

/** The DAMS access column: the state, and what an outstanding link is doing about it. */
function AccessCell({ account }: { account: StaffAccount }) {
  const invitation = invitationState(account);
  const detail = invitationSummary(invitation);
  return <div className="space-y-1">
    <span className={`inline-flex rounded-full border px-2.5 py-1 text-[11px] font-semibold ${accessTone(account.access)}`}>{accessLabel(account.access)}</span>
    {detail && <p className="text-xs text-[var(--text-muted)]">{detail}</p>}
  </div>;
}

/**
 * Page-level feedback. A warning is its own tone on purpose: an account that exists but could
 * not be emailed is neither a success nor a failure, and showing it as either causes the
 * wrong next move.
 */
function NoticeBanner({ notice, onDismiss }: { notice: Notice; onDismiss: () => void }) {
  const tone = notice.tone === "success" ? "border-emerald-500/25 bg-emerald-500/[0.07] text-emerald-300"
    : notice.tone === "warning" ? "border-amber-500/25 bg-amber-500/[0.07] text-amber-200"
    : "border-rose-500/25 bg-rose-500/[0.07] text-rose-300";
  return <div role={notice.tone === "error" ? "alert" : "status"} className={`flex items-start justify-between gap-3 rounded-xl border px-4 py-3 text-sm ${tone}`}>
    <span>{notice.message}</span>
    <button type="button" onClick={onDismiss} aria-label="Dismiss" className="shrink-0 rounded-lg px-2 text-lg leading-none opacity-70 hover:opacity-100">×</button>
  </div>;
}

function SourceModal({ item, onClose, onSaved }: { item: LeadSource | null; onClose: () => void; onSaved: () => void | Promise<void> }) {
  const [form, setForm] = useState({ code: item?.code ?? "", name: item?.name ?? "", displayOrder: item?.displayOrder?.toString() ?? "100", customerSource: item?.customerSource ?? "Other", isActive: item?.isActive ?? true }); const [saving, setSaving] = useState(false); const [error, setError] = useState<string | null>(null);
  const save = async () => { setSaving(true); setError(null); try { await apiJson(item ? `/api/lead-config/sources/${item.id}` : "/api/lead-config/sources", jsonRequest(item ? "PUT" : "POST", { ...form, displayOrder: Number(form.displayOrder) })); await onSaved(); } catch (e) { setError(e instanceof Error ? e.message : "Source could not be saved."); } finally { setSaving(false); } };
  return <CrmModal open title={item ? "Edit lead source" : "Create lead source"} onClose={onClose} footer={<ModalFooter saving={saving} onClose={onClose} onSave={() => void save()} />}><div className="space-y-4">{error && <ErrorBanner message={error} />}<TextField label="Name" required value={form.name} onChange={(v) => setForm({ ...form, name: v })} /><TextField label="Code" required disabled={Boolean(item)} value={form.code} onChange={(v) => setForm({ ...form, code: v.toLowerCase().replace(/[^a-z0-9_]/g, "_") })} /><SelectField label="Converted customer source" value={form.customerSource} onChange={(v) => setForm({ ...form, customerSource: v })} options={["WalkIn", "Phone", "Referral", "Website", "Other"].map((v) => [v, v])} /><TextField label="Display order" type="number" value={form.displayOrder} onChange={(v) => setForm({ ...form, displayOrder: v })} />{item && <Checkbox label="Active" checked={form.isActive} onChange={(v) => setForm({ ...form, isActive: v })} />}</div></CrmModal>;
}

function ReasonModal({ item, onClose, onSaved }: { item: ClosureReason | null; onClose: () => void; onSaved: () => void | Promise<void> }) {
  const [form, setForm] = useState({ code: item?.code ?? "", name: item?.name ?? "", kind: item?.kind ?? "Both", displayOrder: item?.displayOrder?.toString() ?? "100", isActive: item?.isActive ?? true }); const [saving, setSaving] = useState(false); const [error, setError] = useState<string | null>(null);
  const save = async () => { setSaving(true); setError(null); try { await apiJson(item ? `/api/lead-config/closure-reasons/${item.id}` : "/api/lead-config/closure-reasons", jsonRequest(item ? "PUT" : "POST", { ...form, displayOrder: Number(form.displayOrder) })); await onSaved(); } catch (e) { setError(e instanceof Error ? e.message : "Reason could not be saved."); } finally { setSaving(false); } };
  return <CrmModal open title={item ? "Edit closure reason" : "Create closure reason"} onClose={onClose} footer={<ModalFooter saving={saving} onClose={onClose} onSave={() => void save()} />}><div className="space-y-4">{error && <ErrorBanner message={error} />}<TextField label="Reason" required value={form.name} onChange={(v) => setForm({ ...form, name: v })} /><TextField label="Code" required disabled={Boolean(item)} value={form.code} onChange={(v) => setForm({ ...form, code: v.toLowerCase().replace(/[^a-z0-9_]/g, "_") })} /><SelectField label="Applies to" value={form.kind} onChange={(v) => setForm({ ...form, kind: v as ClosureReason["kind"] })} options={[["Lost", "Lost"], ["Dormant", "Dormant"], ["Both", "Both"]]} /><TextField label="Display order" type="number" value={form.displayOrder} onChange={(v) => setForm({ ...form, displayOrder: v })} />{item && <Checkbox label="Active" checked={form.isActive} onChange={(v) => setForm({ ...form, isActive: v })} />}</div></CrmModal>;
}

function TeamModal({ item, staff, onClose, onSaved }: { item: Team | null; staff: StaffMember[]; onClose: () => void; onSaved: () => void | Promise<void> }) {
  const [form, setForm] = useState({ name: item?.name ?? "", managerEmployeeId: item?.managerEmployeeId?.toString() ?? "", isActive: item?.isActive ?? true }); const [saving, setSaving] = useState(false); const [error, setError] = useState<string | null>(null);
  const save = async () => { setSaving(true); setError(null); try { await apiJson(item ? `/api/lead-config/teams/${item.id}` : "/api/lead-config/teams", jsonRequest(item ? "PUT" : "POST", { ...form, managerEmployeeId: form.managerEmployeeId ? Number(form.managerEmployeeId) : null })); await onSaved(); } catch (e) { setError(e instanceof Error ? e.message : "Team could not be saved."); } finally { setSaving(false); } };
  return <CrmModal open title={item ? "Edit sales team" : "Create sales team"} onClose={onClose} footer={<ModalFooter saving={saving} onClose={onClose} onSave={() => void save()} />}><div className="space-y-4">{error && <ErrorBanner message={error} />}<TextField label="Team name" required value={form.name} onChange={(v) => setForm({ ...form, name: v })} /><SelectField label="Manager" value={form.managerEmployeeId} onChange={(v) => setForm({ ...form, managerEmployeeId: v })} options={staff.filter((e) => e.role === "Manager" || e.role === "Admin").map((e) => [String(e.employeeId), e.fullName])} empty="No manager" /><Checkbox label="Active" checked={form.isActive} onChange={(v) => setForm({ ...form, isActive: v })} /></div></CrmModal>;
}

function ModalFooter({ saving, onClose, onSave }: { saving: boolean; onClose: () => void; onSave: () => void }) { return <div className="flex justify-end gap-2"><Button variant="ghost" onClick={onClose}>Cancel</Button><Button disabled={saving} onClick={onSave}>{saving ? "Saving…" : "Save"}</Button></div>; }
function TextField({ label, value, onChange, required, type = "text", disabled }: { label: string; value: string; onChange: (value: string) => void; required?: boolean; type?: string; disabled?: boolean }) { return <div><Label required={required}>{label}</Label><input className={inputClass} type={type} required={required} disabled={disabled} value={value} onChange={(e) => onChange(e.target.value)} /></div>; }
function ReadOnlyField({ label, value }: { label: string; value: string }) { return <div><Label>{label}</Label><p className="rounded-xl border border-[var(--border)] bg-[var(--bg-muted)] px-3.5 py-2.5 text-sm text-[var(--text-secondary)]">{value}</p></div>; }
function SelectField({ label, value, onChange, options, required, empty }: { label: string; value: string; onChange: (value: string) => void; options: string[][]; required?: boolean; empty?: string }) { return <div><Label required={required}>{label}</Label><select className={inputClass} required={required} value={value} onChange={(e) => onChange(e.target.value)}>{(empty || !value) && <option value="">{empty ?? "Select…"}</option>}{options.map(([v, l]) => <option key={v} value={v}>{l}</option>)}</select></div>; }
function Checkbox({ label, checked, onChange }: { label: string; checked: boolean; onChange: (value: boolean) => void }) { return <label className="flex items-center gap-3 rounded-xl border border-[var(--border)] px-4 py-3 text-sm text-[var(--text-secondary)]"><input type="checkbox" checked={checked} onChange={(e) => onChange(e.target.checked)} />{label}</label>; }
function roleLabel(role?: string | null) { return role === "Manager" ? "Sales Manager" : role === "Employee" ? "Sales Employee" : role ?? "No login"; }
