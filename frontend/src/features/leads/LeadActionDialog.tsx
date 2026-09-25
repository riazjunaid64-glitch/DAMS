import { useEffect, useState } from "react";
import type { User } from "../../App.tsx";
import { api } from "../../api/api.ts";
import Button from "../../lib/Button.tsx";
import AppSelect from "../../lib/AppSelect.tsx";
import type { LeadLookups } from "../../pages/LeadDetailPage.tsx";
import {
  CrmModal,
  ErrorBanner,
  inputClass,
  Label,
} from "./CrmUi.tsx";
import { oneHourFromNow, toLocalInput } from "./dateTimeInput.ts";
import { apiJson, jsonRequest, loadUnits } from "./leadApi.ts";
import {
  enumLabel,
  leadStages,
  stageLabel,
  type FollowUp,
  type Lead,
  type SiteVisit,
  type UnitLookup,
} from "./types.ts";

export type LeadAction =
  | { type: "edit" }
  | { type: "assign" }
  | { type: "stage" }
  | { type: "qualification" }
  | { type: "communication" }
  | { type: "followUp" }
  | { type: "completeFollowUp"; item: FollowUp }
  | { type: "rescheduleFollowUp"; item: FollowUp }
  | { type: "cancelFollowUp"; item: FollowUp }
  | { type: "siteVisit" }
  | { type: "completeVisit"; item: SiteVisit }
  | { type: "rescheduleVisit"; item: SiteVisit }
  | { type: "closeVisit"; item: SiteVisit; visitDisposition?: "cancel" | "missed" }
  | { type: "comment" }
  | { type: "document" }
  | { type: "close" }
  | { type: "reopen" }
  | { type: "convert" };

type Props = {
  action: LeadAction | null;
  lead: Lead;
  lookups: LeadLookups;
  user: User;
  onClose: () => void;
  onSaved: (destination?: string) => void | Promise<void>;
};

