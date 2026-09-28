import { useEffect, useId, useMemo, useState } from "react";
import {
  Checkbox,
  ChoiceChips,
  DateField,
  Dropdown,
  FieldShell,
  Modal,
  Notice,
  TextArea,
  TextField,
  useToast,
} from "../../components/ui";
import { useProjects } from "../../contexts/projectsContextValue.ts";
import { karachiDateInput } from "../../lib/dates.ts";
import { apiJson, jsonRequest, loadUnits } from "./leadApi.ts";
import { DialogSummary, FormSection } from "./LeadDialogParts.tsx";
import { describeEditConflict, editChanges, editForm, saveLeadEdit, type EditConflict, type EditForm } from "./leadEditMerge.ts";
import { BUYING_FOR_CHOICES, contactErrors, hasErrors, PAYMENT_CHOICES, type ContactErrors } from "./leadForm.ts";
import { paymentPreferenceLabel } from "./labels.ts";
import { statusAndOwner, unitChoices } from "./leadPage.ts";
import type { ClosureReason, Lead, UnitLookup } from "./types.ts";
import { useLeadSave } from "./useLeadSave.ts";

type DialogProps = { lead: Lead; onClose: () => void; onSaved: () => void };

/**
 * Edit details: the New lead fields without source and owner. Only changed fields are sent, with
 * the version the form was opened on; if the lead changed meanwhile, the newer details are merged
 * into the form for review instead of being overwritten.
 */
