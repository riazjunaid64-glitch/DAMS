import { useId, useState, type ReactNode } from "react";
import {
  Button,
  Checkbox,
  ChoiceChips,
  Dropdown,
  FieldShell,
  IconAlert,
  Modal,
  Notice,
  TextArea,
  TextField,
  useToast,
} from "../../components/ui";
import { describeConflict, describeDuplicate, type DuplicateMatch, type DuplicateWarning } from "./duplicateResolution.ts";
import { paymentPreferenceLabel, purchaseIntentLabel } from "./labels.ts";
import { apiJson, jsonRequest, type CrmLookups } from "./leadApi.ts";
import type { Lead } from "./types.ts";

type Props = {
  onClose: () => void;
  lookups: CrmLookups | null;
  /** Admin and Sales manager choose the owner; a Sales employee's lead is assigned to them by the API. */
  canAssign: boolean;
  /** Opens a lead: the one just created, or the one the details already belong to. */
  onOpenLead: (leadId: number) => void;
};

type IntakeResult = {
  isDuplicate: boolean;
  identityConflict?: boolean;
  conflictingMatches?: DuplicateMatch[];
  message?: string;
  match?: DuplicateMatch;
  lead?: Lead;
};

const EMPTY = {
  firstName: "",
  lastName: "",
  phone: "",
  city: "",
  whatsappNumber: "",
  email: "",
  sourceCode: "",
  assignedEmployeeId: "",
  propertyType: "",
  paymentPreference: "",
  purchaseIntent: "",
  notes: "",
};

const PAYMENT_CHOICES = ["Installments", "NeedsDetails", "Cash"].map((value) => ({ value, label: paymentPreferenceLabel(value)!.label }));
const BUYING_FOR_CHOICES = ["SelfUse", "Investment"].map((value) => ({ value, label: purchaseIntentLabel(value)! }));

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/** Digits in a phone number: the API needs at least seven to match or reach anyone. */
const digits = (value: string) => value.replace(/\D/g, "").length;