export default function LeadActionDialog({ action, lead, lookups, user, onClose, onSaved }: Props) {
  const [form, setForm] = useState<Record<string, string | boolean>>({});
  const [units, setUnits] = useState<UnitLookup[]>([]);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [customerResults, setCustomerResults] = useState<{ id: number; fullName: string; phone: string; email: string; cnic?: string | null }[]>([]);
  const [file, setFile] = useState<File | null>(null);

  useEffect(() => {
    if (!action) return;
    setError(null); setFile(null); setCustomerResults([]);
    setForm(initialForm(action, lead));
  }, [action, lead]);

  const projectId = Number(form.projectId || form.interestedProjectId || lead.interestedProjectId || 0);
  useEffect(() => {
    if (!action || !["edit", "siteVisit", "convert"].includes(action.type) || !projectId) { setUnits([]); return; }
    void loadUnits(projectId).then(setUnits).catch(() => setUnits([]));
  }, [action, projectId]);

  const set = (key: string, value: string | boolean) => setForm((current) => ({ ...current, [key]: value }));
  const value = (key: string) => String(form[key] ?? "");
  const checked = (key: string) => Boolean(form[key]);

  const title = action ? actionTitle(action) : "";
  const canSubmit = Boolean(action) && !saving;
  const eligibleWorkers = lookups.staff.filter((member) =>
    member.canOwnLeads &&
    (user.role !== "Employee" ||
      member.userId === Number(user.userId) ||
      member.employeeId === lead.assignedEmployeeId));
  const assignmentStaff = eligibleWorkers.filter((member) =>
    !value("teamId") || member.teamId === Number(value("teamId")));
  const setAssignmentTeam = (teamId: string) => setForm((current) => {
    const employee = lookups.staff.find((member) => member.employeeId === Number(current.employeeId));
    return {
      ...current,
      teamId,
      employeeId: employee && teamId && employee.teamId !== Number(teamId) ? "" : current.employeeId,
    };
  });
  const setAssignmentEmployee = (employeeId: string) => setForm((current) => {
    const employee = lookups.staff.find((member) => member.employeeId === Number(employeeId));
    return { ...current, employeeId, teamId: employee?.teamId?.toString() ?? current.teamId };
  });

  const submit = async () => {
    if (!action) return;
    setSaving(true); setError(null);
    try {
      const destination = await performAction(action, lead, form, file, user.role);
      await onSaved(destination);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "The action could not be completed.");
    } finally { setSaving(false); }
  };

  const searchCustomers = async () => {
    const search = value("customerSearch").trim();
    if (search.length < 2) { setError("Enter at least two characters to search customers."); return; }
    setError(null);
    try {
      setCustomerResults(await apiJson(`/api/staff/customer-lookup?search=${encodeURIComponent(search)}`));
    } catch (caught) { setError(caught instanceof Error ? caught.message : "Customer search failed."); }
  };

  if (!action) return null;

  return (
    <CrmModal
      open
      onClose={onClose}
      wide={["edit", "convert"].includes(action.type)}
      title={title}
      subtitle={actionSubtitle(action)}
      footer={<div className="flex justify-end gap-2"><Button variant="ghost" onClick={onClose} disabled={saving}>Cancel</Button><Button variant={["close", "closeVisit", "cancelFollowUp"].includes(action.type) ? "danger" : "primary"} onClick={() => void submit()} disabled={!canSubmit}>{saving ? "Saving…" : submitLabel(action)}</Button></div>}
    >
      <form onSubmit={(event) => { event.preventDefault(); void submit(); }} className="space-y-4">
        {error && <ErrorBanner message={error} />}

        {action.type === "edit" && (
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="First name" required value={value("firstName")} onChange={(v) => set("firstName", v)} />
            <Field label="Last name" value={value("lastName")} onChange={(v) => set("lastName", v)} />
            <Field label="Phone" value={value("phone")} onChange={(v) => set("phone", v)} />
            <Field label="WhatsApp" value={value("whatsappNumber")} onChange={(v) => set("whatsappNumber", v)} />
            <Field label="Email" type="email" value={value("email")} onChange={(v) => set("email", v)} />
            <Field label="City" value={value("city")} onChange={(v) => set("city", v)} />
            <Field label="Address" value={value("address")} onChange={(v) => set("address", v)} wide />
            <Select label="Preferred contact" value={value("preferredContactMethod")} onChange={(v) => set("preferredContactMethod", v)} options={["Phone", "Whatsapp", "Email", "Sms", "InPerson"]} />
            <Field label="Preferred contact time" value={value("preferredContactTime")} onChange={(v) => set("preferredContactTime", v)} />
            <Field label="Source details" value={value("sourceDetails")} onChange={(v) => set("sourceDetails", v)} />
            <Field label="Campaign" value={value("campaignName")} onChange={(v) => set("campaignName", v)} />
            <Field label="Campaign reference" value={value("campaignReference")} onChange={(v) => set("campaignReference", v)} />
            <Field label="Ad reference" value={value("adReference")} onChange={(v) => set("adReference", v)} />
            <Select label="Interested project" value={value("interestedProjectId")} onChange={(v) => { set("interestedProjectId", v); set("interestedUnitId", ""); }} allowEmpty options={lookups.projects.map((p) => ({ value: String(p.id), label: p.name }))} />
            <Select label="Interested unit" value={value("interestedUnitId")} onChange={(v) => set("interestedUnitId", v)} allowEmpty options={units.map((u) => ({ value: String(u.id), label: u.number }))} />
            <Field label="Property type" value={value("propertyType")} onChange={(v) => set("propertyType", v)} />
            <Field label="Preferred location" value={value("preferredLocation")} onChange={(v) => set("preferredLocation", v)} />
            <Field label="Minimum budget" type="number" value={value("budgetMin")} onChange={(v) => set("budgetMin", v)} />
            <Field label="Maximum budget" type="number" value={value("budgetMax")} onChange={(v) => set("budgetMax", v)} />
            <Select label="Purchase intent" value={value("purchaseIntent")} onChange={(v) => set("purchaseIntent", v)} options={["Unknown", "SelfUse", "Investment", "Rental", "Resale"]} />
            <TextArea label="Notes" value={value("notes")} onChange={(v) => set("notes", v)} wide />
          </div>
        )}

        {action.type === "assign" && (
          <>
            <Select label="Team" value={value("teamId")} onChange={setAssignmentTeam} allowEmpty emptyLabel="No team" options={lookups.teams.filter((t) => t.isActive).map((t) => ({ value: String(t.id), label: t.name }))} />
            <Select label="Employee" value={value("employeeId")} onChange={setAssignmentEmployee} allowEmpty emptyLabel="Unassigned" options={assignmentStaff.map((s) => ({ value: String(s.employeeId), label: `${s.fullName}${s.teamName ? ` · ${s.teamName}` : ""}` }))} />
            <TextArea label="Reason for ownership change" required value={value("reason")} onChange={(v) => set("reason", v)} />
          </>
        )}

        {action.type === "stage" && (
          <>
            <Select label="Next stage" required value={value("stage")} onChange={(v) => set("stage", v)} options={leadStages.filter((s) => !["Won", "Lost", "Dormant", "SiteVisitScheduled", "SiteVisitCompleted"].includes(s)).map((s) => ({ value: s, label: stageLabel(s) }))} />
            <TextArea label="Stage note" value={value("notes")} onChange={(v) => set("notes", v)} />
            {value("stage") === "Contacted" && !lead.firstContactAt && <Hint>Record a connected customer communication first. A contact attempt alone does not satisfy this rule.</Hint>}
            <Hint>Site Visit stages are advanced through the dedicated schedule and completion flows. Won is available only through conversion.</Hint>
          </>
        )}

        {action.type === "qualification" && (
          <>
            <Select label="Qualification" required value={value("qualification")} onChange={(v) => set("qualification", v)} options={["Unqualified", "Cold", "Warm", "Hot"]} />
            <TextArea label="Qualification note" value={value("notes")} onChange={(v) => set("notes", v)} />
          </>
        )}

        {action.type === "communication" && (
          <>
            <div className="grid gap-4 sm:grid-cols-2">
              <Select label="Channel" required value={value("channel")} onChange={(v) => set("channel", v)} options={["Phone", "Whatsapp", "Email", "Sms", "Meeting", "OfficeVisit", "SiteVisit", "Other"]} />
              <Select label="Direction" required value={value("direction")} onChange={(v) => set("direction", v)} options={["Outbound", "Inbound"]} />
              <Field label="Occurred at" type="datetime-local" required value={value("occurredAt")} onChange={(v) => set("occurredAt", v)} />
              <label className="flex items-center gap-3 rounded-xl border border-[var(--border)] px-4 py-3 text-sm text-[var(--text-secondary)]"><input type="checkbox" checked={checked("connected")} onChange={(e) => set("connected", e.target.checked)} />Customer was reached</label>
            </div>
            <TextArea label="Summary" required value={value("summary")} onChange={(v) => set("summary", v)} />
            <TextArea label="Customer response" value={value("customerResponse")} onChange={(v) => set("customerResponse", v)} />
            <div className="grid gap-4 sm:grid-cols-2"><Field label="Next action" value={value("nextAction")} onChange={(v) => set("nextAction", v)} /><Field label="Next action at" type="datetime-local" value={value("nextActionAt")} onChange={(v) => set("nextActionAt", v)} /></div>
          </>
        )}

        {action.type === "followUp" && (
          <>
            <div className="grid gap-4 sm:grid-cols-2">
              <Select label="Type" required value={value("followUpType")} onChange={(v) => set("followUpType", v)} options={["Call", "FollowUp", "Whatsapp", "Email", "Meeting", "SiteVisit", "DocumentCollection", "ManagerReview", "Other"]} />
              <Select label="Priority" required value={value("priority")} onChange={(v) => set("priority", v)} options={["Low", "Medium", "High", "Urgent"]} />
              <Select label="Assigned employee" required value={value("assignedEmployeeId")} onChange={(v) => set("assignedEmployeeId", v)} options={eligibleWorkers.map((s) => ({ value: String(s.employeeId), label: s.fullName }))} />
              <Field label="Due at" type="datetime-local" required value={value("dueAt")} onChange={(v) => set("dueAt", v)} />
            </div>
            <Field label="Title" required value={value("title")} onChange={(v) => set("title", v)} />
            <TextArea label="Notes" value={value("notes")} onChange={(v) => set("notes", v)} />
          </>
        )}

        {action.type === "completeFollowUp" && (
          <>
            <Hint>Completing “{action.item.title}” preserves it in history. You can schedule the next follow-up in the same action.</Hint>
            <TextArea label="Outcome" required value={value("outcome")} onChange={(v) => set("outcome", v)} />
            <div className="grid gap-4 sm:grid-cols-2"><Field label="Next follow-up at" type="datetime-local" value={value("nextFollowUpAt")} onChange={(v) => set("nextFollowUpAt", v)} /><Field label="Next follow-up title" value={value("nextFollowUpTitle")} onChange={(v) => set("nextFollowUpTitle", v)} /></div>
          </>
        )}

        {action.type === "rescheduleFollowUp" && (
          <>
            <Field label="New due date and time" type="datetime-local" required value={value("dueAt")} onChange={(v) => set("dueAt", v)} />
            <TextArea label="Reason for rescheduling" required value={value("reason")} onChange={(v) => set("reason", v)} />
          </>
        )}

        {action.type === "cancelFollowUp" && (
          <TextArea label="Cancellation reason" required value={value("reason")} onChange={(v) => set("reason", v)} />
        )}

        {action.type === "siteVisit" && (
          <>
            <div className="grid gap-4 sm:grid-cols-2">
              <Select label="Project" value={value("projectId")} onChange={(v) => { set("projectId", v); set("unitId", ""); }} options={lookups.projects.map((p) => ({ value: String(p.id), label: p.name }))} />
              <Select label="Unit" value={value("unitId")} onChange={(v) => set("unitId", v)} allowEmpty options={units.map((u) => ({ value: String(u.id), label: u.number }))} />
              <Select label="Assigned employee" required value={value("assignedEmployeeId")} onChange={(v) => set("assignedEmployeeId", v)} options={eligibleWorkers.map((s) => ({ value: String(s.employeeId), label: s.fullName }))} />
              <Field label="Scheduled at" type="datetime-local" required value={value("scheduledAt")} onChange={(v) => set("scheduledAt", v)} />
            </div>
            <Field label="Meeting location" required value={value("meetingLocation")} onChange={(v) => set("meetingLocation", v)} />
            <div className="grid gap-4 sm:grid-cols-2"><Field label="Customer attendees" value={value("customerAttendees")} onChange={(v) => set("customerAttendees", v)} /><Field label="Internal attendees" value={value("internalAttendees")} onChange={(v) => set("internalAttendees", v)} /></div>
            <TextArea label="Notes" value={value("notes")} onChange={(v) => set("notes", v)} />
          </>
        )}

        {action.type === "completeVisit" && (
          <>
            <Select label="Outcome" required value={value("outcome")} onChange={(v) => set("outcome", v)} options={["VeryInterested", "Interested", "Undecided", "NotInterested", "WantsAnotherOption", "ReadyToBook"]} />
            <TextArea label="Outcome notes" value={value("outcomeNotes")} onChange={(v) => set("outcomeNotes", v)} />
            <TextArea label="Customer feedback" value={value("customerFeedback")} onChange={(v) => set("customerFeedback", v)} />
            <TextArea label="Next action" required value={value("nextAction")} onChange={(v) => set("nextAction", v)} />
          </>
        )}

        {action.type === "rescheduleVisit" && (
          <>
            <Field label="New date and time" type="datetime-local" required value={value("scheduledAt")} onChange={(v) => set("scheduledAt", v)} />
            <Field label="Meeting location" value={value("meetingLocation")} onChange={(v) => set("meetingLocation", v)} />
            <TextArea label="Reason" required value={value("reason")} onChange={(v) => set("reason", v)} />
          </>
        )}

        {action.type === "closeVisit" && <TextArea label={action.visitDisposition === "missed" ? "Why was the visit missed?" : "Cancellation reason"} required value={value("reason")} onChange={(v) => set("reason", v)} />}

        {action.type === "comment" && (
          <>
            <TextArea label="Internal note" required value={value("body")} onChange={(v) => set("body", v)} />
            <div className="grid gap-2 sm:grid-cols-2">
              <label className="flex items-center gap-3 rounded-xl border border-[var(--border)] px-4 py-3 text-sm"><input type="checkbox" checked={checked("managerReview")} onChange={(e) => set("managerReview", e.target.checked)} />Request manager review</label>
              <label className="flex items-center gap-3 rounded-xl border border-[var(--border)] px-4 py-3 text-sm"><input type="checkbox" checked={checked("decisionRecord")} onChange={(e) => set("decisionRecord", e.target.checked)} />Mark as decision record</label>
            </div>
            <div><Label>Mention colleague</Label><select multiple className={`${inputClass} min-h-28`} value={value("mentionedUserIds").split(",").filter(Boolean)} onChange={(e) => set("mentionedUserIds", Array.from(e.target.selectedOptions).map((o) => o.value).join(","))}>{lookups.staff.filter((s) => s.userId && s.userId !== Number(user.userId)).map((s) => <option key={s.userId} value={s.userId!}>{s.fullName} · {s.role ? enumLabel(s.role) : "Staff"}</option>)}</select><p className="mt-1 text-xs text-[var(--text-muted)]">Use Ctrl/Cmd to select more than one person.</p></div>
          </>
        )}

        {action.type === "document" && (
          <>
            <div><Label required>File</Label><input className={inputClass} type="file" required onChange={(e) => setFile(e.target.files?.[0] ?? null)} /><p className="mt-1 text-xs text-[var(--text-muted)]">Stored privately and served only through the permission-checked download endpoint.</p></div>
            <Select label="Document type" required value={value("category")} onChange={(v) => set("category", v)} options={["Quotation", "FloorPlan", "PricePlan", "ApplicationDocument", "IdentityDocument", "PaymentPlan", "SiteVisitMaterial", "AgreementDraft", "Other"]} />
            <TextArea label="Description" value={value("description")} onChange={(v) => set("description", v)} />
          </>
        )}

        {action.type === "close" && (
          <>
            <Select label="Outcome" required value={value("closureType")} onChange={(v) => { set("closureType", v); set("closureReasonId", ""); }} options={[{ value: "lost", label: "Lost" }, { value: "dormant", label: "Dormant" }]} />
            <Select label="Configured reason" required value={value("closureReasonId")} onChange={(v) => set("closureReasonId", v)} options={lookups.reasons.filter((r) => r.isActive && (r.kind === "Both" || r.kind.toLowerCase() === value("closureType"))).map((r) => ({ value: String(r.id), label: r.name }))} />
            <TextArea label="Notes" value={value("notes")} onChange={(v) => set("notes", v)} />
            {value("closureType") === "dormant" && <Field label="Future follow-up date" type="date" value={value("reactivateOn")} onChange={(v) => set("reactivateOn", v)} />}
          </>
        )}

        {action.type === "reopen" && (
          <>
            <Select label="Restore to stage" required value={value("stage")} onChange={(v) => set("stage", v)} options={["New", "FirstContactPending", "Contacted", "Qualified", "Negotiation"].map((v) => ({ value: v, label: stageLabel(v) }))} />
            <TextArea label="Reason for reopening" required value={value("reason")} onChange={(v) => set("reason", v)} />
          </>
        )}

        {action.type === "convert" && (
          <>
            <Hint>This creates or links a Customer and creates one Booking atomically. Repeated submission returns the existing conversion instead of duplicating it.</Hint>
            <div className="grid gap-4 sm:grid-cols-2">
              <Select label="Project" required value={value("projectId")} onChange={(v) => { set("projectId", v); set("unitId", ""); }} options={lookups.projects.map((p) => ({ value: String(p.id), label: p.name }))} />
              <Select label="Unit" required value={value("unitId")} onChange={(v) => set("unitId", v)} options={units.map((u) => ({ value: String(u.id), label: u.number }))} />
            </div>
            <div className="rounded-xl border border-[var(--border)] p-4">
              <Label>Search existing customer</Label>
              <div className="flex gap-2"><input className={inputClass} value={value("customerSearch")} onChange={(e) => set("customerSearch", e.target.value)} placeholder="Name, phone, email, or CNIC" /><Button type="button" variant="outline" onClick={() => void searchCustomers()}>Search</Button></div>
              {customerResults.length > 0 && <div className="mt-3 space-y-2">{customerResults.map((customer) => <button type="button" key={customer.id} onClick={() => set("customerId", String(customer.id))} className={`w-full rounded-lg border p-3 text-left text-sm ${value("customerId") === String(customer.id) ? "border-[var(--accent)] bg-[var(--accent-glow)]" : "border-[var(--border)]"}`}><span className="font-semibold text-[var(--text-heading)]">{customer.fullName}</span><span className="ml-2 text-[var(--text-muted)]">{customer.phone} · {customer.email}</span></button>)}</div>}
              <p className="mt-2 text-xs text-[var(--text-muted)]">{value("customerId") ? `Existing customer #${value("customerId")} selected.` : "No selection means DAMS will safely resolve or create a customer from lead contact details."}</p>
            </div>
            <div className="grid gap-4 sm:grid-cols-2">
              <Field label="CNIC" value={value("cnic")} onChange={(v) => set("cnic", v)} />
              <Field label="Father name" value={value("fatherName")} onChange={(v) => set("fatherName", v)} />
              <Field label="Agreed sale price" type="number" value={value("agreedSalePrice")} onChange={(v) => set("agreedSalePrice", v)} />
              <Field label="Discount percent" type="number" value={value("discountPercent")} onChange={(v) => set("discountPercent", v)} />
              <Field label="Booking amount required" type="number" value={value("bookingAmountRequired")} onChange={(v) => set("bookingAmountRequired", v)} />
              <Field label="Booking amount due" type="date" value={value("bookingAmountDueDate")} onChange={(v) => set("bookingAmountDueDate", v)} />
            </div>
            <TextArea label="Discount reason" value={value("discountReason")} onChange={(v) => set("discountReason", v)} />
            <TextArea label="Conversion notes" value={value("notes")} onChange={(v) => set("notes", v)} />
            <label className="flex items-start gap-3 rounded-xl border border-amber-500/25 bg-amber-500/[0.06] p-4 text-sm text-amber-100"><input type="checkbox" required checked={checked("confirmed")} onChange={(e) => set("confirmed", e.target.checked)} /><span>I have checked the customer and unit details and understand this will create a booking.</span></label>
          </>
        )}
      </form>
    </CrmModal>
  );
}