export function EditLeadDialog({ lead, apartmentTypes, onClose, onSaved }: DialogProps & { apartmentTypes: string[] }) {
  const toast = useToast();
  const formId = useId();
  const [base, setBase] = useState(lead);
  const [form, setForm] = useState<EditForm>(() => editForm(lead));
  const [sameWhatsapp, setSameWhatsapp] = useState(() => !!lead.phone && lead.whatsappNumber === lead.phone);
  const [errors, setErrors] = useState<ContactErrors>({});
  const [conflicts, setConflicts] = useState<EditConflict[] | null>(null);
  const [saving, setSaving] = useState(false);
  // A lead that arrived without a phone (an email-only ad lead) can still be edited without one.
  const phoneRequired = !!lead.phone;

  const values: EditForm = { ...form, whatsappNumber: sameWhatsapp ? form.phone : form.whatsappNumber };
  const changed = Object.keys(editChanges(base, values)).length > 0;
  const ready = changed && !!form.firstName.trim() && (!phoneRequired || !!form.phone.trim());

  const set = (key: string, value: string) => {
    setForm((current) => ({ ...current, [key]: value }));
    if (key in errors) setErrors((current) => ({ ...current, [key]: undefined }));
  };

  // A legacy apartment type stays selectable, so opening the form never changes it.
  const types = lead.propertyType && !apartmentTypes.includes(lead.propertyType) ? [...apartmentTypes, lead.propertyType] : apartmentTypes;

  const submit = async () => {
    const found = contactErrors({ firstName: form.firstName, phone: form.phone, email: form.email }, phoneRequired);
    setErrors(found);
    if (hasErrors(found)) return;
    setSaving(true);
    try {
      const outcome = await saveLeadEdit(base, values,
        (concurrencyToken) => apiJson(`/api/leads/${lead.id}`, jsonRequest("PUT", { ...editChanges(base, values), concurrencyToken })),
        () => apiJson<Lead>(`/api/leads/${lead.id}`));
      if (outcome.saved) {
        toast.success("Lead details saved");
        onSaved();
        return;
      }
      setBase(outcome.base);
      setForm(outcome.form);
      setSameWhatsapp(!!outcome.form.phone && outcome.form.whatsappNumber === outcome.form.phone);
      setConflicts(outcome.conflicts);
    } catch (caught) {
      toast.error(caught instanceof Error ? caught.message : "The lead could not be saved.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      open
      onClose={onClose}
      title="Edit lead details"
      size="lg"
      phoneLayout="fullscreen"
      busy={saving}
      primaryAction={{ label: "Save changes", form: formId, loading: saving, disabled: !ready }}
    >
      <form id={formId} noValidate onSubmit={(event) => { event.preventDefault(); void submit(); }} className="flex flex-col gap-5">
        {conflicts && (
          <Notice tone="gold" title="This lead changed while you were editing" message={describeEditConflict(conflicts)}>
            {conflicts.length > 0 && (
              <dl className="m-0 mt-2 flex flex-col gap-1.5">
                {conflicts.map((conflict) => (
                  <div key={conflict.label}>
                    <dt className="font-bold">{conflict.label} — newer value</dt>
                    <dd className="m-0 whitespace-pre-wrap break-words">{conflict.theirs || "(empty)"}</dd>
                  </div>
                ))}
              </dl>
            )}
          </Notice>
        )}

        <FormSection title="Contact">
          <TextField label="First name" required autoComplete="off" value={form.firstName} error={errors.firstName} onChange={(e) => set("firstName", e.target.value)} />
          <TextField label="Last name" autoComplete="off" value={form.lastName} onChange={(e) => set("lastName", e.target.value)} />
          <TextField label="Phone" required={phoneRequired} type="tel" placeholder="03xx xxxxxxx" value={form.phone} error={errors.phone} onChange={(e) => set("phone", e.target.value)} />
          <TextField label="City" placeholder="e.g. Lahore" value={form.city} onChange={(e) => set("city", e.target.value)} />
          <FieldShell as="fieldset" label="WhatsApp" className="sm:col-span-2">
            <Checkbox label="Same as phone number" checked={sameWhatsapp} onChange={setSameWhatsapp} />
            {!sameWhatsapp && (
              <TextField aria-label="WhatsApp number" type="tel" placeholder="03xx xxxxxxx" value={form.whatsappNumber} onChange={(e) => set("whatsappNumber", e.target.value)} />
            )}
          </FieldShell>
          <TextField label="Email" type="email" placeholder="Optional" value={form.email} error={errors.email} onChange={(e) => set("email", e.target.value)} className="sm:col-span-2" />
        </FormSection>

        <FormSection title="Requirement">
          <ChoiceChips label="Apartment type" className="sm:col-span-2" value={form.propertyType} onChange={(value) => set("propertyType", value)} options={types.map((type) => ({ value: type, label: type }))} />
          <ChoiceChips label="5-year installment plan?" className="sm:col-span-2" value={form.paymentPreference} onChange={(value) => set("paymentPreference", value)} options={PAYMENT_CHOICES} />
          <ChoiceChips label="Buying for" className="sm:col-span-2" value={form.purchaseIntent} onChange={(value) => set("purchaseIntent", value)} options={BUYING_FOR_CHOICES} />
        </FormSection>

        <TextArea label="Notes" value={form.notes} onChange={(e) => set("notes", e.target.value)} />
      </form>
    </Modal>
  );
}

/** Assign / reassign: Admin and Sales manager only. No "nobody" and no reason. */
export function AssignLeadDialog({ lead, choices, onClose, onSaved }: DialogProps & { choices: { value: string; label: string }[] }) {
  const { saving, run } = useLeadSave(onSaved);
  const current = lead.assignedEmployeeId ? String(lead.assignedEmployeeId) : "";
  const [employeeId, setEmployeeId] = useState(current);
  const ready = !!employeeId && employeeId !== current;
  const name = () => choices.find((choice) => choice.value === employeeId)?.label.replace(/^Me \((.*)\)$/, "$1") ?? "them";

  return (
    <Modal
      open
      onClose={onClose}
      title="Assign lead"
      size="sm"
      phoneLayout="popup"
      busy={saving}
      primaryAction={{
        label: "Save",
        loading: saving,
        disabled: !ready,
        onClick: () => void run(() => apiJson(`/api/leads/${lead.id}/assign`, jsonRequest("POST", { employeeId: Number(employeeId) })), `Lead assigned to ${name()}`),
      }}
    >
      <div className="flex flex-col gap-5">
        <DialogSummary
          title={lead.fullName}
          detail={lead.assignedEmployeeName ? <>Currently with <strong className="text-ink">{lead.assignedEmployeeName}</strong></> : "Unassigned"}
        />
        <Dropdown label="Assign to" options={choices} value={employeeId} onChange={setEmployeeId} placeholder={choices.length ? "Choose a person" : "Loading…"} />
      </div>
    </Modal>
  );
}

const OUTCOMES = [
  { value: "Lost", label: "Lost" },
  { value: "Dormant", label: "Dormant" },
];

/** Lost / dormant: an outcome and a configured reason; Dormant can set a day to bring the lead back. */
export function CloseLeadDialog({ lead, reasons, onClose, onSaved }: DialogProps & { reasons: ClosureReason[] | null }) {
  const formId = useId();
  const { saving, run } = useLeadSave(onSaved);
  const [outcome, setOutcome] = useState<"Lost" | "Dormant">("Lost");
  const [reasonId, setReasonId] = useState("");
  const [bringBackOn, setBringBackOn] = useState("");
  const [notes, setNotes] = useState("");
  const dormant = outcome === "Dormant";
  const today = karachiDateInput(0);

  const options = (reasons ?? [])
    .filter((reason) => reason.isActive && (reason.kind === outcome || reason.kind === "Both"))
    .sort((a, b) => a.displayOrder - b.displayOrder)
    .map((reason) => ({ value: String(reason.id), label: reason.name }));
  const ready = !!reasonId && (!dormant || !bringBackOn || bringBackOn >= today);

  const save = () => void run(
    () => apiJson(`/api/leads/${lead.id}/${dormant ? "dormant" : "lost"}`, jsonRequest("POST", {
      closureReasonId: Number(reasonId),
      notes: notes.trim() || null,
      reactivateOn: dormant && bringBackOn ? bringBackOn : null,
    })),
    dormant ? "Lead marked as dormant" : "Lead marked as lost",
  );

  return (
    <Modal
      open
      onClose={onClose}
      title="Close lead"
      size="md"
      phoneLayout="fullscreen"
      busy={saving}
      primaryAction={{ label: dormant ? "Mark as dormant" : "Mark as lost", variant: dormant ? "primary" : "danger", form: formId, loading: saving, disabled: !ready }}
    >
      <form id={formId} noValidate onSubmit={(event) => { event.preventDefault(); if (ready) save(); }} className="flex flex-col gap-5">
        <DialogSummary title={lead.fullName} detail={statusAndOwner(lead)} />
        <ChoiceChips label="Outcome" required options={OUTCOMES} value={outcome} onChange={(value) => { setOutcome(value as "Lost" | "Dormant"); setReasonId(""); }} />
        <Dropdown label="Reason" required options={options} value={reasonId} onChange={setReasonId} placeholder={reasons ? "Choose a reason" : "Loading…"} />
        {dormant && (
          <DateField label="Bring back on" min={today} value={bringBackOn} error={bringBackOn && bringBackOn < today ? "Choose today or a later day." : undefined} onChange={(e) => setBringBackOn(e.target.value)} />
        )}
        <TextArea label="Notes" placeholder="Optional" value={notes} onChange={(e) => setNotes(e.target.value)} />
      </form>
    </Modal>
  );
}

type ProjectUnits = { id: number; name: string; units: UnitLookup[] };

// Projects that are finished, cancelled or archived have nothing left to sell.
const CLOSED_PROJECT = new Set<number | string>([3, 4, 5, "Completed", "Cancelled", "Archived"]);

/**
 * Convert to booking: pick an available unit; the accountant completes the booking later. The
 * project is the lead's own, otherwise any open project with units left. A Project choice appears
 * only when more than one has units available.
 */
export function ConvertLeadDialog({ lead, onClose, onSaved }: DialogProps) {
  const formId = useId();
  const { projects, loading: projectsLoading } = useProjects();
  const { saving, run } = useLeadSave(onSaved);
  const [loaded, setLoaded] = useState<ProjectUnits[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [projectId, setProjectId] = useState("");
  const [unitId, setUnitId] = useState("");
  const [notes, setNotes] = useState("");

  const candidates = useMemo(() => (lead.interestedProjectId
    ? [{ id: lead.interestedProjectId, name: lead.interestedProjectName ?? projects.find((p) => p.id === lead.interestedProjectId)?.projectName ?? "" }]
    : projects.filter((project) => !CLOSED_PROJECT.has(project.status)).map((project) => ({ id: project.id, name: project.projectName }))),
  [lead.interestedProjectId, lead.interestedProjectName, projects]);

  useEffect(() => {
    if (candidates.length === 0) return;
    const controller = new AbortController();
    Promise.all(candidates.map(async (project) => ({ ...project, units: await loadUnits(project.id, controller.signal) })))
      .then((all) => {
        const open = all.filter((project) => project.units.some((unit) => unit.status === "Available"));
        setLoaded(open);
        setProjectId((current) => current || String(open[0]?.id ?? ""));
      })
      .catch(() => { if (!controller.signal.aborted) setFailed(true); });
    return () => controller.abort();
  }, [candidates]);

  const project = loaded?.find((item) => String(item.id) === projectId);
  const units = unitChoices(project?.units ?? [], lead.propertyType);
  const nothingToLoad = candidates.length === 0 && !projectsLoading;
  const placeholder = failed ? "Units could not be loaded"
    : !loaded && !nothingToLoad ? "Loading units…"
    : units.length ? "Choose a unit" : "No units available";

  const save = () => void run(
    () => apiJson<{ bookingReference: string }>(`/api/leads/${lead.id}/convert`, jsonRequest("POST", { unitId: Number(unitId), notes: notes.trim() || null })),
    (result) => `Converted to booking ${result.bookingReference}`,
  );

  return (
    <Modal
      open
      onClose={onClose}
      title="Convert to booking"
      size="lg"
      phoneLayout="fullscreen"
      busy={saving}
      primaryAction={{ label: "Convert to booking", variant: "success", form: formId, loading: saving, disabled: !unitId }}
    >
      <form id={formId} noValidate onSubmit={(event) => { event.preventDefault(); if (unitId) save(); }} className="flex flex-col gap-5">
        <DialogSummary title={lead.fullName} detail={[lead.phone, lead.propertyType, paymentPreferenceLabel(lead.paymentPreference)?.label].filter(Boolean).join(" · ")} />
        {loaded && loaded.length > 1 && (
          <Dropdown label="Project" required options={loaded.map((item) => ({ value: String(item.id), label: item.name }))} value={projectId} onChange={(value) => { setProjectId(value); setUnitId(""); }} />
        )}
        <Dropdown label="Unit" required options={units} value={unitId} onChange={setUnitId} placeholder={placeholder} disabled={!units.length} />
        <TextArea label="Notes" placeholder="Optional — anything accounts should know" value={notes} onChange={(e) => setNotes(e.target.value)} />
      </form>
    </Modal>
  );
}