/** New lead: a popup on desktop, a full-screen form on phone. Opens the lead once it is saved. */
export function NewLeadDialog({ onClose, lookups, canAssign, onOpenLead }: Props) {
  const toast = useToast();
  const formId = useId();
  const [form, setForm] = useState(EMPTY);
  const [sameWhatsapp, setSameWhatsapp] = useState(true);
  const [errors, setErrors] = useState<Partial<Record<"firstName" | "phone" | "email", string>>>({});
  const [saving, setSaving] = useState(false);
  // The open lead(s) these contact details already belong to, from the last submit.
  const [warning, setWarning] = useState<DuplicateWarning | null>(null);

  const set = (key: keyof typeof EMPTY, value: string) => {
    setForm((current) => ({ ...current, [key]: value }));
    if (key === "firstName" || key === "phone" || key === "email") setErrors((current) => ({ ...current, [key]: undefined }));
    // The warning describes the lead these contact details matched. Once they change, adding
    // could match nothing, or another lead than the one it names.
    if (key === "phone" || key === "whatsappNumber" || key === "email") setWarning(null);
  };

  const ready = !!form.firstName.trim() && !!form.phone.trim() && !!form.sourceCode;
  const owners = (lookups?.staff ?? []).filter((member) => member.canOwnLeads);

  // With addToLeadId the enquiry may only be added to that lead; the API writes nothing if the
  // details no longer match it, rather than adding to another lead or creating a new one.
  const submit = async (addToLeadId?: number) => {
    const found = {
      firstName: form.firstName.trim().length < 2 ? "Enter at least 2 letters." : undefined,
      phone: digits(form.phone) < 7 ? "Enter the full phone number." : undefined,
      email: form.email.trim() && !EMAIL.test(form.email.trim()) ? "Enter a valid email address." : undefined,
    };
    setErrors(found);
    if (found.firstName || found.phone || found.email) return;

    const phone = form.phone.trim();
    const optional = (value: string) => value.trim() || null;
    setSaving(true);
    try {
      const result = await apiJson<IntakeResult>("/api/leads", jsonRequest("POST", {
        firstName: form.firstName.trim(),
        lastName: optional(form.lastName),
        phone,
        whatsappNumber: sameWhatsapp ? phone : optional(form.whatsappNumber),
        email: optional(form.email),
        city: optional(form.city),
        sourceCode: form.sourceCode,
        assignedEmployeeId: canAssign && form.assignedEmployeeId ? Number(form.assignedEmployeeId) : null,
        propertyType: form.propertyType || null,
        paymentPreference: form.paymentPreference || "Unknown",
        purchaseIntent: form.purchaseIntent || "Unknown",
        notes: optional(form.notes),
        allowDuplicate: addToLeadId != null,
        expectedExistingLeadId: addToLeadId ?? null,
      }));
      if (result.lead) {
        toast.success(addToLeadId != null ? `Enquiry added to ${result.lead.leadReference}` : "Lead created");
        onOpenLead(result.lead.id);
        return;
      }
      if (result.identityConflict) setWarning(describeConflict(result.conflictingMatches ?? []));
      else if (result.isDuplicate) setWarning(describeDuplicate(result.match ?? {}));
      // A first match speaks for itself; only an add that was refused needs its reason.
      if (addToLeadId != null || (!result.identityConflict && !result.isDuplicate)) {
        toast.error(result.message || "Nothing was saved. Check the details and try again.");
      }
    } catch (caught) {
      toast.error(caught instanceof Error ? caught.message : "The lead could not be created.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      open
      onClose={onClose}
      title="New lead"
      size="lg"
      phoneLayout="fullscreen"
      busy={saving}
      primaryAction={{ label: "Create lead", form: formId, loading: saving, disabled: !ready }}
    >
      <form id={formId} noValidate onSubmit={(event) => { event.preventDefault(); void submit(); }} className="flex flex-col gap-5">
        {warning && warning.choices.length > 0 && (
          <Notice tone="gold" icon={<IconAlert size={18} />} title={warning.title}>
            <ul className="m-0 flex list-none flex-col gap-3 p-0">
              {warning.choices.map((choice) => (
                <li key={choice.leadId}>
                  <p className="m-0">{choice.detail}</p>
                  <div className="mt-2 grid grid-cols-2 gap-2 md:flex">
                    <Button variant="outline" size="sm" disabled={saving} onClick={() => onOpenLead(choice.leadId)}>Open that lead</Button>
                    <Button size="sm" disabled={saving} onClick={() => void submit(choice.leadId)}>Add to that lead</Button>
                  </div>
                </li>
              ))}
            </ul>
          </Notice>
        )}

        <Section title="Contact">
          <TextField label="First name" required placeholder="First name" autoComplete="off" value={form.firstName} error={errors.firstName} onChange={(e) => set("firstName", e.target.value)} />
          <TextField label="Last name" placeholder="Last name" autoComplete="off" value={form.lastName} onChange={(e) => set("lastName", e.target.value)} />
          <TextField label="Phone" required type="tel" placeholder="03xx xxxxxxx" value={form.phone} error={errors.phone} onChange={(e) => set("phone", e.target.value)} />
          <TextField label="City" placeholder="e.g. Lahore" value={form.city} onChange={(e) => set("city", e.target.value)} />
          <FieldShell as="fieldset" label="WhatsApp" className="sm:col-span-2">
            <Checkbox label="Same as phone number" checked={sameWhatsapp} onChange={(checked) => { setSameWhatsapp(checked); setWarning(null); }} />
            {!sameWhatsapp && (
              <TextField aria-label="WhatsApp number" type="tel" placeholder="03xx xxxxxxx" value={form.whatsappNumber} onChange={(e) => set("whatsappNumber", e.target.value)} />
            )}
          </FieldShell>
          <TextField label="Email" type="email" placeholder="Optional" value={form.email} error={errors.email} onChange={(e) => set("email", e.target.value)} className="sm:col-span-2" />
        </Section>

        <Section title="Where did they come from?">
          <Dropdown
            label="Source"
            required
            placeholder="Choose a source"
            value={form.sourceCode}
            onChange={(value) => set("sourceCode", value)}
            options={(lookups?.sources ?? []).map((source) => ({ value: source.code, label: source.name }))}
          />
          {canAssign && (
            <Dropdown
              label="Assign to"
              value={form.assignedEmployeeId}
              onChange={(value) => set("assignedEmployeeId", value)}
              options={[{ value: "", label: "Leave unassigned" }, ...owners.map((member) => ({ value: String(member.employeeId), label: member.fullName }))]}
            />
          )}
        </Section>

        <Section title="Requirement">
          <ChoiceChips
            label="Apartment type"
            className="sm:col-span-2"
            value={form.propertyType}
            onChange={(value) => set("propertyType", value)}
            options={(lookups?.apartmentTypes ?? []).map((type) => ({ value: type, label: type }))}
          />
          <ChoiceChips label="5-year installment plan?" className="sm:col-span-2" value={form.paymentPreference} onChange={(value) => set("paymentPreference", value)} options={PAYMENT_CHOICES} />
          <ChoiceChips label="Buying for" className="sm:col-span-2" value={form.purchaseIntent} onChange={(value) => set("purchaseIntent", value)} options={BUYING_FOR_CHOICES} />
        </Section>

        <TextArea label="Notes" value={form.notes} onChange={(e) => set("notes", e.target.value)} />
      </form>
    </Modal>
  );
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="flex flex-col gap-4">
      <h3 className="m-0 border-b border-line-soft pb-2 text-small font-extrabold text-ink">{title}</h3>
      <div className="grid gap-4 sm:grid-cols-2">{children}</div>
    </section>
  );
}