function initialForm(action: LeadAction, lead: Lead): Record<string, string | boolean> {
  switch (action.type) {
    case "edit": return {
      firstName: lead.firstName, lastName: lead.lastName ?? "", phone: lead.phone ?? "", whatsappNumber: lead.whatsappNumber ?? "", email: lead.email ?? "", address: lead.address ?? "", city: lead.city ?? "", preferredContactMethod: lead.preferredContactMethod, preferredContactTime: lead.preferredContactTime ?? "", sourceDetails: lead.sourceDetails ?? "", campaignName: lead.campaignName ?? "", campaignReference: lead.campaignReference ?? "", adReference: lead.adReference ?? "", interestedProjectId: lead.interestedProjectId?.toString() ?? "", interestedUnitId: lead.interestedUnitId?.toString() ?? "", propertyType: lead.propertyType ?? "", preferredLocation: lead.preferredLocation ?? "", budgetMin: lead.budgetMin?.toString() ?? "", budgetMax: lead.budgetMax?.toString() ?? "", purchaseIntent: lead.purchaseIntent, notes: lead.notes ?? "",
    };
    case "assign": return { employeeId: lead.assignedEmployeeId?.toString() ?? "", teamId: lead.assignedTeamId?.toString() ?? "", reason: "" };
    case "stage": return { stage: "", notes: "" };
    case "qualification": return { qualification: lead.qualification, notes: "" };
    case "communication": return { channel: "Phone", direction: "Outbound", occurredAt: toLocalInput(new Date()), connected: true, summary: "", customerResponse: "", nextAction: "", nextActionAt: "" };
    case "followUp": return { followUpType: "FollowUp", priority: "Medium", assignedEmployeeId: lead.assignedEmployeeId?.toString() ?? "", dueAt: toLocalInput(oneHourFromNow()), title: "", notes: "" };
    case "completeFollowUp": return { outcome: "", nextFollowUpAt: "", nextFollowUpTitle: "" };
    case "rescheduleFollowUp": return { dueAt: toLocalInput(action.item.dueAt), reason: "" };
    case "cancelFollowUp": return { reason: "" };
    case "siteVisit": return { projectId: lead.interestedProjectId?.toString() ?? "", unitId: lead.interestedUnitId?.toString() ?? "", assignedEmployeeId: lead.assignedEmployeeId?.toString() ?? "", scheduledAt: toLocalInput(oneHourFromNow()), meetingLocation: "", customerAttendees: lead.fullName, internalAttendees: "", notes: "" };
    case "completeVisit": return { outcome: "Interested", outcomeNotes: "", customerFeedback: "", nextAction: "" };
    case "rescheduleVisit": return { scheduledAt: toLocalInput(action.item.scheduledAt), meetingLocation: action.item.meetingLocation, reason: "" };
    case "closeVisit": return { reason: "" };
    case "comment": return { body: "", managerReview: false, decisionRecord: false, mentionedUserIds: "" };
    case "document": return { category: "Quotation", description: "" };
    case "close": return { closureType: "lost", closureReasonId: "", notes: "", reactivateOn: "" };
    case "reopen": return { stage: "Contacted", reason: "" };
    case "convert": return { projectId: lead.interestedProjectId?.toString() ?? "", unitId: lead.interestedUnitId?.toString() ?? "", customerSearch: "", customerId: "", cnic: "", fatherName: "", agreedSalePrice: "", discountPercent: "", discountReason: "", bookingAmountRequired: "", bookingAmountDueDate: "", notes: "", confirmed: false };
  }
}

