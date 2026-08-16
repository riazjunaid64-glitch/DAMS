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
import type { ClosureReason, LeadSource, StaffMember, Team } from "../features/leads/types.ts";

type Props = { user: User | null };
type LinkableUser = { userId: number; fullName: string; email: string; role: string };

export default function CrmSettingsPage({ user }: Props) {
  return <CrmAccess user={user}>{user && (user.role === "Admin" ? <SettingsWorkspace user={user} /> : <StatePanel title="Admin access required" message="Only an Admin can manage staff accounts, lead sources, closure reasons, and sales teams." />)}</CrmAccess>;
}

function SettingsWorkspace({ user }: { user: User }) {
  // Returning from Meta's consent screen should land on the tab that sent you there.
  const [tab, setTab] = useState(() => {
    const params = new URLSearchParams(window.location.search);
    return params.get("tab") === "integrations" || params.has("meta") ? "integrations" : "staff";
  });
  const [staff, setStaff] = useState<StaffMember[]>([]);
  const [linkable, setLinkable] = useState<LinkableUser[]>([]);
  const [sources, setSources] = useState<LeadSource[]>([]);
  const [reasons, setReasons] = useState<ClosureReason[]>([]);
  const [teams, setTeams] = useState<Team[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [modal, setModal] = useState<"staff" | "source" | "reason" | "team" | null>(null);
  const [editing, setEditing] = useState<StaffMember | LeadSource | ClosureReason | Team | null>(null);

  const load = useCallback(async () => {
    setLoading(true); setError(null);
    try {
      const [staffRows, users, sourceRows, reasonRows, teamRows] = await Promise.all([
        apiJson<StaffMember[]>("/api/staff/accounts"),
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

  return (
    <>
      <CrmHeader title="CRM administration" subtitle="Manage secure staff access and the controlled configuration used by the Lead workflow." role={user.role} actions={<Button variant="outline" onClick={() => location.assign("/crm")}>← Lead workspace</Button>} />
      <div className="mx-auto w-full max-w-[1350px] space-y-5 px-4 py-6 sm:px-6 lg:px-8">
        {error && <ErrorBanner message={error} onRetry={() => void load()} />}
        <CrmTabs active={tab} onChange={setTab} items={[{ id: "staff", label: "Staff accounts", count: staff.length }, { id: "teams", label: "Sales teams", count: teams.length }, { id: "sources", label: "Lead sources", count: sources.length }, { id: "reasons", label: "Closure reasons", count: reasons.length }, { id: "integrations", label: "Integrations" }]} />
        <section className="rounded-2xl border border-[var(--border)] bg-[var(--bg-card)] p-4 sm:p-6">
          {/* Integrations loads its own data, so it stays usable even if the shared
              configuration fetch above failed. */}
          {tab === "integrations" ? <MetaIntegrationsPanel /> : loading ? <p className="py-16 text-center text-sm text-[var(--text-muted)]">Loading configuration…</p> : (
            <>
              {tab === "staff" && <SettingsTable title="Staff accounts" description="Create a secure login or connect an existing account to an employee. Role changes invalidate existing refresh sessions." addLabel="Add staff account" onAdd={() => open("staff")} headers={["Employee", "Login", "Role", "Team", "Status", ""]} rows={staff.map((item) => [<div><p className="font-semibold text-[var(--text-heading)]">{item.fullName}</p><p className="text-xs text-[var(--text-muted)]">{item.jobTitle} · {item.department}</p></div>, item.email ?? "No account", roleLabel(item.role), item.teamName ?? "No team", item.status, <Button size="sm" variant="outline" onClick={() => open("staff", item)}>Manage</Button>])} empty="No employees exist yet." />}
              {tab === "teams" && <SettingsTable title="Sales teams" description="Managers see the team they belong to and any team they manage." addLabel="Create team" onAdd={() => open("team")} headers={["Team", "Manager", "Members", "Status", ""]} rows={teams.map((item) => [item.name, item.managerName ?? "Not assigned", item.memberCount, item.isActive ? "Active" : "Inactive", <Button size="sm" variant="outline" onClick={() => open("team", item)}>Edit</Button>])} empty="No sales teams configured." />}
              {tab === "sources" && <SettingsTable title="Lead sources" description="The original source is preserved when repeat enquiries enrich a lead." addLabel="Create source" onAdd={() => open("source")} headers={["Source", "Code", "Customer mapping", "Status", ""]} rows={sources.map((item) => [item.name, <code className="text-xs">{item.code}</code>, item.customerSource, item.isActive ? "Active" : "Inactive", <Button size="sm" variant="outline" onClick={() => open("source", item)}>Edit</Button>])} empty="No lead sources configured." />}
              {tab === "reasons" && <SettingsTable title="Closure reasons" description="Lost and Dormant cannot be selected without an active configured reason." addLabel="Create reason" onAdd={() => open("reason")} headers={["Reason", "Code", "Applies to", "Status", ""]} rows={reasons.map((item) => [item.name, <code className="text-xs">{item.code}</code>, item.kind, item.isActive ? "Active" : "Inactive", <Button size="sm" variant="outline" onClick={() => open("reason", item)}>Edit</Button>])} empty="No closure reasons configured." />}
            </>
          )}
        </section>
      </div>

      {modal === "staff" && <StaffModal item={editing as StaffMember | null} staff={staff} users={linkable} teams={teams} onClose={() => setModal(null)} onSaved={async () => { setModal(null); await load(); }} />}
      {modal === "source" && <SourceModal item={editing as LeadSource | null} onClose={() => setModal(null)} onSaved={async () => { setModal(null); await load(); }} />}
      {modal === "reason" && <ReasonModal item={editing as ClosureReason | null} onClose={() => setModal(null)} onSaved={async () => { setModal(null); await load(); }} />}
      {modal === "team" && <TeamModal item={editing as Team | null} staff={staff} onClose={() => setModal(null)} onSaved={async () => { setModal(null); await load(); }} />}
    </>
  );
}

function SettingsTable({ title, description, addLabel, onAdd, headers, rows, empty }: { title: string; description: string; addLabel: string; onAdd: () => void; headers: string[]; rows: React.ReactNode[][]; empty: string }) {
  return <div><div className="mb-5 flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between"><div><h2 className="text-lg font-semibold text-[var(--text-heading)]">{title}</h2><p className="mt-1 text-sm text-[var(--text-muted)]">{description}</p></div><Button size="sm" onClick={onAdd}>+ {addLabel}</Button></div>{rows.length === 0 ? <p className="rounded-xl border border-dashed border-[var(--border)] py-16 text-center text-sm text-[var(--text-muted)]">{empty}</p> : <div className="overflow-x-auto"><table className="w-full min-w-[720px] text-left text-sm"><thead><tr className="border-b border-[var(--border)] text-xs uppercase tracking-wider text-[var(--text-muted)]">{headers.map((h, i) => <th className="px-3 py-3" key={`${h}-${i}`}>{h}</th>)}</tr></thead><tbody>{rows.map((row, i) => <tr className="border-b border-[var(--border)] last:border-0" key={i}>{row.map((cell, j) => <td className="px-3 py-4 text-[var(--text-secondary)]" key={j}>{cell}</td>)}</tr>)}</tbody></table></div>}</div>;
}

function StaffModal({ item, staff, users, teams, onClose, onSaved }: { item: StaffMember | null; staff: StaffMember[]; users: LinkableUser[]; teams: Team[]; onClose: () => void; onSaved: () => void | Promise<void> }) {
  const [form, setForm] = useState({ existingUserId: "", existingEmployeeId: "", fullName: item?.fullName ?? "", email: item?.email ?? "", temporaryPassword: "", role: item?.role ?? "Employee", teamId: item?.teamId?.toString() ?? "", jobTitle: item?.jobTitle ?? "Sales Executive", department: item?.department ?? "Sales", phone: item?.phone ?? "", joinDate: item?.joinDate?.slice(0, 10) ?? new Date().toISOString().slice(0, 10), status: item?.status ?? "Active", newTemporaryPassword: "" });
  const [saving, setSaving] = useState(false); const [error, setError] = useState<string | null>(null);
  const set = (key: keyof typeof form, value: string) => setForm((f) => ({ ...f, [key]: value }));
  const pickEmployee = (id: string) => { const employee = staff.find((e) => e.employeeId === Number(id)); setForm((f) => ({ ...f, existingEmployeeId: id, fullName: employee?.fullName ?? f.fullName, email: employee?.email ?? f.email, phone: employee?.phone ?? f.phone, jobTitle: employee?.jobTitle ?? f.jobTitle, department: employee?.department ?? f.department, teamId: employee?.teamId?.toString() ?? f.teamId })); };
  const pickUser = (id: string) => { const account = users.find((u) => u.userId === Number(id)); setForm((f) => ({ ...f, existingUserId: id, fullName: account?.fullName ?? f.fullName, email: account?.email ?? f.email })); };
  const save = async () => {
    setSaving(true); setError(null);
    try {
      if (item) await apiJson(`/api/staff/accounts/${item.employeeId}`, jsonRequest("PUT", { role: form.role, teamId: form.teamId ? Number(form.teamId) : -1, status: form.status, newTemporaryPassword: form.newTemporaryPassword || null }));
      else await apiJson("/api/staff/accounts", jsonRequest("POST", { ...form, existingUserId: form.existingUserId ? Number(form.existingUserId) : null, existingEmployeeId: form.existingEmployeeId ? Number(form.existingEmployeeId) : null, teamId: form.teamId ? Number(form.teamId) : null, temporaryPassword: form.existingUserId ? null : form.temporaryPassword, joinDate: form.joinDate }));
      await onSaved();
    } catch (caught) { setError(caught instanceof Error ? caught.message : "Staff account could not be saved."); } finally { setSaving(false); }
  };
  return <CrmModal open title={item ? "Manage staff account" : "Add staff account"} subtitle="Staff logins are created by an Admin; public sign-up remains customer-only." onClose={onClose} footer={<ModalFooter saving={saving} onClose={onClose} onSave={() => void save()} />}>
    <div className="space-y-4">{error && <ErrorBanner message={error} />}{!item && <><SelectField label="Connect existing employee" value={form.existingEmployeeId} onChange={pickEmployee} options={staff.filter((e) => !e.userId).map((e) => [String(e.employeeId), e.fullName])} empty="Create a new employee" /><SelectField label="Connect existing login" value={form.existingUserId} onChange={pickUser} options={users.map((u) => [String(u.userId), `${u.fullName} · ${u.email}`])} empty="Create a new login" /></>}<div className="grid gap-4 sm:grid-cols-2"><TextField label="Full name" required disabled={Boolean(item)} value={form.fullName} onChange={(v) => set("fullName", v)} /><TextField label="Email" required disabled={Boolean(item)} type="email" value={form.email} onChange={(v) => set("email", v)} /><SelectField label="CRM role" required value={form.role} onChange={(v) => set("role", v)} options={[["Admin", "Admin"], ["Manager", "Sales Manager"], ["Employee", "Sales Employee"]]} /><SelectField label="Team" value={form.teamId} onChange={(v) => set("teamId", v)} options={teams.filter((t) => t.isActive).map((t) => [String(t.id), t.name])} empty="No team" />{!item && <><TextField label="Job title" required value={form.jobTitle} onChange={(v) => set("jobTitle", v)} /><TextField label="Department" required value={form.department} onChange={(v) => set("department", v)} /><TextField label="Phone" required value={form.phone} onChange={(v) => set("phone", v)} /><TextField label="Join date" required type="date" value={form.joinDate} onChange={(v) => set("joinDate", v)} />{!form.existingUserId && <TextField label="Temporary password" required type="password" value={form.temporaryPassword} onChange={(v) => set("temporaryPassword", v)} />}</>}{item && <><SelectField label="Employment status" value={form.status} onChange={(v) => set("status", v)} options={[["Active", "Active"], ["OnLeave", "On leave"], ["Terminated", "Terminated"]]} /><TextField label="Reset temporary password" type="password" value={form.newTemporaryPassword} onChange={(v) => set("newTemporaryPassword", v)} /></>}</div></div>
  </CrmModal>;
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
function SelectField({ label, value, onChange, options, required, empty }: { label: string; value: string; onChange: (value: string) => void; options: string[][]; required?: boolean; empty?: string }) { return <div><Label required={required}>{label}</Label><select className={inputClass} required={required} value={value} onChange={(e) => onChange(e.target.value)}>{(empty || !value) && <option value="">{empty ?? "Select…"}</option>}{options.map(([v, l]) => <option key={v} value={v}>{l}</option>)}</select></div>; }
function Checkbox({ label, checked, onChange }: { label: string; checked: boolean; onChange: (value: boolean) => void }) { return <label className="flex items-center gap-3 rounded-xl border border-[var(--border)] px-4 py-3 text-sm text-[var(--text-secondary)]"><input type="checkbox" checked={checked} onChange={(e) => onChange(e.target.checked)} />{label}</label>; }
function roleLabel(role?: string | null) { return role === "Manager" ? "Sales Manager" : role === "Employee" ? "Sales Employee" : role ?? "No login"; }