async function performAction(action: LeadAction, lead: Lead, form: Record<string, string | boolean>, file: File | null, userRole: string): Promise<string | undefined> {
  const s = (key: string) => String(form[key] ?? "").trim();
  const numberOrNull = (key: string) => s(key) ? Number(s(key)) : null;
  const dateOrNull = (key: string) => s(key) ? new Date(s(key)).toISOString() : null;
  switch (action.type) {
    case "edit":
      await apiJson(`/api/leads/${lead.id}`, jsonRequest("PUT", { ...form, budgetMin: numberOrNull("budgetMin"), budgetMax: numberOrNull("budgetMax"), interestedProjectId: numberOrNull("interestedProjectId"), interestedUnitId: numberOrNull("interestedUnitId") })); break;
    case "assign":
      await apiJson(`/api/leads/${lead.id}/assign`, jsonRequest("POST", { employeeId: numberOrNull("employeeId"), teamId: numberOrNull("teamId"), reason: s("reason") })); break;
    case "stage":
      if (!s("stage")) throw new Error("Choose a stage.");
      await apiJson(`/api/leads/${lead.id}/stage`, jsonRequest("POST", { stage: s("stage"), notes: s("notes") || null })); break;
    case "qualification":
      await apiJson(`/api/leads/${lead.id}/qualification`, jsonRequest("POST", { qualification: s("qualification"), notes: s("notes") || null })); break;
    case "communication":
      if (s("summary").length < 2) throw new Error("Add a communication summary.");
      await apiJson(`/api/leads/${lead.id}/communications`, jsonRequest("POST", { channel: s("channel"), direction: s("direction"), occurredAt: dateOrNull("occurredAt"), connected: Boolean(form.connected), summary: s("summary"), customerResponse: s("customerResponse") || null, nextAction: s("nextAction") || null, nextActionAt: dateOrNull("nextActionAt") })); break;
    case "followUp":
      if (!numberOrNull("assignedEmployeeId")) throw new Error("Assign the follow-up to an employee.");
      await apiJson(`/api/leads/${lead.id}/follow-ups`, jsonRequest("POST", { type: s("followUpType"), priority: s("priority"), assignedEmployeeId: numberOrNull("assignedEmployeeId"), dueAt: dateOrNull("dueAt"), title: s("title"), notes: s("notes") || null })); break;
    case "completeFollowUp":
      await apiJson(`/api/leads/follow-ups/${action.item.id}/complete`, jsonRequest("POST", { outcome: s("outcome"), nextFollowUpAt: dateOrNull("nextFollowUpAt"), nextFollowUpTitle: s("nextFollowUpTitle") || null })); break;
    case "rescheduleFollowUp":
      await apiJson(`/api/leads/follow-ups/${action.item.id}/reschedule`, jsonRequest("POST", { dueAt: dateOrNull("dueAt"), reason: s("reason") })); break;
    case "cancelFollowUp":
      await apiJson(`/api/leads/follow-ups/${action.item.id}/cancel`, jsonRequest("POST", { reason: s("reason") })); break;
    case "siteVisit":
      await apiJson(`/api/leads/${lead.id}/site-visits`, jsonRequest("POST", { projectId: numberOrNull("projectId"), unitId: numberOrNull("unitId"), assignedEmployeeId: numberOrNull("assignedEmployeeId"), scheduledAt: dateOrNull("scheduledAt"), meetingLocation: s("meetingLocation"), customerAttendees: s("customerAttendees") || null, internalAttendees: s("internalAttendees") || null, notes: s("notes") || null })); break;
    case "completeVisit":
      await apiJson(`/api/leads/site-visits/${action.item.id}/complete`, jsonRequest("POST", { outcome: s("outcome"), outcomeNotes: s("outcomeNotes") || null, customerFeedback: s("customerFeedback") || null, nextAction: s("nextAction") })); break;
    case "rescheduleVisit":
      await apiJson(`/api/leads/site-visits/${action.item.id}/reschedule`, jsonRequest("POST", { scheduledAt: dateOrNull("scheduledAt"), meetingLocation: s("meetingLocation") || null, reason: s("reason") })); break;
    case "closeVisit":
      await apiJson(`/api/leads/site-visits/${action.item.id}/${action.visitDisposition === "missed" ? "missed" : "cancel"}`, jsonRequest("POST", { reason: s("reason") })); break;
    case "comment":
      await apiJson(`/api/leads/${lead.id}/comments`, jsonRequest("POST", { body: s("body"), isManagerReviewRequest: Boolean(form.managerReview), isDecisionRecord: Boolean(form.decisionRecord), mentionedUserIds: s("mentionedUserIds").split(",").filter(Boolean).map(Number) })); break;
    case "document": {
      if (!file) throw new Error("Choose a document.");
      const payload = new FormData();
      payload.append("file", file); payload.append("category", s("category")); payload.append("description", s("description"));
      const response = await api(`/api/leads/${lead.id}/documents`, { method: "POST", body: payload });
      if (!response.ok) { const body = await response.json().catch(() => ({})) as { message?: string }; throw new Error(body.message ?? "The document could not be uploaded."); }
      break;
    }
    case "close":
      if (!numberOrNull("closureReasonId")) throw new Error("Choose a configured closure reason.");
      await apiJson(`/api/leads/${lead.id}/${s("closureType")}`, jsonRequest("POST", { closureReasonId: numberOrNull("closureReasonId"), notes: s("notes") || null, reactivateOn: s("closureType") === "dormant" ? dateOrNull("reactivateOn") : null })); break;
    case "reopen":
      await apiJson(`/api/leads/${lead.id}/reopen`, jsonRequest("POST", { stage: s("stage"), reason: s("reason") })); break;
    case "convert": {
      if (!form.confirmed) throw new Error("Confirm the conversion details.");
      if (!numberOrNull("unitId")) throw new Error("Choose the unit for the booking.");
      const result = await apiJson<{ bookingId: number }>(`/api/leads/${lead.id}/convert`, jsonRequest("POST", { unitId: numberOrNull("unitId"), customerId: numberOrNull("customerId"), cnic: s("cnic") || null, fatherName: s("fatherName") || null, agreedSalePrice: numberOrNull("agreedSalePrice"), discountPercent: numberOrNull("discountPercent"), discountReason: s("discountReason") || null, bookingAmountRequired: numberOrNull("bookingAmountRequired"), bookingAmountDueDate: dateOrNull("bookingAmountDueDate"), notes: s("notes") || null }));
      return userRole === "Admin" ? `/confirmed-bookings/${result.bookingId}` : undefined;
    }
  }
  return undefined;
}

function actionTitle(action: LeadAction) {
  const titles: Record<LeadAction["type"], string> = { edit: "Edit lead details", assign: "Assign or reassign lead", stage: "Move pipeline stage", qualification: "Update qualification", communication: "Log customer communication", followUp: "Create follow-up or task", completeFollowUp: "Complete follow-up", rescheduleFollowUp: "Reschedule follow-up", cancelFollowUp: "Cancel follow-up", siteVisit: "Schedule site visit", completeVisit: "Complete site visit", rescheduleVisit: "Reschedule site visit", closeVisit: "Close site visit", comment: "Add internal collaboration", document: "Upload lead document", close: "Close lead", reopen: "Reopen lead", convert: "Convert lead to booking" };
  return titles[action.type];
}
function actionSubtitle(action: LeadAction) {
  if (action.type === "close") return "Lost and Dormant are recorded outcomes and require a configured reason.";
  if (action.type === "stage") return "DAMS validates transition prerequisites on the server.";
  if (action.type === "convert") return "Review carefully—successful conversion is the only action that sets Won.";
  return undefined;
}
function submitLabel(action: LeadAction) {
  if (action.type === "convert") return "Confirm conversion";
  if (action.type === "close") return "Close lead";
  if (action.type === "document") return "Upload";
  if (action.type === "communication") return "Record activity";
  return "Save";
}

type Option = string | { value: string; label: string };
function Select({ label, value, onChange, options, required, allowEmpty, emptyLabel = "Select…" }: { label: string; value: string; onChange: (value: string) => void; options: Option[]; required?: boolean; allowEmpty?: boolean; emptyLabel?: string }) {
  return <div><Label required={required}>{label}</Label><AppSelect required={required} className={inputClass} value={value} onChange={(e) => onChange(e.target.value)}>{(allowEmpty || !value) && <option value="">{emptyLabel}</option>}{options.map((option) => { const o = typeof option === "string" ? { value: option, label: enumLabel(option) } : option; return <option key={o.value} value={o.value}>{o.label}</option>; })}</AppSelect></div>;
}
function Field({ label, value, onChange, required, type = "text", wide }: { label: string; value: string; onChange: (value: string) => void; required?: boolean; type?: string; wide?: boolean }) {
  return <div className={wide ? "sm:col-span-2" : ""}><Label required={required}>{label}</Label><input className={inputClass} required={required} type={type} value={value} onChange={(e) => onChange(e.target.value)} /></div>;
}
function TextArea({ label, value, onChange, required, wide }: { label: string; value: string; onChange: (value: string) => void; required?: boolean; wide?: boolean }) {
  return <div className={wide ? "sm:col-span-2" : ""}><Label required={required}>{label}</Label><textarea className={`${inputClass} min-h-24 resize-y`} required={required} value={value} onChange={(e) => onChange(e.target.value)} /></div>;
}
function Hint({ children }: { children: React.ReactNode }) { return <div className="rounded-xl border border-indigo-500/20 bg-indigo-500/[0.06] p-4 text-sm leading-6 text-[var(--text-secondary)]">{children}</div>; }
